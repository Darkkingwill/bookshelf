using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Catches a file that's been imported onto the wrong book by the same author - e.g. a
    // "Manual Import" batch where several rows were left checked and the bulk "Select Book"
    // action stamped one book onto all of them. Only flags a file when another book by the
    // same author is a clearly better textual match than the one it's currently attached to,
    // so generic/numbered filenames (which won't match anything) don't create noise.
    [CheckOn(typeof(TrackImportedEvent))]
    [CheckOn(typeof(BookImportedEvent))]
    public class BookFileTitleMismatchCheck : HealthCheckBase
    {
        private const int MinCleanTitleLength = 6;

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

                var booksById = books.ToDictionary(b => b.Id);
                var candidateBooks = books.Where(b => b.CleanTitle.IsNotNullOrWhiteSpace() && b.CleanTitle.Length >= MinCleanTitleLength).ToList();

                if (!candidateBooks.Any())
                {
                    continue;
                }

                foreach (var file in _mediaFileService.GetFilesByAuthor(author.Id))
                {
                    var edition = file.Edition?.Value;

                    if (edition == null || !booksById.TryGetValue(edition.BookId, out var attachedBook))
                    {
                        continue;
                    }

                    var cleanFileName = file.GetSceneOrFileName().CleanAuthorName();

                    if (cleanFileName.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    // If the currently-attached book's own title is already a reasonable match,
                    // leave it alone even if another title happens to also appear in the name
                    // (e.g. omnibus/boxset filenames that legitimately mention several titles).
                    if (attachedBook.CleanTitle.IsNotNullOrWhiteSpace() && cleanFileName.Contains(attachedBook.CleanTitle))
                    {
                        continue;
                    }

                    var betterMatch = candidateBooks.FirstOrDefault(b => b.Id != attachedBook.Id && cleanFileName.Contains(b.CleanTitle));

                    if (betterMatch != null)
                    {
                        mismatches.Add($"{author.Name}: \"{Path.GetFileName(file.Path)}\" is filed under \"{attachedBook.Title}\" but looks like \"{betterMatch.Title}\"");
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
