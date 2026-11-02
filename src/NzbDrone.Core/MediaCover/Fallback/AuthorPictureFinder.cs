using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaCover.Fallback
{
    public interface IAuthorPictureFinder
    {
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
                }
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
