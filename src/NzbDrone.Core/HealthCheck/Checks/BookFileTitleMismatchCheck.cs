using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Catches a file that's been imported onto the wrong book by the same author - e.g. a
    // "Manual Import" batch where several rows were left checked and the bulk "Select Book"
    // action stamped one book onto all of them. Scores the filename against every book title
    // by the same author (word-overlap + Levenshtein, same FuzzyMatch used elsewhere for
    // author/book identification) and only flags a file when some other book scores clearly
    // higher than the one it's currently attached to. An earlier plain-substring version of
    // this check flagged ~10x as many files as this one does on a real library - subtitles
    // ("Plain Truth" vs "Plain Truth: A Novel"), omnibus/series-collection titles, and shared
    // recurring words (a character name repeated across several book titles) all produced
    // false hits under substring matching but score correctly under fuzzy matching.
    // Results are cached until something re-runs the check, so deletion has to be a trigger
    // too: without it a file that was removed or repointed keeps being reported for up to the
    // scheduled interval (6 hours), which reads as the library still being wrong long after it
    // was fixed.
    [CheckOn(typeof(TrackImportedEvent))]
    [CheckOn(typeof(BookImportedEvent))]
    [CheckOn(typeof(BookFileDeletedEvent))]
    [CheckOn(typeof(BookFileEditionChangedEvent))]
    public class BookFileTitleMismatchCheck : HealthCheckBase
    {
        private const int MinNormalizedTitleLength = 4;
        private const double MaxAttachedScoreToFlag = 0.5;
        private const double MinBetterMatchScore = 0.75;
        private const double MinScoreMargin = 0.25;

        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IMediaFileService _mediaFileService;

        public BookFileTitleMismatchCheck(IAuthorService authorService,
                                           IBookService bookService,
                                           IMediaFileService mediaFileService,
                                           ILocalizationService localizationService)
            : base(localizationService)
        {
            _authorService = authorService;
            _bookService = bookService;
            _mediaFileService = mediaFileService;
        }

        public override HealthCheck Check()
        {
            var mismatches = new List<string>();

            foreach (var author in _authorService.GetAllAuthors())
            {
                var books = _bookService.GetBooksByAuthor(author.Id);

                // Need at least one other book by this author to suspect a swap against.
                if (books.Count < 2)
                {
                    continue;
                }

                var candidates = books
                    .Select(b => (Book: b, Normalized: Parser.Parser.NormalizeTitle(b.Title)))
                    .Where(c => c.Normalized.Length >= MinNormalizedTitleLength)
                    .ToList();

                if (!candidates.Any())
                {
                    continue;
                }

                var booksById = books.ToDictionary(b => b.Id);

                foreach (var file in _mediaFileService.GetFilesByAuthor(author.Id))
                {
                    var edition = file.Edition?.Value;

                    if (edition == null || !booksById.TryGetValue(edition.BookId, out var attachedBook))
                    {
                        continue;
                    }

                    var fileNormalized = Parser.Parser.NormalizeTitle(file.GetSceneOrFileName());

                    if (fileNormalized.Length < MinNormalizedTitleLength)
                    {
                        continue;
                    }

                    var scored = candidates.Select(c => (c.Book, Score: fileNormalized.FuzzyMatch(c.Normalized))).ToList();
                    var attachedScore = scored.FirstOrDefault(s => s.Book.Id == attachedBook.Id).Score;
                    var best = scored.Where(s => s.Book.Id != attachedBook.Id).OrderByDescending(s => s.Score).FirstOrDefault();

                    if (best.Book != null &&
                        attachedScore < MaxAttachedScoreToFlag &&
                        best.Score >= MinBetterMatchScore &&
                        best.Score - attachedScore >= MinScoreMargin)
                    {
                        mismatches.Add($"{author.Name}: \"{Path.GetFileName(file.Path)}\" is filed under \"{attachedBook.Title}\" but looks like \"{best.Book.Title}\"");
                    }
                }
            }

            if (mismatches.Any())
            {
                var message = string.Format(_localizationService.GetLocalizedString("BookFileTitleMismatchHealthCheckMessage"), string.Join(" | ", mismatches));
                return new HealthCheck(GetType(), HealthCheckResult.Warning, message, "#book-file-title-mismatch");
            }

            return new HealthCheck(GetType());
        }
    }
}
