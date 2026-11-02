namespace NzbDrone.Core.MediaCover.Fallback
{
    public class CoverSearch
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Isbn13 { get; set; }
    }

    public class CoverFallbackResult
    {
        public CoverFallbackResult(string source, string url)
        {
            Source = source;
            Url = url;
        }

        public string Source { get; }
        public string Url { get; }
    }

    // One place to look for a book's cover when the metadata source gave none (or an unusable one). Providers are tried
    // in ascending Order and must return null, never throw, when they have nothing good.
    public interface ICoverFallbackProvider
    {
        string Name { get; }
        int Order { get; }
        string FindCoverUrl(CoverSearch search);
    }
}
