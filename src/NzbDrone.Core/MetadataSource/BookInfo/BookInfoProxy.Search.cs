using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    // Searching for new authors and books.
    public partial class BookInfoProxy
    {
        public List<object> SearchForNewEntity(string title, string source = null)
        {
            // A pasted Goodreads link is an exact id, so it goes through the id lookup below even when
            // the Goodreads source is selected - that path is a name search and would treat the URL as
            // an author's name.
            var normalized = GoodreadsLinkParser.Normalize(title);
            var isGoodreadsLink = normalized != title;

            // Only "goodreads" gets its own path - same reasoning as GetAuthorInfo/GetBookInfo's
            // source routing: it's the one source with an independently-verified id space and a
            // working direct client, so it's worth bypassing bookinfo.pro's own (occasionally
            // wrong) title-to-id mapping for. Any other non-empty value is a provider key, handled below.
            if (!isGoodreadsLink && source.IsNotNullOrWhiteSpace() && source.Equals("goodreads", StringComparison.OrdinalIgnoreCase))
            {
                return SearchGoodreadsForNewEntity(title);
            }

            var isProviderSearch = !isGoodreadsLink && source.IsNotNullOrWhiteSpace();

            // Any other non-empty source is a metadata provider key (hardcover, audible, ...):
            // ask only that provider instead of the default lookup chain.
            var books = isProviderSearch
                ? _metadataProviderService.SearchBooks(source, normalized).Select(MapMetadataResultToBook).ToList()
                : SearchForNewBook(normalized, null, false);

            var result = new List<object>();
            var seenAuthorIds = new HashSet<string>();
            foreach (var book in books)
            {
                var author = book.Author.Value;

                // Provider results each carry their own Author instance, so dedupe by id there.
                if (isProviderSearch ? seenAuthorIds.Add(author.Metadata.Value.ForeignAuthorId) : !result.Contains(author))
                {
                    result.Add(author);
                }

                result.Add(book);
            }

            return result;
        }

        // Goodreads has no general-purpose title search reachable from this client (see
        // GoodreadsSearchProxy - that one goes through bookinfo.pro, which is exactly what this
        // is trying to avoid). What it does have is a reliable name-to-author resolver
        // (SearchAuthorByName) plus a full book list once you have the author id, so "search
        // Goodreads for X" here means "find the author named X and list everything of theirs" -
        // the same shape SearchByGoodreadsAuthorId already uses for an explicit "author:<id>"
        // query, just entered by name instead of by id.
        private List<object> SearchGoodreadsForNewEntity(string title)
        {
            var authorResult = _goodreadsProxy.SearchAuthorByName(title);

            if (authorResult == null)
            {
                return new List<object>();
            }

            Author author;
            try
            {
                author = GetAuthorInfo(authorResult.ForeignAuthorId, false, "goodreads");
            }
            catch (AuthorNotFoundException)
            {
                return new List<object>();
            }

            var books = author.Books.Value;
            var authors = new Dictionary<string, AuthorMetadata> { { authorResult.ForeignAuthorId, author.Metadata.Value } };

            // Mirrors SearchForNewEntity's own dedup below: AddDbIds may swap book.Author for an
            // existing local record (with its real db id), which is the copy that needs to end up
            // in the result, not the fresh-from-Goodreads one still held in `author`.
            var result = new List<object>();
            foreach (var book in books)
            {
                AddDbIds(authorResult.ForeignAuthorId, book, authors);

                var bookAuthor = book.Author.Value;

                if (!result.Contains(bookAuthor))
                {
                    result.Add(bookAuthor);
                }

                result.Add(book);
            }

            return result;
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            var books = SearchForNewBook(title, null);

            var authors = books
                .Select(x => x.Author.Value)
                .DistinctBy(x => x.ForeignAuthorId)
                .ToList();

            if (authors.Any())
            {
                return authors;
            }

            // Fall back to multi-provider search
            _logger.Info("Legacy search returned no results for '{0}', trying metadata providers", title);
            try
            {
                var providerResults = _metadataProviderService.SearchAuthors(title);
                if (providerResults != null && providerResults.Any())
                {
                    return providerResults.Select(MapMetadataResultToAuthor).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Multi-provider author search failed for '{0}'", title);
            }

            return authors;
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            title = GoodreadsLinkParser.Normalize(title);

            var q = title.ToLower().Trim();
            if (author != null)
            {
                q += " " + author;
            }

            List<Book> legacyResults = null;

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                var split = lowerTitle.Split(':');
                var prefix = split[0];

                if (split.Length == 2 && new[] { "author", "work", "edition", "isbn", "asin" }.Contains(prefix))
                {
                    var slug = split[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Book>();
                    }

                    if (prefix == "author" || prefix == "work" || prefix == "edition")
                    {
                        var isValid = int.TryParse(slug, out var searchId);
                        if (!isValid)
                        {
                            return new List<Book>();
                        }

                        if (prefix == "author")
                        {
                            return SearchByGoodreadsAuthorId(searchId);
                        }

                        if (prefix == "work")
                        {
                            return SearchByGoodreadsWorkId(searchId);
                        }

                        if (prefix == "edition")
                        {
                            return SearchByGoodreadsBookId(searchId, getAllEditions);
                        }
                    }

                    // to handle isbn / asin
                    q = slug;
                }

                legacyResults = Search(q, getAllEditions);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Legacy search failed for '{0}', trying metadata providers", title);
            }

            if (legacyResults != null && legacyResults.Any())
            {
                return legacyResults;
            }

            // The local proxy found nothing (or is unreachable) - try real Goodreads directly
            // before falling through to unrelated providers below, since it's the same
            // authoritative source the local proxy's own cache is built from, just fetched
            // live instead of pre-seeded. SearchGoodreadsForNewEntity only works as an
            // author-name search (a real Goodreads API limitation, not specific to this
            // fallback) - so when a separate author name is known (e.g. CandidateService's
            // disk-scan matching, which always passes both a book tag and an author tag),
            // search Goodreads for THAT name and let the caller pick the matching title out
            // of the author's full bibliography, the same pattern SearchByGoodreadsAuthorId
            // already uses. Searching by `title` instead (the book's title, not an author's
            // name) would only ever match an author who happens to be named after that book,
            // which is never - confirmed live: every one of these lookups came back empty.
            // Only fall back to treating the book title itself as an author-name guess when no
            // author is known, which is the shape a plain author-search-box query takes.
            var goodreadsNameQuery = author.IsNotNullOrWhiteSpace() ? author : title;
            _logger.Info("Legacy search returned no results for '{0}', trying Goodreads directly for author '{1}'", title, goodreadsNameQuery);
            try
            {
                var goodreadsResults = SearchGoodreadsForNewEntity(goodreadsNameQuery).OfType<Book>().ToList();
                if (goodreadsResults.Any())
                {
                    return goodreadsResults;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Direct Goodreads search failed for '{0}'", goodreadsNameQuery);
            }

            // Fall back to multi-provider search
            _logger.Info("Goodreads search returned no results for '{0}', trying metadata providers", title);
            try
            {
                var searchQuery = author != null ? $"{title} {author}" : title;
                var providerResults = _metadataProviderService.SearchBooks(searchQuery);
                if (providerResults != null && providerResults.Any())
                {
                    return providerResults.Select(MapMetadataResultToBook).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Multi-provider book search failed for '{0}'", title);
            }

            return legacyResults ?? new List<Book>();
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            return Search(isbn, true);
        }

        public List<Book> SearchByAsin(string asin)
        {
            return Search(asin, true);
        }

        private List<Book> Search(string query, bool getAllEditions)
        {
            List<SearchJsonResource> result;
            try
            {
                result = _goodreadsSearchProxy.Search(query);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Error searching for {0}", query);
                return new List<Book>();
            }

            var books = new List<Book>();

            if (getAllEditions)
            {
                // Slower but more exhaustive, less intensive on metadata API
                var bookIds = result.Select(x => x.WorkId).ToList();

                var idMap = result.Select(x => new { AuthorId = x.Author.Id, BookId = x.WorkId })
                    .GroupBy(x => x.AuthorId)
                    .ToDictionary(x => x.Key, x => x.Select(i => i.BookId.ToString()).ToList());

                List<Book> authorBooks;
                foreach (var author in idMap.Keys)
                {
                    authorBooks = SearchByGoodreadsAuthorId(author);
                    books.AddRange(authorBooks.Where(b => idMap[author].Contains(b.ForeignBookId)));
                }

                var missingBooks = bookIds.ExceptBy(x => x.ToString(), books, x => x.ForeignBookId, StringComparer.Ordinal).ToList();
                foreach (var book in missingBooks)
                {
                    books.AddRange(SearchByGoodreadsWorkId(book));
                }

                return books;
            }
            else
            {
                // Use sparingly, hits metadata API quite hard
                var ids = result.Select(x => x.BookId).ToList();

                if (ids.Count == 0)
                {
                    return new List<Book>();
                }

                if (ids.Count == 1)
                {
                    return SearchByGoodreadsBookId(ids[0], false);
                }

                try
                {
                    return MapSearchResult(ids);
                }
                catch (HttpException ex)
                {
                    _logger.Warn(ex);
                    throw new BookInfoException("Search for '{0}' failed. Unable to communicate with ReadarrAPI, returning status code: {1}.", ex, query, ex.Response.StatusCode);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Error mapping search results");

                    return new List<Book>();
                }
            }
        }

        private List<Book> SearchByGoodreadsAuthorId(int id)
        {
            try
            {
                var authorId = id.ToString();
                var result = GetAuthorInfo(authorId);
                var books = result.Books.Value;
                var authors = new Dictionary<string, AuthorMetadata> { { authorId, result.Metadata.Value } };

                foreach (var book in books)
                {
                    AddDbIds(authorId, book, authors);
                }

                return books;
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by author id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsWorkId(int id)
        {
            try
            {
                var tuple = GetBookInfo(id.ToString());
                AddDbIds(tuple.Item1, tuple.Item2, tuple.Item3.ToDictionary(x => x.ForeignAuthorId));
                return new List<Book> { tuple.Item2 };
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by work id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsBookId(int id, bool getAllEditions)
        {
            try
            {
                var book = GetEditionInfo(id, getAllEditions);

                return new List<Book> { book };
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (EditionNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by book id");
                return new List<Book>();
            }
        }

        private Book GetEditionInfo(int id, bool getAllEditions)
        {
            HttpRequest httpRequest;
            HttpResponse httpResponse;

            while (true)
            {
                httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"book/{id}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // we expect a redirect
                httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!httpResponse.HasHttpRedirect)
            {
                throw new BookInfoException($"Unexpected response from {httpRequest.Url}");
            }

            var location = httpResponse.Headers.GetSingleValue("Location");
            var split = location.Split('/').Reverse().ToList();
            var newId = split[0];
            var type = split[1];

            Book book;
            List<AuthorMetadata> authors;

            if (type == "author")
            {
                var author = PollAuthor(newId);

                book = author.Books.Value.FirstOrDefault(b => b.Editions.Value.Any(e => e.ForeignEditionId == id.ToString()));
                authors = new List<AuthorMetadata> { author.Metadata.Value };
            }
            else if (type == "work")
            {
                var tuple = PollBook(newId);

                book = tuple.Item2;
                authors = tuple.Item3;
            }
            else
            {
                throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
            }

            if (book == null || book.Editions.Value.All(e => e.ForeignEditionId != id.ToString()))
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!getAllEditions)
            {
                var trimmed = new Book();
                trimmed.UseMetadataFrom(book);
                trimmed.Author.Value.Metadata = book.AuthorMetadata.Value;
                trimmed.AuthorMetadata = book.AuthorMetadata.Value;
                trimmed.SeriesLinks = book.SeriesLinks;
                var edition = book.Editions.Value.SingleOrDefault(e => e.ForeignEditionId == id.ToString());
                if (edition != null)
                {
                    edition.Monitored = true;
                }

                trimmed.Editions = new List<Edition> { edition };
                book = trimmed;
            }

            var authorDict = authors.ToDictionary(x => x.ForeignAuthorId);
            AddDbIds(book.AuthorMetadata.Value.ForeignAuthorId, book, authorDict);

            return book;
        }

        private List<Book> MapSearchResult(List<int> ids)
        {
            HttpResponse<BulkBookResource> httpResponse;

            while (true)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", "book/bulk")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                httpRequest.SetContent(ids.ToJson());
                httpRequest.ContentSummary = ids.ToJson(Formatting.None);

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.TooManyRequests };

                httpResponse = _httpClient.Post<BulkBookResource>(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            return MapBulkBook(httpResponse.Resource);
        }

        private List<Book> MapBulkBook(BulkBookResource resource)
        {
            var books = new List<Book>();

            if (resource == null)
            {
                return books;
            }

            var authors = resource.Authors.Select(BookInfoMapper.MapAuthorMetadata).ToDictionary(x => x.ForeignAuthorId, x => x);
            var series = resource.Series.Select(BookInfoMapper.MapSeries).ToList();

            foreach (var work in resource.Works)
            {
                var book = BookInfoMapper.MapBook(work);
                var authorId = work.Books.OrderByDescending(b => b.AverageRating * b.RatingCount).First().Contributors.First().ForeignId.ToString();

                AddDbIds(authorId, book, authors);

                books.Add(book);
            }

            BookInfoMapper.MapSeriesLinks(series, books, resource.Series);

            return books;
        }

        private void AddDbIds(string authorId, Book book, Dictionary<string, AuthorMetadata> authors)
        {
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);

                var editions = _editionService.GetEditionsByBook(dbBook.Id).ToDictionary(x => x.ForeignEditionId);

                // If we have any database editions, exactly one will be monitored.
                // So unmonitor all the found editions and let the UseDbFieldsFrom set
                // the monitored status
                foreach (var edition in book.Editions.Value)
                {
                    edition.Monitored = false;
                    if (editions.TryGetValue(edition.ForeignEditionId, out var dbEdition))
                    {
                        edition.UseDbFieldsFrom(dbEdition);
                    }
                }

                // Double check at least one edition is monitored
                if (book.Editions.Value.Any() && !book.Editions.Value.Any(x => x.Monitored))
                {
                    var mostPopular = book.Editions.Value.OrderByDescending(x => x.Ratings.Popularity).First();
                    mostPopular.Monitored = true;
                }
            }

            var author = _authorService.FindById(authorId);

            if (author == null)
            {
                if (!authors.TryGetValue(authorId, out var metadata))
                {
                    throw new BookInfoException(string.Format("Expected author metadata for id [{0}] in book data {1}", authorId, book));
                }

                author = new Author
                {
                    CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                    Metadata = metadata
                };
            }

            book.Author = author;
            book.AuthorMetadata = author.Metadata.Value;
            book.AuthorMetadataId = author.AuthorMetadataId;
        }
    }
}
