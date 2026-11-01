using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaCover
{
    // Looks up a cover (Audible, then Open Library, then Google Books when a key is set) for every book that has no usable
    // one. Run it from the API with {"name": "FindMissingCovers"}; refreshes also do it for the books they touch.
    public class FindMissingCoversCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override string CompletionMessage => "Completed";
    }
}
