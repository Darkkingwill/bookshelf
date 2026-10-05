using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.M4bConversion;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.M4bConversionTests
{
    [TestFixture]
    public class AutoConvertM4bOnImportFixture : CoreTest<AutoConvertM4bOnImport>
    {
        private Book _book;

        [SetUp]
        public void Setup()
        {
            _book = new Book { Id = 7, Title = "Foe" };

            GivenAutoConvertIs(true);
        }

        private void GivenAutoConvertIs(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.AutoConvertToM4b)
                .Returns(enabled);
        }

        private BookImportedEvent GivenImport(Quality quality, string downloadId = "abc", int fileCount = 3)
        {
            var files = new List<BookFile>();

            for (var i = 0; i < fileCount; i++)
            {
                files.Add(new BookFile { Id = i + 1, Quality = new QualityModel(quality) });
            }

            var item = downloadId == null ? null : new DownloadClientItem { DownloadId = downloadId };

            return new BookImportedEvent(new Author(), _book, files, new List<BookFile>(), true, item);
        }

        private void VerifyConversionQueued(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    x => x.Push(It.Is<ConvertToM4bCommand>(c => c.BookId == 7), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                    times);
        }

        [Test]
        public void should_queue_a_conversion_after_a_downloaded_mp3_book_is_imported()
        {
            Subject.Handle(GivenImport(Quality.MP3));

            VerifyConversionQueued(Times.Once());
        }

        [Test]
        public void should_not_convert_when_the_setting_is_off()
        {
            GivenAutoConvertIs(false);

            Subject.Handle(GivenImport(Quality.MP3));

            VerifyConversionQueued(Times.Never());
        }

        [Test]
        public void should_not_convert_an_import_that_did_not_come_from_a_download()
        {
            Subject.Handle(GivenImport(Quality.MP3, downloadId: null));

            VerifyConversionQueued(Times.Never());
        }

        [Test]
        public void should_not_convert_a_book_that_is_already_an_m4b()
        {
            Subject.Handle(GivenImport(Quality.M4B, fileCount: 1));

            VerifyConversionQueued(Times.Never());
        }

        [Test]
        public void should_not_convert_when_nothing_was_imported()
        {
            Subject.Handle(GivenImport(Quality.MP3, fileCount: 0));

            VerifyConversionQueued(Times.Never());
        }
    }
}
