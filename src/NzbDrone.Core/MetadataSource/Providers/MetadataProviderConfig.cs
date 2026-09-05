using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Persisted configuration for a single metadata provider.
    /// Stored as part of the app's config and exposed via the settings UI.
    /// </summary>
    public class MetadataProviderConfig
    {
        /// <summary>
        /// Provider key matching IMetadataProvider.Key.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Whether this provider is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Priority order (lower = tried first). User-reorderable in settings.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Provider-specific settings (API keys, base URLs, etc.).
        /// </summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Whether this is a user-added custom provider (ABS-compatible).
        /// </summary>
        public bool IsCustom { get; set; }

        /// <summary>
        /// Display name for custom providers.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Base URL for custom providers (e.g. "https://my-provider.example.com").
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Optional authorization token for custom providers.
        /// Sent as the AUTHORIZATION header value.
        /// </summary>
        public string AuthToken { get; set; }
    }

    /// <summary>
    /// Container for the full provider configuration list.
    /// Serialized to/from the config database.
    /// </summary>
    public class MetadataProviderConfigList
    {
        public List<MetadataProviderConfig> Providers { get; set; } = new List<MetadataProviderConfig>();
    }
}
