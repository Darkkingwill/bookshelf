using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Books
{
    public static class EditionExtensions
    {
        // A book is expected to have exactly one monitored edition, but a refresh, a manual add or a provider that
        // reports no monitored edition can leave it with none (or several). Prefer the monitored one and fall back to
        // the first edition instead of letting Single() throw; the result is null only when there are no editions.
        public static Edition GetMonitoredEdition(this IEnumerable<Edition> editions)
        {
            return editions?.FirstOrDefault(x => x.Monitored) ?? editions?.FirstOrDefault();
        }
    }
}
