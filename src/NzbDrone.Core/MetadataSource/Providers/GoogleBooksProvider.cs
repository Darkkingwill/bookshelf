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
    public class GoogleBooksProvider : IMetadataProvider
    {
        private const string BaseUrl = "https://www.googleapis.com/books/v1";
        private const int MaxResults = 20;
        private const int TimeoutSeconds = 10;

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public string Key => "googlebooks";
        public string DisplayName => "Google Books";
        public bool RequiresAuth => false;

        public GoogleBooksProvider(IHttpClient httpClient, IConfigService configService, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        public List<MetadataSearchResult> SearchBooks(string query)
        {
            return Search($"volumes?q={Uri.EscapeDataString(query)}&maxResults={MaxResults}");
        }

        public List<MetadataSearchResult> SearchAuthors(string query)
        {
            // Google Books doesn't have a dedicated author search.
            // Use inauthor: prefix to weight author matches.
            return Search($"volumes?q=inauthor:{Uri.EscapeDataString(query)}&maxResults={MaxResults}");
        }

        public MetadataSearchResult SearchByIsbn(string isbn)
        {
            var results = Search($"volumes?q=isbn:{Uri.EscapeDataString(isbn)}&maxResults=1");
            return results.FirstOrDefault();
        }

        public MetadataSearchResult SearchByAsin(string asin)
        {
            // Google Books doesn't index by ASIN. Try a general search.
            var results = Search($"volumes?q={Uri.EscapeDataString(asin)}&maxResults=1");
            return results.FirstOrDefault();
        }

        public MetadataAuthorResult GetAuthorInfo(string foreignId)
        {
            // Google Books doesn't have author-level endpoints.
            // Search for books by this author and aggregate.
            var results = Search($"volumes?q=inauthor:{Uri.EscapeDataString(foreignId)}&maxResults=40");

            if (!results.Any())
            {
                return null;
            }

            // foreignId for Google Books author queries is the author name itself
            var authorName = results
                .SelectMany(r => r.Authors)
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;

            return new MetadataAuthorResult
            {
                ForeignId = foreignId,
                ProviderKey = Key,
                Name = authorName,
                SortName = ToSortName(authorName),
                Works = results
            };
        }

        public MetadataBookResult GetBookInfo(string foreignId)
        {
            try
            {
                var request = BuildRequest($"volumes/{foreignId}");
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }

                var json = JObject.Parse(response.Content);
                var volumeInfo = json["volumeInfo"];
                if (volumeInfo == null)
                {
                    return null;
                }

                var result = ParseVolumeInfo(json);

                return new MetadataBookResult
                {
                    ForeignId = foreignId,
                    ProviderKey = Key,
                    Title = result.Title,
                    Authors = result.Authors,
                    Description = result.Description,
                    CoverUrl = result.CoverUrl,
                    ExternalIds = result.ExternalIds,
                    Editions = new List<MetadataEditionResult>
                    {
                        new MetadataEditionResult
                        {
                            ForeignId = foreignId,
                            Title = result.Title,
                            Isbn = result.Isbn,
                            Isbn13 = result.Isbn13,
                            PageCount = result.PageCount,
                            Publisher = result.Publisher,
                            Language = result.Language,
                            CoverUrl = result.CoverUrl,
                            PublishedYear = result.PublishedYear
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get book info from Google Books for {0}", foreignId);
                return null;
            }
        }

        public bool TestConnection()
        {
            try
            {
                // Make a minimal request to verify the API is reachable.
                // A 429 (rate limit) still means the service is up and responding.
                var request = BuildRequest("volumes?q=test&maxResults=1");
                var response = _httpClient.Get(request);
                return response.StatusCode == HttpStatusCode.OK ||
                       response.StatusCode == HttpStatusCode.TooManyRequests;
            }
            catch
            {
                return false;
            }
        }

        private List<MetadataSearchResult> Search(string path)
        {
            try
            {
                var request = BuildRequest(path);
                var response = _httpClient.Get(request);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.Warn("Google Books API rate limit exceeded");
                    return new List<MetadataSearchResult>();
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Google Books API returned {0}", response.StatusCode);
                    return new List<MetadataSearchResult>();
                }

                var json = JObject.Parse(response.Content);
                var totalItems = json["totalItems"]?.Value<int>() ?? 0;

                if (totalItems == 0)
                {
                    return new List<MetadataSearchResult>();
                }

                var items = json["items"] as JArray;
                if (items == null)
                {
                    return new List<MetadataSearchResult>();
                }

                return items
                    .Select(ParseVolumeInfo)
                    .Where(r => r != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Google Books search failed");
                return new List<MetadataSearchResult>();
            }
        }

        private MetadataSearchResult ParseVolumeInfo(JToken item)
        {
            try
            {
                var id = item["id"]?.Value<string>();
                var volumeInfo = item["volumeInfo"];

                if (volumeInfo == null || id == null)
                {
                    return null;
                }

                var title = volumeInfo["title"]?.Value<string>();
                var subtitle = volumeInfo["subtitle"]?.Value<string>();
                var authors = volumeInfo["authors"]?.Select(a => a.Value<string>()).ToList()
                    ?? new List<string>();
                var description = volumeInfo["description"]?.Value<string>();
                var pageCount = volumeInfo["pageCount"]?.Value<int>();
                var publisher = volumeInfo["publisher"]?.Value<string>();
                var language = volumeInfo["language"]?.Value<string>();
                var publishedDate = volumeInfo["publishedDate"]?.Value<string>();
                var categories = volumeInfo["categories"]?.Select(c => c.Value<string>()).ToList()
                    ?? new List<string>();

                // Extract ISBNs
                string isbn10 = null;
                string isbn13 = null;
                var identifiers = volumeInfo["industryIdentifiers"] as JArray;
                if (identifiers != null)
                {
                    foreach (var identifier in identifiers)
                    {
                        var type = identifier["type"]?.Value<string>();
                        var value = identifier["identifier"]?.Value<string>();
                        if (type == "ISBN_10")
                        {
                            isbn10 = value;
                        }
                        else if (type == "ISBN_13")
                        {
                            isbn13 = value;
                        }
                    }
                }

                // Cover image - prefer large, fall back
                string coverUrl = null;
                var imageLinks = volumeInfo["imageLinks"];
                if (imageLinks != null)
                {
                    coverUrl = imageLinks["large"]?.Value<string>()
                        ?? imageLinks["medium"]?.Value<string>()
                        ?? imageLinks["thumbnail"]?.Value<string>();

                    // Google returns http URLs; upgrade to https
                    if (coverUrl != null && coverUrl.StartsWith("http://"))
                    {
                        coverUrl = "https://" + coverUrl.Substring(7);
                    }
                }

                // Parse year from publishedDate (can be "2020", "2020-01", "2020-01-15")
                int? publishedYear = null;
                if (publishedDate != null && publishedDate.Length >= 4 &&
                    int.TryParse(publishedDate.Substring(0, 4), out var year))
                {
                    publishedYear = year;
                }

                var externalIds = new Dictionary<string, string>
                {
                    { "googlebooks", id }
                };

                if (isbn13 != null)
                {
                    externalIds["isbn13"] = isbn13;
                }

                if (isbn10 != null)
                {
                    externalIds["isbn10"] = isbn10;
                }

                return new MetadataSearchResult
                {
                    ForeignId = id,
                    ProviderKey = Key,
                    Title = title,
                    Subtitle = subtitle,
                    Authors = authors,
                    Isbn = isbn10,
                    Isbn13 = isbn13,
                    Description = description,
                    CoverUrl = coverUrl,
                    PageCount = pageCount,
                    PublishedYear = publishedYear,
                    Publisher = publisher,
                    Language = language,
                    Genres = categories,
                    ExternalIds = externalIds
                };
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse Google Books volume");
                return null;
            }
        }

        private HttpRequest BuildRequest(string path)
        {
            var url = $"{BaseUrl}/{path}";

            // Append Google Books API key if configured
            var apiKey = GetApiKey();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                url += (url.Contains("?") ? "&" : "?") + $"key={Uri.EscapeDataString(apiKey)}";
            }

            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = true,
                RequestTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            request.Headers.Accept = "application/json";
            return request;
        }

        private string GetApiKey()
        {
            var configs = _configService.GetMetadataProviderConfigs();
            var config = configs.FirstOrDefault(c =>
                c.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

            if (config?.Settings != null &&
                config.Settings.TryGetValue("apiKey", out var key) &&
                !string.IsNullOrWhiteSpace(key))
            {
                return key;
            }

            return null;
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
