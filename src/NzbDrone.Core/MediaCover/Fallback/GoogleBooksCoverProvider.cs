using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Google Books refuses unauthenticated requests (HTTP 429) after a handful of calls, so this provider only runs when
    // an API key is set under Settings > Metadata > Google Books, the same key the Google metadata provider uses.
    public class GoogleBooksCoverProvider : ICoverFallbackProvider
    {
        private const string BaseUrl = "https://www.googleapis.com/books/v1/volumes";
        private const string ProviderKey = "googlebooks";

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public GoogleBooksCoverProvider(IHttpClient httpClient, IConfigService configService, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        public string Name => "Google Books";

        public int Order => 2;

        public string FindCoverUrl(CoverSearch search)
        {
            var apiKey = GetApiKey();

            if (apiKey.IsNullOrWhiteSpace() || search.Title.IsNullOrWhiteSpace() || search.Author.IsNullOrWhiteSpace())
            {
                return null;
            }

            var query = $"intitle:\"{search.Title}\" inauthor:\"{search.Author}\"";
            var url = $"{BaseUrl}?q={Uri.EscapeDataString(query)}&maxResults=10&key={Uri.EscapeDataString(apiKey)}";

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
                _logger.Debug("Google Books cover search for {0} returned {1}", search.Title, response.StatusCode);
                return null;
            }

            if (!(JObject.Parse(response.Content)["items"] is JArray items))
            {
                return null;
            }

            foreach (var item in items)
            {
                var volume = item["volumeInfo"];
                var authors = (volume?["authors"] as JArray)?.Select(a => a.Value<string>());
                var image = volume?["imageLinks"]?["thumbnail"]?.Value<string>();

                if (image.IsNullOrWhiteSpace() ||
                    !CoverMatcher.AuthorsMatch(search.Author, authors) ||
                    !CoverMatcher.TitlesMatch(search.Title, volume["title"]?.Value<string>(), volume["subtitle"]?.Value<string>()))
                {
                    continue;
                }

                // Google serves http thumbnails with a page-curl effect and at 128 px (zoom=1); ask for the plain https image
                // at the next size up.
                return image.Replace("http://", "https://").Replace("&edge=curl", string.Empty).Replace("zoom=1", "zoom=2");
            }

            return null;
        }

        private string GetApiKey()
        {
            var config = _configService.GetMetadataProviderConfigs()
                .FirstOrDefault(c => ProviderKey.Equals(c.Key, StringComparison.OrdinalIgnoreCase));

            return config?.Settings != null && config.Settings.TryGetValue("apiKey", out var key) ? key : null;
        }
    }
}
