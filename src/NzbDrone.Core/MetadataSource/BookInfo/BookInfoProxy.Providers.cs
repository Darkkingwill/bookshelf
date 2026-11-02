using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Providers;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    // Metadata sources other than the default one.
    public partial class BookInfoProxy
    {
        private Tuple<string, Book, List<AuthorMetadata>> GetBookInfoFromSource(string metadataSource, string foreignBookId)
        {
            if (IsGoodreadsProxySource(metadataSource))
            {
                // Same Goodreads id space and the same proxy-first book lookup as "goodreads"; only the
                // tag it leaves on the author metadata differs, so the pin survives a refresh.
                var tuple = GetGoodreadsBookInfo(foreignBookId);

                foreach (var authorMetadata in tuple.Item3)
                {
                    authorMetadata.MetadataSource = GoodreadsProxySource;
                }

                return tuple;
            }

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

        // "goodreads-proxy" is an opt-in, per-author variant of the "goodreads" pin. It keeps the same
        // Goodreads id space (so nothing about identity changes) but fetches the author's book list
        // from the local metadata proxy first instead of straight from Goodreads. The direct
        // author/list feed carries no language for any edition, which makes a language-restricted
        // metadata profile either drop every new book (only "eng" allowed) or let every foreign
        // edition in ("eng, null"). The proxy's list is deduplicated and has real languages.
        // The trade-off is freshness: the proxy may serve an author for days, where the direct feed
        // is at most a day old.
        private static bool IsGoodreadsProxySource(string metadataSource)
        {
            return metadataSource.IsNotNullOrWhiteSpace() &&
                   metadataSource.Equals(GoodreadsProxySource, StringComparison.OrdinalIgnoreCase);
        }

        private Author GetGoodreadsProxyAuthorInfo(string foreignAuthorId)
        {
            Author author;

            try
            {
                author = PollAuthor(foreignAuthorId);
            }
            catch (Exception ex)
            {
                // Any miss falls back, including a clean "not found". Unlike the book path (where a
                // work id and an edition id are different numbering spaces, so a retry can return a
                // wrong book), an author id is the same in the proxy and on Goodreads, and a refresh
                // treats "not found" as the author being gone - an author with no files is deleted.
                // So only Goodreads' own answer to the same id is allowed to say that.
                _logger.Warn(ex, "Local metadata proxy could not supply author {0}, falling back to Goodreads directly", foreignAuthorId);
                author = GetAuthorInfoFromSource("goodreads", foreignAuthorId);
            }

            // Whichever path resolved it, keep the pin so later refreshes route back here.
            author.Metadata.Value.MetadataSource = GoodreadsProxySource;
            return author;
        }

        private Author GetAuthorInfoFromSource(string metadataSource, string foreignAuthorId)
        {
            if (IsGoodreadsProxySource(metadataSource))
            {
                return GetGoodreadsProxyAuthorInfo(foreignAuthorId);
            }

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

            return BookInfoMapper.MapProviderAuthorResult(metadataSource, result);
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
