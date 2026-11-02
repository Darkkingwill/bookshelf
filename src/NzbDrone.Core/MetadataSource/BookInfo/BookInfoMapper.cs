using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Providers;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    // Pure translations from the metadata service's and the providers' resources to Bookshelf's model. Nothing here touches the
    // network, the database or a cache, so it can be read and tested on its own.
    public static class BookInfoMapper
    {
        public static Author MapProviderAuthorResult(string metadataSource, MetadataAuthorResult result)
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

        public static Book MapProviderWorkToBook(string metadataSource, MetadataSearchResult result)
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

        public static AuthorMetadata MapAuthorMetadata(AuthorResource resource)
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

        public static Author MapAuthor(AuthorResource resource)
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

        public static void MapSeriesLinks(List<Series> series, List<Book> books, List<SeriesResource> resource)
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

        public static Series MapSeries(SeriesResource resource)
        {
            var series = new Series
            {
                ForeignSeriesId = resource.ForeignId.ToString(),
                Title = resource.Title,
                Description = resource.Description
            };

            return series;
        }

        public static Book MapBook(WorkResource resource)
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

        public static Edition MapEdition(BookResource resource)
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

        public static int GetAuthorId(WorkResource b)
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
    }
}
