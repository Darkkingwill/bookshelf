using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.MetadataSource.Providers;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Decides whether a search hit from a cover provider is really the book we are looking for. A wrong cover is worse
    // than no cover, so the title threshold is strict: "The Perfect Run" must not match "The Perfect Run 2".
    public static class CoverMatcher
    {
        private const double TitleThreshold = 0.92;
        private const double AuthorThreshold = 0.8;

        private static readonly Regex Bracketed = new Regex(@"\(.*?\)|\[.*?\]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

        // "1% Lifesteal, Book One" is listed as "1% Lifesteal"; only the first volume loses its number this way.
        private static readonly Regex FirstVolumeSuffix = new Regex(@"[\s,:\-]+(?:book|volume|vol|part)\.?\s*(?:one|1)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool TitlesMatch(string localTitle, string candidateTitle, string candidateSubtitle = null)
        {
            var local = TitleForms(localTitle).ToList();
            var candidate = TitleForms(candidateTitle).Concat(TitleForms(candidateSubtitle)).ToList();

            return local.Any(l => candidate.Any(c => FuzzyMatcher.Similarity(l, c) >= TitleThreshold));
        }

        public static bool AuthorsMatch(string localAuthor, IEnumerable<string> candidateAuthors)
        {
            var local = FuzzyMatcher.Normalize(localAuthor);

            if (local.Length == 0 || candidateAuthors == null)
            {
                return false;
            }

            return candidateAuthors.Any(candidate =>
            {
                var other = FuzzyMatcher.Normalize(candidate);

                return other.Length > 0 &&
                       (FuzzyMatcher.Similarity(local, other) >= AuthorThreshold || SameNameInAnotherForm(local, other));
            });
        }

        // The titles worth putting in a provider's search box: the title without "(1)" or "[Unabridged]" noise, then without a
        // trailing "Book One". Provider searches match words exactly, so "In Fury Born (1)" finds nothing but "In Fury Born"
        // does.
        public static IEnumerable<string> SearchTitles(string title)
        {
            var cleaned = CleanTitle(title);

            if (cleaned.Length == 0)
            {
                yield break;
            }

            yield return cleaned;

            var firstVolume = FirstVolumeSuffix.Replace(cleaned, string.Empty).Trim();

            if (firstVolume.Length > 0 && firstVolume != cleaned)
            {
                yield return firstVolume;
            }
        }

        private static string CleanTitle(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? string.Empty : Spaces.Replace(Bracketed.Replace(title, " "), " ").Trim();
        }

        // Audible often puts the real title in the subtitle ("Dark Tower I" / "The Gunslinger"), and a subtitle after a
        // colon is optional, so compare the title with and without it. A first book is often listed without its number.
        private static IEnumerable<string> TitleForms(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                yield break;
            }

            var cleaned = CleanTitle(title);
            var forms = new List<string>(SearchTitles(title));
            var colon = cleaned.IndexOf(':');

            if (colon > 0)
            {
                forms.Add(cleaned.Substring(0, colon));
                forms.Add(cleaned.Substring(colon + 1));
            }

            foreach (var form in forms)
            {
                var normalized = FuzzyMatcher.Normalize(form);

                if (normalized.Length == 0)
                {
                    continue;
                }

                yield return normalized;

                if (normalized.EndsWith(" 1", StringComparison.Ordinal) && normalized.Length > 4)
                {
                    yield return normalized.Substring(0, normalized.Length - 2);
                }
            }
        }

        // "Cixin Liu" and "Liu Cixin" (same words, other order), or "J Smith" and "John Smith" (an initial and a first name
        // with the same last name). "Andy Weir" and "Anthony Weir" are two different people and must not match.
        private static bool SameNameInAnotherForm(string a, string b)
        {
            var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (ta.Length == 0 || tb.Length == 0)
            {
                return false;
            }

            return ta.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(tb.OrderBy(t => t, StringComparer.Ordinal)) ||
                   (ta[^1] == tb[^1] && ta[0][0] == tb[0][0] && (ta[0].Length == 1 || tb[0].Length == 1));
        }
    }
}
