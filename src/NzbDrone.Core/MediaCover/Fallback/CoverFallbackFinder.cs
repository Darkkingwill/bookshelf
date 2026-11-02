using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.MediaCover.Fallback
{
    public interface ICoverFallbackFinder
    {
        // Null means every provider answered and none had a cover. Throws FallbackLookupUnavailableException when none had
        // one but a provider could not answer, so the lookup is worth repeating.
        CoverFallbackResult Find(CoverSearch search);
    }

    public class CoverFallbackFinder : ICoverFallbackFinder
    {
        private readonly List<ICoverFallbackProvider> _providers;
        private readonly Logger _logger;

        public CoverFallbackFinder(IEnumerable<ICoverFallbackProvider> providers, Logger logger)
        {
            _providers = providers.OrderBy(p => p.Order).ToList();
            _logger = logger;
        }

        public CoverFallbackResult Find(CoverSearch search)
        {
            var titles = CoverMatcher.SearchTitles(search.Title).ToList();
            Exception unavailable = null;

            foreach (var provider in _providers)
            {
                try
                {
                    foreach (var title in titles)
                    {
                        var attempt = title == search.Title
                            ? search
                            : new CoverSearch { Title = title, Author = search.Author, Isbn13 = search.Isbn13 };
                        var url = provider.FindCoverUrl(attempt);

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return new CoverFallbackResult(provider.Name, url);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // One provider being down or rate limited must not stop the next one from being tried.
                    _logger.Debug(ex, "Cover lookup via {0} failed for {1}", provider.Name, search.Title);

                    if (unavailable == null && FallbackLookupUnavailableException.IsTransient(ex))
                    {
                        unavailable = ex;
                    }
                }
            }

            // Nothing matched, but a provider could not answer. That is not "no cover", so it must not be remembered as one.
            if (unavailable != null)
            {
                throw new FallbackLookupUnavailableException($"The cover lookup for {search.Title} was incomplete: {unavailable.Message}", unavailable);
            }

            return null;
        }
    }
}
