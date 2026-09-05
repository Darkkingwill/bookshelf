using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Lightweight search result from any metadata provider.
    /// Normalized to a common shape so the pipeline can compare
    /// and rank results across providers.
    /// </summary>
    public class MetadataSearchResult
    {
        /// <summary>
        /// Provider-specific identifier (Goodreads work ID, Google Books volume ID,
        /// Open Library work key, Hardcover book ID, etc.).
        /// </summary>
        public string ForeignId { get; set; }

        /// <summary>
        /// Which provider returned this result.
        /// </summary>
        public string ProviderKey { get; set; }

        public string Title { get; set; }
        public string Subtitle { get; set; }
        public List<string> Authors { get; set; } = new List<string>();
        public string Isbn { get; set; }
        public string Isbn13 { get; set; }
        public string Asin { get; set; }
        public string Description { get; set; }
        public string CoverUrl { get; set; }
        public int? PageCount { get; set; }
        public int? PublishedYear { get; set; }
        public string Publisher { get; set; }
        public string Language { get; set; }
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Cross-reference IDs from other systems when available.
        /// Key = system name (e.g. "goodreads", "openlibrary"), Value = ID.
        /// </summary>
        public Dictionary<string, string> ExternalIds { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Match confidence score (0.0 - 1.0) set by the fuzzy matching layer.
        /// Higher is better. -1 means not yet scored.
        /// </summary>
        public double Confidence { get; set; } = -1;
    }

    /// <summary>
    /// Full author info returned by GetAuthorInfo.
    /// </summary>
    public class MetadataAuthorResult
    {
        public string ForeignId { get; set; }
        public string ProviderKey { get; set; }
        public string Name { get; set; }
        public string SortName { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
        public Dictionary<string, string> ExternalIds { get; set; } = new Dictionary<string, string>();
        public List<MetadataSearchResult> Works { get; set; } = new List<MetadataSearchResult>();
    }

    /// <summary>
    /// Full book info returned by GetBookInfo, including editions.
    /// </summary>
    public class MetadataBookResult
    {
        public string ForeignId { get; set; }
        public string ProviderKey { get; set; }
        public string Title { get; set; }
        public List<string> Authors { get; set; } = new List<string>();
        public string AuthorForeignId { get; set; }
        public string Description { get; set; }
        public string CoverUrl { get; set; }
        public List<MetadataEditionResult> Editions { get; set; } = new List<MetadataEditionResult>();
        public Dictionary<string, string> ExternalIds { get; set; } = new Dictionary<string, string>();
        public MetadataSeriesLink Series { get; set; }
    }

    public class MetadataEditionResult
    {
        public string ForeignId { get; set; }
        public string Title { get; set; }
        public string Isbn { get; set; }
        public string Isbn13 { get; set; }
        public string Asin { get; set; }
        public string Format { get; set; }
        public int? PageCount { get; set; }
        public string Publisher { get; set; }
        public string Language { get; set; }
        public string CoverUrl { get; set; }
        public int? PublishedYear { get; set; }
        public int RatingCount { get; set; }
        public double AverageRating { get; set; }
    }

    public class MetadataSeriesLink
    {
        public string SeriesId { get; set; }
        public string SeriesTitle { get; set; }
        public string Position { get; set; }
    }
}
