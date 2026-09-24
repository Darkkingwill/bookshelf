using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class CandidateServiceFolderFixture : CoreTest<CandidateService>
    {
        private const string BookPath = "/media/audiobooks/Neal Stephenson/The Baroque Cycle (8 volume)/7 - Currency/Currency.m4b";

        private Author _author;

        [SetUp]
        public void SetUp()
        {
            _author = new Author
            {
                AuthorMetadataId = 10,
                Metadata = new AuthorMetadata { Name = "Neal Stephenson" }
            };

            // the fuzzy author search returns whatever is "close"; the service must insist on an exact name
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetCandidates(It.IsAny<string>()))
                .Returns((string name) => name == "Neal Stephenson"
                    ? new List<Author> { _author }
                    : new List<Author>());

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionsByBook(It.IsAny<int>()))
                .Returns((int bookId) => new List<Edition> { new Edition { Id = bookId * 10, BookId = bookId, Title = "e" } });
        }

        private static LocalEdition GivenLocalEdition(string path, params string[] tagAuthors)
        {
            return new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        Path = path,
                        FileTrackInfo = new ParsedTrackInfo { Authors = tagAuthors.ToList() }
                    }
                }
            };
        }

        private void GivenBooks(params string[] titles)
        {
            var id = 0;

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(10))
                .Returns(titles.Select(t => new Book { Id = ++id, Title = t, AuthorMetadataId = 10 }).ToList());
        }

        [Test]
        public void should_find_the_one_book_named_by_the_folder_using_the_author_folder()
        {
            GivenBooks("Quicksilver", "Currency", "The System of the World");

            var result = Subject.GetDbCandidatesFromFolder(GivenLocalEdition(BookPath), false);

            result.Should().HaveCount(1);
            result[0].Edition.BookId.Should().Be(2);
        }

        [Test]
        public void should_find_the_author_from_the_tags_when_no_folder_names_it()
        {
            GivenBooks("Quicksilver", "Currency");

            var result = Subject.GetDbCandidatesFromFolder(GivenLocalEdition("/downloads/x/7 - Currency/Currency.m4b", "Neal Stephenson"), false);

            result.Should().HaveCount(1);
            result[0].Edition.BookId.Should().Be(2);
        }

        [Test]
        public void should_return_nothing_when_no_book_has_that_title()
        {
            GivenBooks("Quicksilver", "The System of the World");

            Subject.GetDbCandidatesFromFolder(GivenLocalEdition(BookPath), false).Should().BeEmpty();
        }

        [Test]
        public void should_return_nothing_when_more_than_one_book_has_that_title()
        {
            GivenBooks("Currency", "Currency: A Novel");

            Subject.GetDbCandidatesFromFolder(GivenLocalEdition(BookPath), false).Should().BeEmpty();
        }

        [Test]
        public void should_return_nothing_when_the_author_cannot_be_named_exactly()
        {
            GivenBooks("Currency");

            // no folder above the book, and the tags name someone the library has no exact author for
            Subject.GetDbCandidatesFromFolder(GivenLocalEdition("/7 - Currency/Currency.m4b", "Neil Stevenson"), false).Should().BeEmpty();
        }

        [Test]
        public void should_return_nothing_for_a_file_with_no_folder()
        {
            GivenBooks("Currency");

            Subject.GetDbCandidatesFromFolder(GivenLocalEdition("Currency.m4b", "Neal Stephenson"), false).Should().BeEmpty();
        }
    }
}
