using System;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaCover
{
    public class FindMissingCoversService : IExecute<FindMissingCoversCommand>
    {
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IFillMissingCovers _fillMissingCovers;
        private readonly IFillMissingAuthorPictures _fillMissingAuthorPictures;
        private readonly Logger _logger;

        public FindMissingCoversService(IBookService bookService,
                                        IAuthorService authorService,
                                        IFillMissingCovers fillMissingCovers,
                                        IFillMissingAuthorPictures fillMissingAuthorPictures,
                                        Logger logger)
        {
            _bookService = bookService;
            _authorService = authorService;
            _fillMissingCovers = fillMissingCovers;
            _fillMissingAuthorPictures = fillMissingAuthorPictures;
            _logger = logger;
        }

        public void Execute(FindMissingCoversCommand message)
        {
            var books = _bookService.GetAllBooks();
            var filledCovers = 0;

            foreach (var book in books)
            {
                try
                {
                    if (_fillMissingCovers.FillMissingCover(book))
                    {
                        filledCovers++;
                    }
                }
                catch (Exception ex)
                {
                    // One book failing must not stop the rest of the library.
                    _logger.Warn(ex, "Cover lookup failed for {0}", book);
                }
            }

            var authors = _authorService.GetAllAuthors();
            var filledPictures = 0;

            foreach (var author in authors)
            {
                try
                {
                    if (_fillMissingAuthorPictures.FillMissingPicture(author))
                    {
                        filledPictures++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Picture lookup failed for {0}", author);
                }
            }

            _logger.Info("Found covers for {0} of {1} books and pictures for {2} of {3} authors", filledCovers, books.Count, filledPictures, authors.Count);
        }
    }
}
