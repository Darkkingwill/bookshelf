using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    // Merges every current BookFile for a book (one file, or every part of a multi-file
    // audiobook) into a single chaptered .m4b via ffmpeg, then replaces the old file
    // record(s) with the new one. See NzbDrone.Core.MediaFiles.M4bConversion.
    public class ConvertToM4bCommand : Command
    {
        public int BookId { get; set; }

        public ConvertToM4bCommand()
        {
        }

        public ConvertToM4bCommand(int bookId)
        {
            BookId = bookId;
        }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override string CompletionMessage => "Completed";
    }
}
