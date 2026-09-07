using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IProvideBookInfo
    {
        // metadataSource pins the lookup to a specific provider, same as
        // IProvideAuthorInfo.GetAuthorInfo - pass null/empty to fall back to legacy behavior.
        Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id, string metadataSource = null);
    }
}
