using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover.Fallback
{
    // Thrown by a finder when no provider found a match and at least one of them could not answer (rate limited, down or timed
    // out). "Nothing found" is then not a real answer, so the caller must not remember it as a miss.
    public class FallbackLookupUnavailableException : Exception
    {
        public FallbackLookupUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        // A failure that says nothing about whether the picture or cover exists. Any other failure (a 403, a 404, an unreadable
        // answer) is treated as "no match", as before, so a provider that always fails cannot keep a book or author from
        // being marked as looked up.
        public static bool IsTransient(Exception exception)
        {
            switch (exception)
            {
                case HttpException http when http.Response != null:
                    return http.Response.StatusCode == HttpStatusCode.TooManyRequests || (int)http.Response.StatusCode >= 500;
                case WebException _:
                case HttpRequestException _:
                case TimeoutException _:
                case TaskCanceledException _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
