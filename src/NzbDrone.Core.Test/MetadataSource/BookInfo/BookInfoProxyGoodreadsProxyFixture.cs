using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // "goodreads-proxy" is an opt-in per-author variant of the "goodreads" pin: same id space, but the
    // author's book list comes from the local metadata proxy first. The direct Goodreads feed has no
    // language on any edition, so a language-restricted metadata profile can't work off it.
    [TestFixture]
    public class BookInfoProxyGoodreadsProxyFixture : CoreTest<BookInfoProxy>
    {
        private const string AuthorJson = "{\"ForeignId\":3389,\"Name\":\"Stephen King\",\"Works\":[],\"Series\":[]}";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMetadataRequestBuilder>()
                .Setup(x => x.GetRequestBuilder())
                .Returns(new HttpRequestBuilder("http://proxy.invalid/{route}").CreateFactory());
        }

        private void GivenProxyResponds(HttpStatusCode status, string content = "")
        {
            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) =>
                    new HttpResponse(request, new HttpHeader(), content, status));
        }

        private void GivenGoodreadsDirectHasAuthor()
        {
            Mocker.GetMock<IGoodreadsProxy>()
                .Setup(x => x.GetAuthorInfo(3389, It.IsAny<bool>()))
                .Returns(new Author
                {
                    Metadata = new AuthorMetadata { ForeignAuthorId = "3389", Name = "Stephen King", MetadataSource = "goodreads" }
                });
        }

        [Test]
        public void should_read_the_author_from_the_proxy_and_keep_the_pin()
        {
            GivenProxyResponds(HttpStatusCode.OK, AuthorJson);

            var author = Subject.GetAuthorInfo("3389", false, "goodreads-proxy");

            author.Metadata.Value.Name.Should().Be("Stephen King");
            author.Metadata.Value.MetadataSource.Should().Be("goodreads-proxy");

            Mocker.GetMock<IGoodreadsProxy>()
                .Verify(x => x.GetAuthorInfo(It.IsAny<long>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_fall_back_to_goodreads_direct_when_the_proxy_is_unavailable_and_keep_the_pin()
        {
            GivenProxyResponds(HttpStatusCode.InternalServerError);
            GivenGoodreadsDirectHasAuthor();

            var author = Subject.GetAuthorInfo("3389", false, "goodreads-proxy");

            author.Metadata.Value.Name.Should().Be("Stephen King");
            author.Metadata.Value.MetadataSource.Should().Be("goodreads-proxy");

            Mocker.GetMock<IGoodreadsProxy>()
                .Verify(x => x.GetAuthorInfo(3389, It.IsAny<bool>()), Times.Once());

            ExceptionVerification.IgnoreWarns();
        }

        // A refresh treats "author not found" as the author being gone (deleted if it has no files), so
        // a miss in the proxy must not be the last word - Goodreads gets asked about the same id.
        [Test]
        public void should_ask_goodreads_direct_when_the_proxy_does_not_know_the_author()
        {
            GivenProxyResponds(HttpStatusCode.NotFound);
            GivenGoodreadsDirectHasAuthor();

            var author = Subject.GetAuthorInfo("3389", false, "goodreads-proxy");

            author.Metadata.Value.Name.Should().Be("Stephen King");
            author.Metadata.Value.MetadataSource.Should().Be("goodreads-proxy");

            Mocker.GetMock<IGoodreadsProxy>()
                .Verify(x => x.GetAuthorInfo(3389, It.IsAny<bool>()), Times.Once());

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_report_not_found_only_when_goodreads_direct_also_does_not_know_the_author()
        {
            GivenProxyResponds(HttpStatusCode.NotFound);

            Mocker.GetMock<IGoodreadsProxy>()
                .Setup(x => x.GetAuthorInfo(3389, It.IsAny<bool>()))
                .Throws(new AuthorNotFoundException("3389"));

            Action act = () => Subject.GetAuthorInfo("3389", false, "goodreads-proxy");

            act.Should().Throw<AuthorNotFoundException>();

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void plain_goodreads_pin_should_still_read_the_author_from_goodreads_direct()
        {
            GivenGoodreadsDirectHasAuthor();

            var author = Subject.GetAuthorInfo("3389", false, "goodreads");

            author.Metadata.Value.MetadataSource.Should().Be("goodreads");

            Mocker.GetMock<ICachedHttpResponseService>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()), Times.Never());
        }
    }
}
