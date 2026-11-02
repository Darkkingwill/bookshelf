using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Open Library needs no key and covers many books Audible does not (older and print-only titles).
    public class OpenLibraryCoverProvider : ICoverFallbackProvider
    {
        private const string SearchUrl = "https://openlibrary.org/search.json";
        private const string BookUrl = "https://openlibrary.org/books";
        private const string CoverUrl = "https://covers.openlibrary.org/b/id/{0}-L.jpg";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public OpenLibraryCoverProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string Name => "Open Library";

        public int Order => 1;

        public string FindCoverUrl(CoverSearch search)
        {
            if (search.Title.IsNullOrWhiteSpace() || search.Author.IsNullOrWhiteSpace())
            {
                return null;
            }

            var url = $"{SearchUrl}?title={Uri.EscapeDataString(search.Title)}&author={Uri.EscapeDataString(search.Author)}" +
                      "&limit=10&fields=title,author_name,cover_i,cover_edition_key,language";

            var response = _httpClient.Get(CreateRequest(url));

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.Debug("Open Library cover search for {0} returned {1}", search.Title, response.StatusCode);
                return null;
            }

            if (!(JObject.Parse(response.Content)["docs"] is JArray docs))
            {
                return null;
            }

            foreach (var doc in docs)
            {
                var coverId = doc["cover_i"]?.Value<long?>();

                if (coverId == null)
                {
                    continue;
                }

                var authors = (doc["author_name"] as JArray)?.Select(a => a.Value<string>());

                if (CoverMatcher.AuthorsMatch(search.Author, authors) &&
                    CoverMatcher.TitlesMatch(search.Title, doc["title"]?.Value<string>()) &&
                    HasEnglishEdition(doc["language"] as JArray) &&
                    CoverEditionIsEnglishOrUnknown(doc["cover_edition_key"]?.Value<string>()))
                {
                    return string.Format(CoverUrl, coverId);
                }
            }

            return null;
        }

        private static HttpRequest CreateRequest(string url)
        {
            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = true,
                RequestTimeout = TimeSpan.FromSeconds(20),
                RateLimit = TimeSpan.FromMilliseconds(300)
            };
            request.Headers.Accept = "application/json";

            return request;
        }

        // A work lists every language it has been published in; one without English is not the book we hold.
        private static bool HasEnglishEdition(JArray languages)
        {
            return languages == null || languages.Count == 0 || languages.Any(l => string.Equals(l.Value<string>(), "eng", StringComparison.OrdinalIgnoreCase));
        }

        // A work's cover is the cover of one of its editions, which can be a translation (the Turkish edition of an English
        // novel shows a Turkish title), so look at the language of that very edition. Unknown counts as fine.
        private bool CoverEditionIsEnglishOrUnknown(string editionKey)
        {
            if (editionKey.IsNullOrWhiteSpace())
            {
                return true;
            }

            var response = _httpClient.Get(CreateRequest($"{BookUrl}/{Uri.EscapeDataString(editionKey)}.json"));

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return true;
            }

            var languages = (JObject.Parse(response.Content)["languages"] as JArray)?.Select(l => l["key"]?.Value<string>()).Where(k => k != null).ToList();

            return languages == null || languages.Count == 0 || languages.Any(k => k.EndsWith("/eng", StringComparison.OrdinalIgnoreCase));
        }
    }
}
