using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover.Fallback;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaCover
{
    public interface IMapCoversToLocal
    {
        void ConvertToLocalUrls(int entityId, MediaCoverEntity coverEntity, IEnumerable<MediaCover> covers);
        string GetCoverPath(int entityId, MediaCoverEntity coverEntity, MediaCoverTypes coverType, string extension, int? height = null);
        void EnsureBookCovers(Book book);
    }

    public interface IFillMissingCovers
    {
        // Looks up a cover for a book that has no usable one. Returns true only when a new cover was stored.
        bool FillMissingCover(Book book);
    }

    public class MediaCoverService :
        IHandleAsync<AuthorRefreshCompleteEvent>,
        IHandleAsync<AuthorDeletedEvent>,
        IHandleAsync<BookDeletedEvent>,
        IMapCoversToLocal,
        IFillMissingCovers
    {
        private const string USER_AGENT = "Dalvik/2.1.0 (Linux; U; Android 10; SM-G975U Build/QP1A.190711.020)";

        // A cover found by the fallback is stored next to the book's cover with this marker, so a later refresh does not
        // download the metadata source's (missing or worse) cover over it.
        private const string FALLBACK_MARKER = "cover.fallback";

        // Remembers that no provider had a cover, so a refresh does not repeat the lookups for every such book.
        private const string NO_FALLBACK_MARKER = "cover.nofallback";

        // Real covers are tens of kilobytes; the placeholder thumbnails some sources return are a few.
        private const long MIN_USABLE_COVER_BYTES = 6000;

        private static readonly TimeSpan NoFallbackRetry = TimeSpan.FromDays(30);

        // The extensions a stored book cover can have; the fallback writes .jpg, metadata sources also gave .jpeg and .png.
        private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png" };

        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IImageResizer _resizer;
        private readonly IBookService _bookService;
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoverExistsSpecification _coverExistsSpecification;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigService _configService;
        private readonly ICoverFallbackFinder _coverFallbackFinder;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private readonly string _coverRootFolder;

        // Each resize runs an ffmpeg process, so limit the number of concurrent resizing tasks
        private static SemaphoreSlim _semaphore = new SemaphoreSlim((int)Math.Ceiling(Environment.ProcessorCount / 2.0));

        public MediaCoverService(IMediaCoverProxy mediaCoverProxy,
                                 IImageResizer resizer,
                                 IBookService bookService,
                                 IHttpClient httpClient,
                                 IDiskProvider diskProvider,
                                 IAppFolderInfo appFolderInfo,
                                 ICoverExistsSpecification coverExistsSpecification,
                                 IConfigFileProvider configFileProvider,
                                 IConfigService configService,
                                 ICoverFallbackFinder coverFallbackFinder,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _configService = configService;
            _coverFallbackFinder = coverFallbackFinder;
            _mediaCoverProxy = mediaCoverProxy;
            _resizer = resizer;
            _bookService = bookService;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _configFileProvider = configFileProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _coverRootFolder = appFolderInfo.GetMediaCoverPath();
        }

        public string GetCoverPath(int entityId, MediaCoverEntity coverEntity, MediaCoverTypes coverType, string extension, int? height = null)
        {
            var heightSuffix = height.HasValue ? "-" + height.ToString() : "";

            if (coverEntity == MediaCoverEntity.Book)
            {
                return Path.Combine(GetBookCoverPath(entityId), coverType.ToString().ToLower() + heightSuffix + GetExtension(coverType, extension));
            }

            return Path.Combine(GetAuthorCoverPath(entityId), coverType.ToString().ToLower() + heightSuffix + GetExtension(coverType, extension));
        }

        public void ConvertToLocalUrls(int entityId, MediaCoverEntity coverEntity, IEnumerable<MediaCover> covers)
        {
            if (entityId == 0)
            {
                // Author isn't in Readarr yet, map via a proxy to circument referrer issues
                foreach (var mediaCover in covers)
                {
                    mediaCover.RemoteUrl = mediaCover.Url;
                    mediaCover.Url = _mediaCoverProxy.RegisterUrl(mediaCover.RemoteUrl);
                }
            }
            else
            {
                // A book whose metadata has no cover entry but has a stored fallback cover must still show it; without an
                // entry the UI would never ask for the file.
                if (coverEntity == MediaCoverEntity.Book &&
                    covers is ICollection<MediaCover> coverList &&
                    !coverList.IsReadOnly &&
                    coverList.Count == 0)
                {
                    var stored = FindStoredBookCoverExtension(entityId);

                    if (stored != null)
                    {
                        coverList.Add(new MediaCover(MediaCoverTypes.Cover, "cover" + stored));
                    }
                }

                foreach (var mediaCover in covers)
                {
                    if (mediaCover.CoverType == MediaCoverTypes.Unknown)
                    {
                        continue;
                    }

                    var extension = mediaCover.Extension;

                    // A book whose source gave a cover entry without a usable URL ("null") has no extension, but the
                    // fallback stores its cover as cover.jpg, so point the entry at whatever cover is stored.
                    if (coverEntity == MediaCoverEntity.Book &&
                        mediaCover.CoverType == MediaCoverTypes.Cover &&
                        extension.IsNullOrWhiteSpace())
                    {
                        extension = FindStoredBookCoverExtension(entityId) ?? extension;
                    }

                    var filePath = GetCoverPath(entityId, coverEntity, mediaCover.CoverType, extension, null);

                    mediaCover.RemoteUrl = mediaCover.Url;

                    if (coverEntity == MediaCoverEntity.Book)
                    {
                        mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/Books/" + entityId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType, extension);
                    }
                    else
                    {
                        mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/" + entityId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType, extension);
                    }

                    if (_diskProvider.FileExists(filePath))
                    {
                        var lastWrite = _diskProvider.FileGetLastWrite(filePath);
                        mediaCover.Url += "?lastWrite=" + lastWrite.Ticks;
                    }
                }
            }
        }

        private string GetAuthorCoverPath(int authorId)
        {
            return Path.Combine(_coverRootFolder, authorId.ToString());
        }

        private string GetBookCoverPath(int bookId)
        {
            return Path.Combine(_coverRootFolder, "Books", bookId.ToString());
        }

        private void EnsureAuthorCovers(Author author)
        {
            var toResize = new List<Tuple<MediaCover, bool>>();

            foreach (var cover in author.Metadata.Value.Images)
            {
                if (cover.CoverType == MediaCoverTypes.Unknown)
                {
                    continue;
                }

                if (cover.Url.IsNullOrWhiteSpace() || !cover.Url.IsValidUrl())
                {
                    _logger.Debug("Skipping {0} cover for {1}: no valid URL", cover.CoverType, author);
                    continue;
                }

                var fileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension);
                var alreadyExists = false;

                try
                {
                    var serverFileHeaders = GetServerHeaders(cover.Url);

                    alreadyExists = _coverExistsSpecification.AlreadyExists(serverFileHeaders.LastModified, GetContentLength(serverFileHeaders), fileName);

                    if (!alreadyExists)
                    {
                        DownloadCover(author, cover, serverFileHeaders.LastModified ?? DateTime.Now);
                    }
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", author, e.Message);
                }
                catch (WebException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", author, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't download media cover for {0}", author);
                }

                toResize.Add(Tuple.Create(cover, alreadyExists));
            }

            try
            {
                _semaphore.Wait();

                foreach (var tuple in toResize)
                {
                    EnsureResizedCovers(author, tuple.Item1, !tuple.Item2);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void EnsureBookCovers(Book book)
        {
            // A book can have no monitored edition (or several) after a refresh; one of those must not
            // throw, because HandleAsync loops every book of the author and would skip the rest.
            var edition = book.Editions.Value.FirstOrDefault(x => x.Monitored);
            if (edition == null)
            {
                return;
            }

            if (_diskProvider.FileExists(GetBookMarkerPath(book.Id, FALLBACK_MARKER)))
            {
                // A fallback cover is pinned to this book; the metadata source's cover must not replace it.
                return;
            }

            foreach (var cover in edition.Images.Where(e => e.CoverType == MediaCoverTypes.Cover))
            {
                if (cover.CoverType == MediaCoverTypes.Unknown)
                {
                    continue;
                }

                if (cover.Url.IsNullOrWhiteSpace() || !cover.Url.IsValidUrl())
                {
                    _logger.Debug("Skipping {0} cover for {1}: no valid URL", cover.CoverType, book);
                    continue;
                }

                var fileName = GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);
                var alreadyExists = false;

                try
                {
                    var serverFileHeaders = GetServerHeaders(cover.Url);

                    alreadyExists = _coverExistsSpecification.AlreadyExists(serverFileHeaders.LastModified, GetContentLength(serverFileHeaders), fileName);

                    if (!alreadyExists)
                    {
                        DownloadBookCover(book, cover, serverFileHeaders.LastModified ?? DateTime.Now);
                    }
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", book, e.Message);
                }
                catch (WebException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", book, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't download media cover for {0}", book);
                }
            }

            FillMissingCover(book, edition);
        }

        public bool FillMissingCover(Book book)
        {
            var edition = book.Editions.Value.FirstOrDefault(x => x.Monitored);

            return edition != null && FillMissingCover(book, edition);
        }

        private bool FillMissingCover(Book book, Edition edition)
        {
            if (!_configService.CoverFallbackEnabled)
            {
                return false;
            }

            // Store the cover where the book's own cover entry expects it, so the existing URL mapping picks it up.
            var entry = edition.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Cover);
            var extension = entry != null && entry.Extension.IsNotNullOrWhiteSpace() ? entry.Extension : ".jpg";
            var target = GetCoverPath(book.Id, MediaCoverEntity.Book, MediaCoverTypes.Cover, extension, null);

            if (IsUsableCover(target))
            {
                return false;
            }

            var skipMarker = GetBookMarkerPath(book.Id, NO_FALLBACK_MARKER);

            if (_diskProvider.FileExists(skipMarker) && DateTime.UtcNow - _diskProvider.FileGetLastWrite(skipMarker).ToUniversalTime() < NoFallbackRetry)
            {
                return false;
            }

            var author = book.Author?.Value?.Name;
            var found = _coverFallbackFinder.Find(new CoverSearch { Title = book.Title, Author = author, Isbn13 = edition.Isbn13 });

            if (found == null)
            {
                _logger.Debug("No fallback cover found for {0}", book);
                WriteMarker(skipMarker, "none");

                return false;
            }

            try
            {
                _logger.Info("Using {0} cover for {1}: {2}", found.Source, book, found.Url);
                _httpClient.DownloadFile(found.Url, target, USER_AGENT);

                if (!LooksLikeAnImage(target) || _diskProvider.GetFileSize(target) < MIN_USABLE_COVER_BYTES)
                {
                    _logger.Debug("The {0} cover for {1} was not a usable image", found.Source, book);
                    _diskProvider.DeleteFile(target);
                    WriteMarker(skipMarker, "unusable " + found.Url);

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't download the {0} cover for {1}", found.Source, book);

                if (_diskProvider.FileExists(target))
                {
                    _diskProvider.DeleteFile(target);
                }

                return false;
            }

            WriteMarker(GetBookMarkerPath(book.Id, FALLBACK_MARKER), found.Source + "\n" + found.Url);

            if (_diskProvider.FileExists(skipMarker))
            {
                _diskProvider.DeleteFile(skipMarker);
            }

            return true;
        }

        private string FindStoredBookCoverExtension(int bookId)
        {
            return CoverExtensions.FirstOrDefault(e => _diskProvider.FileExists(GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, e, null)));
        }

        private bool IsUsableCover(string path)
        {
            return _diskProvider.FileExists(path) && _diskProvider.GetFileSize(path) >= MIN_USABLE_COVER_BYTES;
        }

        private string GetBookMarkerPath(int bookId, string marker)
        {
            return Path.Combine(GetBookCoverPath(bookId), marker);
        }

        private void WriteMarker(string path, string contents)
        {
            try
            {
                _diskProvider.EnsureFolder(Path.GetDirectoryName(path));
                _diskProvider.WriteAllText(path, contents);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to write cover marker {0}", path);
            }
        }

        // JPEG, PNG or WebP by their first bytes; an HTML error page saved as a cover is not an image.
        private bool LooksLikeAnImage(string path)
        {
            var header = new byte[12];

            using (var stream = _diskProvider.OpenReadStream(path))
            {
                if (stream.Read(header, 0, header.Length) < header.Length)
                {
                    return false;
                }
            }

            var jpeg = header[0] == 0xFF && header[1] == 0xD8;
            var png = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            var webp = header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45;

            return jpeg || png || webp;
        }

        private void DownloadCover(Author author, MediaCover cover, DateTime lastModified)
        {
            var fileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension);

            _logger.Info("Downloading {0} for {1} {2}", cover.CoverType, author, cover.Url);
            _httpClient.DownloadFile(cover.Url, fileName, USER_AGENT);

            try
            {
                _diskProvider.FileSetLastWriteTime(fileName, lastModified);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to set modified date for {0} image for author {1}", cover.CoverType, author);
            }
        }

        private void DownloadBookCover(Book book, MediaCover cover, DateTime lastModified)
        {
            var fileName = GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);

            _logger.Info("Downloading {0} for {1} {2}", cover.CoverType, book, cover.Url);
            _httpClient.DownloadFile(cover.Url, fileName, USER_AGENT);

            try
            {
                _diskProvider.FileSetLastWriteTime(fileName, lastModified);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to set modified date for {0} image for book {1}", cover.CoverType, book);
            }
        }

        private void EnsureResizedCovers(Author author, MediaCover cover, bool forceResize, Book book = null)
        {
            var heights = GetDefaultHeights(cover.CoverType);

            foreach (var height in heights)
            {
                var mainFileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension);
                var resizeFileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension, height);

                if (forceResize || !_diskProvider.FileExists(resizeFileName) || _diskProvider.GetFileSize(resizeFileName) == 0)
                {
                    _logger.Debug("Resizing {0}-{1} for {2}", cover.CoverType, height, author);

                    try
                    {
                        _resizer.Resize(mainFileName, resizeFileName, height);
                    }
                    catch
                    {
                        _logger.Debug("Couldn't resize media cover {0}-{1} for author {2}, using full size image instead.", cover.CoverType, height, author);
                    }
                }
            }
        }

        private int[] GetDefaultHeights(MediaCoverTypes coverType)
        {
            switch (coverType)
            {
                default:
                    return new int[] { };

                case MediaCoverTypes.Poster:
                case MediaCoverTypes.Disc:
                case MediaCoverTypes.Cover:
                case MediaCoverTypes.Logo:
                case MediaCoverTypes.Headshot:
                    return new[] { 500, 250 };

                case MediaCoverTypes.Banner:
                    return new[] { 70, 35 };

                case MediaCoverTypes.Fanart:
                case MediaCoverTypes.Screenshot:
                    return new[] { 360, 180 };
            }
        }

        private string GetExtension(MediaCoverTypes coverType, string defaultExtension)
        {
            return coverType switch
            {
                MediaCoverTypes.Clearlogo => ".png",
                _ => defaultExtension
            };
        }

        private HttpHeader GetServerHeaders(string url)
        {
            // Goodreads doesn't allow a HEAD, so request a zero byte range instead
            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = true,
            };

            request.Headers.Add("Range", "bytes=0-0");
            request.Headers.Add("User-Agent", USER_AGENT);

            return _httpClient.Get(request).Headers;
        }

        private long? GetContentLength(HttpHeader headers)
        {
            var range = headers.Get("content-range");

            if (range == null)
            {
                return null;
            }

            var split = range.Split('/');
            if (split.Length == 2 && long.TryParse(split[1], out var length))
            {
                return length;
            }

            return null;
        }

        public void HandleAsync(AuthorRefreshCompleteEvent message)
        {
            EnsureAuthorCovers(message.Author);

            var books = _bookService.GetBooksByAuthor(message.Author.Id);
            foreach (var book in books)
            {
                EnsureBookCovers(book);
            }

            _eventAggregator.PublishEvent(new MediaCoversUpdatedEvent(message.Author));
        }

        public void HandleAsync(AuthorDeletedEvent message)
        {
            var path = GetAuthorCoverPath(message.Author.Id);
            if (_diskProvider.FolderExists(path))
            {
                _diskProvider.DeleteFolder(path, true);
            }
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            var path = GetBookCoverPath(message.Book.Id);
            if (_diskProvider.FolderExists(path))
            {
                _diskProvider.DeleteFolder(path, true);
            }
        }
    }
}
