using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    // Turns a pasted Goodreads link (or the "goodreads:<id>" form the search box placeholder has
    // always advertised) into the edition:/work:/author: search prefixes the lookup already
    // understands, so a book can be added straight from its Goodreads page.
    public static class GoodreadsLinkParser
    {
        private static readonly Regex LinkRegex = new Regex(
            @"^(?:https?://)?(?:www\.|m\.)?goodreads\.com/(?<type>book/show|work/(?:editions|show|quotes|best_book)|author/(?:show|list))/(?<id>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PrefixRegex = new Regex(
            @"^goodreads\s*:\s*(?<id>\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Normalize(string term)
        {
            if (term.IsNullOrWhiteSpace())
            {
                return term;
            }

            var trimmed = term.Trim().Trim('<', '>', '"', '\'');

            var prefix = PrefixRegex.Match(trimmed);
            if (prefix.Success)
            {
                // Goodreads numbers in /book/show/ links are edition ids.
                return $"edition:{prefix.Groups["id"].Value}";
            }

            var link = LinkRegex.Match(trimmed);
            if (!link.Success)
            {
                return term;
            }

            var id = link.Groups["id"].Value;
            var type = link.Groups["type"].Value.ToLowerInvariant();

            if (type.StartsWith("book"))
            {
                return $"edition:{id}";
            }

            if (type.StartsWith("work"))
            {
                return $"work:{id}";
            }

            return $"author:{id}";
        }
    }
}
