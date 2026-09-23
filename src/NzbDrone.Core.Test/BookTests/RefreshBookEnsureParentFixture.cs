using System.Reflection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Refreshing a book that isn't in its author's own list fetches it separately and pins it to the
    // local author's metadata id, so it stays put. EnsureNewParent still saw the upstream credited
    // author and added them - an author no book ever points at. Seen live: refreshing James Patterson
    // added "Zbigniew Herbert" for a mis-matched Polish poetry collection, and 16 empty authors came
    // from refreshing 8.
    [TestFixture]
    public class RefreshBookEnsureParentFixture : CoreTest<RefreshBookService>
    {
        private static readonly MethodInfo EnsureMethod = typeof(RefreshBookService)
            .GetMethod("EnsureNewParent", BindingFlags.Instance | BindingFlags.NonPublic);

        private Book _local;

        [SetUp]
        public void Setup()
        {
            var patterson = new AuthorMetadata { Id = 5, ForeignAuthorId = "3780", Name = "James Patterson" };

            _local = new Book
            {
                Title = "Poezje (Kolekcja poezji polskiej XX wieku)",
                AuthorMetadataId = patterson.Id,
                AuthorMetadata = patterson,
                Author = new Author { Id = 138, Metadata = patterson, MetadataProfileId = 1, QualityProfileId = 1, Path = "/media/audiobooks/James Patterson" }
            };
        }

        private void Ensure(Book remote)
        {
            EnsureMethod.Invoke(Subject, new object[] { _local, remote });
        }

        [Test]
        public void should_not_add_credited_author_when_book_stays_with_its_current_author()
        {
            var remote = new Book
            {
                AuthorMetadata = new AuthorMetadata { Id = _local.AuthorMetadataId, ForeignAuthorId = "49926", Name = "Zbigniew Herbert" }
            };

            Ensure(remote);

            Mocker.GetMock<IAddAuthorService>().Verify(x => x.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_add_new_author_when_book_really_moves()
        {
            var remote = new Book
            {
                AuthorMetadata = new AuthorMetadata { Id = 0, ForeignAuthorId = "49926", Name = "Zbigniew Herbert" }
            };

            Ensure(remote);

            Mocker.GetMock<IAddAuthorService>().Verify(x => x.AddAuthor(It.Is<Author>(a => a.Metadata.Value.ForeignAuthorId == "49926"), It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void should_not_add_author_that_already_exists()
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById("49926"))
                .Returns(new Author { Id = 700 });

            var remote = new Book
            {
                AuthorMetadata = new AuthorMetadata { Id = 0, ForeignAuthorId = "49926", Name = "Zbigniew Herbert" }
            };

            Ensure(remote);

            Mocker.GetMock<IAddAuthorService>().Verify(x => x.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
