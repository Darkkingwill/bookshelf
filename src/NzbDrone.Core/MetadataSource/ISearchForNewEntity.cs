using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewEntity
    {
        // source pins the search to a specific provider, same as IProvideAuthorInfo.GetAuthorInfo
        // - pass null/empty to use the default (legacy/bookinfo.pro) search.
        List<object> SearchForNewEntity(string title, string source = null);
    }
}
