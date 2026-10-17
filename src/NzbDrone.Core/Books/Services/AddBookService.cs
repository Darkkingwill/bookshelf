using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Books
{
    public interface IAddBookService
    {
        Book AddBook(Book book, bool doRefresh = true);
        List<Book> AddBooks(List<Book> books, bool doRefresh = true);
    }

    public class AddBookService : IAddBookService
    {
        private readonly IAuthorService _authorService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IBookService _bookService;
        private readonly ISeriesService _seriesService;
        private readonly ISeriesBookLinkService _seriesBookLinkService;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        public AddBookService(IAuthorService authorService,
                               IAddAuthorService addAuthorService,
                               IBookService bookService,
                               ISeriesService seriesService,
                               ISeriesBookLinkService seriesBookLinkService,
                               IProvideBookInfo bookInfo,
                               IImportListExclusionService importListExclusionService,
                               Logger logger)
        {
            _authorService = authorService;
            _addAuthorService = addAuthorService;
            _bookService = bookService;
            _seriesService = seriesService;
            _seriesBookLinkService = seriesBookLinkService;
            _bookInfo = bookInfo;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        public Book AddBook(Book book, bool doRefresh = true)
        {
            _logger.Debug($"Adding book {book}");

            book = AddSkyhookData(book, out var seriesLink);

            // we allow adding extra editions, so check if the book already exists
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);
            }

            // Remove any import list exclusions preventing addition
            _importListExclusionService.Delete(book.ForeignBookId);
            _importListExclusionService.Delete(book.AuthorMetadata.Value.ForeignAuthorId);

            // Note it's a manual addition so it's not deleted on next refresh
            book.AddOptions.AddType = BookAddType.Manual;
            book.Editions.Value.Single(x => x.Monitored).ManualAdd = true;

            // Add the author if necessary. The book may have come from a different metadata
            // source than the author's existing library record (e.g. an import list using
            // Goodreads ids for an author who was originally added via Hardcover), so also
            // check by name before concluding this is really a brand new author - otherwise
            // this creates a colliding duplicate, and re-fetching "new" author info can fail
            // outright if that other source doesn't have (or can't authenticate) author data.
            var dbAuthor = _authorService.FindById(book.AuthorMetadata.Value.ForeignAuthorId)
                ?? _authorService.FindByName(book.AuthorMetadata.Value.Name);

            if (dbAuthor == null)
            {
                var author = book.Author.Value;

                author.Metadata.Value.ForeignAuthorId = book.AuthorMetadata.Value.ForeignAuthorId;

                dbAuthor = _addAuthorService.AddAuthor(author, false);
            }

            book.Author = dbAuthor;
            book.AuthorMetadataId = dbAuthor.AuthorMetadataId;
            _bookService.AddBook(book, doRefresh);

            // The metadata the book was added from knows its series, but the author feed the later
            // refresh uses often doesn't, so link it now - before the first import names the folder.
            LinkSeries(book, seriesLink);

            return book;
        }

        // Pinned so a refresh never deletes it: RefreshSeriesService only removes unpinned links when
        // the author feed doesn't report the series.
        private void LinkSeries(Book book, SeriesBookLink source)
        {
            var title = source?.Series?.Value?.Title?.Trim();

            if (title.IsNullOrWhiteSpace())
            {
                return;
            }

            try
            {
                if (_seriesBookLinkService.GetLinksByBook(new List<int> { book.Id }).Any())
                {
                    return;
                }

                var foreignSeriesId = "goodreads-title:" + title.ToLowerInvariant();
                var series = _seriesService.FindById(foreignSeriesId) ?? _seriesService.FindByTitle(title);

                if (series == null)
                {
                    series = new Series
                    {
                        ForeignSeriesId = foreignSeriesId,
                        Title = title,
                        Numbered = true,
                        WorkCount = 1
                    };

                    _seriesService.InsertMany(new List<Series> { series });
                }

                _seriesBookLinkService.Insert(new SeriesBookLink
                {
                    SeriesId = series.Id,
                    BookId = book.Id,
                    Position = source.Position,
                    SeriesPosition = source.SeriesPosition,
                    IsPrimary = false,
                    Pinned = true
                });
            }
            catch (Exception ex)
            {
                // The book is already added, a missing series link must not fail the add.
                _logger.Warn(ex, "Failed to link series '{0}' for book {1}", title, book);
            }
        }

        public List<Book> AddBooks(List<Book> books, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var addedBooks = new List<Book>();

            foreach (var a in books)
            {
                a.Added = added;
                try
                {
                    addedBooks.Add(AddBook(a, doRefresh));
                }
                catch (Exception ex)
                {
                    // Could be a bad id from an import list
                    _logger.Error(ex, "Failed to import id: {0} - {1}", a.ForeignBookId, a.Title);
                }
            }

            return addedBooks;
        }

        private Book AddSkyhookData(Book newBook, out SeriesBookLink seriesLink)
        {
            var editionId = newBook.Editions.Value.Single(x => x.Monitored).ForeignEditionId;

            Tuple<string, Book, List<AuthorMetadata>> tuple = null;
            try
            {
                // If the author's already known (e.g. this book came in alongside an
                // already-resolved author from an import list), route through their pinned
                // source rather than whatever's globally configured - a bare legacy id is
                // otherwise ambiguous between providers. Safe to leave null (falls back to the
                // prior behavior unchanged) when Author isn't populated yet.
                var metadataSource = newBook.Author?.Value?.Metadata?.Value?.MetadataSource;
                tuple = _bookInfo.GetBookInfo(newBook.ForeignBookId, metadataSource);
            }
            catch (BookNotFoundException)
            {
                _logger.Error("Book with Foreign Id {0} was not found, it may have been removed from Goodreads.", newBook.ForeignBookId);

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("GoodreadsId", "A book with this ID was not found", newBook.ForeignBookId)
                                              });
            }

            // Same pick as the "Series #N" text on a search result: lowest series position first.
            seriesLink = tuple.Item2.SeriesLinks?.Value?
                .Where(x => x.Series?.Value?.Title.IsNotNullOrWhiteSpace() ?? false)
                .OrderBy(x => x.SeriesPosition)
                .FirstOrDefault();

            newBook.UseMetadataFrom(tuple.Item2);
            newBook.Added = DateTime.UtcNow;

            newBook.Editions = tuple.Item2.Editions.Value;
            newBook.Editions.Value.ForEach(x => x.Monitored = false);
            newBook.Editions.Value.Single(x => x.ForeignEditionId == editionId).Monitored = true;

            var metadata = tuple.Item3.FirstOrDefault(x => x.ForeignAuthorId == tuple.Item1);
            newBook.AuthorMetadata = metadata;

            return newBook;
        }
    }
}
