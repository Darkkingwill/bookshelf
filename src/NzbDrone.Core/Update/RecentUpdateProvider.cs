using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Update
{
    public interface IRecentUpdateProvider
    {
        List<UpdatePackage> GetRecentUpdatePackages();
    }

    // Bookshelf is a Docker-image-based fork; it isn't published to the
    // upstream Servarr update service, so the "What's New" list is our own
    // fork changelog instead of a call out to services.readarr.com.
    public class RecentUpdateProvider : IRecentUpdateProvider
    {
        public List<UpdatePackage> GetRecentUpdatePackages()
        {
            return new List<UpdatePackage>
            {
                new UpdatePackage
                {
                    Version = new Version(1, 0, 0, 0),
                    ReleaseDate = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                    Branch = "hardcover",
                    Changes = new UpdateChanges
                    {
                        New = new List<string>
                        {
                            "Convert to M4B: merge a multi-part audiobook into a single chaptered M4B file, with embedded cover art, right from the book page",
                            "MyAnonaMouse search results now show narrator and series info parsed from the release name",
                            "Goodreads requests are now rate-limited and back off automatically on 403s, with a health check that flags a dead API key on 401"
                        },
                        Fixed = new List<string>
                        {
                            "Command status messages (e.g. an in-progress M4B conversion) now show up in the sidebar immediately on page load, instead of only after a reconnect"
                        }
                    }
                }
            };
        }
    }
}
