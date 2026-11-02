using System;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaCover
{
    public class FindMissingCoversService : IExecute<FindMissingCoversCommand>
    {
        private readonly IBookService _bookService;
        private readonly IFillMissingCovers _fillMissingCovers;
        private readonly Logger _logger;

        public FindMissingCoversService(IBookService bookService,
                                        IFillMissingCovers fillMissingCovers,
                                        Logger logger)
        {
            _bookService = bookService;
            _fillMissingCovers = fillMissingCovers;
            _logger = logger;
        }

        public void Execute(FindMissingCoversCommand message)
        {
            var books = _bookService.GetAllBooks();
            var filled = 0;

            foreach (var book in books)
            {
                try
                {
                    if (_fillMissingCovers.FillMissingCover(book))
                    {
                        filled++;
                    }
                }
                catch (Exception ex)
                {
                    // One book failing must not stop the rest of the library.
                    _logger.Warn(ex, "Cover lookup failed for {0}", book);
                }
            }

            _logger.Info("Found covers for {0} of {1} books", filled, books.Count);
        }
    }
}
