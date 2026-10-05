using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Test.BookTests
{
    // Exposes the protected refresh hooks so they can be tested without running a whole refresh.
    public class RefreshAuthorServiceTestHarness : RefreshAuthorService
    {
        public RefreshAuthorServiceTestHarness(IProvideAuthorInfo authorInfo,
                                               IAuthorService authorService,
                                               IAuthorMetadataService authorMetadataService,
                                               IBookService bookService,
                                               IMetadataProfileService metadataProfileService,
                                               IRefreshBookService refreshBookService,
                                               IRefreshSeriesService refreshSeriesService,
                                               IEventAggregator eventAggregator,
                                               IManageCommandQueue commandQueueManager,
                                               IMediaFileService mediaFileService,
                                               IHistoryService historyService,
                                               IRootFolderService rootFolderService,
                                               ICheckIfAuthorShouldBeRefreshed checkIfAuthorShouldBeRefreshed,
                                               IMonitorNewBookService monitorNewBookService,
                                               IConfigService configService,
                                               IImportListExclusionService importListExclusionService,
                                               Logger logger)
            : base(authorInfo,
                   authorService,
                   authorMetadataService,
                   bookService,
                   metadataProfileService,
                   refreshBookService,
                   refreshSeriesService,
                   eventAggregator,
                   commandQueueManager,
                   mediaFileService,
                   historyService,
                   rootFolderService,
                   checkIfAuthorShouldBeRefreshed,
                   monitorNewBookService,
                   configService,
                   importListExclusionService,
                   logger)
        {
        }

        public bool IsPinned(Book book) => IsChildPinned(book);
    }
}
