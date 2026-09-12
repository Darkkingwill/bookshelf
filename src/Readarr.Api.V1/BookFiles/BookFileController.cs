using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Http.REST.Attributes;
using NzbDrone.SignalR;
using Readarr.Http;
using Readarr.Http.REST;
using BadRequestException = NzbDrone.Core.Exceptions.BadRequestException;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Readarr.Api.V1.BookFiles
{
    [V1ApiController]
    public class BookFileController : RestControllerWithSignalR<BookFileResource, BookFile>,
                                 IHandle<BookFileAddedEvent>,
                                 IHandle<BookFileDeletedEvent>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IDeleteMediaFiles _mediaFileDeletionService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IUpgradableSpecification _upgradableSpecification;

        public BookFileController(IBroadcastSignalRMessage signalRBroadcaster,
                               IMediaFileService mediaFileService,
                               IDeleteMediaFiles mediaFileDeletionService,
                               IMetadataTagService metadataTagService,
                               IAuthorService authorService,
                               IBookService bookService,
                               IEditionService editionService,
                               IEventAggregator eventAggregator,
                               IUpgradableSpecification upgradableSpecification)
            : base(signalRBroadcaster)
        {
            _mediaFileService = mediaFileService;
            _mediaFileDeletionService = mediaFileDeletionService;
            _metadataTagService = metadataTagService;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _eventAggregator = eventAggregator;
            _upgradableSpecification = upgradableSpecification;
        }

        private BookFileResource MapToResource(BookFile bookFile)
        {
            if (bookFile.EditionId > 0 && bookFile.Author != null && bookFile.Author.Value != null)
            {
                return bookFile.ToResource(bookFile.Author.Value, _upgradableSpecification);
            }
            else
            {
                return bookFile.ToResource();
            }
        }

        protected override BookFileResource GetResourceById(int id)
        {
            var resource = MapToResource(_mediaFileService.Get(id));
            resource.AudioTags = _metadataTagService.ReadTags((FileInfoBase)new FileInfo(resource.Path));
            return resource;
        }

        [HttpGet]
        public List<BookFileResource> GetBookFiles(int? authorId, [FromQuery]List<int> bookFileIds, [FromQuery(Name="bookId")]List<int> bookIds, bool? unmapped)
        {
            if (!authorId.HasValue && !bookFileIds.Any() && !bookIds.Any() && !unmapped.HasValue)
            {
                throw new BadRequestException("authorId, bookId, bookFileIds or unmapped must be provided");
            }

            if (unmapped.HasValue && unmapped.Value)
            {
                var files = _mediaFileService.GetUnmappedFiles();
                return files.ConvertAll(f => MapToResource(f));
            }

            if (authorId.HasValue && !bookIds.Any())
            {
                var author = _authorService.GetAuthor(authorId.Value);

                return _mediaFileService.GetFilesByAuthor(authorId.Value).ConvertAll(f => f.ToResource(author, _upgradableSpecification));
            }

            if (bookIds.Any())
            {
                var result = new List<BookFileResource>();
                foreach (var bookId in bookIds)
                {
                    var book = _bookService.GetBook(bookId);
                    var bookAuthor = _authorService.GetAuthor(book.AuthorId);
                    result.AddRange(_mediaFileService.GetFilesByBook(book.Id).ConvertAll(f => f.ToResource(bookAuthor, _upgradableSpecification)));
                }

                return result;
            }
            else
            {
                // trackfiles will come back with the author already populated
                var bookFiles = _mediaFileService.Get(bookFileIds);
                return bookFiles.ConvertAll(e => MapToResource(e));
            }
        }

        [HttpGet("{id:int}/download")]
        public IActionResult DownloadBookFile(int id)
        {
            var bookFile = _mediaFileService.Get(id);

            if (bookFile == null)
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Book file not found");
            }

            if (!global::System.IO.File.Exists(bookFile.Path))
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "File does not exist on disk");
            }

            return PhysicalFile(bookFile.Path, "application/octet-stream", Path.GetFileName(bookFile.Path));
        }

        [RestPutById]
        public ActionResult<BookFileResource> SetQuality(BookFileResource bookFileResource)
        {
            var bookFile = _mediaFileService.Get(bookFileResource.Id);
            var changedEdition = 0;

            if (bookFileResource.Quality != null)
            {
                bookFile.Quality = bookFileResource.Quality;
            }

            // Repointing a file at a different edition is how a mis-identified file gets
            // corrected without deleting it and re-importing through interactive import.
            // Zero means "not supplied" rather than "clear it", so that a client sending
            // only a quality change does not silently unmap the file.
            if (bookFileResource.EditionId > 0 && bookFileResource.EditionId != bookFile.EditionId)
            {
                var edition = _editionService.GetEdition(bookFileResource.EditionId);

                if (edition == null)
                {
                    throw new NzbDroneClientException(HttpStatusCode.NotFound, "Edition not found");
                }

                bookFile.EditionId = edition.Id;
                changedEdition = edition.Id;
            }

            _mediaFileService.Update(bookFile);

            if (changedEdition > 0)
            {
                _eventAggregator.PublishEvent(new BookFileEditionChangedEvent(new List<BookFile> { bookFile }, changedEdition));
            }

            return Accepted(bookFile.Id);
        }

        [HttpPut("editor")]
        public IActionResult SetQuality([FromBody] BookFileListResource resource)
        {
            var bookFiles = _mediaFileService.Get(resource.BookFileIds);

            // Resolve the edition once: every selected file is being pointed at the same one,
            // and a bad id should fail before any file is touched rather than half way through.
            Edition edition = null;

            if (resource.EditionId.HasValue && resource.EditionId.Value > 0)
            {
                edition = _editionService.GetEdition(resource.EditionId.Value);

                if (edition == null)
                {
                    throw new NzbDroneClientException(HttpStatusCode.NotFound, "Edition not found");
                }
            }

            foreach (var bookFile in bookFiles)
            {
                if (resource.Quality != null)
                {
                    bookFile.Quality = resource.Quality;
                }

                if (edition != null)
                {
                    bookFile.EditionId = edition.Id;
                }
            }

            _mediaFileService.Update(bookFiles);

            if (edition != null)
            {
                _eventAggregator.PublishEvent(new BookFileEditionChangedEvent(bookFiles, edition.Id));
            }

            return Accepted(bookFiles.ConvertAll(f => f.ToResource(bookFiles.First().Author.Value, _upgradableSpecification)));
        }

        [RestDeleteById]
        public void DeleteBookFile(int id)
        {
            var bookFile = _mediaFileService.Get(id);

            if (bookFile == null)
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Book file not found");
            }

            if (bookFile.EditionId > 0 && bookFile.Author != null && bookFile.Author.Value != null)
            {
                _mediaFileDeletionService.DeleteTrackFile(bookFile.Author.Value, bookFile);
            }
            else
            {
                _mediaFileDeletionService.DeleteTrackFile(bookFile, "Unmapped_Files");
            }
        }

        [HttpDelete("bulk")]
        public object DeleteTrackFiles([FromBody] BookFileListResource resource)
        {
            var bookFiles = _mediaFileService.Get(resource.BookFileIds);

            foreach (var bookFile in bookFiles)
            {
                if (bookFile.EditionId > 0 && bookFile.Author != null && bookFile.Author.Value != null)
                {
                    _mediaFileDeletionService.DeleteTrackFile(bookFile.Author.Value, bookFile);
                }
                else
                {
                    _mediaFileDeletionService.DeleteTrackFile(bookFile, "Unmapped_Files");
                }
            }

            return new { };
        }

        [NonAction]
        public void Handle(BookFileAddedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.BookFile));
        }

        [NonAction]
        public void Handle(BookFileDeletedEvent message)
        {
            BroadcastResourceChange(ModelAction.Deleted, MapToResource(message.BookFile));
        }
    }
}
