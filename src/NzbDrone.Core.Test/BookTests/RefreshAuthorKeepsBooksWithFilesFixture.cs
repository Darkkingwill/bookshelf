using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // The proxy can hand back a partial author list. An author refresh must not delete a book that
    // has files only because the list omitted it, or the files end up orphaned.
    [TestFixture]
    public class RefreshAuthorKeepsBooksWithFilesFixture : CoreTest<RefreshAuthorServiceTestHarness>
    {
        [Test]
        public void should_keep_a_book_that_has_files()
        {
            var book = new Book { Id = 42, ForeignBookId = "123", Title = "The Martian" };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(42))
                .Returns(new List<BookFile> { new BookFile { Id = 1, Path = "/media/The Martian.m4b" } });

            Subject.IsPinned(book).Should().BeTrue();
        }

        [Test]
        public void should_not_keep_a_book_without_files()
        {
            var book = new Book { Id = 43, ForeignBookId = "456", Title = "Artemis" };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(43))
                .Returns(new List<BookFile>());

            Subject.IsPinned(book).Should().BeFalse();
        }

        [Test]
        public void should_not_keep_an_unsaved_book()
        {
            Subject.IsPinned(new Book { Id = 0, ForeignBookId = "789", Title = "New" }).Should().BeFalse();

            Mocker.GetMock<IMediaFileService>().Verify(x => x.GetFilesByBook(It.IsAny<int>()), Times.Never());
        }
    }
}
