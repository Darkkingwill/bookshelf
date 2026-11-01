using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaCover.Fallback;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class MediaCoverServiceFallbackFixture : CoreTest<MediaCoverService>
    {
        private const string FoundUrl = "https://img.example/found.jpg";

        private Book _book;
        private Edition _edition;
        private string _downloadedFile;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAppFolderInfo>(new AppFolderInfo(Mocker.Resolve<IStartupContext>()));

            var author = Builder<Author>.CreateNew().With(a => a.Id = 2).Build();
            author.Name = "Andy Weir";

            _edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 8)
                .With(e => e.Monitored = true)
                .With(e => e.Isbn13 = "9780593135204")
                .With(e => e.Images = new List<MediaCover.MediaCover>())
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(b => b.Id = 4)
                .With(b => b.Title = "Project Hail Mary")
                .With(b => b.Editions = new List<Edition> { _edition })
                .With(b => b.Author = author)
                .Build();

            Mocker.GetMock<IConfigService>().SetupGet(c => c.CoverFallbackEnabled).Returns(true);

            Mocker.GetMock<ICoverFallbackFinder>()
                  .Setup(f => f.Find(It.IsAny<CoverSearch>()))
                  .Returns(new CoverFallbackResult("Audible", FoundUrl));

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns(new HttpResponse(null, new HttpHeader(), string.Empty));

            // Whatever the fallback "downloads" is this file: a JPEG header unless a test says otherwise.
            _downloadedFile = Path.GetTempFileName();
            GivenDownloadedBytes(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 });

            Mocker.GetMock<IDiskProvider>().Setup(d => d.OpenReadStream(It.IsAny<string>())).Returns(() => File.OpenRead(_downloadedFile));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(It.IsAny<string>())).Returns(40000);
        }

        [TearDown]
        public void TearDown()
        {
            File.Delete(_downloadedFile);
        }

        private void GivenDownloadedBytes(byte[] bytes)
        {
            File.WriteAllBytes(_downloadedFile, bytes);
        }

        private void GivenFileExists(Func<string, bool> predicate)
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(p => predicate(p));
        }

        private static bool IsMarker(string path)
        {
            return path.EndsWith("cover.fallback") || path.EndsWith("cover.nofallback");
        }

        private void GivenMetadataCover(string url)
        {
            _edition.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, url) };
        }

        [Test]
        public void should_look_up_and_store_a_cover_when_the_book_has_none()
        {
            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<ICoverFallbackFinder>().Verify(
                f => f.Find(It.Is<CoverSearch>(s => s.Title == "Project Hail Mary" && s.Author == "Andy Weir" && s.Isbn13 == "9780593135204")),
                Times.Once());

            Mocker.GetMock<IHttpClient>().Verify(
                c => c.DownloadFile(FoundUrl, It.Is<string>(p => p.EndsWith(Path.Combine("Books", "4", "cover.jpg"))), It.IsAny<string>()),
                Times.Once());

            Mocker.GetMock<IDiskProvider>().Verify(
                d => d.WriteAllText(It.Is<string>(p => p.EndsWith("cover.fallback")), It.Is<string>(s => s.Contains(FoundUrl))),
                Times.Once());
        }

        [Test]
        public void should_report_that_a_cover_was_stored()
        {
            Subject.FillMissingCover(_book).Should().BeTrue();
        }

        [Test]
        public void should_store_the_cover_where_the_books_own_cover_entry_expects_it()
        {
            GivenMetadataCover("https://goodreads.example/tiny.png");

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<IHttpClient>().Verify(
                c => c.DownloadFile(FoundUrl, It.Is<string>(p => p.EndsWith(Path.Combine("Books", "4", "cover.png"))), It.IsAny<string>()),
                Times.Once());
        }

        [Test]
        public void should_not_look_for_a_cover_when_the_book_already_has_a_usable_one()
        {
            GivenMetadataCover("https://goodreads.example/good.jpg");
            GivenFileExists(p => !IsMarker(p));

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<ICoverFallbackFinder>().Verify(f => f.Find(It.IsAny<CoverSearch>()), Times.Never());
        }

        [Test]
        public void should_replace_a_tiny_placeholder_cover()
        {
            GivenMetadataCover("https://goodreads.example/placeholder.jpg");
            GivenFileExists(p => !IsMarker(p));

            // the existing file is a few KB; the downloaded replacement is a normal size
            Mocker.GetMock<IDiskProvider>().SetupSequence(d => d.GetFileSize(It.IsAny<string>())).Returns(3000).Returns(40000);

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<ICoverFallbackFinder>().Verify(f => f.Find(It.IsAny<CoverSearch>()), Times.Once());
        }

        [Test]
        public void should_never_replace_a_pinned_fallback_cover_with_the_metadata_cover()
        {
            GivenMetadataCover("https://goodreads.example/other.jpg");
            GivenFileExists(p => p.EndsWith("cover.fallback"));

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICoverFallbackFinder>().Verify(f => f.Find(It.IsAny<CoverSearch>()), Times.Never());
        }

        [Test]
        public void should_remember_when_no_provider_has_a_cover()
        {
            Mocker.GetMock<ICoverFallbackFinder>().Setup(f => f.Find(It.IsAny<CoverSearch>())).Returns((CoverFallbackResult)null);

            Subject.FillMissingCover(_book).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("cover.nofallback")), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_not_repeat_a_lookup_that_failed_yesterday()
        {
            GivenFileExists(p => p.EndsWith("cover.nofallback"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(DateTime.UtcNow.AddDays(-1));

            Subject.FillMissingCover(_book).Should().BeFalse();

            Mocker.GetMock<ICoverFallbackFinder>().Verify(f => f.Find(It.IsAny<CoverSearch>()), Times.Never());
        }

        [Test]
        public void should_try_again_after_a_month()
        {
            GivenFileExists(p => p.EndsWith("cover.nofallback"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(DateTime.UtcNow.AddDays(-40));

            Subject.FillMissingCover(_book);

            Mocker.GetMock<ICoverFallbackFinder>().Verify(f => f.Find(It.IsAny<CoverSearch>()), Times.Once());
        }

        [Test]
        public void should_discard_a_download_that_is_not_an_image()
        {
            GivenDownloadedBytes(Encoding.ASCII.GetBytes("<html><body>Not found</body></html>"));

            Subject.FillMissingCover(_book).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.Is<string>(p => p.EndsWith("cover.jpg"))), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("cover.fallback")), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("cover.nofallback")), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_not_pin_anything_when_the_download_fails()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new System.Net.WebException("connection reset"));

            Subject.FillMissingCover(_book).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("cover.fallback")), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_do_nothing_when_the_fallback_is_turned_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CoverFallbackEnabled).Returns(false);

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<ICoverFallbackFinder>().Verify(f => f.Find(It.IsAny<CoverSearch>()), Times.Never());
        }

        [Test]
        public void should_show_a_stored_cover_for_a_book_whose_metadata_has_no_cover_entry()
        {
            var covers = new List<MediaCover.MediaCover>();

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.Is<string>(p => p.EndsWith("cover.jpg")))).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(new DateTime(1234));

            Subject.ConvertToLocalUrls(4, MediaCoverEntity.Book, covers);

            covers.Single().Url.Should().Be("/MediaCover/Books/4/cover.jpg?lastWrite=1234");
        }

        [Test]
        public void should_not_invent_a_cover_entry_when_there_is_no_stored_file()
        {
            var covers = new List<MediaCover.MediaCover>();

            Subject.ConvertToLocalUrls(4, MediaCoverEntity.Book, covers);

            covers.Should().BeEmpty();
        }

        [Test]
        public void should_not_add_a_second_entry_when_the_metadata_already_has_a_cover()
        {
            var covers = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://goodreads.example/x.jpg") };

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(new DateTime(1234));

            Subject.ConvertToLocalUrls(4, MediaCoverEntity.Book, covers);

            covers.Should().ContainSingle();
        }
    }
}
