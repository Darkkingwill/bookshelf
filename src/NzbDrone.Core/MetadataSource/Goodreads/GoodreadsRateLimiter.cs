using System;
using System.Threading;
using NLog;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    public interface IGoodreadsRateLimiter
    {
        void WaitForSlot();
        void RecordRateLimited();
        void RecordApiKeyRejected();
        void RecordSuccess();
        bool IsApiKeyLikelyDead { get; }
    }

    // Shared across every GoodreadsProxy call (registered as a singleton, like every other
    // interface-implementing service in this app) so all Goodreads requests - regardless of
    // which author/book/series is being refreshed, or how many refreshes are running
    // concurrently - are throttled through one clock instead of each racing ahead
    // independently. Goodreads has no public rate-limit docs for this scraped surface, so this
    // errs conservative (a few requests per second) rather than tuning against a number that
    // could change without notice.
    //
    // A 403 (WAFForbiddenException - AWS is rate-limiting this IP) means retrying immediately
    // makes things worse, not better: it extends a cooldown window that blocks every
    // subsequent request, Goodreads-source or not, until it passes. A 401 (the API key itself
    // was rejected) is a different failure entirely - see GoodreadsApiKeyException - and does
    // not trigger this cooldown, since a bad key isn't evidence the IP is being throttled.
    public class GoodreadsRateLimiter : IGoodreadsRateLimiter
    {
        private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1000.0 / 3); // ~3 req/s
        private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromSeconds(60);

        private readonly object _lock = new object();
        private readonly Logger _logger;

        private DateTime _nextAllowedRequestUtc = DateTime.MinValue;
        private DateTime _blockedUntilUtc = DateTime.MinValue;
        private bool _apiKeyRejected;

        public GoodreadsRateLimiter(Logger logger)
        {
            _logger = logger;
        }

        // There's no known live surface that re-embeds this key the way ricetim/
        // readarr-rresurrected's GraphQL key gets re-embedded in Goodreads' own current web
        // pages (confirmed: neither key this app uses shows up on a current goodreads.com book
        // page) - so there is nothing to automatically re-scrape here. What this CAN do
        // honestly is make the failure visible immediately via GoodreadsApiKeyCheck instead of
        // sitting silently in trace logs, and self-clear the moment a manually-updated key
        // starts working again, rather than needing a restart to notice.
        public bool IsApiKeyLikelyDead => _apiKeyRejected;

        public void RecordApiKeyRejected()
        {
            lock (_lock)
            {
                if (!_apiKeyRejected)
                {
                    _logger.Error("Goodreads rejected the API key (401). Metadata refreshes for Goodreads-sourced authors and books will fail until this is fixed.");
                }

                _apiKeyRejected = true;
            }
        }

        public void RecordSuccess()
        {
            if (_apiKeyRejected)
            {
                lock (_lock)
                {
                    if (_apiKeyRejected)
                    {
                        _logger.Info("A Goodreads request succeeded again; clearing the earlier API key rejection.");
                    }

                    _apiKeyRejected = false;
                }
            }
        }

        public void WaitForSlot()
        {
            TimeSpan wait;

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var earliestAllowed = _blockedUntilUtc > _nextAllowedRequestUtc ? _blockedUntilUtc : _nextAllowedRequestUtc;

                wait = earliestAllowed > now ? earliestAllowed - now : TimeSpan.Zero;

                _nextAllowedRequestUtc = now + wait + MinInterval;
            }

            if (wait > TimeSpan.Zero)
            {
                Thread.Sleep(wait);
            }
        }

        public void RecordRateLimited()
        {
            lock (_lock)
            {
                var cooldownUntil = DateTime.UtcNow + RateLimitCooldown;

                if (cooldownUntil > _blockedUntilUtc)
                {
                    _blockedUntilUtc = cooldownUntil;
                    _logger.Warn("Goodreads is rate-limiting this IP (403); pausing all Goodreads requests for {0}s.", RateLimitCooldown.TotalSeconds);
                }
            }
        }
    }
}
