using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaCover.Fallback
{
    public interface IAuthorPictureFinder
    {
        // Null means every provider answered and none had a picture. Throws FallbackLookupUnavailableException when none had
        // one but a provider could not answer, so the lookup is worth repeating.
        CoverFallbackResult Find(AuthorPictureSearch search);
    }

    public class AuthorPictureFinder : IAuthorPictureFinder
    {
        private readonly List<IAuthorPictureProvider> _providers;
        private readonly Logger _logger;

        public AuthorPictureFinder(IEnumerable<IAuthorPictureProvider> providers, Logger logger)
        {
            _providers = providers.OrderBy(p => p.Order).ToList();
            _logger = logger;
        }

        public CoverFallbackResult Find(AuthorPictureSearch search)
        {
            if (!IsSearchable(search))
            {
                return null;
            }

            Exception unavailable = null;

            foreach (var provider in _providers)
            {
                try
                {
                    var url = provider.FindPictureUrl(search);

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return new CoverFallbackResult(provider.Name, url);
                    }
                }
                catch (Exception ex)
                {
                    // One provider being down or rate limited must not stop the next one from being tried.
                    _logger.Debug(ex, "Picture lookup via {0} failed for {1}", provider.Name, search.Name);

                    if (unavailable == null && FallbackLookupUnavailableException.IsTransient(ex))
                    {
                        unavailable = ex;
                    }
                }
            }

            // Nothing matched, but a provider could not answer. That is not "no picture", so it must not be remembered as one.
            if (unavailable != null)
            {
                throw new FallbackLookupUnavailableException($"The picture lookup for {search.Name} was incomplete: {unavailable.Message}", unavailable);
            }

            return null;
        }

        // Several people in one name ("Rhett C. Bruno; M.B. Vance") or no person at all ("Anonymous") has no one face to find,
        // and a picture is only trusted when one of the person's books confirms it.
        private static bool IsSearchable(AuthorPictureSearch search)
        {
            return search.Name.IsNotNullOrWhiteSpace() &&
                   !search.Name.Contains(';') &&
                   !string.Equals(search.Name.Trim(), "Anonymous", StringComparison.OrdinalIgnoreCase) &&
                   search.BookTitles != null &&
                   search.BookTitles.Any(t => t.IsNotNullOrWhiteSpace());
        }
    }
}
