using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Surfaces a dead Goodreads API key (see GoodreadsRateLimiter.RecordApiKeyRejected) as a
    // visible System -> Status warning instead of leaving it in trace logs, since a 401 here
    // silently breaks metadata refresh for every Goodreads-pinned author. Runs on the normal
    // health-check schedule (CheckOnSchedule defaults to true), so it clears itself on its own
    // once GoodreadsRateLimiter.RecordSuccess fires from a working key.
    public class GoodreadsApiKeyCheck : HealthCheckBase
    {
        private readonly IGoodreadsRateLimiter _rateLimiter;

        public GoodreadsApiKeyCheck(IGoodreadsRateLimiter rateLimiter, ILocalizationService localizationService)
            : base(localizationService)
        {
            _rateLimiter = rateLimiter;
        }

        public override HealthCheck Check()
        {
            if (_rateLimiter.IsApiKeyLikelyDead)
            {
                return new HealthCheck(GetType(), HealthCheckResult.Error, _localizationService.GetLocalizedString("GoodreadsApiKeyHealthCheckMessage"));
            }

            return new HealthCheck(GetType());
        }
    }
}
