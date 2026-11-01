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
                      "&limit=10&fields=title,author_name,cover_i";

            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = true,
                RequestTimeout = TimeSpan.FromSeconds(20),
                RateLimit = TimeSpan.FromMilliseconds(300)
            };
            request.Headers.Accept = "application/json";

            var response = _httpClient.Get(request);

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

                if (CoverMatcher.AuthorsMatch(search.Author, authors) && CoverMatcher.TitlesMatch(search.Title, doc["title"]?.Value<string>()))
                {
                    return string.Format(CoverUrl, coverId);
                }
            }

            return null;
        }
    }
}
