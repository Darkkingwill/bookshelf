using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    // Thrown on a 403 from Goodreads - AWS WAF rate-limiting this IP, not a rejected request.
    // Distinct from every other Goodreads failure so callers (and logs) don't mistake "we are
    // being throttled, back off" for "this author/book/series doesn't exist" or "the API key is
    // dead" - retrying either of the latter is reasonable, retrying this one just prolongs it.
    public class GoodreadsRateLimitedException : NzbDroneException
    {
        public HttpRequest Request { get; }

        public GoodreadsRateLimitedException(HttpRequest request)
            : base("Goodreads returned 403 (rate-limited) for {0}", request.Url)
        {
            Request = request;
        }
    }
}
