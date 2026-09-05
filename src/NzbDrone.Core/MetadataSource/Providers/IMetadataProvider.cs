using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Unified interface for metadata providers. Each provider (Google Books,
    /// Open Library, Hardcover, rreading-glasses) implements this interface.
    /// The MetadataProviderService queries them in user-defined priority order.
    /// </summary>
    public interface IMetadataProvider
    {
        /// <summary>
        /// Unique key used in configuration (e.g. "googlebooks", "openlibrary",
        /// "hardcover", "rreadingglasses").
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Human-readable name for the settings UI.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this provider requires an API key or auth token.
        /// </summary>
        bool RequiresAuth { get; }

        /// <summary>
        /// Search for books by free-text query. Returns lightweight results
        /// that may be incomplete (no full edition list, etc.).
        /// </summary>
        List<MetadataSearchResult> SearchBooks(string query);

        /// <summary>
        /// Search for authors by name. Returns lightweight author info.
        /// </summary>
        List<MetadataSearchResult> SearchAuthors(string query);

        /// <summary>
        /// Look up a book by ISBN. Returns null if not found.
        /// </summary>
        MetadataSearchResult SearchByIsbn(string isbn);

        /// <summary>
        /// Look up a book by ASIN. Returns null if not found or unsupported.
        /// </summary>
        MetadataSearchResult SearchByAsin(string asin);

        /// <summary>
        /// Get full author details including their works.
        /// The foreignId format is provider-specific.
        /// </summary>
        MetadataAuthorResult GetAuthorInfo(string foreignId);

        /// <summary>
        /// Get full book/work details including editions.
        /// </summary>
        MetadataBookResult GetBookInfo(string foreignId);

        /// <summary>
        /// Test whether this provider is reachable and configured correctly.
        /// Returns true if a basic health check succeeds.
        /// </summary>
        bool TestConnection();
    }
}
