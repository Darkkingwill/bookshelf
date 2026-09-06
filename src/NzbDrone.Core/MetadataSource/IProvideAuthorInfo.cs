using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IProvideAuthorInfo
    {
        // metadataSource pins the lookup to a specific provider ("hardcover", "goodreads",
        // "googlebooks", "openlibrary", "audible", "rreadingglasses") rather than whatever's
        // configured as the global primary metadata source. Pass null/empty to fall back to
        // that legacy behavior.
        Author GetAuthorInfo(string readarrId, bool useCache = true, string metadataSource = null);
        HashSet<string> GetChangedAuthors(DateTime startTime);
    }
}
