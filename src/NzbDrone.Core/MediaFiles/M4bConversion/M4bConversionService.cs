using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.M4bConversion
{
    public class M4bConversionService : IExecute<ConvertToM4bCommand>
    {
        private static readonly Regex LeadingTrackNumberRegex = new Regex(@"^[\d\s._-]+", RegexOptions.Compiled);

        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDeleteMediaFiles _mediaFileDeletionService;
        private readonly IBuildFileNames _buildFileNames;
        private readonly IMapCoversToLocal _coverMapper;
        private readonly IFfmpegM4bBuilder _ffmpegBuilder;
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
                                     IDiskProvider diskProvider,
                                     Logger logger)
        {
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

            var chapterTitles = orderedFiles
                .Select(f => CleanChapterTitle(Path.GetFileNameWithoutExtension(f.Path)))
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
                    edition.Title.IsNotNullOrWhiteSpace() ? edition.Title : book.Title,
                    author.Metadata.Value.Name,
                    edition.Title.IsNotNullOrWhiteSpace() ? edition.Title : book.Title,
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
