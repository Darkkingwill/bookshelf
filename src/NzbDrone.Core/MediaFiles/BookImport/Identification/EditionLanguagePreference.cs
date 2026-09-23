using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Profiles.Metadata;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    // Resolves which edition languages a candidate should be steered toward when the files being
    // identified don't say what language they are in. Built once per identification run so the
    // profiles are read a single time rather than per candidate.
    public class EditionLanguagePreference
    {
        private readonly Dictionary<int, HashSet<string>> _byProfile = new Dictionary<int, HashSet<string>>();
        private readonly HashSet<string> _fallback;

        public EditionLanguagePreference(IEnumerable<MetadataProfile> profiles)
        {
            var fallback = new HashSet<string>();
            var fallbackUnrestricted = false;

            foreach (var profile in profiles ?? Enumerable.Empty<MetadataProfile>())
            {
                var allowed = ParseAllowedLanguages(profile.AllowedLanguages);
                _byProfile[profile.Id] = allowed;

                // The None profile exists so an author can be tracked without pulling books; its empty
                // language list must not switch the preference off for everybody else.
                if (profile.Name == MetadataProfileService.NONE_PROFILE_NAME)
                {
                    continue;
                }

                if (allowed == null)
                {
                    fallbackUnrestricted = true;
                }
                else
                {
                    fallback.UnionWith(allowed);
                }
            }

            _fallback = fallbackUnrestricted || fallback.Count == 0 ? null : fallback;
        }

        // Null means no preference: every language is acceptable.
        public ICollection<string> For(Edition edition)
        {
            var author = edition?.Book?.Value?.Author?.Value;

            if (author != null &&
                author.MetadataProfileId > 0 &&
                _byProfile.TryGetValue(author.MetadataProfileId, out var allowed))
            {
                return allowed;
            }

            // An author not in the library yet has no profile; use the languages the configured profiles allow.
            return _fallback;
        }

        // Mirrors MetadataProfileService.FilterEditions: "null" canonicalizes to null, which lets untagged editions through.
        private static HashSet<string> ParseAllowedLanguages(string allowedLanguages)
        {
            if (allowedLanguages.IsNullOrWhiteSpace())
            {
                return null;
            }

            return new HashSet<string>(allowedLanguages.Trim(',').Split(',').Select(x => x.CanonicalizeLanguage()));
        }
    }
}
