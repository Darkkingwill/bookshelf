using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Raised when existing files are repointed at a different edition, which is a change of
    // which book a file belongs to rather than an import or a delete. Nothing else signals it -
    // MediaFileService.Update writes straight to the repository - so anything that caches a
    // view of which file belongs to which book has no way to know it is now out of date.
    public class BookFileEditionChangedEvent : IEvent
    {
        public BookFileEditionChangedEvent(List<BookFile> bookFiles, int editionId)
        {
            BookFiles = bookFiles;
            EditionId = editionId;
        }

        public List<BookFile> BookFiles { get; private set; }
        public int EditionId { get; private set; }
    }
}
