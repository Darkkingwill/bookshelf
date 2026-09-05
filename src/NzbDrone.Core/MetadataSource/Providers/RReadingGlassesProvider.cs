using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Wraps the existing rreading-glasses / BookInfo metadata API as an
    /// IMetadataProvider. This is the legacy metadata path — it proxies
    /// GoodReads or Hardcover via a remote (or self-hosted) API.
    ///
    /// Unlike the other providers, this one returns GoodReads-compatible IDs
    /// that the rest of the app already understands, making it the most
    /// backward-compatible option but also the least reliable (depends on
    /// an external service that may be overloaded).
    /// </summary>
    public class RReadingGlassesProvider : IMetadataProvider
    {
        private const int TimeoutSeconds = 15;

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public string Key => "rreadingglasses";
        public string DisplayName => "rreading-glasses (GoodReads/Hardcover proxy)";
        public bool RequiresAuth => false;

        public RReadingGlassesProvider(IHttpClient httpClient,
            IConfigService configService,
            Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        private string GetBaseUrl()
        {
            // Check provider-specific config first
            var configs = _configService.GetMetadataProviderConfigs();
            var config = configs.FirstOrDefault(c =>
                c.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

            if (config?.Settings != null &&
                config.Settings.TryGetValue("baseUrl", out var url) &&
                !string.IsNullOrWhiteSpace(url))
            {
                return url.TrimEnd('/');
            }

            // Fall back to the legacy MetadataSource config
            var legacyUrl = _configService.MetadataSource;
            if (!string.IsNullOrWhiteSpace(legacyUrl))
            {
                return legacyUrl.TrimEnd('/');
            }

            return "https://api.bookinfo.pro";
        }

        public List<MetadataSearchResult> SearchBooks(string query)
        {
            try
            {
                var baseUrl = GetBaseUrl();
                var request = BuildRequest($"{baseUrl}/search?q={Uri.EscapeDataString(query)}");
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("rreading-glasses search returned {0}", response.StatusCode);
                    return new List<MetadataSearchResult>();
                }

                var results = JArray.Parse(response.Content);
                return results.Select(r =>
                {
                    var bookId = r["bookId"]?.Value<int>();
                    var workId = r["workId"]?.Value<int>();
                    var authorId = r["author"]?["id"]?.Value<int>();

                    var externalIds = new Dictionary<string, string>();
                    if (workId.HasValue)
                    {
                        externalIds["goodreads_work"] = workId.Value.ToString();
                    }

                    if (bookId.HasValue)
                    {
                        externalIds["goodreads_book"] = bookId.Value.ToString();
                    }

                    if (authorId.HasValue)
                    {
                        externalIds["goodreads_author"] = authorId.Value.ToString();
                    }

                    return new MetadataSearchResult
                    {
                        ForeignId = workId?.ToString() ?? bookId?.ToString(),
                        ProviderKey = Key,
                        Title = $"Work {workId}",  // Search endpoint only returns IDs
                        ExternalIds = externalIds
                    };
                }).Where(r => r.ForeignId != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "rreading-glasses search failed");
                return new List<MetadataSearchResult>();
            }
        }

        public List<MetadataSearchResult> SearchAuthors(string query)
        {
            // The rreading-glasses search endpoint returns book/work/author IDs.
            // We extract unique author IDs and return them.
            var bookResults = SearchBooks(query);

            var authorIds = bookResults
                .Where(r => r.ExternalIds.ContainsKey("goodreads_author"))
                .Select(r => r.ExternalIds["goodreads_author"])
                .Distinct()
                .ToList();

            return authorIds.Select(id => new MetadataSearchResult
            {
                ForeignId = id,
                ProviderKey = Key,
                Title = $"Author {id}",
                ExternalIds = new Dictionary<string, string>
                {
                    { "goodreads_author", id }
                }
            }).ToList();
        }

        public MetadataSearchResult SearchByIsbn(string isbn)
        {
            var results = SearchBooks(isbn);
            return results.FirstOrDefault();
        }

        public MetadataSearchResult SearchByAsin(string asin)
        {
            var results = SearchBooks(asin);
            return results.FirstOrDefault();
        }

        public MetadataAuthorResult GetAuthorInfo(string foreignId)
        {
            try
            {
                var baseUrl = GetBaseUrl();
                var request = BuildRequest($"{baseUrl}/author/{foreignId}");
                var response = _httpClient.Get(request);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.Warn("rreading-glasses author endpoint rate limited");
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("rreading-glasses author returned {0}", response.StatusCode);
                    return null;
                }

                var json = JObject.Parse(response.Content);
                var name = json["name"]?.Value<string>();
                var works = json["works"] as JArray;

                if (name == null)
                {
                    // Data still loading (rreading-glasses loads asynchronously)
                    _logger.Info("Author {0} data still loading on rreading-glasses", foreignId);
                    return null;
                }

                var workResults = new List<MetadataSearchResult>();
                if (works != null)
                {
                    foreach (var work in works)
                    {
                        var workId = work["id"]?.Value<int>();
                        var title = work["title"]?.Value<string>();

                        if (workId.HasValue && title != null)
                        {
                            workResults.Add(new MetadataSearchResult
                            {
                                ForeignId = workId.Value.ToString(),
                                ProviderKey = Key,
                                Title = title,
                                Authors = new List<string> { name },
                                ExternalIds = new Dictionary<string, string>
                                {
                                    { "goodreads_work", workId.Value.ToString() }
                                }
                            });
                        }
                    }
                }

                return new MetadataAuthorResult
                {
                    ForeignId = foreignId,
                    ProviderKey = Key,
                    Name = name,
                    SortName = ToSortName(name),
                    Works = workResults,
                    ExternalIds = new Dictionary<string, string>
                    {
                        { "goodreads_author", foreignId }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "rreading-glasses GetAuthorInfo failed for {0}", foreignId);
                return null;
            }
        }

        public MetadataBookResult GetBookInfo(string foreignId)
        {
            try
            {
                var baseUrl = GetBaseUrl();
                var request = BuildRequest($"{baseUrl}/work/{foreignId}");
                var response = _httpClient.Get(request);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.Warn("rreading-glasses work endpoint rate limited");
                    return null;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }

                var json = JObject.Parse(response.Content);
                var title = json["title"]?.Value<string>();

                return new MetadataBookResult
                {
                    ForeignId = foreignId,
                    ProviderKey = Key,
                    Title = title,
                    ExternalIds = new Dictionary<string, string>
                    {
                        { "goodreads_work", foreignId }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "rreading-glasses GetBookInfo failed for {0}", foreignId);
                return null;
            }
        }

        public bool TestConnection()
        {
            try
            {
                var baseUrl = GetBaseUrl();
                var request = BuildRequest($"{baseUrl}/search?q=test");
                request.RequestTimeout = TimeSpan.FromSeconds(5);
                var response = _httpClient.Get(request);
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        private HttpRequest BuildRequest(string url)
        {
            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = true,
                RequestTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            request.Headers.Accept = "application/json";
            return request;
        }

        private static string ToSortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var parts = name.Trim().Split(' ');
            if (parts.Length < 2)
            {
                return name;
            }

            return $"{parts.Last()}, {string.Join(" ", parts.Take(parts.Length - 1))}";
        }
    }
}
