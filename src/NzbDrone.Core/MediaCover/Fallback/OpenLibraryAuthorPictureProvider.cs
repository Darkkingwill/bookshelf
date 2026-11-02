using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Open Library keeps a photo for many authors of print books. Authors share names ("Jack Carr" is also a Canadian
    // geographer), so a candidate counts only when its works include one of the books we hold.
    public class OpenLibraryAuthorPictureProvider : IAuthorPictureProvider
    {
        private const string SearchUrl = "https://openlibrary.org/search/authors.json";
        private const string AuthorUrl = "https://openlibrary.org/authors";

        // default=false makes a missing photo a 404 instead of a blank placeholder image.
        private const string PhotoUrl = "https://covers.openlibrary.org/a/olid/{0}-L.jpg?default=false";

        private const int MaxCandidates = 3;

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public OpenLibraryAuthorPictureProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string Name => "Open Library";

        public int Order => 1;

        public string FindPictureUrl(AuthorPictureSearch search)
        {
            var response = _httpClient.Get(CreateRequest($"{SearchUrl}?q={Uri.EscapeDataString(search.Name)}&limit=10"));

            if (response.StatusCode != HttpStatusCode.OK || !(JObject.Parse(response.Content)["docs"] is JArray docs))
            {
                return null;
            }

            var candidates = docs.Where(doc => IsSameAuthor(search.Name, doc)).Take(MaxCandidates);

            foreach (var doc in candidates)
            {
                var key = doc["key"]?.Value<string>();

                if (key.IsNullOrWhiteSpace() || !WroteOneOf(key, search) || !HasPhoto(key))
                {
                    continue;
                }

                return string.Format(PhotoUrl, key);
            }

            return null;
        }

        private static bool IsSameAuthor(string name, JToken doc)
        {
            var names = new[] { doc["name"]?.Value<string>() }
                .Concat((doc["alternate_names"] as JArray)?.Select(n => n.Value<string>()) ?? Enumerable.Empty<string>());

            return names.Any(n => CoverMatcher.SameAuthor(name, n));
        }

        private bool WroteOneOf(string authorKey, AuthorPictureSearch search)
        {
            var response = _httpClient.Get(CreateRequest($"{AuthorUrl}/{Uri.EscapeDataString(authorKey)}/works.json?limit=200"));

            if (response.StatusCode != HttpStatusCode.OK || !(JObject.Parse(response.Content)["entries"] is JArray entries))
            {
                return false;
            }

            return CoverMatcher.AnyTitleMatches(search.BookTitles, entries.Select(e => e["title"]?.Value<string>()));
        }

        private bool HasPhoto(string authorKey)
        {
            var request = CreateRequest(string.Format(PhotoUrl, authorKey));

            // A 404 is the answer "no photo", not a failure.
            request.SuppressHttpError = true;

            var response = _httpClient.Head(request);

            _logger.Debug("Open Library photo for {0} returned {1}", authorKey, response.StatusCode);

            return response.StatusCode == HttpStatusCode.OK;
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
    }
}
