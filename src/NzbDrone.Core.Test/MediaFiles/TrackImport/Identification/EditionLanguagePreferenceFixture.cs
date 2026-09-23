using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    // Audiobook files almost never carry a language tag, so the language term never applied and a
    // translated edition could win on title or year alone. Once a file sits on an edition, the
    // metadata profile's language filter exempts that edition permanently. Seen live on an
    // English-only library: 27 files ended up on Italian, German, Portuguese and other editions,
    // e.g. Stephen King's "Cell" on a Spanish edition also titled "Cell".
    [TestFixture]
    public class EditionLanguagePreferenceFixture : CoreTest
    {
        private const string AuthorName = "Stephen King";
        private static readonly string[] English = { "eng" };

        private static Edition GivenEdition(string title, string language, int metadataProfileId = 1)
        {
            var metadata = new AuthorMetadata { Name = AuthorName };

            var book = new Book
            {
                Title = title,
                AuthorMetadata = metadata,
                Author = new Author { Metadata = metadata, MetadataProfileId = metadataProfileId }
            };

            var edition = new Edition
            {
                Title = title,
                Language = language,
                Monitored = true,
                Book = book
            };

            book.Editions = new List<Edition> { edition };

            return edition;
        }

        private static List<LocalBook> GivenUntaggedAudiobook(string title, string language = null)
        {
            return new List<LocalBook>
            {
                new LocalBook
                {
                    Path = $"/media/{AuthorName}/{title}/{title}.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        BookTitle = title,
                        Authors = new List<string> { AuthorName },
                        Language = language
                    }
                }
            };
        }

        [Test]
        public void untagged_audiobook_should_prefer_allowed_language_over_same_titled_translation()
        {
            var tracks = GivenUntaggedAudiobook("Cell");
            var english = GivenEdition("Cell", "eng");
            var spanish = GivenEdition("Cell", "spa");

            // Before the fix: nothing tells the two apart, so whichever is scored first wins.
            DistanceCalculator.BookDistance(tracks, english).NormalizedDistance()
                .Should().Be(DistanceCalculator.BookDistance(tracks, spanish).NormalizedDistance());

            var dEnglish = DistanceCalculator.BookDistance(tracks, english, English).NormalizedDistance();
            var dSpanish = DistanceCalculator.BookDistance(tracks, spanish, English).NormalizedDistance();

            dEnglish.Should().BeLessThan(dSpanish);
        }

        [Test]
        public void language_codes_should_be_compared_after_canonicalizing()
        {
            // "ger" and "deu" are both German; the profile stores canonical codes.
            var tracks = GivenUntaggedAudiobook("The Bazaar of Bad Dreams");
            var german = GivenEdition("The Bazaar of Bad Dreams", "ger");
            var english = GivenEdition("The Bazaar of Bad Dreams", "eng");

            DistanceCalculator.BookDistance(tracks, english, English).NormalizedDistance()
                .Should().BeLessThan(DistanceCalculator.BookDistance(tracks, german, English).NormalizedDistance());
        }

        [Test]
        public void explicit_file_language_should_win_over_profile_preference()
        {
            // A file that says it is Italian is Italian, whatever the profile prefers.
            var tracks = GivenUntaggedAudiobook("The Dome", "ita");
            var italian = GivenEdition("The Dome", "ita");
            var english = GivenEdition("The Dome", "eng");

            DistanceCalculator.BookDistance(tracks, italian, English).NormalizedDistance()
                .Should().BeLessThan(DistanceCalculator.BookDistance(tracks, english, English).NormalizedDistance());
        }

        [Test]
        public void edition_without_a_language_should_not_be_penalised()
        {
            var tracks = GivenUntaggedAudiobook("Revival");
            var unknown = GivenEdition("Revival", null);

            DistanceCalculator.BookDistance(tracks, unknown, English).NormalizedDistance()
                .Should().Be(DistanceCalculator.BookDistance(tracks, unknown).NormalizedDistance());
        }

        [Test]
        public void preference_should_come_from_the_authors_metadata_profile()
        {
            var preference = new EditionLanguagePreference(new[]
            {
                new MetadataProfile { Id = 1, Name = "Standard", AllowedLanguages = "eng" },
                new MetadataProfile { Id = 3, Name = "Foreign", AllowedLanguages = "ger, fre" }
            });

            preference.For(GivenEdition("Cell", "eng", metadataProfileId: 1)).Should().BeEquivalentTo(new[] { "eng" });
            preference.For(GivenEdition("Cell", "eng", metadataProfileId: 3)).Should().BeEquivalentTo(new[] { "deu", "fra" });
        }

        [Test]
        public void author_not_in_library_should_use_configured_profiles_but_ignore_the_none_profile()
        {
            var preference = new EditionLanguagePreference(new[]
            {
                new MetadataProfile { Id = 1, Name = "Standard", AllowedLanguages = "eng" },
                new MetadataProfile { Id = 2, Name = MetadataProfileService.NONE_PROFILE_NAME, AllowedLanguages = "" },
                new MetadataProfile { Id = 3, Name = "Indie", AllowedLanguages = "eng" }
            });

            preference.For(GivenEdition("Cell", "spa", metadataProfileId: 0)).Should().BeEquivalentTo(new[] { "eng" });
        }

        [Test]
        public void unrestricted_profile_should_turn_the_preference_off()
        {
            var preference = new EditionLanguagePreference(new[]
            {
                new MetadataProfile { Id = 1, Name = "Standard", AllowedLanguages = "eng" },
                new MetadataProfile { Id = 4, Name = "Anything", AllowedLanguages = "" }
            });

            preference.For(GivenEdition("Cell", "spa", metadataProfileId: 4)).Should().BeNull();
            preference.For(GivenEdition("Cell", "spa", metadataProfileId: 0)).Should().BeNull();
        }
    }
}
