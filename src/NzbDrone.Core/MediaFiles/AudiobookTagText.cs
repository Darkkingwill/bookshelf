using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.History;

namespace NzbDrone.Core.MediaFiles
{
    // The text that goes into an audiobook's tags, shared by the M4B converter and the tag writer so a book
    // is described the same way whichever of them touches it.
    public static class AudiobookTagText
    {
        private const int MinimumDescriptionLength = 40;
        private const int MaximumDescriptionLength = 4000;

        private static readonly Regex NarratorRegex = new Regex(@"narrated by\s+(?<narrator>[^\[\(]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        // Release titles look like "Title by Author, narrated by Jane Doe [ENG / M4B]".
        public static string ParseNarrator(string releaseTitle)
        {
            if (releaseTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var match = NarratorRegex.Match(releaseTitle);

            return match.Success ? match.Groups["narrator"].Value.Trim() : null;
        }

        // The narrator named by the most recent release the book was grabbed as, if any named one.
        public static string NarratorFromGrabs(IEnumerable<EntityHistory> grabs)
        {
            return grabs?
                .OrderByDescending(h => h.Date)
                .Select(h => ParseNarrator(h.SourceTitle))
                .FirstOrDefault(n => n.IsNotNullOrWhiteSpace());
        }

        // Goodreads descriptions carry HTML and line breaks, and some editions only have a catalogue stub
        // ("262 pages ; 18 cm"). Keep a real description, as plain text on one line, and drop the rest.
        public static string CleanDescription(string overview)
        {
            if (overview.IsNullOrWhiteSpace())
            {
                return null;
            }

            var text = HtmlTagRegex.Replace(overview, " ");
            text = WebUtility.HtmlDecode(text);
            text = WhitespaceRegex.Replace(text, " ").Trim();

            return text.Length >= MinimumDescriptionLength ? text.Substring(0, System.Math.Min(text.Length, MaximumDescriptionLength)) : null;
        }
    }
}
