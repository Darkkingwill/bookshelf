namespace NzbDrone.Core.MediaFiles.M4bConversion
{
    // The tags written into a converted audiobook. Empty values are left out of the file.
    public class M4bTags
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string AlbumArtist { get; set; }
        public string Album { get; set; }
        public int? Year { get; set; }
        public string Narrator { get; set; }
        public string Description { get; set; }
    }
}
