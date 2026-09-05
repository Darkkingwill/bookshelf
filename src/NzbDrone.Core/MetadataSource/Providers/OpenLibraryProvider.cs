using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.Providers
{
    public class OpenLibraryProvider : IMetadataProvider
    {
        private const string BaseUrl = "https://openlibrary.org";
        private const string CoversUrl = "https://covers.openlibrary.org";
        private const int TimeoutSeconds = 10;

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public string Key => "openlibrary";
        public string DisplayName => "Open Library";
        public bool RequiresAuth => false;

        public OpenLibraryProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<MetadataSearchResult> SearchBooks(string query)
        {
            return SearchInternal($"search.json?q={Uri.EscapeDataString(query)}&limit=20&fields=key,title,subtitle,author_name,author_key,isbn,cover_i,first_publish_year,publisher,number_of_pages_median,language,subject");
        }

        public List<MetadataSearchResult> SearchAuthors(string query)
        {
            try
            {
                var request = BuildRequest($"search/authors.json?q={Uri.EscapeDataString(query)}&limit=10");
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new List<MetadataSearchResult>();
                }

                var json = JObject.Parse(response.Content);
                var docs = json["docs"] as JArray;
                if (docs == null || !docs.Any())
                {
                    return new List<MetadataSearchResult>();
                }

                var results = new List<MetadataSearchResult>();
                foreach (var doc in docs)
                {
                    var authorKey = doc["key"]?.Value<string>();
                    var name = doc["name"]?.Value<string>();

                    if (authorKey == null || name == null)
                    {
                        continue;
                    }

                    results.Add(new MetadataSearchResult
                    {
                        ForeignId = authorKey,
                        ProviderKey = Key,
                        Title = name,
                        Authors = new List<string> { name },
                        ExternalIds = new Dictionary<string, string>
                        {
                            { "openlibrary_author", authorKey }
                        }
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Open Library author search failed");
                return new List<MetadataSearchResult>();
            }
        }

        public MetadataSearchResult SearchByIsbn(string isbn)
        {
            try
            {
                var request = BuildRequest($"isbn/{isbn}.json");
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }

                var json = JObject.Parse(response.Content);
                return ParseEdition(json);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Open Library ISBN lookup failed for {0}", isbn);
                return null;
            }
        }

        public MetadataSearchResult SearchByAsin(string asin)
        {
            // Open Library doesn't natively index by ASIN
            var results = SearchInternal($"search.json?q={Uri.EscapeDataString(asin)}&limit=1");
            return results.FirstOrDefault();
        }

        public MetadataAuthorResult GetAuthorInfo(string foreignId)
        {
            try
            {
                // foreignId can be an OL author key like "OL34184A" or a name
                string authorKey;
                string authorName;

                if (foreignId.StartsWith("OL") && foreignId.EndsWith("A"))
                {
                    authorKey = foreignId;

                    // Fetch author details
                    var authorRequest = BuildRequest($"authors/{authorKey}.json");
                    var authorResponse = _httpClient.Get(authorRequest);

                    if (authorResponse.StatusCode != HttpStatusCode.OK)
                    {
                        return null;
                    }

                    var authorJson = JObject.Parse(authorResponse.Content);
                    authorName = authorJson["name"]?.Value<string>() ?? foreignId;
                }
                else
                {
                    // Search for author by name, take the first match
                    var searchResults = SearchAuthors(foreignId);
                    if (!searchResults.Any())
                    {
                        return null;
                    }

                    authorKey = searchResults.First().ForeignId;
                    authorName = searchResults.First().Title;
                }

                // Get their works
                var worksRequest = BuildRequest($"authors/{authorKey}/works.json?limit=50");
                var worksResponse = _httpClient.Get(worksRequest);

                var works = new List<MetadataSearchResult>();
                if (worksResponse.StatusCode == HttpStatusCode.OK)
                {
                    var worksJson = JObject.Parse(worksResponse.Content);
                    var entries = worksJson["entries"] as JArray;

                    if (entries != null)
                    {
                        foreach (var entry in entries)
                        {
                            var workKey = entry["key"]?.Value<string>();
                            var title = entry["title"]?.Value<string>();

                            if (workKey == null || title == null)
                            {
                                continue;
                            }

                            // Extract work ID from key like "/works/OL45883W"
                            var workId = workKey.Split('/').Last();
                            var coverId = entry["covers"]?.FirstOrDefault()?.Value<int>();

                            works.Add(new MetadataSearchResult
                            {
                                ForeignId = workId,
                                ProviderKey = Key,
                                Title = title,
                                Authors = new List<string> { authorName },
                                CoverUrl = coverId.HasValue
                                    ? $"{CoversUrl}/b/id/{coverId.Value}-L.jpg"
                                    : null,
                                ExternalIds = new Dictionary<string, string>
                                {
                                    { "openlibrary_work", workId }
                                }
                            });
                        }
                    }
                }

                return new MetadataAuthorResult
                {
                    ForeignId = authorKey,
                    ProviderKey = Key,
                    Name = authorName,
                    SortName = ToSortName(authorName),
                    Works = works,
                    ExternalIds = new Dictionary<string, string>
                    {
                        { "openlibrary_author", authorKey }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get author info from Open Library for {0}", foreignId);
                return null;
            }
        }

        public MetadataBookResult GetBookInfo(string foreignId)
        {
            try
            {
                var request = BuildRequest($"works/{foreignId}.json");
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }

                var json = JObject.Parse(response.Content);
                var title = json["title"]?.Value<string>();

                // Get author names
                var authors = new List<string>();
                var authorRefs = json["authors"] as JArray;
                var authorForeignId = "";
                if (authorRefs != null)
                {
                    foreach (var authorRef in authorRefs)
                    {
                        var authorKey = (authorRef["author"]?["key"] ?? authorRef["key"])?.Value<string>();
                        if (authorKey != null)
                        {
                            if (string.IsNullOrEmpty(authorForeignId))
                            {
                                authorForeignId = authorKey.Split('/').Last();
                            }

                            try
                            {
                                var authorReq = BuildRequest($"{authorKey.TrimStart('/')}.json");
                                var authorResp = _httpClient.Get(authorReq);
                                if (authorResp.StatusCode == HttpStatusCode.OK)
                                {
                                    var authorJson = JObject.Parse(authorResp.Content);
                                    var name = authorJson["name"]?.Value<string>();
                                    if (name != null)
                                    {
                                        authors.Add(name);
                                    }
                                }
                            }
                            catch
                            {
                                // Skip author name resolution failure
                            }
                        }
                    }
                }

                var coverId = json["covers"]?.FirstOrDefault()?.Value<int>();
                var description = json["description"]?.Type == JTokenType.String
                    ? json["description"].Value<string>()
                    : json["description"]?["value"]?.Value<string>();

                // Get editions
                var editions = new List<MetadataEditionResult>();
                try
                {
                    var editionsReq = BuildRequest($"works/{foreignId}/editions.json?limit=20");
                    var editionsResp = _httpClient.Get(editionsReq);
                    if (editionsResp.StatusCode == HttpStatusCode.OK)
                    {
                        var editionsJson = JObject.Parse(editionsResp.Content);
                        var entries = editionsJson["entries"] as JArray;
                        if (entries != null)
                        {
                            foreach (var entry in entries)
                            {
                                var editionKey = entry["key"]?.Value<string>()?.Split('/').Last();
                                var editionTitle = entry["title"]?.Value<string>() ?? title;
                                var isbns10 = entry["isbn_10"] as JArray;
                                var isbns13 = entry["isbn_13"] as JArray;
                                var edCoverId = entry["covers"]?.FirstOrDefault()?.Value<int>();
                                var pages = entry["number_of_pages"]?.Value<int>();
                                var publisher = (entry["publishers"] as JArray)?.FirstOrDefault()?.Value<string>();
                                var publishDate = entry["publish_date"]?.Value<string>();

                                int? pubYear = null;
                                if (publishDate != null && publishDate.Length >= 4)
                                {
                                    // Try to extract year from various formats
                                    var yearStr = publishDate.Length == 4 ? publishDate : publishDate.Substring(publishDate.Length - 4);
                                    if (int.TryParse(yearStr, out var y) && y > 1000 && y < 3000)
                                    {
                                        pubYear = y;
                                    }
                                }

                                editions.Add(new MetadataEditionResult
                                {
                                    ForeignId = editionKey,
                                    Title = editionTitle,
                                    Isbn = isbns10?.FirstOrDefault()?.Value<string>(),
                                    Isbn13 = isbns13?.FirstOrDefault()?.Value<string>(),
                                    PageCount = pages,
                                    Publisher = publisher,
                                    CoverUrl = edCoverId.HasValue
                                        ? $"{CoversUrl}/b/id/{edCoverId.Value}-L.jpg"
                                        : null,
                                    PublishedYear = pubYear
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to get editions for work {0}", foreignId);
                }

                return new MetadataBookResult
                {
                    ForeignId = foreignId,
                    ProviderKey = Key,
                    Title = title,
                    Authors = authors,
                    AuthorForeignId = authorForeignId,
                    Description = description,
                    CoverUrl = coverId.HasValue
                        ? $"{CoversUrl}/b/id/{coverId.Value}-L.jpg"
                        : null,
                    Editions = editions,
                    ExternalIds = new Dictionary<string, string>
                    {
                        { "openlibrary_work", foreignId }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get book info from Open Library for {0}", foreignId);
                return null;
            }
        }

        public bool TestConnection()
        {
            try
            {
                // Just verify the API is reachable and responds with 200.
                // Empty results still mean the service is working.
                var request = BuildRequest("search.json?q=test&limit=1");
                var response = _httpClient.Get(request);
                return response.StatusCode == HttpStatusCode.OK ||
                       response.StatusCode == HttpStatusCode.TooManyRequests;
            }
            catch
            {
                return false;
            }
        }

        private List<MetadataSearchResult> SearchInternal(string path)
        {
            try
            {
                var request = BuildRequest(path);
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Open Library returned {0}", response.StatusCode);
                    return new List<MetadataSearchResult>();
                }

                var json = JObject.Parse(response.Content);
                var docs = json["docs"] as JArray;

                if (docs == null || !docs.Any())
                {
                    return new List<MetadataSearchResult>();
                }

                return docs
                    .Select(ParseSearchDoc)
                    .Where(r => r != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Open Library search failed");
                return new List<MetadataSearchResult>();
            }
        }

        private MetadataSearchResult ParseSearchDoc(JToken doc)
        {
            try
            {
                var workKey = doc["key"]?.Value<string>();
                var title = doc["title"]?.Value<string>();

                if (workKey == null || title == null)
                {
                    return null;
                }

                var workId = workKey.Split('/').Last();
                var authors = doc["author_name"]?.Select(a => a.Value<string>()).ToList()
                    ?? new List<string>();
                var isbns = doc["isbn"] as JArray;
                var coverId = doc["cover_i"]?.Value<int>();
                var publishYear = doc["first_publish_year"]?.Value<int>();
                var publishers = doc["publisher"] as JArray;
                var pageCount = doc["number_of_pages_median"]?.Value<int>();
                var subjects = doc["subject"]?.Select(s => s.Value<string>()).Take(5).ToList()
                    ?? new List<string>();

                // Pick best ISBN (prefer 13-digit)
                string isbn10 = null;
                string isbn13 = null;
                if (isbns != null)
                {
                    foreach (var isbn in isbns.Select(i => i.Value<string>()))
                    {
                        if (isbn?.Length == 13 && isbn13 == null)
                        {
                            isbn13 = isbn;
                        }
                        else if (isbn?.Length == 10 && isbn10 == null)
                        {
                            isbn10 = isbn;
                        }

                        if (isbn10 != null && isbn13 != null)
                        {
                            break;
                        }
                    }
                }

                var externalIds = new Dictionary<string, string>
                {
                    { "openlibrary_work", workId }
                };

                var authorKeys = doc["author_key"] as JArray;
                if (authorKeys != null && authorKeys.Any())
                {
                    externalIds["openlibrary_author"] = authorKeys.First().Value<string>();
                }

                return new MetadataSearchResult
                {
                    ForeignId = workId,
                    ProviderKey = Key,
                    Title = title,
                    Authors = authors,
                    Isbn = isbn10,
                    Isbn13 = isbn13,
                    CoverUrl = coverId.HasValue
                        ? $"{CoversUrl}/b/id/{coverId.Value}-L.jpg"
                        : null,
                    PageCount = pageCount,
                    PublishedYear = publishYear,
                    Publisher = publishers?.FirstOrDefault()?.Value<string>(),
                    Genres = subjects,
                    ExternalIds = externalIds
                };
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse Open Library search doc");
                return null;
            }
        }

        private MetadataSearchResult ParseEdition(JObject json)
        {
            var title = json["title"]?.Value<string>();
            var editionKey = json["key"]?.Value<string>()?.Split('/').Last();
            var isbns10 = json["isbn_10"] as JArray;
            var isbns13 = json["isbn_13"] as JArray;

            // Get work key
            var workKeys = json["works"] as JArray;
            var workId = workKeys?.FirstOrDefault()?["key"]?.Value<string>()?.Split('/').Last();

            var externalIds = new Dictionary<string, string>();
            if (workId != null)
            {
                externalIds["openlibrary_work"] = workId;
            }

            if (editionKey != null)
            {
                externalIds["openlibrary_edition"] = editionKey;
            }

            var coverId = json["covers"]?.FirstOrDefault()?.Value<int>();
            var pages = json["number_of_pages"]?.Value<int>();
            var publishers = json["publishers"] as JArray;

            return new MetadataSearchResult
            {
                ForeignId = workId ?? editionKey,
                ProviderKey = Key,
                Title = title,
                Isbn = isbns10?.FirstOrDefault()?.Value<string>(),
                Isbn13 = isbns13?.FirstOrDefault()?.Value<string>(),
                CoverUrl = coverId.HasValue
                    ? $"{CoversUrl}/b/id/{coverId.Value}-L.jpg"
                    : null,
                PageCount = pages,
                Publisher = publishers?.FirstOrDefault()?.Value<string>(),
                ExternalIds = externalIds
            };
        }

        private HttpRequest BuildRequest(string path)
        {
            var url = path.StartsWith("http") ? path : $"{BaseUrl}/{path}";
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
