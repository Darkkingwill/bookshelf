using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.MediaCover.Fallback
{
    public interface ICoverFallbackFinder
    {
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
            foreach (var provider in _providers)
            {
                try
                {
                    var url = provider.FindCoverUrl(search);

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return new CoverFallbackResult(provider.Name, url);
                    }
                }
                catch (Exception ex)
                {
                    // One provider being down or rate limited must not stop the next one from being tried.
                    _logger.Debug(ex, "Cover lookup via {0} failed for {1}", provider.Name, search.Title);
                }
            }

            return null;
        }
    }
}
