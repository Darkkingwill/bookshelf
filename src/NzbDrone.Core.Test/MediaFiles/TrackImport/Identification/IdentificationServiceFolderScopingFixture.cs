using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
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
    [TestFixture]
    public class IdentificationServiceFolderScopingFixture : CoreTest<IdentificationService>
    {
        private const string AuthorName = "Tami Hoag";
        private const string FolderBeingScanned = "/media/Tami Hoag/Kovac and Liska/5 - The Bitter Season";
        private const string OtherFolder = "/media/Tami Hoag/Broussard and Fourcade/2 - The Boy";

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IAugmentingService>()
                .Setup(x => x.Augment(It.IsAny<LocalEdition>()));

            Mocker.GetMock<IAugmentingService>()
                .Setup(x => x.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()));
        }

        private static LocalBook GivenLocalBook(string folder, string bookTitle, int part)
        {
            return new LocalBook
            {
                Path = string.Format("{0}/{1} - {2:00}.mp3", folder, bookTitle, part),
                Size = 1000,
                Quality = new QualityModel(Quality.MP3),
                FileTrackInfo = new ParsedTrackInfo
                {
                    BookTitle = bookTitle,
                    Authors = new List<string> { AuthorName }
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

        private static BookFile GivenBookFile(string path)
        {
            return new BookFile
            {
                Path = path,
                Size = 1000,
                Quality = new QualityModel(Quality.MP3)
            };
        }

        private static ImportDecisionMakerConfig GivenScanConfig()
        {
            // Mirrors DiskScanService, which is the only caller that sets IncludeExisting.
            return new ImportDecisionMakerConfig
            {
                SingleRelease = true,
                IncludeExisting = true,
                KeepAllEditions = true
            };
        }

        [Test]
        public void should_not_match_candidate_whose_supporting_files_are_in_another_folder()
        {
            var localTracks = Enumerable.Range(1, 3)
                .Select(i => GivenLocalBook(FolderBeingScanned, "The Bitter Season", i))
                .ToList();

            var correctEdition = GivenEdition(1, "The Bitter Season");

            // A rival candidate already holding far more files than the folder being scanned, all of
            // them in a different folder.  Those files used to be folded into the set BookDistance
            // scores by majority vote, so the rival won on the strength of its own files and had the
            // whole folder reassigned to it.
            var rivalEdition = GivenEdition(2, "The Boy");
            var rivalExistingFiles = Enumerable.Range(1, 30)
                .Select(i => GivenBookFile(string.Format("{0}/The Boy - {1:00}.mp3", OtherFolder, i)))
                .ToList();

            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>
                {
                    new CandidateEdition { Edition = rivalEdition, ExistingFiles = rivalExistingFiles },
                    new CandidateEdition { Edition = correctEdition, ExistingFiles = new List<BookFile>() }
                });

            // Any tag read for a rival file claims to be "The Boy"; if those files reach the distance
            // calculation at all they outvote the folder we are actually identifying.
            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(It.IsAny<FileInfoBase>()))
                .Returns(new ParsedTrackInfo
                {
                    BookTitle = "The Boy",
                    Authors = new List<string> { AuthorName }
                });

            var result = Subject.Identify(localTracks, new IdentificationOverrides(), GivenScanConfig());

            result.Should().HaveCount(1);
            result[0].Edition.Title.Should().Be("The Bitter Season");
            result[0].LocalBooks.Select(x => x.Path)
                .Should().OnlyContain(x => x.StartsWith(FolderBeingScanned));
        }

        [Test]
        public void should_still_use_existing_files_from_the_same_folder()
        {
            // Half the folder is already imported.  Those files must still count towards the match so
            // that re-scanning a partially imported folder keeps working.
            var localTracks = Enumerable.Range(1, 2)
                .Select(i => GivenLocalBook(FolderBeingScanned, "The Bitter Season", i))
                .ToList();

            var edition = GivenEdition(1, "The Bitter Season");
            var alreadyImported = Enumerable.Range(3, 2)
                .Select(i => GivenBookFile(string.Format("{0}/The Bitter Season - {1:00}.mp3", FolderBeingScanned, i)))
                .ToList();

            Mocker.GetMock<ICandidateService>()
                .Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>
                {
                    new CandidateEdition { Edition = edition, ExistingFiles = alreadyImported }
                });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(It.IsAny<FileInfoBase>()))
                .Returns(new ParsedTrackInfo
                {
                    BookTitle = "The Bitter Season",
                    Authors = new List<string> { AuthorName }
                });

            var result = Subject.Identify(localTracks, new IdentificationOverrides(), GivenScanConfig());

            result.Should().HaveCount(1);
            result[0].Edition.Title.Should().Be("The Bitter Season");
            result[0].ExistingTracks.Should().HaveCount(2);
        }
    }
}
