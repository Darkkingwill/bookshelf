using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    // Audiobook libraries are normally laid out as Author/[Series/][N - ]Book Title/files, and the folder
    // name is very often the cleanest statement of what the book is when the tags are missing or noisy.
    // These helpers turn a file path into that title and compare it to a book title *exactly* (after
    // dropping punctuation, case and a few harmless decorations) - never fuzzily.
    public static class FolderTitleMatcher
    {
        // Anything shorter than this is too easy to hit by accident ("It", "UR", "Us").
        private const int MinimumTitleLength = 4;

        // A leading series position such as "3 - ", "7.5 - " or "1-5 - ".
        private static readonly Regex SeriesPrefix = new Regex(@"^[\d.\-\s]+-\s+", RegexOptions.Compiled);
        private static readonly Regex LeadingDash = new Regex(@"^-\s*", RegexOptions.Compiled);
        private static readonly Regex Parenthetical = new Regex(@"\(.*?\)", RegexOptions.Compiled);
        private static readonly Regex LeadingArticle = new Regex(@"^the\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string GetFolderTitle(string filePath)
        {
            if (filePath.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var directory = Path.GetDirectoryName(filePath);

            if (directory.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var name = Path.GetFileName(directory.TrimEnd('/', '\\'));

            if (name.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            name = SeriesPrefix.Replace(name, string.Empty);
            name = LeadingDash.Replace(name, string.Empty);

            return name.Trim();
        }

        public static string Clean(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            return new string(value.ToLowerInvariant().RemoveAccent().Where(char.IsLetterOrDigit).ToArray());
        }

        public static bool MatchesTitle(string bookTitle, string folderTitle)
        {
            var target = Clean(folderTitle);

            if (target.Length < MinimumTitleLength)
            {
                return false;
            }

            var variants = TitleVariants(bookTitle);

            // "The Title" folder against a "Title" book is the same harmless decoration, just on the other side
            return variants.Contains(target) ||
                   (folderTitle.IsNotNullOrWhiteSpace() && variants.Contains(Clean(LeadingArticle.Replace(folderTitle, string.Empty))));
        }

        private static HashSet<string> TitleVariants(string title)
        {
            var variants = new HashSet<string>();

            if (title.IsNullOrWhiteSpace())
            {
                return variants;
            }

            variants.Add(Clean(title));

            // "Title: Subtitle" - the folder often carries only the main title.
            variants.Add(Clean(title.Split(':')[0]));

            // "Title (Series, Book 3)"
            variants.Add(Clean(Parenthetical.Replace(title, string.Empty)));

            // "The Title" vs "Title"
            variants.Add(Clean(LeadingArticle.Replace(title, string.Empty)));

            variants.RemoveWhere(x => x.Length < MinimumTitleLength);

            return variants;
        }
    }
}
