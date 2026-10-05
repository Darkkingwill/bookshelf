using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.M4bConversion
{
    public class M4bConversionService : IExecute<ConvertToM4bCommand>
    {
        private static readonly Regex LeadingTrackNumberRegex = new Regex(@"^[\d\s._-]+", RegexOptions.Compiled);
        private static readonly Regex NarratorRegex = new Regex(@"narrated by\s+(?<narrator>[^\[\(]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDeleteMediaFiles _mediaFileDeletionService;
        private readonly IBuildFileNames _buildFileNames;
        private readonly IMapCoversToLocal _coverMapper;
        private readonly IFfmpegM4bBuilder _ffmpegBuilder;
        private readonly IHistoryService _historyService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public M4bConversionService(IBookService bookService,
                                     IAuthorService authorService,
                                     IEditionService editionService,
                                     IMediaFileService mediaFileService,
                                     IDeleteMediaFiles mediaFileDeletionService,
                                     IBuildFileNames buildFileNames,
                                     IMapCoversToLocal coverMapper,
                                     IFfmpegM4bBuilder ffmpegBuilder,
                                     IHistoryService historyService,
                                     IDiskProvider diskProvider,
                                     Logger logger)
        {
            _historyService = historyService;
            _bookService = bookService;
            _authorService = authorService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _mediaFileDeletionService = mediaFileDeletionService;
            _buildFileNames = buildFileNames;
            _coverMapper = coverMapper;
            _ffmpegBuilder = ffmpegBuilder;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(ConvertToM4bCommand message)
        {
            var book = _bookService.GetBook(message.BookId);
            var author = _authorService.GetAuthor(book.AuthorId);
            var existingFiles = _mediaFileService.GetFilesByBook(book.Id);

            if (existingFiles.Empty())
            {
                _logger.Warn("No files found for '{0}', nothing to convert", book.Title);
                return;
            }

            var edition = _editionService.GetEdition(existingFiles.First().EditionId);

            // Multi-part downloads are almost always already numbered in track order by the
            // uploader (Part, when set, comes from that); fall back to path so a release with
            // no Part set (e.g. a single file) still gets a stable, deterministic order.
            var orderedFiles = existingFiles
                .OrderBy(f => f.Part > 0 ? f.Part : int.MaxValue)
                .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.ProgressInfo("Converting {0} file(s) to M4B for '{1}'", orderedFiles.Count, book.Title);

            // The import renames files to the naming template, which throws away the uploader's chapter names
            // ("19 - Part 2, Chapter 19"); the import history still has each file's original name.
            var originalPaths = GetOriginalPaths(book);

            var chapterTitles = orderedFiles
                .Select(f => ChapterTitleFor(originalPaths, f.Path))
                .ToList();

            var coverPath = FindCoverPath(book, edition);

            var destinationPath = BuildDestinationPath(author, edition);
            var destinationFolder = Path.GetDirectoryName(destinationPath);
            if (destinationFolder.IsNotNullOrWhiteSpace())
            {
                _diskProvider.CreateFolder(destinationFolder);
            }

            // Check every folder we'll need to write to or delete from up front, before
            // spending 15-20 minutes on the ffmpeg encode. A folder with mismatched
            // ownership/permissions (seen in practice: a folder owned by a different
            // user than the app runs as) fails silently until the delete step, by which
            // point the encode work is wasted and the book is left in a mixed state.
            EnsureFoldersWritable(orderedFiles, destinationFolder);

            var tempOutputPath = Path.Combine(Path.GetTempPath(), $"m4b-convert-{Guid.NewGuid()}.m4b");

            try
            {
                _ffmpegBuilder.BuildM4b(
                    orderedFiles.Select(f => f.Path).ToList(),
                    chapterTitles,
                    BuildTags(author, book, edition, orderedFiles),
                    coverPath,
                    tempOutputPath);

                if (!_diskProvider.FileExists(tempOutputPath) || _diskProvider.GetFileSize(tempOutputPath) == 0)
                {
                    throw new InvalidOperationException("ffmpeg did not produce a usable output file");
                }

                _diskProvider.MoveFile(tempOutputPath, destinationPath, true);

                var newSize = _diskProvider.GetFileSize(destinationPath);

                // Delete the old part-files only after the merged replacement is confirmed
                // in place, so a failed conversion never leaves the book with no files at all.
                foreach (var oldFile in orderedFiles)
                {
                    _mediaFileDeletionService.DeleteTrackFile(author, oldFile);
                }

                var newBookFile = new BookFile
                {
                    Path = destinationPath,
                    Size = newSize,
                    Modified = DateTime.UtcNow,
                    DateAdded = DateTime.UtcNow,
                    Quality = new QualityModel(Quality.M4B),
                    EditionId = edition.Id,
                    Edition = edition,
                    Part = 1
                };

                _mediaFileService.Add(newBookFile);

                _logger.ProgressInfo("Finished converting '{0}' to M4B", book.Title);
            }
            finally
            {
                if (_diskProvider.FileExists(tempOutputPath))
                {
                    _diskProvider.DeleteFile(tempOutputPath);
                }
            }
        }

        private void EnsureFoldersWritable(List<BookFile> orderedFiles, string destinationFolder)
        {
            var foldersToCheck = orderedFiles
                .Select(f => Path.GetDirectoryName(f.Path))
                .Where(d => d.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (destinationFolder.IsNotNullOrWhiteSpace() && !foldersToCheck.Contains(destinationFolder, StringComparer.OrdinalIgnoreCase))
            {
                foldersToCheck.Add(destinationFolder);
            }

            foreach (var folder in foldersToCheck)
            {
                if (!_diskProvider.FolderWritable(folder))
                {
                    throw new InvalidOperationException($"'{folder}' is not writable by the app - fix its permissions/ownership before converting. No files were changed.");
                }
            }
        }

        private string BuildDestinationPath(Author author, Edition edition)
        {
            // BuildBookFileName only reads Quality/extension-relevant fields off the BookFile
            // it's given, not Path, so a placeholder with the real target Quality is enough
            // to get a naming-template-correct filename before the real BookFile row exists.
            var placeholder = new BookFile
            {
                EditionId = edition.Id,
                Quality = new QualityModel(Quality.M4B)
            };

            var fileName = _buildFileNames.BuildBookFileName(author, edition, placeholder);
            return _buildFileNames.BuildBookFilePath(author, edition, fileName, ".m4b");
        }

        // Title, artist and year come from the book the file is attached to. The narrator comes from the
        // release the book was grabbed as ("narrated by X"), falling back to what the uploader tagged.
        private M4bTags BuildTags(Author author, Book book, Edition edition, List<BookFile> orderedFiles)
        {
            var title = edition.Title.IsNotNullOrWhiteSpace() ? edition.Title : book.Title;
            var authorName = author.Metadata.Value.Name;
            return new M4bTags
            {
                Title = title,
                Album = title,
                Artist = authorName,
                AlbumArtist = authorName,
                Year = PickYear(book.ReleaseDate, edition.ReleaseDate),
                Description = CleanDescription(edition.Overview),
                Narrator = FindNarrator(book, authorName, orderedFiles)
            };
        }

        // The year the book was first published. An edition's date is a reprint, so it is only the fallback.
        internal static int? PickYear(DateTime? bookDate, DateTime? editionDate)
        {
            var year = bookDate?.Year ?? editionDate?.Year;

            return year.HasValue && year.Value >= 1500 ? year : null;
        }

        // importedPath -> the name the file had when it was downloaded, from the import history.
        private Dictionary<string, string> GetOriginalPaths(Book book)
        {
            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var history in _historyService.GetByBook(book.Id, EntityHistoryEventType.BookFileImported).OrderBy(h => h.Date))
                {
                    var imported = history.Data.FirstOrDefault(d => d.Key.Equals("importedPath", StringComparison.OrdinalIgnoreCase)).Value;
                    var dropped = history.Data.FirstOrDefault(d => d.Key.Equals("droppedPath", StringComparison.OrdinalIgnoreCase)).Value;

                    if (imported.IsNotNullOrWhiteSpace() && dropped.IsNotNullOrWhiteSpace())
                    {
                        originals[imported] = dropped;
                    }
                }
            }
            catch (Exception ex)
            {
                // Chapter names are an enrichment; fall back to the library file names.
                _logger.Debug(ex, "Could not read the original file names for '{0}'", book.Title);
            }

            return originals;
        }

        internal static string ChapterTitleFor(IDictionary<string, string> originalPaths, string libraryPath)
        {
            var path = originalPaths != null && originalPaths.TryGetValue(libraryPath, out var original) ? original : libraryPath;

            return CleanChapterTitle(Path.GetFileNameWithoutExtension(path));
        }

        private string FindNarrator(Book book, string authorName, List<BookFile> orderedFiles)
        {
            try
            {
                var grabbed = _historyService.GetByBook(book.Id, EntityHistoryEventType.Grabbed)
                    .OrderByDescending(h => h.Date)
                    .Select(h => ParseNarrator(h.SourceTitle))
                    .FirstOrDefault(n => n.IsNotNullOrWhiteSpace());

                if (grabbed.IsNotNullOrWhiteSpace())
                {
                    return grabbed;
                }

                var sourceTags = _ffmpegBuilder.ReadTags(orderedFiles.First().Path);

                foreach (var key in new[] { "narrator", "composer" })
                {
                    if (sourceTags.TryGetValue(key, out var value) &&
                        value.IsNotNullOrWhiteSpace() &&
                        !value.Equals(authorName, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                // The narrator is an enrichment; a failed lookup must not stop the conversion.
                _logger.Debug(ex, "Could not work out the narrator for '{0}'", book.Title);
            }

            return null;
        }

        // Release titles look like "Title by Author, narrated by Jane Doe [ENG / M4B]".
        internal static string ParseNarrator(string releaseTitle)
        {
            if (releaseTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var match = NarratorRegex.Match(releaseTitle);

            return match.Success ? match.Groups["narrator"].Value.Trim() : null;
        }

        // Goodreads descriptions carry HTML and line breaks, and some editions only have a catalogue stub
        // ("262 pages ; 18 cm"). Keep a real description, as plain text on one line, and drop the rest.
        internal static string CleanDescription(string overview)
        {
            if (overview.IsNullOrWhiteSpace())
            {
                return null;
            }

            var text = HtmlTagRegex.Replace(overview, " ");
            text = WebUtility.HtmlDecode(text);
            text = WhitespaceRegex.Replace(text, " ").Trim();

            return text.Length >= 40 ? text.Substring(0, Math.Min(text.Length, 4000)) : null;
        }

        private string FindCoverPath(Book book, Edition edition)
        {
            var cover = edition.Images?.FirstOrDefault(i => i.CoverType == MediaCoverTypes.Cover);

            if (cover == null)
            {
                return null;
            }

            var path = _coverMapper.GetCoverPath(book.Id, MediaCoverEntity.Book, MediaCoverTypes.Cover, cover.Extension);

            return _diskProvider.FileExists(path) ? path : null;
        }

        private static string CleanChapterTitle(string fileName)
        {
            var cleaned = LeadingTrackNumberRegex.Replace(fileName, string.Empty).Trim();
            return cleaned.IsNotNullOrWhiteSpace() ? cleaned : fileName;
        }
    }
}
