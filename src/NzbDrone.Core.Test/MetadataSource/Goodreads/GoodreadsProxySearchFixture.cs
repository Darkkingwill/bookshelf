using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    // Every test here calls the public api.bookinfo.pro service, which is offline (HTTP 404), so the whole fixture is
    // Explicit: run it on purpose when a live metadata server is available.
    [TestFixture]
    [Explicit("api.bookinfo.pro is offline; needs a live metadata server")]
    public class GoodreadsProxySearchFixture : CoreTest<GoodreadsSearchProxy>
    {
        [SetUp]
        public void Setup()
        {
            UseRealHttp();

            Mocker.GetMock<IConfigService>()
                .Setup(x => x.MetadataSource)
                .Returns("https://api.bookinfo.pro/");

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get<List<SearchJsonResource>>(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => Mocker.Resolve<IHttpClient>().Get<List<SearchJsonResource>>(request));
        }

        [TestCase("Robert Harris", 575)]
        [TestCase("Lyndsay Ely", 8056539)]
        [TestCase("Elisa Puricelli Guerra", 4481805)]
        public void successful_author_search(string title, int expected)
        {
            var result = Subject.Search(title);

            result.Should().NotBeEmpty();

            result[0].Author.Id.Should().Be(expected);

            ExceptionVerification.IgnoreWarns();
        }

        [TestCase("Harry Potter and the sorcerer's stone a detailed summary", 72245296)]
        [TestCase("B0192CTMYG", 61209488)]
        [TestCase("9780439554930", 3)]
        public void successful_book_search(string title, int expected)
        {
            var result = Subject.Search(title);

            result.Should().NotBeEmpty();

            result[0].BookId.Should().Be(expected);

            ExceptionVerification.IgnoreWarns();
        }

        [TestCase("readarrid:")]
        [TestCase("readarrid: 99999999999999999999")]
        [TestCase("readarrid: 0")]
        [TestCase("readarrid: -12")]
        [TestCase("readarrid: aaaa")]
        [TestCase("adjalkwdjkalwdjklawjdlKAJD")]
        public void no_author_search_result(string term)
        {
            var result = Subject.Search(term);
            result.Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();
        }
    }
}
