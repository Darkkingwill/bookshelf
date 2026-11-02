using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.Providers;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public partial class BookInfoProxy : IProvideAuthorInfo, IProvideBookInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        // Opt-in, per-author variant of the "goodreads" pin - see GetGoodreadsProxyAuthorInfo.
        private const string GoodreadsProxySource = "goodreads-proxy";

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

        // Book/author IDs sourced from a fallback metadata provider are synthesized as
        // "{providerKey}:{rawId}" (see MapMetadataResultToBook/Author below). The legacy
        // REST lookup (PollBook) doesn't understand that shape and 400s on it, so route
        // those back to the provider that produced them instead.
        private static readonly HashSet<string> KnownProviderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "googlebooks", "openlibrary", "audible", "hardcover", "rreadingglasses"
        };
    }
}
