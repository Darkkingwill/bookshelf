using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Test.BookTests
{
    // Exposes the protected refresh hooks so they can be tested without running a whole refresh.
    public class RefreshBookServiceTestHarness : RefreshBookService
    {
        public RefreshBookServiceTestHarness(IBookService bookService,
                                             IAuthorService authorService,
                                             IRootFolderService rootFolderService,
                                             IAddAuthorService addAuthorService,
                                             IEditionService editionService,
                                             IAuthorMetadataService authorMetadataService,
                                             IProvideAuthorInfo authorInfo,
                                             IProvideBookInfo bookInfo,
                                             IRefreshEditionService refreshEditionService,
                                             IMediaFileService mediaFileService,
                                             IHistoryService historyService,
                                             IEventAggregator eventAggregator,
                                             ICheckIfBookShouldBeRefreshed checkIfBookShouldBeRefreshed,
                                             IMapCoversToLocal mediaCoverService,
                                             Logger logger)
            : base(bookService,
                   authorService,
                   rootFolderService,
                   addAuthorService,
                   editionService,
                   authorMetadataService,
                   authorInfo,
                   bookInfo,
                   refreshEditionService,
                   mediaFileService,
                   historyService,
                   eventAggregator,
                   checkIfBookShouldBeRefreshed,
                   mediaCoverService,
                   logger)
        {
        }

        public bool IsPinned(Edition edition) => IsChildPinned(edition);
    }
}
