using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.Providers;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public class BookInfoProxy : IProvideAuthorInfo, IProvideBookInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        private static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            Converters = { new STJUtcConverter() }
        };

        private readonly IHttpClient _httpClient;
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly IGoodreadsSearchProxy _goodreadsSearchProxy;
        private readonly IGoodreadsProxy _goodreadsProxy;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly Logger _logger;
        private readonly IMetadataRequestBuilder _requestBuilder;
        private readonly IMetadataProviderService _metadataProviderService;
        private readonly ICached<HashSet<string>> _cache;
        private readonly CachingService _authorCache;

        public BookInfoProxy(IHttpClient httpClient,
                             ICachedHttpResponseService cachedHttpClient,
                             IGoodreadsSearchProxy goodreadsSearchProxy,
                             IGoodreadsProxy goodreadsProxy,
                             IAuthorService authorService,
                             IBookService bookService,
                             IEditionService editionService,
                             IMetadataRequestBuilder requestBuilder,
                             IMetadataProviderService metadataProviderService,
                             Logger logger,
                             ICacheManager cacheManager)
        {
            _httpClient = httpClient;
            _cachedHttpClient = cachedHttpClient;
            _goodreadsSearchProxy = goodreadsSearchProxy;
            _goodreadsProxy = goodreadsProxy;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _requestBuilder = requestBuilder;
            _metadataProviderService = metadataProviderService;
            _cache = cacheManager.GetCache<HashSet<string>>(GetType());
            _logger = logger;

            _authorCache = new CachingService(new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 })));
            _authorCache.DefaultCachePolicy = new CacheDefaults
            {
                DefaultCacheDurationSeconds = 60
            };
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                .SetSegment("route", "author/changed")
                .AddQueryParam("since", startTime.ToString("o"))
                .Build();

            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<RecentUpdatesResource>(httpRequest);

            if (httpResponse.Resource == null || httpResponse.Resource.Limited)
            {
                return null;
            }

            return new HashSet<string>(httpResponse.Resource.Ids.Select(x => x.ToString()));
        }

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = false, string metadataSource = null)
        {
            _logger.Debug("Getting Author details GoodreadsId of {0}", foreignAuthorId);

            // Only take this branch when the caller explicitly knows which provider this id
            // belongs to (e.g. an author's own pinned MetadataSource). Never guess from the id's
            // shape alone - a bare number is ambiguous between providers (see the incident this
            // was built to fix: a Hardcover author id collided in value, not meaning, with an
            // unrelated real Goodreads author sharing the same number).
            //
            // "hardcover" is deliberately excluded here: for a bare legacy id it means "whatever
            // the primary metadata source is configured as" (the self-hosted proxy that
            // originally assigned it), which is a DIFFERENT id space than the real Hardcover API
            // reached via IMetadataProviderService - routing there returned a wrong, unrelated
            // match instead of failing cleanly. Only route elsewhere for a source with its own
            // independently-verified id space (currently just goodreads).
            if (metadataSource.IsNotNullOrWhiteSpace() && !metadataSource.Equals("hardcover", StringComparison.OrdinalIgnoreCase))
            {
                return GetAuthorInfoFromSource(metadataSource, foreignAuthorId);
            }

            try
            {
                if (useCache)
                {
                    return PollAuthor(foreignAuthorId);
                }

                return PollAuthorUncached(foreignAuthorId);
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Unexpected error getting author info: {foreignAuthorId}", foreignAuthorId);
                throw;
            }
        }

        public HashSet<string> GetChangedBooks(DateTime startTime)
        {
            return _cache.Get("ChangedBooks", () => GetChangedBooksUncached(startTime), TimeSpan.FromMinutes(30));
        }

        private HashSet<string> GetChangedBooksUncached(DateTime startTime)
        {
            return null;
        }

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId, string metadataSource = null)
        {
            if (TryParseProviderForeignId(foreignBookId, out var providerKey, out var rawId))
            {
                return GetBookInfoFromProvider(providerKey, rawId, foreignBookId);
            }

            // Same principle as GetAuthorInfo, "hardcover" exclusion included - see the comment
            // there for why.
            if (metadataSource.IsNotNullOrWhiteSpace() && !metadataSource.Equals("hardcover", StringComparison.OrdinalIgnoreCase))
            {
                return GetBookInfoFromSource(metadataSource, foreignBookId);
            }

            try
            {
                return PollBook(foreignBookId);
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Unexpected error getting book info: {foreignBookId}", foreignBookId);
                throw;
            }
        }

        private Tuple<string, Book, List<AuthorMetadata>> GetBookInfoFromSource(string metadataSource, string foreignBookId)
        {
            if (metadataSource.Equals("goodreads", StringComparison.OrdinalIgnoreCase))
            {
                return GetGoodreadsBookInfo(foreignBookId);
            }

            // Any other tag reaching here (googlebooks/openlibrary/audible/rreadingglasses) means
            // the user manually re-pinned an author who still has an old bare legacy id under
            // that tag - resolved the same way as a fallback-provider add, keeping the existing
            // unprefixed id instead of synthesizing a new "provider:id" one, since this book
            // already exists locally under that bare id and refreshes must keep matching it.
            // Best-effort: these providers were never the origin of a bare legacy id, so this
            // will usually just fail cleanly rather than resolve.
            return GetBookInfoFromProvider(metadataSource, foreignBookId, foreignBookId);
        }

        // foreignBookId here is always a work id - the id space every caller of GetBookInfo
        // actually deals in (search results, PollBook, an author's synced book list). Try the
        // self-hosted rreading-glasses proxy first: its work/{id} route resolves a work to its
        // real editions correctly and is backed by a large pre-seeded cache, unlike
        // GoodreadsProxy.GetBookInfo, which only understands edition-level ids and hits real
        // goodreads.com directly under this app's own account - treating a work id as an
        // edition id there silently returns a wrong, unrelated book (two different id spaces
        // that happen to share numbers). Only fall back to hitting Goodreads directly if the
        // local proxy is unreachable; a clean "not found" from it is trusted as-is rather than
        // retried against the other id space.
        private Tuple<string, Book, List<AuthorMetadata>> GetGoodreadsBookInfo(string foreignBookId)
        {
            Tuple<string, Book, List<AuthorMetadata>> tuple;

            try
            {
                tuple = PollBook(foreignBookId);
            }
            catch (BookNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Local metadata proxy unreachable for work {0}, falling back to Goodreads directly", foreignBookId);

                var book = _goodreadsProxy.GetBookInfo(foreignBookId);

                if (book?.AuthorMetadata?.Value == null)
                {
                    throw new BookNotFoundException(foreignBookId);
                }

                var bookAuthorMetadata = book.AuthorMetadata.Value;
                tuple = Tuple.Create(bookAuthorMetadata.ForeignAuthorId, book, new List<AuthorMetadata> { bookAuthorMetadata });
            }

            // Caller explicitly asked for goodreads, so tag it regardless of which path
            // resolved it - PollBook's own legacy mapping leaves this unset, since a bare id
            // there is otherwise source-agnostic, but downstream refreshes need this to keep
            // routing back here.
            foreach (var authorMetadata in tuple.Item3)
            {
                authorMetadata.MetadataSource = "goodreads";
            }

            return tuple;
        }

        // Book/author IDs sourced from a fallback metadata provider are synthesized as
        // "{providerKey}:{rawId}" (see MapMetadataResultToBook/Author below). The legacy
        // REST lookup (PollBook) doesn't understand that shape and 400s on it, so route
        // those back to the provider that produced them instead.
        private static readonly HashSet<string> KnownProviderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "googlebooks", "openlibrary", "audible", "hardcover", "rreadingglasses"
        };

        private bool TryParseProviderForeignId(string foreignId, out string providerKey, out string rawId)
        {
            providerKey = null;
            rawId = null;

            var idx = foreignId?.IndexOf(':') ?? -1;
            if (idx <= 0)
            {
                return false;
            }

            var prefix = foreignId.Substring(0, idx);
            if (!KnownProviderKeys.Contains(prefix))
            {
                return false;
            }

            providerKey = prefix;
            rawId = foreignId.Substring(idx + 1);
            return true;
        }

        private Tuple<string, Book, List<AuthorMetadata>> GetBookInfoFromProvider(string providerKey, string rawId, string compositeForeignId)
        {
            var result = _metadataProviderService.GetBookInfo(providerKey, rawId);

            if (result == null)
            {
                throw new BookNotFoundException(compositeForeignId);
            }

            var authorName = result.Authors?.FirstOrDefault() ?? "Unknown Author";

            // If this author is already in the library (added via a different provider/source
            // originally), reuse their real id instead of a fresh provider-specific one - otherwise
            // this would try to create a colliding duplicate author record for the same person.
            var existingAuthor = _authorService.FindByName(authorName);
            var authorForeignId = existingAuthor?.Metadata.Value.ForeignAuthorId
                ?? result.AuthorForeignId
                ?? $"{providerKey}:author:{authorName.ToLowerInvariant().Replace(" ", "-")}";

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = authorForeignId,
                Name = authorName,
                SortName = authorName,
                TitleSlug = authorForeignId.Replace(":", "-"),
                Status = AuthorStatusType.Continuing,
                MetadataSource = providerKey,
                Images = new List<MediaCover.MediaCover>(),
                Links = new List<Links> { new Links { Url = $"https://www.google.com/search?q={Uri.EscapeDataString(authorName)}", Name = "Google" } }
            };

            var author = new Author
            {
                CleanName = Parser.Parser.CleanAuthorName(authorName),
                Metadata = authorMetadata,
                Monitored = false
            };

            var editionResults = result.Editions != null && result.Editions.Any()
                ? result.Editions
                : new List<MetadataEditionResult> { new MetadataEditionResult { ForeignId = rawId, Title = result.Title, CoverUrl = result.CoverUrl } };

            var editions = editionResults.Select((e, idx) =>
            {
                var editionForeignId = idx == 0 ? compositeForeignId : $"{providerKey}:{e.ForeignId ?? rawId}-{idx}";
                var edition = new Edition
                {
                    // The first/primary edition keeps the same id the search result was picked
                    // by (AddSkyhookData matches on it) - any extras get their own distinct id.
                    ForeignEditionId = editionForeignId,
                    TitleSlug = editionForeignId.Replace(":", "-"),
                    Title = e.Title ?? result.Title ?? "Unknown",
                    Isbn13 = e.Isbn13,
                    Asin = e.Asin,
                    Overview = result.Description,
                    PageCount = e.PageCount ?? 0,
                    Publisher = e.Publisher,
                    Images = new List<MediaCover.MediaCover>(),
                    Monitored = true
                };

                var coverUrl = e.CoverUrl ?? result.CoverUrl;
                if (!string.IsNullOrWhiteSpace(coverUrl))
                {
                    edition.Images.Add(new MediaCover.MediaCover
                    {
                        CoverType = MediaCover.MediaCoverTypes.Cover,
                        Url = coverUrl,
                        RemoteUrl = coverUrl
                    });
                }

                return edition;
            }).ToList();

            var book = new Book
            {
                ForeignBookId = compositeForeignId,
                TitleSlug = compositeForeignId.Replace(":", "-"),
                Title = result.Title ?? "Unknown",
                CleanTitle = Parser.Parser.CleanAuthorName(result.Title ?? "Unknown"),
                Author = new LazyLoaded<Author>(author),
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                Editions = new LazyLoaded<List<Edition>>(editions)
            };

            return Tuple.Create(authorForeignId, book, new List<AuthorMetadata> { authorMetadata });
        }

        private Author GetAuthorInfoFromSource(string metadataSource, string foreignAuthorId)
        {
            if (metadataSource.Equals("goodreads", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(foreignAuthorId, out var goodreadsAuthorId))
                {
                    throw new AuthorNotFoundException(foreignAuthorId);
                }

                return _goodreadsProxy.GetAuthorInfo(goodreadsAuthorId);
            }

            var result = _metadataProviderService.GetAuthorInfo(metadataSource, foreignAuthorId);

            if (result == null)
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            return MapProviderAuthorResult(metadataSource, result);
        }

        private static Author MapProviderAuthorResult(string metadataSource, MetadataAuthorResult result)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = result.ForeignId,
                TitleSlug = result.ForeignId,
                Name = result.Name,
                SortName = result.Name?.ToLowerInvariant(),
                Overview = result.Description,
                Status = AuthorStatusType.Continuing,
                MetadataSource = metadataSource
            };

            if (result.ImageUrl.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover { Url = result.ImageUrl, CoverType = MediaCover.MediaCoverTypes.Poster });
            }

            var books = (result.Works ?? new List<MetadataSearchResult>())
                .Where(w => w.ForeignId.IsNotNullOrWhiteSpace())
                .Select(w => MapProviderWorkToBook(metadataSource, w))
                .DistinctBy(b => b.ForeignBookId)
                .ToList();

            books.ForEach(b => b.AuthorMetadata = metadata);

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = new List<Series>()
            };
        }

        private static Book MapProviderWorkToBook(string metadataSource, MetadataSearchResult result)
        {
            var foreignId = $"{metadataSource}:{result.ForeignId}";

            var edition = new Edition
            {
                ForeignEditionId = foreignId,
                TitleSlug = foreignId.Replace(":", "-"),
                Title = result.Title ?? "Unknown",
                Isbn13 = result.Isbn13,
                Asin = result.Asin,
                Overview = result.Description,
                PageCount = result.PageCount ?? 0,
                Publisher = result.Publisher,
                Images = new List<MediaCover.MediaCover>(),
                Monitored = true
            };

            if (result.CoverUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover { Url = result.CoverUrl, CoverType = MediaCover.MediaCoverTypes.Cover });
            }

            return new Book
            {
                ForeignBookId = foreignId,
                TitleSlug = foreignId.Replace(":", "-"),
                Title = result.Title ?? "Unknown",
                CleanTitle = Parser.Parser.CleanAuthorName(result.Title ?? "Unknown"),
                AnyEditionOk = true,
                Editions = new List<Edition> { edition }
            };
        }

        public List<object> SearchForNewEntity(string title, string source = null)
        {
            // Only "goodreads" gets its own path - same reasoning as GetAuthorInfo/GetBookInfo's
            // source routing: it's the one source with an independently-verified id space and a
            // working direct client, so it's worth bypassing bookinfo.pro's own (occasionally
            // wrong) title-to-id mapping for. Anything else (null, "hardcover", or an unrecognized
            // value) keeps the existing default behavior unchanged.
            if (source.IsNotNullOrWhiteSpace() && source.Equals("goodreads", StringComparison.OrdinalIgnoreCase))
            {
                return SearchGoodreadsForNewEntity(title);
            }

            var books = SearchForNewBook(title, null, false);

            var result = new List<object>();
            foreach (var book in books)
            {
                var author = book.Author.Value;

                if (!result.Contains(author))
                {
                    result.Add(author);
                }

                result.Add(book);
            }

            return result;
        }

        // Goodreads has no general-purpose title search reachable from this client (see
        // GoodreadsSearchProxy - that one goes through bookinfo.pro, which is exactly what this
        // is trying to avoid). What it does have is a reliable name-to-author resolver
        // (SearchAuthorByName) plus a full book list once you have the author id, so "search
        // Goodreads for X" here means "find the author named X and list everything of theirs" -
        // the same shape SearchByGoodreadsAuthorId already uses for an explicit "author:<id>"
        // query, just entered by name instead of by id.
        private List<object> SearchGoodreadsForNewEntity(string title)
        {
            var authorResult = _goodreadsProxy.SearchAuthorByName(title);

            if (authorResult == null)
            {
                return new List<object>();
            }

            Author author;
            try
            {
                author = GetAuthorInfo(authorResult.ForeignAuthorId, false, "goodreads");
            }
            catch (AuthorNotFoundException)
            {
                return new List<object>();
            }

            var books = author.Books.Value;
            var authors = new Dictionary<string, AuthorMetadata> { { authorResult.ForeignAuthorId, author.Metadata.Value } };

            // Mirrors SearchForNewEntity's own dedup below: AddDbIds may swap book.Author for an
            // existing local record (with its real db id), which is the copy that needs to end up
            // in the result, not the fresh-from-Goodreads one still held in `author`.
            var result = new List<object>();
            foreach (var book in books)
            {
                AddDbIds(authorResult.ForeignAuthorId, book, authors);

                var bookAuthor = book.Author.Value;

                if (!result.Contains(bookAuthor))
                {
                    result.Add(bookAuthor);
                }

                result.Add(book);
            }

            return result;
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            var books = SearchForNewBook(title, null);

            var authors = books
                .Select(x => x.Author.Value)
                .DistinctBy(x => x.ForeignAuthorId)
                .ToList();

            if (authors.Any())
            {
                return authors;
            }

            // Fall back to multi-provider search
            _logger.Info("Legacy search returned no results for '{0}', trying metadata providers", title);
            try
            {
                var providerResults = _metadataProviderService.SearchAuthors(title);
                if (providerResults != null && providerResults.Any())
                {
                    return providerResults.Select(MapMetadataResultToAuthor).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Multi-provider author search failed for '{0}'", title);
            }

            return authors;
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            var q = title.ToLower().Trim();
            if (author != null)
            {
                q += " " + author;
            }

            List<Book> legacyResults = null;

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                var split = lowerTitle.Split(':');
                var prefix = split[0];

                if (split.Length == 2 && new[] { "author", "work", "edition", "isbn", "asin" }.Contains(prefix))
                {
                    var slug = split[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Book>();
                    }

                    if (prefix == "author" || prefix == "work" || prefix == "edition")
                    {
                        var isValid = int.TryParse(slug, out var searchId);
                        if (!isValid)
                        {
                            return new List<Book>();
                        }

                        if (prefix == "author")
                        {
                            return SearchByGoodreadsAuthorId(searchId);
                        }

                        if (prefix == "work")
                        {
                            return SearchByGoodreadsWorkId(searchId);
                        }

                        if (prefix == "edition")
                        {
                            return SearchByGoodreadsBookId(searchId, getAllEditions);
                        }
                    }

                    // to handle isbn / asin
                    q = slug;
                }

                legacyResults = Search(q, getAllEditions);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Legacy search failed for '{0}', trying metadata providers", title);
            }

            if (legacyResults != null && legacyResults.Any())
            {
                return legacyResults;
            }

            // The local proxy found nothing (or is unreachable) - try real Goodreads directly
            // before falling through to unrelated providers below, since it's the same
            // authoritative source the local proxy's own cache is built from, just fetched
            // live instead of pre-seeded. SearchGoodreadsForNewEntity only works as an
            // author-name search (a real Goodreads API limitation, not specific to this
            // fallback) - so when a separate author name is known (e.g. CandidateService's
            // disk-scan matching, which always passes both a book tag and an author tag),
            // search Goodreads for THAT name and let the caller pick the matching title out
            // of the author's full bibliography, the same pattern SearchByGoodreadsAuthorId
            // already uses. Searching by `title` instead (the book's title, not an author's
            // name) would only ever match an author who happens to be named after that book,
            // which is never - confirmed live: every one of these lookups came back empty.
            // Only fall back to treating the book title itself as an author-name guess when no
            // author is known, which is the shape a plain author-search-box query takes.
            var goodreadsNameQuery = author.IsNotNullOrWhiteSpace() ? author : title;
            _logger.Info("Legacy search returned no results for '{0}', trying Goodreads directly for author '{1}'", title, goodreadsNameQuery);
            try
            {
                var goodreadsResults = SearchGoodreadsForNewEntity(goodreadsNameQuery).OfType<Book>().ToList();
                if (goodreadsResults.Any())
                {
                    return goodreadsResults;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Direct Goodreads search failed for '{0}'", goodreadsNameQuery);
            }

            // Fall back to multi-provider search
            _logger.Info("Goodreads search returned no results for '{0}', trying metadata providers", title);
            try
            {
                var searchQuery = author != null ? $"{title} {author}" : title;
                var providerResults = _metadataProviderService.SearchBooks(searchQuery);
                if (providerResults != null && providerResults.Any())
                {
                    return providerResults.Select(MapMetadataResultToBook).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Multi-provider book search failed for '{0}'", title);
            }

            return legacyResults ?? new List<Book>();
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            return Search(isbn, true);
        }

        public List<Book> SearchByAsin(string asin)
        {
            return Search(asin, true);
        }

        private List<Book> Search(string query, bool getAllEditions)
        {
            List<SearchJsonResource> result;
            try
            {
                result = _goodreadsSearchProxy.Search(query);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Error searching for {0}", query);
                return new List<Book>();
            }

            var books = new List<Book>();

            if (getAllEditions)
            {
                // Slower but more exhaustive, less intensive on metadata API
                var bookIds = result.Select(x => x.WorkId).ToList();

                var idMap = result.Select(x => new { AuthorId = x.Author.Id, BookId = x.WorkId })
                    .GroupBy(x => x.AuthorId)
                    .ToDictionary(x => x.Key, x => x.Select(i => i.BookId.ToString()).ToList());

                List<Book> authorBooks;
                foreach (var author in idMap.Keys)
                {
                    authorBooks = SearchByGoodreadsAuthorId(author);
                    books.AddRange(authorBooks.Where(b => idMap[author].Contains(b.ForeignBookId)));
                }

                var missingBooks = bookIds.ExceptBy(x => x.ToString(), books, x => x.ForeignBookId, StringComparer.Ordinal).ToList();
                foreach (var book in missingBooks)
                {
                    books.AddRange(SearchByGoodreadsWorkId(book));
                }

                return books;
            }
            else
            {
                // Use sparingly, hits metadata API quite hard
                var ids = result.Select(x => x.BookId).ToList();

                if (ids.Count == 0)
                {
                    return new List<Book>();
                }

                if (ids.Count == 1)
                {
                    return SearchByGoodreadsBookId(ids[0], false);
                }

                try
                {
                    return MapSearchResult(ids);
                }
                catch (HttpException ex)
                {
                    _logger.Warn(ex);
                    throw new BookInfoException("Search for '{0}' failed. Unable to communicate with ReadarrAPI, returning status code: {1}.", ex, query, ex.Response.StatusCode);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Error mapping search results");

                    return new List<Book>();
                }
            }
        }

        private List<Book> SearchByGoodreadsAuthorId(int id)
        {
            try
            {
                var authorId = id.ToString();
                var result = GetAuthorInfo(authorId);
                var books = result.Books.Value;
                var authors = new Dictionary<string, AuthorMetadata> { { authorId, result.Metadata.Value } };

                foreach (var book in books)
                {
                    AddDbIds(authorId, book, authors);
                }

                return books;
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by author id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsWorkId(int id)
        {
            try
            {
                var tuple = GetBookInfo(id.ToString());
                AddDbIds(tuple.Item1, tuple.Item2, tuple.Item3.ToDictionary(x => x.ForeignAuthorId));
                return new List<Book> { tuple.Item2 };
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by work id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsBookId(int id, bool getAllEditions)
        {
            try
            {
                var book = GetEditionInfo(id, getAllEditions);

                return new List<Book> { book };
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (EditionNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by book id");
                return new List<Book>();
            }
        }

        private Book GetEditionInfo(int id, bool getAllEditions)
        {
            HttpRequest httpRequest;
            HttpResponse httpResponse;

            while (true)
            {
                httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"book/{id}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // we expect a redirect
                httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!httpResponse.HasHttpRedirect)
            {
                throw new BookInfoException($"Unexpected response from {httpRequest.Url}");
            }

            var location = httpResponse.Headers.GetSingleValue("Location");
            var split = location.Split('/').Reverse().ToList();
            var newId = split[0];
            var type = split[1];

            Book book;
            List<AuthorMetadata> authors;

            if (type == "author")
            {
                var author = PollAuthor(newId);

                book = author.Books.Value.FirstOrDefault(b => b.Editions.Value.Any(e => e.ForeignEditionId == id.ToString()));
                authors = new List<AuthorMetadata> { author.Metadata.Value };
            }
            else if (type == "work")
            {
                var tuple = PollBook(newId);

                book = tuple.Item2;
                authors = tuple.Item3;
            }
            else
            {
                throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
            }

            if (book == null || book.Editions.Value.All(e => e.ForeignEditionId != id.ToString()))
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!getAllEditions)
            {
                var trimmed = new Book();
                trimmed.UseMetadataFrom(book);
                trimmed.Author.Value.Metadata = book.AuthorMetadata.Value;
                trimmed.AuthorMetadata = book.AuthorMetadata.Value;
                trimmed.SeriesLinks = book.SeriesLinks;
                var edition = book.Editions.Value.SingleOrDefault(e => e.ForeignEditionId == id.ToString());
                if (edition != null)
                {
                    edition.Monitored = true;
                }

                trimmed.Editions = new List<Edition> { edition };
                book = trimmed;
            }

            var authorDict = authors.ToDictionary(x => x.ForeignAuthorId);
            AddDbIds(book.AuthorMetadata.Value.ForeignAuthorId, book, authorDict);

            return book;
        }

        private List<Book> MapSearchResult(List<int> ids)
        {
            HttpResponse<BulkBookResource> httpResponse;

            while (true)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", "book/bulk")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                httpRequest.SetContent(ids.ToJson());
                httpRequest.ContentSummary = ids.ToJson(Formatting.None);

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.TooManyRequests };

                httpResponse = _httpClient.Post<BulkBookResource>(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            return MapBulkBook(httpResponse.Resource);
        }

        private List<Book> MapBulkBook(BulkBookResource resource)
        {
            var books = new List<Book>();

            if (resource == null)
            {
                return books;
            }

            var authors = resource.Authors.Select(MapAuthorMetadata).ToDictionary(x => x.ForeignAuthorId, x => x);
            var series = resource.Series.Select(MapSeries).ToList();

            foreach (var work in resource.Works)
            {
                var book = MapBook(work);
                var authorId = work.Books.OrderByDescending(b => b.AverageRating * b.RatingCount).First().Contributors.First().ForeignId.ToString();

                AddDbIds(authorId, book, authors);

                books.Add(book);
            }

            MapSeriesLinks(series, books, resource.Series);

            return books;
        }

        private void AddDbIds(string authorId, Book book, Dictionary<string, AuthorMetadata> authors)
        {
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);

                var editions = _editionService.GetEditionsByBook(dbBook.Id).ToDictionary(x => x.ForeignEditionId);

                // If we have any database editions, exactly one will be monitored.
                // So unmonitor all the found editions and let the UseDbFieldsFrom set
                // the monitored status
                foreach (var edition in book.Editions.Value)
                {
                    edition.Monitored = false;
                    if (editions.TryGetValue(edition.ForeignEditionId, out var dbEdition))
                    {
                        edition.UseDbFieldsFrom(dbEdition);
                    }
                }

                // Double check at least one edition is monitored
                if (book.Editions.Value.Any() && !book.Editions.Value.Any(x => x.Monitored))
                {
                    var mostPopular = book.Editions.Value.OrderByDescending(x => x.Ratings.Popularity).First();
                    mostPopular.Monitored = true;
                }
            }

            var author = _authorService.FindById(authorId);

            if (author == null)
            {
                if (!authors.TryGetValue(authorId, out var metadata))
                {
                    throw new BookInfoException(string.Format("Expected author metadata for id [{0}] in book data {1}", authorId, book));
                }

                author = new Author
                {
                    CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                    Metadata = metadata
                };
            }

            book.Author = author;
            book.AuthorMetadata = author.Metadata.Value;
            book.AuthorMetadataId = author.AuthorMetadataId;
        }

        private Author PollAuthor(string foreignAuthorId)
        {
            return _authorCache.GetOrAdd(foreignAuthorId,
                () => PollAuthorUncached(foreignAuthorId),
                new LazyCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                    ImmediateAbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                    Size = 1,
                    SlidingExpiration = TimeSpan.FromMinutes(1),
                    ExpirationMode = ExpirationMode.ImmediateEviction
                }.RegisterPostEvictionCallback((key, value, reason, state) => _logger.Debug($"Clearing cache for {key} due to {reason}")));
        }

        private Author PollAuthorUncached(string foreignAuthorId)
        {
            AuthorResource resource = null;

            for (var i = 0; i < 60; i++)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"author/{foreignAuthorId}")
                    .Build();

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpError = true;

                var httpResponse = _cachedHttpClient.Get(httpRequest, false, TimeSpan.FromMinutes(30));

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        WaitUntilRetry(httpResponse);
                        continue;
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new AuthorNotFoundException(foreignAuthorId);
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        throw new BadRequestException(foreignAuthorId);
                    }
                    else
                    {
                        throw new BookInfoException("Unexpected error fetching author data");
                    }
                }

                resource = JsonSerializer.Deserialize<AuthorResource>(httpResponse.Content, SerializerSettings);

                if (resource.Works != null)
                {
                    resource.Works ??= new List<WorkResource>();
                    resource.Series ??= new List<SeriesResource>();
                    break;
                }

                Thread.Sleep(2000);
            }

            if (resource?.Works == null)
            {
                throw new BookInfoException($"Failed to get works for {foreignAuthorId}");
            }

            return MapAuthor(resource);
        }

        private Tuple<string, Book, List<AuthorMetadata>> PollBook(string foreignBookId)
        {
            WorkResource resource = null;

            for (var i = 0; i < 60; i++)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"work/{foreignBookId}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // this may redirect to an author
                var httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                    continue;
                }

                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BookNotFoundException(foreignBookId);
                }

                if (httpResponse.HasHttpRedirect)
                {
                    var location = httpResponse.Headers.GetSingleValue("Location");
                    var split = location.Split('/').Reverse().ToList();
                    var newId = split[0];
                    var type = split[1];

                    if (type == "author")
                    {
                        var author = PollAuthor(newId);
                        var authorBook = author.Books.Value.SingleOrDefault(x => x.ForeignBookId == foreignBookId);

                        if (authorBook == null)
                        {
                            throw new BookNotFoundException(foreignBookId);
                        }

                        var authorMetadata = new List<AuthorMetadata> { author.Metadata.Value };

                        return Tuple.Create(author.ForeignAuthorId, authorBook, authorMetadata);
                    }
                    else
                    {
                        throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
                    }
                }

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        throw new BadRequestException(foreignBookId);
                    }
                    else
                    {
                        throw new BookInfoException("Unexpected response fetching book data");
                    }
                }

                resource = JsonSerializer.Deserialize<WorkResource>(httpResponse.Content, SerializerSettings);

                if (resource.Books != null)
                {
                    break;
                }

                Thread.Sleep(2000);
            }

            if (resource?.Books == null || resource?.Authors == null || (!resource?.Authors?.Any() ?? false))
            {
                throw new BookInfoException($"Failed to get books for {foreignBookId}");
            }

            var book = MapBook(resource);
            var authorId = GetAuthorId(resource).ToString();
            var metadata = resource.Authors.Select(MapAuthorMetadata).ToList();

            var series = resource.Series.Select(MapSeries).ToList();
            MapSeriesLinks(series, new List<Book> { book }, resource.Series);

            return Tuple.Create(authorId, book, metadata);
        }

        private void WaitUntilRetry(HttpResponse response)
        {
            var seconds = 5;

            if (response.Headers.ContainsKey("Retry-After"))
            {
                var retryAfter = response.Headers["Retry-After"];

                if (!int.TryParse(retryAfter, out seconds))
                {
                    seconds = 5;
                }
            }

            _logger.Info("BookInfo returned 429, backing off for {0}s", seconds);

            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }

        private static AuthorMetadata MapAuthorMetadata(AuthorResource resource)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = resource.ForeignId.ToString(),
                TitleSlug = resource.ForeignId.ToString(),
                Name = resource.Name.CleanSpaces(),
                Overview = resource.Description,
                Ratings = new Ratings { Votes = resource.RatingCount, Value = (decimal)resource.AverageRating },
                Status = AuthorStatusType.Continuing
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = resource.ImageUrl,
                    CoverType = MediaCoverTypes.Poster
                });
            }

            if (resource.Url.IsNotNullOrWhiteSpace())
            {
                metadata.Links.Add(new Links { Url = resource.Url, Name = "Goodreads" });
            }

            return metadata;
        }

        private static Author MapAuthor(AuthorResource resource)
        {
            var metadata = MapAuthorMetadata(resource);

            var books = resource.Works
                .Where(x => x.ForeignId > 0 && GetAuthorId(x) == resource.ForeignId)
                .Select(MapBook)
                .ToList();

            books.ForEach(x => x.AuthorMetadata = metadata);

            var series = resource.Series.Select(MapSeries).ToList();

            MapSeriesLinks(series, books, resource.Series);

            var result = new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = series
            };

            return result;
        }

        private static void MapSeriesLinks(List<Series> series, List<Book> books, List<SeriesResource> resource)
        {
            var bookDict = books.ToDictionary(x => x.ForeignBookId);
            var seriesDict = series.ToDictionary(x => x.ForeignSeriesId);

            foreach (var book in books)
            {
                book.SeriesLinks = new List<SeriesBookLink>();
            }

            // only take series where there are some works
            foreach (var s in resource.Where(x => x.LinkItems.Any()))
            {
                if (seriesDict.TryGetValue(s.ForeignId.ToString(), out var curr))
                {
                    curr.LinkItems = s.LinkItems.Where(x => x.ForeignWorkId != 0 && bookDict.ContainsKey(x.ForeignWorkId.ToString())).Select(l => new SeriesBookLink
                    {
                        Book = bookDict[l.ForeignWorkId.ToString()],
                        Series = curr,
                        IsPrimary = l.Primary,
                        Position = l.PositionInSeries,
                        SeriesPosition = l.SeriesPosition
                    }).ToList();

                    foreach (var l in curr.LinkItems.Value)
                    {
                        l.Book.Value.SeriesLinks.Value.Add(l);
                    }
                }
            }
        }

        private static Series MapSeries(SeriesResource resource)
        {
            var series = new Series
            {
                ForeignSeriesId = resource.ForeignId.ToString(),
                Title = resource.Title,
                Description = resource.Description
            };

            return series;
        }

        private static Book MapBook(WorkResource resource)
        {
            var book = new Book
            {
                ForeignBookId = resource.ForeignId.ToString(),
                Title = resource.Title,
                TitleSlug = resource.ForeignId.ToString(),
                CleanTitle = Parser.Parser.CleanAuthorName(resource.Title),
                ReleaseDate = resource.ReleaseDate,
                Genres = resource.Genres,
                RelatedBooks = resource.RelatedWorks
            };

            book.Links.Add(new Links { Url = resource.Url, Name = "Goodreads Editions" });

            if (resource.Books != null)
            {
                book.Editions = resource.Books.Select(x => MapEdition(x)).ToList();

                // monitor the most popular release
                var mostPopular = book.Editions.Value.MaxBy(x => x.Ratings.Popularity);
                if (mostPopular != null)
                {
                    mostPopular.Monitored = true;

                    // fix work title if missing
                    if (book.Title.IsNullOrWhiteSpace())
                    {
                        book.Title = mostPopular.Title;
                    }
                }
            }
            else
            {
                book.Editions = new List<Edition>();
            }

            // If we are missing the book release date, set as the earliest edition release date
            if (!book.ReleaseDate.HasValue)
            {
                var editionReleases = book.Editions.Value
                    .Where(x => x.ReleaseDate.HasValue && x.ReleaseDate.Value.Month != 1 && x.ReleaseDate.Value.Day != 1)
                    .ToList();

                if (editionReleases.Any())
                {
                    book.ReleaseDate = editionReleases.Min(x => x.ReleaseDate.Value);
                }
                else
                {
                    editionReleases = book.Editions.Value.Where(x => x.ReleaseDate.HasValue).ToList();
                    if (editionReleases.Any())
                    {
                        book.ReleaseDate = editionReleases.Min(x => x.ReleaseDate.Value);
                    }
                }
            }

            Debug.Assert(!book.Editions.Value.Any() || book.Editions.Value.Count(x => x.Monitored) == 1, "one edition monitored");

            book.AnyEditionOk = true;

            var ratingCount = book.Editions.Value.Sum(x => x.Ratings.Votes);

            if (ratingCount > 0)
            {
                book.Ratings = new Ratings
                {
                    Votes = ratingCount,
                    Value = book.Editions.Value.Sum(x => x.Ratings.Votes * x.Ratings.Value) / ratingCount
                };
            }
            else
            {
                book.Ratings = new Ratings { Votes = 0, Value = 0 };
            }

            return book;
        }

        private static Edition MapEdition(BookResource resource)
        {
            var edition = new Edition
            {
                ForeignEditionId = resource.ForeignId.ToString(),
                TitleSlug = resource.ForeignId.ToString(),
                Isbn13 = resource.Isbn13,
                Asin = resource.Asin,
                Title = resource.Title.CleanSpaces(),
                Language = resource.Language,
                Overview = resource.Description,
                Format = resource.Format,
                IsEbook = resource.IsEbook,
                Disambiguation = resource.EditionInformation,
                Publisher = resource.Publisher,
                PageCount = resource.NumPages ?? 0,
                ReleaseDate = resource.ReleaseDate,
                Ratings = new Ratings { Votes = resource.RatingCount, Value = (decimal)resource.AverageRating }
            };

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = resource.ImageUrl,
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links { Url = resource.Url, Name = "Goodreads Book" });

            return edition;
        }

        private static int GetAuthorId(WorkResource b)
        {
            // Check if Books collection is null before attempting LINQ operations
            if (b.Books == null || !b.Books.Any())
            {
                return 0;
            }

            var book = b.Books.OrderByDescending(x => x.RatingCount * x.AverageRating)
                .FirstOrDefault(x => x.Contributors != null && x.Contributors.Any());
            return book?.Contributors?.FirstOrDefault()?.ForeignId ?? 0;
        }

        private Author MapMetadataResultToAuthor(MetadataSearchResult result)
        {
            var foreignId = $"{result.ProviderKey}:{result.ForeignId}";
            var name = result.Title ?? "Unknown";

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignId,
                Name = name,
                SortName = name,
                TitleSlug = foreignId.Replace(":", "-"),
                Status = AuthorStatusType.Continuing,
                Overview = result.Description ?? string.Empty,
                Images = new List<MediaCover.MediaCover>(),
                Links = new List<Links> { new Links { Url = $"https://www.google.com/search?q={Uri.EscapeDataString(name)}", Name = "Google" } }
            };

            if (!string.IsNullOrWhiteSpace(result.CoverUrl))
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    CoverType = MediaCover.MediaCoverTypes.Poster,
                    Url = result.CoverUrl,
                    RemoteUrl = result.CoverUrl
                });
            }

            return new Author
            {
                CleanName = Parser.Parser.CleanAuthorName(name),
                Metadata = metadata,
                Monitored = false
            };
        }

        private Book MapMetadataResultToBook(MetadataSearchResult result)
        {
            var foreignId = $"{result.ProviderKey}:{result.ForeignId}";
            var authorName = result.Authors?.FirstOrDefault() ?? "Unknown Author";

            // Reuse the existing author's id if they're already in the library (see
            // GetBookInfoFromProvider) so search results point at the same author record.
            var existingAuthor = _authorService.FindByName(authorName);
            var authorForeignId = existingAuthor?.Metadata.Value.ForeignAuthorId
                ?? $"{result.ProviderKey}:author:{authorName.ToLowerInvariant().Replace(" ", "-")}";

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = authorForeignId,
                Name = authorName,
                SortName = authorName,
                TitleSlug = authorForeignId.Replace(":", "-"),
                Status = AuthorStatusType.Continuing,
                MetadataSource = result.ProviderKey,
                Images = new List<MediaCover.MediaCover>(),
                Links = new List<Links> { new Links { Url = $"https://www.google.com/search?q={Uri.EscapeDataString(authorName)}", Name = "Google" } }
            };

            var author = new Author
            {
                CleanName = Parser.Parser.CleanAuthorName(authorName),
                Metadata = authorMetadata,
                Monitored = false
            };

            var edition = new Edition
            {
                ForeignEditionId = foreignId,
                TitleSlug = foreignId.Replace(":", "-"),
                Title = result.Title ?? "Unknown",
                Isbn13 = result.Isbn13,
                Asin = result.Asin,
                Overview = result.Description,
                PageCount = result.PageCount ?? 0,
                Publisher = result.Publisher,
                Images = new List<MediaCover.MediaCover>(),
                Links = new List<Links> { new Links { Url = $"https://www.google.com/search?q={Uri.EscapeDataString((result.Title ?? "Unknown") + " " + authorName)}", Name = "Google" } },
                Monitored = true
            };

            if (!string.IsNullOrWhiteSpace(result.CoverUrl))
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    CoverType = MediaCover.MediaCoverTypes.Cover,
                    Url = result.CoverUrl,
                    RemoteUrl = result.CoverUrl
                });
            }

            var bookTitle = result.Title ?? "Unknown";
            var book = new Book
            {
                ForeignBookId = foreignId,
                TitleSlug = foreignId.Replace(":", "-"),
                Title = bookTitle,
                CleanTitle = Parser.Parser.CleanAuthorName(bookTitle),
                Links = new List<Links> { new Links { Url = $"https://www.google.com/search?q={Uri.EscapeDataString(bookTitle + " " + authorName)}", Name = "Google" } },
                Author = new LazyLoaded<Author>(author),
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition })
            };

            return book;
        }
    }
}
