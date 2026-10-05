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
    // A download grabbed for one specific book. When its tags are useless (seen live: album = the publisher,
    // no title) the folder naming the same book is enough to accept it - but only then.
    [TestFixture]
    public class IdentificationServiceGrabbedBookFixture : CoreTest<IdentificationService>
    {
        private const string AuthorName = "James Ellroy";
        private Edition _edition;
        private IdentificationOverrides _overrides;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IAugmentingService>()
                .Setup(x => x.Augment(It.IsAny<LocalEdition>()));

            Mocker.GetMock<IAugmentingService>()
                .Setup(x => x.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()));

            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromFolder(It.IsAny<LocalEdition>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>());

            _edition = GivenEdition(421, "The Big Nowhere");
            _overrides = new IdentificationOverrides { Book = _edition.Book.Value };

            // a forced book makes the candidate service return that book's editions
            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = _edition, ExistingFiles = new List<BookFile>() } });
        }

        private static List<LocalBook> GivenTracks(string folder, string album, int count = 3)
        {
            var tracks = new List<LocalBook>();

            for (var i = 1; i <= count; i++)
            {
                tracks.Add(new LocalBook
                {
                    Path = $"{folder}/{i:00} - Part 1, Chapter {i}.mp3",
                    Size = 1000,
                    Quality = new QualityModel(Quality.MP3),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        BookTitle = album,
                        Authors = new List<string> { AuthorName }
                    }
                });
            }

            return tracks;
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

        private static ImportDecisionMakerConfig GivenDownloadConfig()
        {
            return new ImportDecisionMakerConfig
            {
                SingleRelease = true,
                IncludeExisting = false,
                KeepAllEditions = true,
                NewDownload = true
            };
        }

        [Test]
        public void should_accept_the_grabbed_book_when_the_tags_are_useless_and_the_folder_names_it()
        {
            var tracks = GivenTracks("/downloads/James Ellroy - The Big Nowhere (new rip)", "Hachette Audio");

            var result = Subject.Identify(tracks, _overrides, GivenDownloadConfig());

            result.Should().HaveCount(1);
            result[0].Edition.Should().NotBeNull();
            result[0].Edition.BookId.Should().Be(421);

            // CloseBookMatchSpecification rejects anything above 0.50
            result[0].Distance.NormalizedDistance().Should().BeLessThan(0.15);
            result[0].Distance.Reasons.Should().Contain("folder title");
        }

        [Test]
        public void should_accept_it_when_the_named_folder_is_one_level_above_a_disc_folder()
        {
            var tracks = GivenTracks("/downloads/James Ellroy - The Big Nowhere (new rip)/CD 1", "Hachette Audio");

            var result = Subject.Identify(tracks, _overrides, GivenDownloadConfig());

            result[0].Distance.Reasons.Should().Contain("folder title");
        }

        [Test]
        public void should_not_accept_a_download_whose_folder_does_not_name_the_grabbed_book()
        {
            var tracks = GivenTracks("/downloads/James Ellroy collection", "Hachette Audio");

            var result = Subject.Identify(tracks, _overrides, GivenDownloadConfig());

            // the tag match stays as weak as it was; the folder gave it no help
            result[0].Distance.Reasons.Should().NotContain("folder title");
            result[0].Distance.NormalizedDistance().Should().BeGreaterThan(0.15);
        }

        [Test]
        public void should_not_use_the_folder_when_nothing_was_grabbed()
        {
            var tracks = GivenTracks("/downloads/James Ellroy - The Big Nowhere (new rip)", "Hachette Audio");

            var result = Subject.Identify(tracks, new IdentificationOverrides(), GivenDownloadConfig());

            result[0].Distance?.Reasons.Should().NotContain("folder title");
        }

        [Test]
        public void should_keep_the_real_tag_distance_when_the_tags_already_agree_with_the_grabbed_book()
        {
            var tracks = GivenTracks("/downloads/James Ellroy - The Big Nowhere (new rip)", "The Big Nowhere");

            var result = Subject.Identify(tracks, _overrides, GivenDownloadConfig());

            result[0].Edition.BookId.Should().Be(421);
            result[0].Distance.Reasons.Should().NotContain("folder title");
        }
    }
}
