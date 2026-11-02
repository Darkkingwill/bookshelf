using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Wikipedia has a lead photo for most authors who wrote more than a handful of books, including pen names that redirect to
    // the person ("Richard Bachman"). Names collide and pages disagree about who is who, so a page is used only when its
    // title is the name and its text names one of the books we hold.
    public class WikipediaAuthorPictureProvider : IAuthorPictureProvider
    {
        private const string ApiUrl = "https://en.wikipedia.org/w/api.php";

        private static readonly Regex Parenthetical = new Regex(@"\s*\(.*?\)\s*$", RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public WikipediaAuthorPictureProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string Name => "Wikipedia";

        public int Order => 2;

        public string FindPictureUrl(AuthorPictureSearch search)
        {
            // Wikipedia disambiguates people with a trailing word, so ask for the bare name and the usual forms in one go.
            var titles = string.Join("|", new[] { "", " (author)", " (writer)", " (novelist)" }.Select(s => search.Name.Trim() + s));

            var lookup = Query($"action=query&redirects=1&titles={Uri.EscapeDataString(titles)}&prop=pageimages%7Cpageprops&piprop=thumbnail&pithumbsize=600&ppprop=disambiguation");

            if (!(lookup?["query"]?["pages"] is JObject pages))
            {
                return null;
            }

            var redirects = (lookup["query"]["redirects"] as JArray) ?? new JArray();

            foreach (var page in pages.Properties().Select(p => p.Value))
            {
                var title = page["title"]?.Value<string>();
                var thumbnail = page["thumbnail"]?["source"]?.Value<string>();

                if (page["missing"] != null ||
                    page["pageprops"]?["disambiguation"] != null ||
                    title.IsNullOrWhiteSpace() ||
                    thumbnail.IsNullOrWhiteSpace() ||
                    !IsPageAbout(search.Name, title, redirects) ||
                    !PageMentionsABook(title, search))
                {
                    continue;
                }

                return thumbnail;
            }

            return null;
        }

        private JObject Query(string parameters)
        {
            var request = new HttpRequest($"{ApiUrl}?format=json&{parameters}")
            {
                AllowAutoRedirect = true,
                RequestTimeout = TimeSpan.FromSeconds(20),
                RateLimit = TimeSpan.FromMilliseconds(300)
            };
            request.Headers.Accept = "application/json";

            var response = _httpClient.Get(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.Debug("Wikipedia returned {0} for {1}", response.StatusCode, parameters);
                return null;
            }

            return JObject.Parse(response.Content);
        }

        // The page is the person when it is titled with their name ("Greg Bear", "Jack Carr (author)") or when it is where
        // a page titled that way redirects to ("Robert Galbraith (author)" is J. K. Rowling's page).
        private static bool IsPageAbout(string name, string title, JArray redirects)
        {
            if (CoverMatcher.SameAuthor(name, Parenthetical.Replace(title, string.Empty)))
            {
                return true;
            }

            return redirects.Any(r => r["to"]?.Value<string>() == title &&
                                      CoverMatcher.SameAuthor(name, Parenthetical.Replace(r["from"]?.Value<string>() ?? string.Empty, string.Empty)));
        }

        private bool PageMentionsABook(string title, AuthorPictureSearch search)
        {
            var page = Query($"action=query&prop=extracts&explaintext=1&exlimit=1&titles={Uri.EscapeDataString(title)}");
            var text = (page?["query"]?["pages"] as JObject)?.Properties().Select(p => p.Value["extract"]?.Value<string>()).FirstOrDefault();

            return text.IsNotNullOrWhiteSpace() && CoverMatcher.MentionsAnyTitle(text, search.BookTitles);
        }
    }
}
