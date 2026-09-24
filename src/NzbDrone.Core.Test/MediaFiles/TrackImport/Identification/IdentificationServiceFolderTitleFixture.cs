using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    // When the tags give no match, or a poor or different one, an exact and unique folder-title match may
    // decide - see CandidateService.GetDbCandidatesFromFolder.
    [TestFixture]
    public class IdentificationServiceFolderTitleFixture : CoreTest<IdentificationService>
    {
        private const string AuthorName = "Neal Stephenson";
        private const string Folder = "/media/audiobooks/Neal Stephenson/The Baroque Cycle/7 - Currency";

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IAugmentingService>()
                .Setup(x => x.Augment(It.IsAny<LocalEdition>()));

            Mocker.GetMock<IAugmentingService>()
                .Setup(x => x.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()));

            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>());

            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromFolder(It.IsAny<LocalEdition>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>());
        }

        private static List<LocalBook> GivenTracks(string tagTitle)
        {
            return new List<LocalBook>
            {
                new LocalBook
                {
                    Path = Folder + "/Currency.m4b",
                    Size = 1000,
                    Quality = new QualityModel(Quality.MP3),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        BookTitle = tagTitle,
                        Authors = new List<string> { AuthorName }
                    }
                }
            };
        }

        private static Edition GivenEdition(int id, string title)
        {
            var metadata = new AuthorMetadata { Name = AuthorName };

            var book = new Book
            {
                Id = id,
                Title = title,
                AuthorMetadata = metadata,
                Author = new Author { Metadata = metadata },
                SeriesLinks = new List<SeriesBookLink>()
            };

            var edition = new Edition
            {
                Id = id,
                BookId = id,
                Title = title,
                Monitored = true,
                Book = book
            };

            book.Editions = new List<Edition> { edition };

            return edition;
        }

        private static ImportDecisionMakerConfig GivenScanConfig()
        {
            return new ImportDecisionMakerConfig
            {
                SingleRelease = true,
                IncludeExisting = true,
                KeepAllEditions = true
            };
        }

        private void GivenFolderCandidate(Edition edition)
        {
            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromFolder(It.IsAny<LocalEdition>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = edition, ExistingFiles = new List<BookFile>() } });
        }

        private void GivenTagCandidate(Edition edition)
        {
            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = edition, ExistingFiles = new List<BookFile>() } });
        }

        [Test]
        public void should_match_by_folder_title_when_the_tags_find_nothing()
        {
            GivenFolderCandidate(GivenEdition(2, "Currency"));

            var result = Subject.Identify(GivenTracks("Some Unrelated Tag"), new IdentificationOverrides(), GivenScanConfig());

            result.Should().HaveCount(1);
            result[0].Edition.Should().NotBeNull();
            result[0].Edition.Title.Should().Be("Currency");
        }

        [Test]
        public void should_give_a_folder_title_match_a_distance_the_close_match_check_accepts()
        {
            GivenFolderCandidate(GivenEdition(2, "Currency"));

            var result = Subject.Identify(GivenTracks("Some Unrelated Tag"), new IdentificationOverrides(), GivenScanConfig());

            // CloseBookMatchSpecification rejects anything above 0.50
            result[0].Distance.NormalizedDistance().Should().BeLessThan(0.15);
            result[0].Distance.Reasons.Should().Contain("folder title");
        }

        [Test]
        public void should_let_the_folder_title_replace_a_poor_tag_match_to_a_different_book()
        {
            // tags say "Currency", but the only tag candidate is another book of the same series
            GivenTagCandidate(GivenEdition(3, "The System of the World"));
            GivenFolderCandidate(GivenEdition(2, "Currency"));

            var result = Subject.Identify(GivenTracks("Currency"), new IdentificationOverrides(), GivenScanConfig());

            result.Should().HaveCount(1);
            result[0].Edition.Title.Should().Be("Currency");
            result[0].Edition.BookId.Should().Be(2);
        }

        [Test]
        public void should_keep_a_close_tag_match_and_not_even_look_at_the_folder()
        {
            GivenTagCandidate(GivenEdition(2, "Currency"));

            var result = Subject.Identify(GivenTracks("Currency"), new IdentificationOverrides(), GivenScanConfig());

            result[0].Edition.Title.Should().Be("Currency");

            Mocker.GetMock<ICandidateService>()
                .Verify(x => x.GetDbCandidatesFromFolder(It.IsAny<LocalEdition>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_replace_a_match_that_is_already_the_folder_title_book()
        {
            var edition = GivenEdition(2, "Currency");
            GivenTagCandidate(edition);
            GivenFolderCandidate(edition);

            // deliberately weak tags so the folder is consulted, but it names the same book
            var result = Subject.Identify(GivenTracks("Something Else Entirely"), new IdentificationOverrides(), GivenScanConfig());

            result[0].Edition.BookId.Should().Be(2);
            result[0].Distance.Reasons.Should().NotContain("folder title");
        }

        [Test]
        public void should_never_override_a_forced_book()
        {
            var forced = GivenEdition(3, "The System of the World");
            GivenTagCandidate(forced);
            GivenFolderCandidate(GivenEdition(2, "Currency"));

            var overrides = new IdentificationOverrides { Book = forced.Book.Value };

            Subject.Identify(GivenTracks("Currency"), overrides, GivenScanConfig());

            Mocker.GetMock<ICandidateService>()
                .Verify(x => x.GetDbCandidatesFromFolder(It.IsAny<LocalEdition>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_leave_the_file_unmatched_when_neither_tags_nor_folder_find_anything()
        {
            var result = Subject.Identify(GivenTracks("Currency"), new IdentificationOverrides(), GivenScanConfig());

            result.Should().HaveCount(1);
            result[0].Edition.Should().BeNull();
        }
    }
}
