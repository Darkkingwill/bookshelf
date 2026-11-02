using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMetadataTagService
    {
        ParsedTrackInfo ReadTags(IFileInfo file);
        void WriteTags(BookFile trackfile, bool newDownload, bool force = false);
        void SyncTags(List<Edition> books);
        List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId);
        List<RetagBookFilePreview> GetRetagPreviewsByBook(int authorId);
    }

    public class MetadataTagService : IMetadataTagService,
        IExecute<RetagFilesCommand>,
        IExecute<RetagAuthorCommand>
    {
        private readonly IAudioTagService _audioTagService;
        private readonly Logger _logger;

        public MetadataTagService(IAudioTagService audioTagService,
            Logger logger)
        {
            _audioTagService = audioTagService;

            _logger = logger;
        }

        public ParsedTrackInfo ReadTags(IFileInfo file)
        {
            if (MediaFileExtensions.AudioExtensions.Contains(file.Extension))
            {
                return _audioTagService.ReadTags(file.FullName);
            }

            // Only audio files carry tags Bookshelf reads; anything else is identified by its file name.
            return Parser.Parser.ParseTitle(file.FullName);
        }

        public void WriteTags(BookFile bookFile, bool newDownload, bool force = false)
        {
            var extension = Path.GetExtension(bookFile.Path);
            if (MediaFileExtensions.AudioExtensions.Contains(extension))
            {
                _audioTagService.WriteTags(bookFile, newDownload, force);
            }
        }

        public void SyncTags(List<Edition> editions)
        {
            _audioTagService.SyncTags(editions);
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId)
        {
            return _audioTagService.GetRetagPreviewsByAuthor(authorId);
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId)
        {
            return _audioTagService.GetRetagPreviewsByBook(bookId);
        }

        public void Execute(RetagFilesCommand message)
        {
            _audioTagService.RetagFiles(message);
        }

        public void Execute(RetagAuthorCommand message)
        {
            _audioTagService.RetagAuthor(message);
        }
    }
}
