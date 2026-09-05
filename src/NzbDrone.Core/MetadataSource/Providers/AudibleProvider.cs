using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Metadata provider that searches the Audible catalog API for products,
    /// then enriches each result via the Audnexus community API.
    /// Mirrors the Audiobookshelf Audible provider approach.
    /// No authentication required for either endpoint.
    /// </summary>
    public class AudibleProvider : IMetadataProvider
    {
        private const string AudnexusBaseUrl = "https://api.audnex.us";
        private const int MaxResults = 10;
        private const int TimeoutSeconds = 10;

        private static readonly Dictionary<string, string> RegionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "us", ".com" },
            { "ca", ".ca" },
            { "uk", ".co.uk" },
            { "au", ".com.au" },
            { "fr", ".fr" },
            { "de", ".de" },
            { "jp", ".co.jp" },
            { "it", ".it" },
            { "in", ".in" },
            { "es", ".es" }
        };

        private static readonly Regex AsinPattern = new Regex(@"^[A-Z0-9]{10}$", RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public string Key => "audible";
        public string DisplayName => "Audible";
        public bool RequiresAuth => false;

        public AudibleProvider(IHttpClient httpClient, IConfigService configService, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        public List<MetadataSearchResult> SearchBooks(string query)
        {
            return Search(query, null);
        }

        public List<MetadataSearchResult> SearchAuthors(string query)
        {
            return Search(null, query);
        }

        public MetadataSearchResult SearchByIsbn(string isbn)
        {
            // Audible doesn't index by ISBN well; try a general search
            var results = Search(isbn, null);
            return results.FirstOrDefault();
        }

        public MetadataSearchResult SearchByAsin(string asin)
        {
            if (string.IsNullOrWhiteSpace(asin))
            {
                return null;
            }

            var enriched = AsinLookup(asin.Trim().ToUpperInvariant());
            return enriched != null ? CleanResult(enriched) : null;
        }

        public MetadataAuthorResult GetAuthorInfo(string foreignId)
        {
            // Audible catalog doesn't have a direct author info endpoint we can use
            // without auth. Return null to fall through to other providers.
            return null;
        }

        public MetadataBookResult GetBookInfo(string foreignId)
        {
            // Try to extract ASIN from foreignId
            var asin = foreignId;
            if (foreignId != null && foreignId.StartsWith("audible:"))
            {
                asin = foreignId.Substring("audible:".Length);
            }

            var item = AsinLookup(asin);
            if (item == null)
            {
                return null;
            }

            var cleaned = CleanResult(item);
            return new MetadataBookResult
            {
                ForeignId = cleaned.ForeignId,
                ProviderKey = Key,
                Title = cleaned.Title,
                Authors = cleaned.Authors,
                Description = cleaned.Description,
                CoverUrl = cleaned.CoverUrl,
                ExternalIds = cleaned.ExternalIds,
                Editions = new List<MetadataEditionResult>
                {
                    new MetadataEditionResult
                    {
                        ForeignId = cleaned.ForeignId,
                        Title = cleaned.Title,
                        Asin = cleaned.Asin,
                        Isbn = cleaned.Isbn,
                        Publisher = cleaned.Publisher,
                        Language = cleaned.Language,
                        CoverUrl = cleaned.CoverUrl,
                        PublishedYear = cleaned.PublishedYear
                    }
                }
            };
        }

        public bool TestConnection()
        {
            try
            {
                var region = GetRegion();
                var tld = GetTld(region);
                var url = $"https://api.audible{tld}/1.0/catalog/products?num_results=1&title=test";

                var request = new HttpRequest(url)
                {
                    AllowAutoRedirect = true,
                    RequestTimeout = TimeSpan.FromSeconds(5)
                };

                var response = _httpClient.Get(request);
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audible test connection failed");
                return false;
            }
        }

        private List<MetadataSearchResult> Search(string title, string author)
        {
            var results = new List<MetadataSearchResult>();
            var region = GetRegion();
            var tld = GetTld(region);

            // If the query looks like an ASIN, do a direct lookup first
            var query = title ?? author ?? string.Empty;
            if (IsValidAsin(query.Trim().ToUpperInvariant()))
            {
                var item = AsinLookup(query.Trim().ToUpperInvariant());
                if (item != null)
                {
                    results.Add(CleanResult(item));
                    return results;
                }
            }

            // Search the Audible catalog
            var queryParams = new List<string>
            {
                $"num_results={MaxResults}",
                "products_sort_by=Relevance"
            };

            if (!string.IsNullOrWhiteSpace(title))
            {
                queryParams.Add($"title={Uri.EscapeDataString(title)}");
            }

            if (!string.IsNullOrWhiteSpace(author))
            {
                queryParams.Add($"author={Uri.EscapeDataString(author)}");
            }

            var url = $"https://api.audible{tld}/1.0/catalog/products?{string.Join("&", queryParams)}";
            _logger.Debug("[Audible] Search url: {0}", url);

            try
            {
                var request = new HttpRequest(url)
                {
                    AllowAutoRedirect = true,
                    RequestTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
                };

                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("[Audible] Catalog search returned {0}", response.StatusCode);
                    return results;
                }

                var json = JObject.Parse(response.Content);
                var products = json["products"] as JArray;

                if (products == null || !products.Any())
                {
                    _logger.Debug("[Audible] No products found");
                    return results;
                }

                // Enrich each product via Audnexus
                foreach (var product in products)
                {
                    var asin = product.Value<string>("asin");
                    if (string.IsNullOrWhiteSpace(asin))
                    {
                        continue;
                    }

                    try
                    {
                        var enriched = AsinLookup(asin, region);
                        if (enriched != null)
                        {
                            results.Add(CleanResult(enriched));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "[Audible] Failed to enrich ASIN {0}", asin);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "[Audible] Catalog search failed");
            }

            _logger.Debug("[Audible] {0} results", results.Count);
            return results;
        }

        /// <summary>
        /// Look up a single ASIN via the Audnexus community API for rich metadata.
        /// </summary>
        private JObject AsinLookup(string asin, string region = null)
        {
            if (string.IsNullOrWhiteSpace(asin))
            {
                return null;
            }

            var regionParam = !string.IsNullOrWhiteSpace(region) ? $"?region={region}" : "";
            var url = $"{AudnexusBaseUrl}/books/{Uri.EscapeDataString(asin.ToUpperInvariant())}{regionParam}";
            _logger.Debug("[Audible] Audnexus lookup: {0}", url);

            try
            {
                var request = new HttpRequest(url)
                {
                    AllowAutoRedirect = true,
                    RequestTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
                };

                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Debug("[Audible] Audnexus returned {0} for {1}", response.StatusCode, asin);
                    return null;
                }

                var json = JObject.Parse(response.Content);
                if (json.Value<string>("asin") == null)
                {
                    return null;
                }

                // Store region for later use
                json["region"] = region ?? GetRegion();
                return json;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "[Audible] Audnexus lookup failed for {0}", asin);
                return null;
            }
        }

        /// <summary>
        /// Map Audnexus response to our MetadataSearchResult, mirroring ABS cleanResult.
        /// </summary>
        private MetadataSearchResult CleanResult(JObject item)
        {
            var asin = item.Value<string>("asin") ?? string.Empty;
            var title = item.Value<string>("title") ?? string.Empty;
            var releaseDate = item.Value<string>("releaseDate");

            var result = new MetadataSearchResult
            {
                ForeignId = $"audible:{asin}",
                ProviderKey = Key,
                Title = title,
                Subtitle = item.Value<string>("subtitle"),
                Description = item.Value<string>("summary"),
                CoverUrl = item.Value<string>("image"),
                Asin = asin,
                Isbn = item.Value<string>("isbn"),
                Publisher = item.Value<string>("publisherName"),
                Language = CapitalizeFirst(item.Value<string>("language")),
                PublishedYear = !string.IsNullOrWhiteSpace(releaseDate) ? ParseYear(releaseDate) : null
            };

            // Authors
            var authors = item["authors"] as JArray;
            if (authors != null)
            {
                result.Authors = authors
                    .Select(a => a.Value<string>("name"))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
            }

            // Narrators as external ID
            var narrators = item["narrators"] as JArray;
            if (narrators != null)
            {
                var narratorNames = narrators
                    .Select(n => n.Value<string>("name"))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                if (narratorNames.Any())
                {
                    result.ExternalIds["narrator"] = string.Join(", ", narratorNames);
                }
            }

            // Genres and tags
            var genres = item["genres"] as JArray;
            if (genres != null)
            {
                result.Genres = genres
                    .Where(g => g.Value<string>("type") == "genre")
                    .Select(g => g.Value<string>("name"))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var tags = genres
                    .Where(g => g.Value<string>("type") == "tag")
                    .Select(g => g.Value<string>("name"))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                foreach (var tag in tags)
                {
                    if (!result.Genres.Contains(tag))
                    {
                        result.Genres.Add(tag);
                    }
                }
            }

            // Series
            var seriesPrimary = item["seriesPrimary"];
            if (seriesPrimary != null)
            {
                var seriesName = seriesPrimary.Value<string>("name");
                if (!string.IsNullOrWhiteSpace(seriesName))
                {
                    result.ExternalIds["series"] = seriesName;
                    var position = CleanSeriesSequence(seriesPrimary.Value<string>("position"));
                    if (!string.IsNullOrWhiteSpace(position))
                    {
                        result.ExternalIds["seriesPosition"] = position;
                    }
                }
            }

            // Duration
            var runtimeMin = item.Value<int?>("runtimeLengthMin");
            if (runtimeMin.HasValue && runtimeMin.Value > 0)
            {
                result.ExternalIds["durationMinutes"] = runtimeMin.Value.ToString();
            }

            // Format
            var formatType = item.Value<string>("formatType");
            if (!string.IsNullOrWhiteSpace(formatType))
            {
                result.ExternalIds["format"] = formatType;
            }

            return result;
        }

        /// <summary>
        /// Clean series sequence like ABS does — extract numeric portion.
        /// </summary>
        private static string CleanSeriesSequence(string sequence)
        {
            if (string.IsNullOrWhiteSpace(sequence))
            {
                return string.Empty;
            }

            var match = Regex.Match(sequence, @"\.\d+|\d+(?:\.\d+)?");
            return match.Success ? match.Value : sequence;
        }

        private string GetRegion()
        {
            var configs = _configService.GetMetadataProviderConfigs();
            var config = configs.FirstOrDefault(c =>
                c.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

            if (config?.Settings != null &&
                config.Settings.TryGetValue("region", out var region) &&
                !string.IsNullOrWhiteSpace(region))
            {
                return region.ToLowerInvariant();
            }

            return "us";
        }

        private static string GetTld(string region)
        {
            if (RegionMap.TryGetValue(region, out var tld))
            {
                return tld;
            }

            return ".com";
        }

        private static bool IsValidAsin(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && AsinPattern.IsMatch(value);
        }

        private static int? ParseYear(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                return null;
            }

            // releaseDate is typically "YYYY-MM-DD"
            var parts = dateStr.Split('-');
            if (parts.Length > 0 && int.TryParse(parts[0], out var year))
            {
                return year;
            }

            return null;
        }

        private static string CapitalizeFirst(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
