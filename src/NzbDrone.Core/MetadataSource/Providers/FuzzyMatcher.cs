using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MetadataSource.Providers
{
    /// <summary>
    /// Fuzzy matching utilities for metadata search results.
    /// Normalizes titles/authors and scores results using Levenshtein distance.
    /// </summary>
    public static class FuzzyMatcher
    {
        private static readonly Regex StripNonAlphanumeric = new Regex(@"[^a-z0-9\s]", RegexOptions.Compiled);
        private static readonly Regex CollapseWhitespace = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly string[] ArticlePrefixes =
        {
            "the ", "a ", "an "
        };

        private static readonly string[] IdentifierPrefixes =
        {
            "edition:", "work:", "author:", "isbn:", "asin:"
        };

        /// <summary>
        /// Normalize a title or name for comparison:
        /// lowercase, strip articles, remove non-alphanumeric, collapse whitespace.
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalized = input.ToLowerInvariant().Trim();

            // Strip identifier prefixes (from Faustvii's fork)
            foreach (var prefix in IdentifierPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length).Trim();
                }
            }

            // Strip leading articles
            foreach (var article in ArticlePrefixes)
            {
                if (normalized.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(article.Length);
                    break;
                }
            }

            // Remove non-alphanumeric
            normalized = StripNonAlphanumeric.Replace(normalized, " ");
            normalized = CollapseWhitespace.Replace(normalized, " ").Trim();

            return normalized;
        }

        /// <summary>
        /// Compute Levenshtein edit distance between two strings.
        /// </summary>
        public static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.IsNullOrEmpty(t) ? 0 : t.Length;
            }

            if (string.IsNullOrEmpty(t))
            {
                return s.Length;
            }

            var n = s.Length;
            var m = t.Length;

            // Use single-row optimization
            var prev = new int[m + 1];
            var curr = new int[m + 1];

            for (var j = 0; j <= m; j++)
            {
                prev[j] = j;
            }

            for (var i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (var j = 1; j <= m; j++)
                {
                    var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }

                var temp = prev;
                prev = curr;
                curr = temp;
            }

            return prev[m];
        }

        /// <summary>
        /// Compute similarity score between 0.0 (no match) and 1.0 (exact match).
        /// </summary>
        public static double Similarity(string a, string b)
        {
            var na = Normalize(a);
            var nb = Normalize(b);

            if (na == nb)
            {
                return 1.0;
            }

            if (na.Length == 0 || nb.Length == 0)
            {
                return 0.0;
            }

            // Check containment (one contains the other)
            if (na.Contains(nb) || nb.Contains(na))
            {
                var shorter = Math.Min(na.Length, nb.Length);
                var longer = Math.Max(na.Length, nb.Length);
                return (double)shorter / longer;
            }

            var distance = LevenshteinDistance(na, nb);
            var maxLen = Math.Max(na.Length, nb.Length);
            return 1.0 - ((double)distance / maxLen);
        }

        /// <summary>
        /// Score a search result against a query. Returns 0.0-1.0.
        /// Considers title match, author match (if querying by author), and exact ISBN/ASIN match.
        /// </summary>
        public static double ScoreResult(MetadataSearchResult result, string query)
        {
            if (result == null || string.IsNullOrWhiteSpace(query))
            {
                return 0.0;
            }

            var scores = new List<double>();

            // Title similarity
            if (!string.IsNullOrWhiteSpace(result.Title))
            {
                scores.Add(Similarity(result.Title, query));
            }

            // Author similarity (query might be an author name)
            if (result.Authors != null)
            {
                foreach (var author in result.Authors)
                {
                    scores.Add(Similarity(author, query) * 0.9); // Slight discount for author-only match
                }
            }

            // Exact ISBN/ASIN match = perfect score
            var normalizedQuery = query.Replace("-", "").Replace(" ", "");
            if (!string.IsNullOrWhiteSpace(result.Isbn) &&
                result.Isbn.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            if (!string.IsNullOrWhiteSpace(result.Isbn13) &&
                result.Isbn13.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            if (!string.IsNullOrWhiteSpace(result.Asin) &&
                result.Asin.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            return scores.Any() ? scores.Max() : 0.0;
        }

        /// <summary>
        /// Rank and filter search results by relevance to the query.
        /// Returns results with score >= threshold, ordered by score descending.
        /// </summary>
        public static List<MetadataSearchResult> RankResults(
            IEnumerable<MetadataSearchResult> results,
            string query,
            double threshold = 0.3)
        {
            return results
                .Select(r =>
                {
                    r.Confidence = ScoreResult(r, query);
                    return r;
                })
                .Where(r => r.Confidence >= threshold)
                .OrderByDescending(r => r.Confidence)
                .ToList();
        }

        /// <summary>
        /// Generate search candidate queries from a user query.
        /// Handles series-aware patterns like "Author - Series #3" and
        /// produces normalized variants for broader matching.
        /// </summary>
        public static List<string> GenerateCandidates(string query)
        {
            var candidates = new List<string> { query };

            // Strip identifier prefixes
            var stripped = query;
            foreach (var prefix in IdentifierPrefixes)
            {
                if (stripped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    stripped = stripped.Substring(prefix.Length).Trim();
                    candidates.Add(stripped);
                    break;
                }
            }

            // Series pattern: "Title (Series Name #N)" or "Title - Series Name #N"
            var seriesParenMatch = Regex.Match(stripped, @"^(.+?)\s*\((.+?)(?:\s*#\d+)?\)\s*$");
            if (seriesParenMatch.Success)
            {
                candidates.Add(seriesParenMatch.Groups[1].Value.Trim());
                candidates.Add(seriesParenMatch.Groups[2].Value.Trim());
            }

            var seriesDashMatch = Regex.Match(stripped, @"^(.+?)\s*[-–—]\s*(.+?)(?:\s*#\d+)?\s*$");
            if (seriesDashMatch.Success)
            {
                candidates.Add(seriesDashMatch.Groups[1].Value.Trim());
                candidates.Add(seriesDashMatch.Groups[2].Value.Trim());
            }

            // "Author: Title" pattern
            var colonMatch = Regex.Match(stripped, @"^(.+?):\s*(.+)$");
            if (colonMatch.Success)
            {
                candidates.Add(colonMatch.Groups[2].Value.Trim()); // title only
                candidates.Add(colonMatch.Groups[1].Value.Trim()); // author only
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
