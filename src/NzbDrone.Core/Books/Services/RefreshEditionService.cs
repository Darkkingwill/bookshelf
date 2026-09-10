using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Books
{
    public interface IRefreshEditionService
    {
        bool RefreshEditionInfo(List<Edition> add, List<Edition> update, List<Tuple<Edition, Edition>> merge, List<Edition> delete, List<Edition> upToDate, List<Edition> remoteEditions, bool forceUpdateFileTags);
    }

    public class RefreshEditionService : IRefreshEditionService
    {
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly Logger _logger;

        public RefreshEditionService(IEditionService editionService,
            IMediaFileService mediaFileService,
            IMetadataTagService metadataTagService,
            Logger logger)
        {
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _logger = logger;
        }

        public bool RefreshEditionInfo(List<Edition> add, List<Edition> update, List<Tuple<Edition, Edition>> merge, List<Edition> delete, List<Edition> upToDate, List<Edition> remoteEditions, bool forceUpdateFileTags)
        {
            var updateList = new List<Edition>();

            // for editions that need updating, just grab the remote edition and set db ids
            foreach (var edition in update)
            {
                var remoteEdition = remoteEditions.Single(e => e.ForeignEditionId == edition.ForeignEditionId);
                edition.UseMetadataFrom(remoteEdition);

                // make sure title is not null
                edition.Title = edition.Title ?? "Unknown";
                updateList.Add(edition);
            }

            var deleteList = delete.Concat(merge.Select(x => x.Item1)).ToList();

            // Last-resort safety net: RefreshBookService.GetMatchingExistingChildren already
            // avoids deleting a file-bearing edition when it can confidently re-target it in place
            // (the single-local-edition case). A book with more than one local edition falls
            // outside that fallback on purpose - there's no reliable way to know which local
            // edition a given remote one should absorb - so it can still land here. Rather than
            // silently orphan the file (BookFile is linked only by EditionId - see BookFile.cs -
            // so deleting this row leaves it pointing at nothing), surface it loudly.
            foreach (var edition in deleteList)
            {
                var orphanedFiles = _mediaFileService.GetFilesByEdition(edition.Id);

                if (orphanedFiles.Any())
                {
                    _logger.Warn("Deleting edition {0} which still has {1} file(s) attached - they will be orphaned", edition, orphanedFiles.Count);
                }
            }

            _editionService.DeleteMany(deleteList);
            _editionService.UpdateMany(updateList);

            var tagsToUpdate = updateList;
            if (forceUpdateFileTags)
            {
                _logger.Debug("Forcing tag update due to Author/Book/Edition updates");
                tagsToUpdate = updateList.Concat(upToDate).ToList();
            }

            _metadataTagService.SyncTags(tagsToUpdate);

            return add.Any() || delete.Any() || updateList.Any() || merge.Any();
        }
    }
}
