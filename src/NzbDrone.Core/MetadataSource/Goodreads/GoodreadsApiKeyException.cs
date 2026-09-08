using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    // Thrown on a 401 from Goodreads - the API key itself was rejected, most likely because
    // Goodreads rotated it (this has happened before with no notice). Kept distinct from a
    // generic HttpException so this specific failure is unmistakable in logs, and so a future
    // key-rediscovery pass has a single, well-defined signal to catch rather than having to
    // reinspect status codes itself.
    public class GoodreadsApiKeyException : NzbDroneException
    {
        public HttpRequest Request { get; }

        public GoodreadsApiKeyException(HttpRequest request)
            : base("Goodreads rejected the API key (401) for {0}; it may have been rotated", request.Url)
        {
            Request = request;
        }
    }
}
