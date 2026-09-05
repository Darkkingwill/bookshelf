using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Hardcover.app GraphQL API provider.
    /// Endpoint: https://api.hardcover.app/v1/graphql
    /// Requires a free API token (Bearer auth).
    /// Rate limit: 60 req/min, 30s timeout, max query depth 3.
    /// </summary>
    public class HardcoverProvider : IMetadataProvider
    {
        private const string GraphQlEndpoint = "https://api.hardcover.app/v1/graphql";
        private const int TimeoutSeconds = 15;

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public string Key => "hardcover";
        public string DisplayName => "Hardcover";
        public bool RequiresAuth => true;

        public HardcoverProvider(IHttpClient httpClient,
            NzbDrone.Core.Configuration.IConfigService configService,
            Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        private string GetApiToken()
        {
            var configs = _configService.GetMetadataProviderConfigs();
            var config = configs.FirstOrDefault(c =>
                c.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

            if (config?.Settings != null &&
                config.Settings.TryGetValue("apiToken", out var token) &&
                !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            return null;
        }

        public List<MetadataSearchResult> SearchBooks(string query)
        {
            var graphql = @"
                query SearchBooks($query: String!) {
                    search(query: $query, query_type: ""Book"", per_page: 20) {
                        results
                    }
                }";

            var results = ExecuteSearch(graphql, query);
            return results.Select(ParseBookSearchHit).Where(r => r != null).ToList();
        }

        public List<MetadataSearchResult> SearchAuthors(string query)
        {
            var graphql = @"
                query SearchAuthors($query: String!) {
                    search(query: $query, query_type: ""Author"", per_page: 10) {
                        results
                    }
                }";

            var results = ExecuteSearch(graphql, query);
            return results.Select(ParseAuthorSearchHit).Where(r => r != null).ToList();
        }

        public MetadataSearchResult SearchByIsbn(string isbn)
        {
            var results = SearchBooks(isbn);
            return results.FirstOrDefault(r =>
                r.Isbn == isbn || r.Isbn13 == isbn ||
                (r.ExternalIds != null && r.ExternalIds.Values.Contains(isbn)));
        }

        public MetadataSearchResult SearchByAsin(string asin)
        {
            var results = SearchBooks(asin);
            return results.FirstOrDefault();
        }

        public MetadataAuthorResult GetAuthorInfo(string foreignId)
        {
            // For Hardcover, foreignId is the author slug or ID from search
            // Use search to find the author and their books
            var authorResults = SearchAuthors(foreignId);
            if (!authorResults.Any())
            {
                return null;
            }

            var author = authorResults.First();
            var bookResults = SearchBooks(author.Title); // search by author name

            return new MetadataAuthorResult
            {
                ForeignId = foreignId,
                ProviderKey = Key,
                Name = author.Title,
                SortName = ToSortName(author.Title),
                Works = bookResults,
                ExternalIds = author.ExternalIds
            };
        }

        public MetadataBookResult GetBookInfo(string foreignId)
        {
            // Search for the book by its slug/ID
            var results = SearchBooks(foreignId);
            if (!results.Any())
            {
                return null;
            }

            var book = results.First();
            return new MetadataBookResult
            {
                ForeignId = foreignId,
                ProviderKey = Key,
                Title = book.Title,
                Authors = book.Authors,
                Description = book.Description,
                CoverUrl = book.CoverUrl,
                ExternalIds = book.ExternalIds,
                Editions = new List<MetadataEditionResult>
                {
                    new MetadataEditionResult
                    {
                        ForeignId = foreignId,
                        Title = book.Title,
                        Isbn = book.Isbn,
                        Isbn13 = book.Isbn13,
                        PageCount = book.PageCount,
                        Publisher = book.Publisher,
                        Language = book.Language,
                        CoverUrl = book.CoverUrl,
                        PublishedYear = book.PublishedYear
                    }
                }
            };
        }

        public bool TestConnection()
        {
            try
            {
                var token = GetApiToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.Warn("Hardcover API token not configured");
                    return false;
                }

                var results = SearchBooks("test");
                return results.Any();
            }
            catch
            {
                return false;
            }
        }

        private List<JToken> ExecuteSearch(string query, string searchTerm)
        {
            var token = GetApiToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.Debug("Hardcover API token not configured, skipping");
                return new List<JToken>();
            }

            try
            {
                var body = new JObject
                {
                    ["query"] = query,
                    ["variables"] = new JObject { ["query"] = searchTerm }
                };

                var request = new HttpRequest(GraphQlEndpoint)
                {
                    AllowAutoRedirect = true,
                    RequestTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
                };
                request.Headers.Accept = "application/json";
                request.Headers.ContentType = "application/json";

                // Token may or may not include "Bearer " prefix
                var authValue = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? token
                    : $"Bearer {token}";
                request.Headers.Add("Authorization", authValue);

                request.Method = HttpMethod.Post;
                request.ContentData = Encoding.UTF8.GetBytes(body.ToString());
                var httpResponse = _httpClient.Execute(request);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.Warn("Hardcover API rate limit exceeded");
                    return new List<JToken>();
                }

                if (httpResponse.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Hardcover API returned {0}", httpResponse.StatusCode);
                    return new List<JToken>();
                }

                var json = JObject.Parse(httpResponse.Content);
                var errors = json["errors"] as JArray;
                if (errors != null && errors.Any())
                {
                    _logger.Warn("Hardcover GraphQL errors: {0}", errors.ToString());
                    return new List<JToken>();
                }

                var resultsStr = json["data"]?["search"]?["results"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(resultsStr))
                {
                    return new List<JToken>();
                }

                // Results come back as a JSON string that needs to be parsed
                var resultsJson = JObject.Parse(resultsStr);
                var hits = resultsJson["hits"] as JArray;
                return hits?.ToList() ?? new List<JToken>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Hardcover search failed");
                return new List<JToken>();
            }
        }

        private MetadataSearchResult ParseBookSearchHit(JToken hit)
        {
            try
            {
                var document = hit["document"] ?? hit;

                var id = document["id"]?.Value<int>();
                var title = document["title"]?.Value<string>();
                var subtitle = document["subtitle"]?.Value<string>();
                var slug = document["slug"]?.Value<string>();
                var description = document["description"]?.Value<string>();
                var pages = document["pages"]?.Value<int>();
                var releaseYear = document["release_year"]?.Value<int>();
                var rating = document["rating"]?.Value<double>();

                var authorNames = document["author_names"]?.Select(a => a.Value<string>()).ToList()
                    ?? new List<string>();

                var isbns = document["isbns"]?.Select(i => i.Value<string>()).ToList()
                    ?? new List<string>();

                var genres = document["genres"]?.Select(g => g.Value<string>()).Take(5).ToList()
                    ?? new List<string>();

                // Extract cover URL from image object
                string coverUrl = null;
                var image = document["image"];
                if (image != null)
                {
                    coverUrl = image["url"]?.Value<string>();
                }

                // Pick best ISBN
                string isbn10 = null;
                string isbn13 = null;
                foreach (var isbn in isbns)
                {
                    if (isbn?.Length == 13 && isbn13 == null)
                    {
                        isbn13 = isbn;
                    }
                    else if (isbn?.Length == 10 && isbn10 == null)
                    {
                        isbn10 = isbn;
                    }
                }

                var externalIds = new Dictionary<string, string>();
                if (id.HasValue)
                {
                    externalIds["hardcover"] = id.Value.ToString();
                }

                if (slug != null)
                {
                    externalIds["hardcover_slug"] = slug;
                }

                // Series info
                var seriesNames = document["series_names"]?.Select(s => s.Value<string>()).ToList();
                var seriesPosition = document["featured_series_position"]?.Value<string>();

                return new MetadataSearchResult
                {
                    ForeignId = slug ?? id?.ToString(),
                    ProviderKey = Key,
                    Title = title,
                    Subtitle = subtitle,
                    Authors = authorNames,
                    Isbn = isbn10,
                    Isbn13 = isbn13,
                    Description = description,
                    CoverUrl = coverUrl,
                    PageCount = pages,
                    PublishedYear = releaseYear,
                    Genres = genres,
                    ExternalIds = externalIds
                };
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse Hardcover book search hit");
                return null;
            }
        }

        private MetadataSearchResult ParseAuthorSearchHit(JToken hit)
        {
            try
            {
                var document = hit["document"] ?? hit;

                var id = document["id"]?.Value<int>();
                var name = document["name"]?.Value<string>();
                var slug = document["slug"]?.Value<string>();
                var booksCount = document["books_count"]?.Value<int>();

                string imageUrl = null;
                var image = document["image"];
                if (image != null)
                {
                    imageUrl = image["url"]?.Value<string>();
                }

                var externalIds = new Dictionary<string, string>();
                if (id.HasValue)
                {
                    externalIds["hardcover_author"] = id.Value.ToString();
                }

                if (slug != null)
                {
                    externalIds["hardcover_author_slug"] = slug;
                }

                var books = document["books"]?.Select(b => b.Value<string>()).ToList()
                    ?? new List<string>();

                return new MetadataSearchResult
                {
                    ForeignId = slug ?? id?.ToString(),
                    ProviderKey = Key,
                    Title = name,
                    Authors = new List<string> { name },
                    CoverUrl = imageUrl,
                    ExternalIds = externalIds
                };
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse Hardcover author search hit");
                return null;
            }
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
