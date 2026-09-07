using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    public class GoodreadsAuthorSearchResult
    {
        public string ForeignAuthorId { get; set; }
        public string Name { get; set; }
    }

    public interface IGoodreadsProxy
    {
        Book GetBookInfo(string foreignEditionId);
        Author GetAuthorInfo(long foreignAuthorId, bool useCache = false);
        GoodreadsAuthorSearchResult SearchAuthorByName(string name);
    }

    public class GoodreadsProxy : IGoodreadsProxy, IProvideSeriesInfo, IProvideListInfo
    {
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _requestBuilder;

        public GoodreadsProxy(ICachedHttpResponseService cachedHttpClient,
                              Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;

            _requestBuilder = new HttpRequestBuilder("https://www.goodreads.com/{route}")
                .AddQueryParam("key", new string("gSuM2Onzl6sjMU25HY1Xcd".Reverse().ToArray()))
                .AddQueryParam("_nc", "1")
                .SetHeader("User-Agent", "Dalvik/1.6.0 (Linux; U; Android 4.1.2; GT-I9100 Build/JZO54K)")
                .KeepAlive()
                .CreateFactory();
        }

        public SeriesResource GetSeriesInfo(int foreignSeriesId, bool useCache = false)
        {
            _logger.Debug("Getting Series with GoodreadsId of {0}", foreignSeriesId);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"series/{foreignSeriesId}")
                .AddQueryParam("format", "xml")
                .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _cachedHttpClient.Get(httpRequest, useCache, TimeSpan.FromDays(7));

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new BadRequestException(foreignSeriesId.ToString());
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var resource = httpResponse.Deserialize<ShowSeriesResource>();

            return resource.Series;
        }

        public Author GetAuthorInfo(long foreignAuthorId, bool useCache = false)
        {
            _logger.Debug("Getting Author with GoodreadsId of {0}", foreignAuthorId);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"author/show/{foreignAuthorId}")
                .AddQueryParam("format", "xml")
                .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _cachedHttpClient.Get(httpRequest, useCache, TimeSpan.FromDays(1));

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new AuthorNotFoundException(foreignAuthorId.ToString());
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var authorResource = httpResponse.Deserialize<AuthorResource>();
            var bookList = httpResponse.Deserialize<AuthorBookListResource>();

            if (authorResource == null)
            {
                throw new AuthorNotFoundException(foreignAuthorId.ToString());
            }

            return MapAuthorShow(authorResource, bookList);
        }

        // Uses Goodreads' api_author_link method (GET api/author_url/<name>), which resolves
        // a name straight to its canonical author id/page - a single best match, not a fuzzy
        // list. Verified live: returns HTTP 200 with an empty <GoodreadsResponse/> (no <author>
        // element) rather than a 404 when nothing matches, so that has to be checked explicitly
        // rather than relying on HasHttpError.
        public GoodreadsAuthorSearchResult SearchAuthorByName(string name)
        {
            _logger.Debug("Searching Goodreads for author name {0}", name);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"api/author_url/{Uri.EscapeDataString(name)}")
                .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _cachedHttpClient.Get(httpRequest, false, TimeSpan.FromHours(1));

            if (httpResponse.HasHttpError)
            {
                return null;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(httpResponse.Content);
            }
            catch (Exception)
            {
                return null;
            }

            var authorElement = document.Root?.Element("author");
            var id = authorElement?.Attribute("id")?.Value;

            if (id.IsNullOrWhiteSpace())
            {
                return null;
            }

            return new GoodreadsAuthorSearchResult
            {
                ForeignAuthorId = id,
                Name = authorElement.Element("name")?.Value?.Trim()
            };
        }

        public ListResource GetListInfo(int foreignListId, int page, bool useCache = true)
        {
            _logger.Debug("Getting List with GoodreadsId of {0}", foreignListId);

            var httpRequest = new HttpRequestBuilder("https://www.goodreads.com/book/list/listopia.xml")
                .AddQueryParam("key", new string("whFzJP3Ud0gZsAdyXxSr7T".Reverse().ToArray()))
                .AddQueryParam("_nc", "1")
                .AddQueryParam("format", "xml")
                .AddQueryParam("id", foreignListId)
                .AddQueryParam("items_per_page", 30)
                .AddQueryParam("page", page)
                .SetHeader("User-Agent", "Goodreads/3.33.1 (iPhone; iOS 14.3; Scale/3.00)")
                .SetHeader("X_APPLE_DEVICE_MODEL", "iPhone")
                .SetHeader("x-gr-os-version", "iOS 14.3")
                .SetHeader("Accept-Language", "en-GB;q=1")
                .SetHeader("X_APPLE_APP_VERSION", "761")
                .SetHeader("x-gr-app-version", "761")
                .SetHeader("x-gr-hw-model", "iPhone11,6")
                .SetHeader("X_APPLE_SYSTEM_VERSION", "14.3")
                .KeepAlive()
                .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _cachedHttpClient.Get(httpRequest, useCache, TimeSpan.FromDays(7));

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new BadRequestException(foreignListId.ToString());
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            return httpResponse.Deserialize<ListResource>();
        }

        public Book GetBookInfo(string foreignEditionId)
        {
            _logger.Debug("Getting Book with GoodreadsId of {0}", foreignEditionId);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"api/book/basic_book_data/{foreignEditionId}")
                .AddQueryParam("format", "xml")
                .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _cachedHttpClient.Get(httpRequest, false, TimeSpan.FromDays(90));

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BookNotFoundException(foreignEditionId);
                }
                else if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new BadRequestException(foreignEditionId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var resource = httpResponse.Deserialize<BookResource>();

            var book = MapBook(resource);

            var authors = resource.Authors.SelectList(MapAuthor);
            book.AuthorMetadata = authors.First();

            return book;
        }

        private static AuthorMetadata MapAuthor(AuthorSummaryResource resource)
        {
            var author = new AuthorMetadata
            {
                ForeignAuthorId = resource.Id.ToString(),
                Name = resource.Name.CleanSpaces(),
                TitleSlug = resource.Id.ToString()
            };

            author.SortName = author.Name.ToLower();
            author.NameLastFirst = author.Name.ToLastFirst();
            author.SortNameLastFirst = author.NameLastFirst.ToLower();

            if (resource.RatingsCount.HasValue)
            {
                author.Ratings = new Ratings
                {
                    Votes = resource.RatingsCount ?? 0,
                    Value = resource.AverageRating ?? 0
                };
            }

            return author;
        }

        private static Author MapAuthorShow(AuthorResource resource, AuthorBookListResource bookList)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = resource.Id.ToString(),
                TitleSlug = resource.Id.ToString(),
                Name = resource.Name.CleanSpaces(),
                Overview = resource.About,
                Status = AuthorStatusType.Continuing,
                MetadataSource = "goodreads"
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover { Url = resource.ImageUrl, CoverType = MediaCover.MediaCoverTypes.Poster });
            }

            if (resource.Link.IsNotNullOrWhiteSpace())
            {
                metadata.Links.Add(new Links { Url = resource.Link, Name = "Goodreads" });
            }

            // Each entry is a distinct Goodreads book (edition), grouped/keyed by its work id
            // downstream - MapBook already returns one Book per BookResource, so just dedupe
            // in case the same work shows up more than once (e.g. multiple editions listed).
            var resources = (bookList?.List ?? new List<BookResource>())
                .Where(b => b.Work != null && b.Work.Id > 0)
                .ToList();

            var books = resources
                .Select(MapBook)
                .DistinctBy(b => b.ForeignBookId)
                .ToList();

            books.ForEach(b => b.AuthorMetadata = metadata);

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = BuildSeriesFromTitles(resources, books)
            };
        }

        // Goodreads' author/show response has no separate series structure at all - verified
        // live against the real API, despite AuthorSeriesListResource/WorkResource.SetSeriesInfo
        // existing in this codebase for what turned out to be a different (series/show) endpoint
        // that isn't reachable from just an author id. What IS reliable is the title convention
        // Goodreads uses everywhere: a series entry's <title> is <title_without_series> plus a
        // "(Series Name, #Position)" suffix - e.g. "Orphan X (Orphan X, #1)" /
        // "Orphan X". Diffing those two already-parsed fields extracts real series membership
        // with no extra API calls, confirmed against Gregg Hurwitz's real 10-book Orphan X
        // series (including one entry missing the comma - "(Orphan X #8)" - hence the optional
        // comma in the regex).
        private static readonly Regex SeriesSuffixRegex = new Regex(@"^\((?<series>.+?),?\s*#(?<position>[\d.]+)\)$", RegexOptions.Compiled);

        internal static List<Series> BuildSeriesFromTitles(List<BookResource> resources, List<Book> books)
        {
            var bookDict = books.ToDictionary(b => b.ForeignBookId);
            books.ForEach(b => b.SeriesLinks = new List<SeriesBookLink>());

            var seriesByForeignId = new Dictionary<string, Series>();

            foreach (var resource in resources)
            {
                var workId = resource.Work?.Id.ToString();
                if (workId == null || !bookDict.TryGetValue(workId, out var book))
                {
                    continue;
                }

                var fullTitle = resource.Title;
                var cleanTitle = resource.TitleWithoutSeries;

                if (fullTitle.IsNullOrWhiteSpace() || cleanTitle.IsNullOrWhiteSpace() || !fullTitle.StartsWith(cleanTitle))
                {
                    continue;
                }

                var suffix = fullTitle.Substring(cleanTitle.Length).Trim();
                var match = SeriesSuffixRegex.Match(suffix);

                if (!match.Success)
                {
                    continue;
                }

                var seriesName = match.Groups["series"].Value.Trim();
                var position = match.Groups["position"].Value;

                if (seriesName.IsNullOrWhiteSpace())
                {
                    continue;
                }

                // Prefixed and derived from the name (not a real numeric id, since Goodreads
                // doesn't give us one this way) - keeps this id space clearly distinct so it can
                // never collide with a real provider id from elsewhere.
                var foreignSeriesId = "goodreads-title:" + seriesName.ToLowerInvariant();

                if (!seriesByForeignId.TryGetValue(foreignSeriesId, out var series))
                {
                    series = new Series
                    {
                        ForeignSeriesId = foreignSeriesId,
                        Title = seriesName,
                        Numbered = true,
                        LinkItems = new List<SeriesBookLink>()
                    };
                    seriesByForeignId[foreignSeriesId] = series;
                }

                double.TryParse(position, NumberStyles.Any, CultureInfo.InvariantCulture, out var seriesPosition);

                var link = new SeriesBookLink
                {
                    Book = book,
                    Series = series,
                    Position = position,
                    SeriesPosition = (int)seriesPosition
                };

                series.LinkItems.Value.Add(link);
                book.SeriesLinks.Value.Add(link);
            }

            var result = seriesByForeignId.Values.Where(s => s.LinkItems.Value.Count > 0).ToList();
            result.ForEach(s => s.WorkCount = s.LinkItems.Value.Count);

            return result;
        }

        private static Book MapBook(BookResource resource)
        {
            var title = (resource.Work.OriginalTitle ?? resource.TitleWithoutSeries).CleanSpaces();

            var book = new Book
            {
                ForeignBookId = resource.Work.Id.ToString(),
                Title = title,
                CleanTitle = Parser.Parser.CleanAuthorName(title),
                TitleSlug = resource.Work.Id.ToString(),
                ReleaseDate = resource.Work.OriginalPublicationDate ?? resource.PublicationDate,
                Ratings = new Ratings { Votes = resource.Work.RatingsCount, Value = resource.Work.AverageRating },
                AnyEditionOk = true
            };

            if (resource.EditionsUrl != null)
            {
                book.Links.Add(new Links { Url = resource.EditionsUrl, Name = "Goodreads Editions" });
            }

            var edition = new Edition
            {
                ForeignEditionId = resource.Id.ToString(),
                TitleSlug = resource.Id.ToString(),
                Isbn13 = resource.Isbn13,
                Asin = resource.Asin ?? resource.KindleAsin,
                Title = resource.TitleWithoutSeries,
                Language = resource.LanguageCode,
                Overview = resource.Description,
                Format = resource.Format,
                IsEbook = resource.IsEbook,
                Disambiguation = resource.EditionInformation,
                Publisher = resource.Publisher,
                PageCount = resource.Pages,
                ReleaseDate = resource.PublicationDate,
                Ratings = new Ratings { Votes = resource.RatingsCount, Value = resource.AverageRating },
                Monitored = true
            };

            edition.Links.Add(new Links { Url = resource.Url, Name = "Goodreads Book" });

            book.Editions = new List<Edition> { edition };

            Debug.Assert(!book.Editions.Value.Any() || book.Editions.Value.Count(x => x.Monitored) == 1, "one edition monitored");

            return book;
        }
    }
}
