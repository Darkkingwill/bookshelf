using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.Providers;
using Readarr.Http;

namespace Readarr.Api.V1.Author
{
    public class AuthorMetadataSourceLookupResource
    {
        public string ForeignAuthorId { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
    }

    // Looks up an author's real id under a specific Metadata Source, by name - used by the
    // author edit screen's "look up id" control so a user doesn't have to hand-find and paste
    // in a provider's id format themselves. Each source has its own id semantics under the
    // hood (see EditAuthorModalContent.js's help text), so "hardcover" reuses the existing
    // legacy author search (that source isn't part of IMetadataProviderService - see
    // BookInfoProxy.GetAuthorInfo's explicit "hardcover" exclusion), "goodreads" uses
    // Goodreads' own name-to-id endpoint, and the other multi-provider sources go through
    // IMetadataProviderService.SearchAuthors for that one named provider only.
    [V1ApiController("author/lookup/source")]
    public class AuthorMetadataSourceLookupController : Controller
    {
        private readonly ISearchForNewAuthor _legacySearchProxy;
        private readonly IGoodreadsProxy _goodreadsProxy;
        private readonly IMetadataProviderService _metadataProviderService;

        public AuthorMetadataSourceLookupController(
            ISearchForNewAuthor legacySearchProxy,
            IGoodreadsProxy goodreadsProxy,
            IMetadataProviderService metadataProviderService)
        {
            _legacySearchProxy = legacySearchProxy;
            _goodreadsProxy = goodreadsProxy;
            _metadataProviderService = metadataProviderService;
        }

        [HttpGet]
        public List<AuthorMetadataSourceLookupResource> Search([FromQuery] string source, [FromQuery] string term)
        {
            if (source.IsNullOrWhiteSpace() || term.IsNullOrWhiteSpace())
            {
                return new List<AuthorMetadataSourceLookupResource>();
            }

            switch (source.ToLowerInvariant())
            {
                case "hardcover":
                    return SearchLegacy(term);

                case "goodreads":
                    return SearchGoodreads(term);

                case "openlibrary":
                case "rreadingglasses":
                    return SearchProvider(source.ToLowerInvariant(), term);

                default:
                    return new List<AuthorMetadataSourceLookupResource>();
            }
        }

        private List<AuthorMetadataSourceLookupResource> SearchLegacy(string term)
        {
            try
            {
                return _legacySearchProxy.SearchForNewAuthor(term)
                    .Select(a => new AuthorMetadataSourceLookupResource
                    {
                        ForeignAuthorId = a.Metadata.Value.ForeignAuthorId,
                        Name = a.Metadata.Value.Name,
                        ImageUrl = a.Metadata.Value.Images?.FirstOrDefault()?.RemoteUrl
                    })
                    .Take(10)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<AuthorMetadataSourceLookupResource>();
            }
        }

        private List<AuthorMetadataSourceLookupResource> SearchGoodreads(string term)
        {
            var result = _goodreadsProxy.SearchAuthorByName(term);

            if (result == null)
            {
                return new List<AuthorMetadataSourceLookupResource>();
            }

            return new List<AuthorMetadataSourceLookupResource>
            {
                new AuthorMetadataSourceLookupResource
                {
                    ForeignAuthorId = result.ForeignAuthorId,
                    Name = result.Name
                }
            };
        }

        private List<AuthorMetadataSourceLookupResource> SearchProvider(string providerKey, string term)
        {
            return _metadataProviderService.SearchAuthors(providerKey, term)
                .Select(r => new AuthorMetadataSourceLookupResource
                {
                    ForeignAuthorId = r.ForeignId,
                    Name = r.Title,
                    ImageUrl = r.CoverUrl
                })
                .Take(10)
                .ToList();
        }
    }
}
