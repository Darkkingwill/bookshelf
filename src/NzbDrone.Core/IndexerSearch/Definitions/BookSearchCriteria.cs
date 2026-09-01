using NzbDrone.Core.Parser;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class BookSearchCriteria : SearchCriteriaBase
    {
        public string BookTitle { get; set; }
        public int BookYear { get; set; }
        public string BookIsbn { get; set; }
        public string Disambiguation { get; set; }

        // When true, BookTitle holds a user-supplied search term (from Interactive Search's
        // "Search for" box) and should be sent to indexers as close to verbatim as possible,
        // rather than run through the title/subtitle-splitting heuristic meant for real book titles.
        public bool IsCustomTermSearch { get; set; }

        public string BookQuery => IsCustomTermSearch
            ? GetQueryTitle(BookTitle)
            : GetQueryTitle(BookTitle.SplitBookTitle(Author.Name).Item1);

        public override string ToString()
        {
            return $"[{Author.Name} - {BookTitle}]";
        }
    }
}
