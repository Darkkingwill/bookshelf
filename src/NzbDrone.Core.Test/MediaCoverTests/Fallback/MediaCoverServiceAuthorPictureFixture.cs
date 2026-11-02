using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaCover.Fallback;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class MediaCoverServiceAuthorPictureFixture : CoreTest<MediaCoverService>
    {
        private const string Silhouette = "https://i.gr-assets.com/images/S/compressed.photo.goodreads.com/nophoto/user/u_700x933.png";
        private const string RealPhoto = "https://i.gr-assets.com/images/S/compressed.photo.goodreads.com/authors/1/2._UX200_CR0,0,200,200_.jpg";
        private const string FoundUrl = "https://upload.wikimedia.org/greg-bear.jpg";

        private Author _author;
        private string _downloadedFile;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAppFolderInfo>(new AppFolderInfo(Mocker.Resolve<IStartupContext>()));

            _author = Builder<Author>.CreateNew().With(a => a.Id = 2).Build();
            _author.Name = "Greg Bear";
            GivenSourcePicture(Silhouette);

            Mocker.GetMock<IConfigService>().SetupGet(c => c.CoverFallbackEnabled).Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(2))
                  .Returns(new List<Book>
                  {
                      new Book { Title = "Darwin's Radio" },
                      new Book { Title = "Moving Mars" },
                      new Book { Title = "Darwin's Radio" }
                  });

            Mocker.GetMock<IAuthorPictureFinder>()
                  .Setup(f => f.Find(It.IsAny<AuthorPictureSearch>()))
                  .Returns(new CoverFallbackResult("Wikipedia", FoundUrl));

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns(new HttpResponse(null, new HttpHeader(), string.Empty));

            // Whatever the fallback "downloads" is this file: a JPEG header unless a test says otherwise.
            _downloadedFile = Path.GetTempFileName();
            File.WriteAllBytes(_downloadedFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 });

            Mocker.GetMock<IDiskProvider>().Setup(d => d.OpenReadStream(It.IsAny<string>())).Returns(() => File.OpenRead(_downloadedFile));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(It.IsAny<string>())).Returns(40000);
        }

        [TearDown]
        public void TearDown()
        {
            File.Delete(_downloadedFile);
        }

        private void GivenSourcePicture(string url)
        {
            _author.Metadata.Value.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, url) };
        }

        private void GivenFileExists(Func<string, bool> predicate)
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(p => predicate(p));
        }

        private static string Poster(string extension)
        {
            return Path.Combine("2", "poster" + extension);
        }

        [Test]
        public void should_look_up_the_picture_with_the_name_and_the_distinct_titles_of_the_authors_books()
        {
            Subject.FillMissingPicture(_author);

            Mocker.GetMock<IAuthorPictureFinder>().Verify(
                f => f.Find(It.Is<AuthorPictureSearch>(s => s.Name == "Greg Bear" && s.BookTitles.SequenceEqual(new[] { "Darwin's Radio", "Moving Mars" }))),
                Times.Once());
        }

        [Test]
        public void should_store_the_found_picture_as_poster_jpg_and_pin_it()
        {
            Subject.FillMissingPicture(_author).Should().BeTrue();

            Mocker.GetMock<IHttpClient>().Verify(
                c => c.DownloadFile(FoundUrl, It.Is<string>(p => p.EndsWith(Poster(".jpg"))), It.IsAny<string>()),
                Times.Once());

            Mocker.GetMock<IDiskProvider>().Verify(
                d => d.WriteAllText(It.Is<string>(p => p.EndsWith("picture.fallback")), It.Is<string>(s => s.Contains(FoundUrl))),
                Times.Once());
        }

        // The Android user agent the cover downloads send is for Goodreads; Wikimedia wants an honest client name.
        [Test]
        public void should_download_the_picture_with_the_default_user_agent()
        {
            Subject.FillMissingPicture(_author);

            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(FoundUrl, It.IsAny<string>(), null), Times.Once());
        }

        [Test]
        public void should_make_the_small_sizes_of_the_found_picture()
        {
            Subject.FillMissingPicture(_author);

            Mocker.GetMock<IImageResizer>().Verify(r => r.Resize(It.Is<string>(p => p.EndsWith(Poster(".jpg"))), It.Is<string>(p => p.EndsWith("poster-500.jpg")), 500), Times.Once());
            Mocker.GetMock<IImageResizer>().Verify(r => r.Resize(It.Is<string>(p => p.EndsWith(Poster(".jpg"))), It.Is<string>(p => p.EndsWith("poster-250.jpg")), 250), Times.Once());
        }

        [Test]
        public void should_do_nothing_when_the_feature_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CoverFallbackEnabled).Returns(false);

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IAuthorPictureFinder>().Verify(f => f.Find(It.IsAny<AuthorPictureSearch>()), Times.Never());
        }

        [Test]
        public void should_not_look_again_when_a_found_picture_is_already_pinned()
        {
            GivenFileExists(p => p.EndsWith(Poster(".jpg")) || p.EndsWith("picture.fallback"));

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IAuthorPictureFinder>().Verify(f => f.Find(It.IsAny<AuthorPictureSearch>()), Times.Never());
        }

        [Test]
        public void should_remember_that_nothing_was_found_and_not_ask_again_for_a_month()
        {
            Mocker.GetMock<IAuthorPictureFinder>().Setup(f => f.Find(It.IsAny<AuthorPictureSearch>())).Returns((CoverFallbackResult)null);

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("picture.nofallback")), It.IsAny<string>()), Times.Once());

            GivenFileExists(p => p.EndsWith("picture.nofallback"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(DateTime.UtcNow.AddDays(-5));

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IAuthorPictureFinder>().Verify(f => f.Find(It.IsAny<AuthorPictureSearch>()), Times.Once());
        }

        [Test]
        public void should_look_again_after_a_month()
        {
            GivenFileExists(p => p.EndsWith("picture.nofallback"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(DateTime.UtcNow.AddDays(-31));

            Subject.FillMissingPicture(_author).Should().BeTrue();
        }

        [Test]
        public void should_discard_a_download_that_is_not_an_image()
        {
            File.WriteAllBytes(_downloadedFile, System.Text.Encoding.ASCII.GetBytes("<html>blocked</html>"));

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.Is<string>(p => p.EndsWith(Poster(".jpg")))), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("picture.fallback")), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_discard_a_download_that_is_a_tiny_thumbnail()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(It.IsAny<string>())).Returns(900);

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.Is<string>(p => p.EndsWith("picture.fallback")), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_try_again_next_run_when_the_download_fails()
        {
            Mocker.GetMock<IHttpClient>().Setup(c => c.DownloadFile(FoundUrl, It.IsAny<string>(), It.IsAny<string>())).Throws(new InvalidOperationException("offline"));

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_download_the_sources_own_picture_instead_when_it_has_one_that_never_arrived()
        {
            GivenSourcePicture(RealPhoto);

            Subject.FillMissingPicture(_author);

            Mocker.GetMock<IAuthorPictureFinder>().Verify(f => f.Find(It.IsAny<AuthorPictureSearch>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(RealPhoto, It.Is<string>(p => p.EndsWith(Poster(".jpg"))), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_leave_an_author_alone_whose_source_picture_is_already_stored()
        {
            GivenSourcePicture(RealPhoto);
            GivenFileExists(p => p.EndsWith(Poster(".jpg")));

            Subject.FillMissingPicture(_author).Should().BeFalse();

            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IAuthorPictureFinder>().Verify(f => f.Find(It.IsAny<AuthorPictureSearch>()), Times.Never());
        }

        [Test]
        public void should_show_the_found_picture_for_an_author_whose_source_has_only_the_silhouette()
        {
            var covers = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, Silhouette) };

            GivenFileExists(p => p.EndsWith("picture.fallback") || p.EndsWith(Poster(".jpg")));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(new DateTime(1234));

            Subject.ConvertToLocalUrls(2, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be("/MediaCover/2/poster.jpg?lastWrite=1234");
        }

        // The UI hides a picture whose remote URL is the silhouette, so a pinned picture must report where it really came from.
        [Test]
        public void should_report_the_url_the_found_picture_came_from_as_its_remote_url()
        {
            var covers = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, Silhouette) };

            GivenFileExists(p => p.EndsWith("picture.fallback"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.ReadAllText(It.Is<string>(p => p.EndsWith("picture.fallback")))).Returns("Wikipedia\n" + FoundUrl);

            Subject.ConvertToLocalUrls(2, MediaCoverEntity.Author, covers);

            covers.Single().RemoteUrl.Should().Be(FoundUrl);
        }

        [Test]
        public void should_keep_the_silhouette_as_the_remote_url_when_the_marker_cannot_be_read()
        {
            var covers = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, Silhouette) };

            GivenFileExists(p => p.EndsWith("picture.fallback"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.ReadAllText(It.IsAny<string>())).Throws(new IOException("locked"));

            Subject.ConvertToLocalUrls(2, MediaCoverEntity.Author, covers);

            covers.Single().RemoteUrl.Should().Be(Silhouette);
        }

        [Test]
        public void should_keep_pointing_at_the_sources_file_when_no_picture_was_found()
        {
            var covers = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, Silhouette) };

            Subject.ConvertToLocalUrls(2, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be("/MediaCover/2/poster.png");
            covers.Single().RemoteUrl.Should().Be(Silhouette);
        }

        [Test]
        public void should_prefer_the_sources_own_picture_once_it_has_one()
        {
            var covers = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, RealPhoto) };

            GivenFileExists(p => p.EndsWith("picture.fallback"));

            Subject.ConvertToLocalUrls(2, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be("/MediaCover/2/poster.jpg");
        }

        [Test]
        public void should_not_download_the_silhouette_over_a_pinned_picture_when_the_author_is_refreshed()
        {
            GivenFileExists(p => p.EndsWith("picture.fallback"));
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(2)).Returns(new List<Book>());

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(Silhouette, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_replace_the_pinned_picture_when_a_refresh_brings_a_real_one()
        {
            GivenSourcePicture(RealPhoto);
            GivenFileExists(p => p.EndsWith("picture.fallback"));
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(2)).Returns(new List<Book>());

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.Is<string>(p => p.EndsWith("picture.fallback"))), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(RealPhoto, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }
    }
}
