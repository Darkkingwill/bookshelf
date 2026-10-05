using NLog;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BookTests
{
    // Exposes the protected refresh hooks so they can be tested without running a whole refresh.
    public class RefreshSeriesServiceTestHarness : RefreshSeriesService
    {
        public RefreshSeriesServiceTestHarness(IBookService bookService,
                                               ISeriesService seriesService,
                                               ISeriesBookLinkService linkService,
                                               IRefreshSeriesBookLinkService refreshLinkService,
                                               IAuthorMetadataService authorMetadataService,
                                               Logger logger)
            : base(bookService,
                   seriesService,
                   linkService,
                   refreshLinkService,
                   authorMetadataService,
                   logger)
        {
        }

        public void Delete(Series series) => DeleteEntity(series, false);
    }
}
