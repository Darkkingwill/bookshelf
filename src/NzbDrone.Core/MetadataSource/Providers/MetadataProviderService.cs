using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Orchestrates metadata lookups across multiple providers in user-defined
    /// priority order. Implements fallback: if provider 1 fails or returns
    /// nothing, tries provider 2, etc.
    /// </summary>
    public interface IMetadataProviderService
    {
        List<MetadataSearchResult> SearchBooks(string query);
        List<MetadataSearchResult> SearchAuthors(string query);
        MetadataSearchResult SearchByIsbn(string isbn);
        MetadataSearchResult SearchByAsin(string asin);
        MetadataAuthorResult GetAuthorInfo(string providerKey, string foreignId);
        MetadataBookResult GetBookInfo(string providerKey, string foreignId);
        List<IMetadataProvider> GetAvailableProviders();
        List<MetadataProviderConfig> GetProviderConfigs();
        void SaveProviderConfigs(List<MetadataProviderConfig> configs);
        MetadataProviderTestResult TestProvider(string providerKey);
    }

    public class MetadataProviderTestResult
    {
        public string ProviderKey { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class MetadataProviderService : IMetadataProviderService
    {
        private readonly IEnumerable<IMetadataProvider> _providers;
        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MetadataProviderService(
            IEnumerable<IMetadataProvider> providers,
            IConfigService configService,
            IHttpClient httpClient,
            Logger logger)
        {
            _providers = providers;
            _configService = configService;
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<IMetadataProvider> GetAvailableProviders()
        {
            return _providers.Where(p => p.Key != "__custom_placeholder__").ToList();
        }

        public List<MetadataProviderConfig> GetProviderConfigs()
        {
            var configs = _configService.GetMetadataProviderConfigs();

            // Ensure all registered providers have a config entry
            var existingKeys = configs.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var priority = configs.Any() ? configs.Max(c => c.Priority) + 1 : 0;

            foreach (var provider in _providers.Where(p => p.Key != "__custom_placeholder__"))
            {
                if (!existingKeys.Contains(provider.Key))
                {
                    configs.Add(new MetadataProviderConfig
                    {
                        Key = provider.Key,
                        Enabled = provider.Key == "googlebooks" || provider.Key == "openlibrary",
                        Priority = priority++
                    });
                }
            }

            return configs.OrderBy(c => c.Priority).ToList();
        }

        public void SaveProviderConfigs(List<MetadataProviderConfig> configs)
        {
            _configService.SaveMetadataProviderConfigs(configs);
        }

        public MetadataProviderTestResult TestProvider(string providerKey)
        {
            var provider = _providers.FirstOrDefault(p =>
                p.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase));

            // If not a built-in provider, check for custom provider
            if (provider == null)
            {
                var config = _configService.GetMetadataProviderConfigs()
                    .FirstOrDefault(c => c.IsCustom && c.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase));

                if (config != null)
                {
                    provider = CustomMetadataProvider.Create(
                        _httpClient, _logger, config.Key, config.DisplayName, config.Url, config.AuthToken);
                }
            }

            if (provider == null)
            {
                return new MetadataProviderTestResult
                {
                    ProviderKey = providerKey,
                    Success = false,
                    Message = $"Unknown provider: {providerKey}"
                };
            }

            try
            {
                var success = provider.TestConnection();
                return new MetadataProviderTestResult
                {
                    ProviderKey = providerKey,
                    Success = success,
                    Message = success ? "Connection successful" : "Connection failed"
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Provider test failed for {0}", providerKey);
                return new MetadataProviderTestResult
                {
                    ProviderKey = providerKey,
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public List<MetadataSearchResult> SearchBooks(string query)
        {
            var candidates = FuzzyMatcher.GenerateCandidates(query);
            var allResults = new List<MetadataSearchResult>();

            foreach (var candidate in candidates)
            {
                var results = SearchWithFallback(p => p.SearchBooks(candidate), "SearchBooks", candidate);
                allResults.AddRange(results);

                if (allResults.Count >= 5)
                {
                    break;
                }
            }

            return FuzzyMatcher.RankResults(allResults, query, 0.2);
        }

        public List<MetadataSearchResult> SearchAuthors(string query)
        {
            var candidates = FuzzyMatcher.GenerateCandidates(query);
            var allResults = new List<MetadataSearchResult>();

            foreach (var candidate in candidates)
            {
                var results = SearchWithFallback(p => p.SearchAuthors(candidate), "SearchAuthors", candidate);
                allResults.AddRange(results);

                if (allResults.Count >= 5)
                {
                    break;
                }
            }

            return FuzzyMatcher.RankResults(allResults, query, 0.2);
        }

        public MetadataSearchResult SearchByIsbn(string isbn)
        {
            foreach (var provider in GetEnabledProviders())
            {
                try
                {
                    _logger.Debug("Searching ISBN {0} via {1}", isbn, provider.Key);
                    var result = provider.SearchByIsbn(isbn);
                    if (result != null)
                    {
                        result.ProviderKey = provider.Key;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "ISBN search failed for provider {0}", provider.Key);
                }
            }

            return null;
        }

        public MetadataSearchResult SearchByAsin(string asin)
        {
            foreach (var provider in GetEnabledProviders())
            {
                try
                {
                    _logger.Debug("Searching ASIN {0} via {1}", asin, provider.Key);
                    var result = provider.SearchByAsin(asin);
                    if (result != null)
                    {
                        result.ProviderKey = provider.Key;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "ASIN search failed for provider {0}", provider.Key);
                }
            }

            return null;
        }

        public MetadataAuthorResult GetAuthorInfo(string providerKey, string foreignId)
        {
            // Try the specified provider first
            var provider = ResolveProvider(providerKey);

            if (provider != null)
            {
                try
                {
                    var result = provider.GetAuthorInfo(foreignId);
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "GetAuthorInfo failed for {0}:{1}", providerKey, foreignId);
                }
            }

            // Fall back to other enabled providers
            foreach (var fallback in GetEnabledProviders().Where(p => p.Key != providerKey))
            {
                try
                {
                    var result = fallback.GetAuthorInfo(foreignId);
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "GetAuthorInfo fallback failed for {0}:{1}", fallback.Key, foreignId);
                }
            }

            return null;
        }

        public MetadataBookResult GetBookInfo(string providerKey, string foreignId)
        {
            var provider = ResolveProvider(providerKey);

            if (provider != null)
            {
                try
                {
                    var result = provider.GetBookInfo(foreignId);
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "GetBookInfo failed for {0}:{1}", providerKey, foreignId);
                }
            }

            foreach (var fallback in GetEnabledProviders().Where(p => p.Key != providerKey))
            {
                try
                {
                    var result = fallback.GetBookInfo(foreignId);
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "GetBookInfo fallback failed for {0}:{1}", fallback.Key, foreignId);
                }
            }

            return null;
        }

        private List<MetadataSearchResult> SearchWithFallback(
            Func<IMetadataProvider, List<MetadataSearchResult>> searchFunc,
            string operation,
            string query)
        {
            var allResults = new List<MetadataSearchResult>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in GetEnabledProviders())
            {
                try
                {
                    _logger.Debug("{0} '{1}' via {2}", operation, query, provider.Key);
                    var results = searchFunc(provider);

                    if (results != null && results.Any())
                    {
                        foreach (var result in results)
                        {
                            result.ProviderKey = provider.Key;

                            // Deduplicate by ISBN when available
                            var dedupeKey = result.Isbn13 ?? result.Isbn ?? result.Asin
                                ?? $"{provider.Key}:{result.ForeignId}";

                            if (seenIds.Add(dedupeKey))
                            {
                                allResults.Add(result);
                            }
                        }

                        // If we got results from any provider, we can stop
                        // unless we want to aggregate (future: configurable)
                        if (allResults.Count >= 5)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "{0} failed for provider {1}, trying next", operation, provider.Key);
                }
            }

            return allResults;
        }

        private IMetadataProvider ResolveProvider(string providerKey)
        {
            var provider = _providers.FirstOrDefault(p =>
                p.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                var config = _configService.GetMetadataProviderConfigs()
                    .FirstOrDefault(c => c.IsCustom && c.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase));

                if (config != null)
                {
                    provider = CustomMetadataProvider.Create(
                        _httpClient, _logger, config.Key, config.DisplayName, config.Url, config.AuthToken);
                }
            }

            return provider;
        }

        private IEnumerable<IMetadataProvider> GetEnabledProviders()
        {
            var configs = GetProviderConfigs()
                .Where(c => c.Enabled)
                .OrderBy(c => c.Priority)
                .ToList();

            foreach (var config in configs)
            {
                if (config.IsCustom)
                {
                    yield return CustomMetadataProvider.Create(
                        _httpClient, _logger, config.Key, config.DisplayName, config.Url, config.AuthToken);
                }
                else
                {
                    var provider = _providers.FirstOrDefault(p =>
                        p.Key.Equals(config.Key, StringComparison.OrdinalIgnoreCase));

                    if (provider != null)
                    {
                        yield return provider;
                    }
                }
            }
        }
    }
}
