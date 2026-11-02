using System.Collections.Generic;

namespace NzbDrone.Core.MediaCover.Fallback
{
    public class AuthorPictureSearch
    {
        public string Name { get; set; }
        public IList<string> BookTitles { get; set; }
    }

    // One place to look for an author's picture when the metadata source gave none (or only its "no photo" silhouette). A wrong
    // face is worse than none, so a provider must confirm the person it found wrote one of BookTitles. Providers are tried in
    // ascending Order and must return null, never throw, when they have nothing they are sure of.
    public interface IAuthorPictureProvider
    {
        string Name { get; }
        int Order { get; }
        string FindPictureUrl(AuthorPictureSearch search);
    }
}
