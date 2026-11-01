using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Audible's public catalog needs no key and holds the real audiobook cover (a 500x500 square), which is usually the
    // picture a listener expects, so it is tried first.
    public class AudibleCoverProvider : ICoverFallbackProvider
    {
        private const string BaseUrl = "https://api.audible.com/1.0/catalog/products";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public AudibleCoverProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string Name => "Audible";

        public int Order => 0;

        public string FindCoverUrl(CoverSearch search)
        {
            if (search.Title.IsNullOrWhiteSpace() || search.Author.IsNullOrWhiteSpace())
            {
                return null;
            }

            var url = $"{BaseUrl}?title={Uri.EscapeDataString(search.Title)}&author={Uri.EscapeDataString(search.Author)}" +
                      "&num_results=10&response_groups=contributors,media,product_attrs,product_desc";

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
                _logger.Debug("Audible cover search for {0} returned {1}", search.Title, response.StatusCode);
                return null;
            }

            if (!(JObject.Parse(response.Content)["products"] is JArray products))
            {
                return null;
            }

            foreach (var product in products)
            {
                // The same title is listed in many languages; an English book must not get a German cover.
                if (!string.Equals(product["language"]?.Value<string>(), "english", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var authors = (product["authors"] as JArray)?.Select(a => a["name"]?.Value<string>());

                if (!CoverMatcher.AuthorsMatch(search.Author, authors) ||
                    !CoverMatcher.TitlesMatch(search.Title, product["title"]?.Value<string>(), product["subtitle"]?.Value<string>()))
                {
                    continue;
                }

                var images = product["product_images"] as JObject;
                var image = images?["500"]?.Value<string>() ?? images?.Properties().Select(p => p.Value.Value<string>()).LastOrDefault();

                if (image.IsNotNullOrWhiteSpace())
                {
                    return image;
                }
            }

            return null;
        }
    }
}
