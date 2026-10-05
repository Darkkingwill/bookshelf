using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // A language filter can leave a book with no remote editions at all. The local edition that holds a
    // file must survive that, or the file is left pointing at an edition that no longer exists.
    [TestFixture]
    public class RefreshBookKeepsEditionsWithFilesFixture : CoreTest<RefreshBookServiceTestHarness>
    {
        [Test]
        public void should_keep_an_edition_that_has_files()
        {
            var edition = new Edition { Id = 17935, ForeignEditionId = "6288", Title = "The Road" };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(17935))
                .Returns(new List<BookFile> { new BookFile { Id = 44419, Path = "/media/The Road.m4b" } });

            Subject.IsPinned(edition).Should().BeTrue();
        }

        [Test]
        public void should_not_keep_an_edition_without_files()
        {
            var edition = new Edition { Id = 5, ForeignEditionId = "9", Title = "Yol" };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(5))
                .Returns(new List<BookFile>());

            Subject.IsPinned(edition).Should().BeFalse();
        }

        [Test]
        public void should_not_keep_an_unsaved_edition()
        {
            Subject.IsPinned(new Edition { Id = 0, ForeignEditionId = "1", Title = "New" }).Should().BeFalse();

            Mocker.GetMock<IMediaFileService>().Verify(x => x.GetFilesByEdition(It.IsAny<int>()), Times.Never());
        }
    }
}
