using Equ;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class SeriesBookLink : Entity<SeriesBookLink>
    {
        public string Position { get; set; }
        public int SeriesPosition { get; set; }
        public int SeriesId { get; set; }
        public int BookId { get; set; }
        public bool IsPrimary { get; set; }

        // User-set overrides. Never touched by UseMetadataFrom, so they survive metadata provider refreshes.
        public string TitleOverride { get; set; }
        public string PositionOverride { get; set; }
        public bool? IsPrimaryOverride { get; set; }

        // A manually created/kept link. Refresh will never delete this even if the metadata
        // provider stops reporting it, so a user's manual fix survives future refreshes.
        public bool Pinned { get; set; }

        [MemberwiseEqualityIgnore]
        public LazyLoaded<Series> Series { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<Book> Book { get; set; }

        public override void UseMetadataFrom(SeriesBookLink other)
        {
            Position = other.Position;
            SeriesPosition = other.SeriesPosition;
            IsPrimary = other.IsPrimary;
        }

        public override void UseDbFieldsFrom(SeriesBookLink other)
        {
            Id = other.Id;
            SeriesId = other.SeriesId;
            BookId = other.BookId;
        }
    }
}
