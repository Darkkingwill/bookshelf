using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using LazyCache;
using Microsoft.Extensions.Caching.Memory;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    // Polling the metadata service while it builds an author or book.
    public partial class BookInfoProxy
    {
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

            return BookInfoMapper.MapAuthor(resource);
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

            var book = BookInfoMapper.MapBook(resource);
            var authorId = BookInfoMapper.GetAuthorId(resource).ToString();
            var metadata = resource.Authors.Select(BookInfoMapper.MapAuthorMetadata).ToList();

            var series = resource.Series.Select(BookInfoMapper.MapSeries).ToList();
            BookInfoMapper.MapSeriesLinks(series, new List<Book> { book }, resource.Series);

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
    }
}
