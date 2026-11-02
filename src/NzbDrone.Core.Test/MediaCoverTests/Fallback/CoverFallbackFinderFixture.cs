using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MediaCover.Fallback;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class CoverFallbackFinderFixture : CoreTest<CoverFallbackFinder>
    {
        private static readonly CoverSearch Search = new CoverSearch { Title = "Artemis", Author = "Andy Weir" };

        private Mock<ICoverFallbackProvider> GivenProvider(string name, int order, string url)
        {
            var provider = new Mock<ICoverFallbackProvider>();
            provider.SetupGet(p => p.Name).Returns(name);
            provider.SetupGet(p => p.Order).Returns(order);
            provider.Setup(p => p.FindCoverUrl(Search)).Returns(url);

            return provider;
        }

        private void GivenProviders(params Mock<ICoverFallbackProvider>[] providers)
        {
            Mocker.SetConstant<IEnumerable<ICoverFallbackProvider>>(Array.ConvertAll(providers, p => p.Object));
        }

        [Test]
        public void should_try_providers_in_order_and_stop_at_the_first_cover()
        {
            var second = GivenProvider("Second", 1, "https://img/second.jpg");
            var first = GivenProvider("First", 0, "https://img/first.jpg");
            GivenProviders(second, first);

            var result = Subject.Find(Search);

            result.Source.Should().Be("First");
            result.Url.Should().Be("https://img/first.jpg");
            second.Verify(p => p.FindCoverUrl(It.IsAny<CoverSearch>()), Times.Never());
        }

        [Test]
        public void should_fall_through_to_the_next_provider_when_one_has_nothing()
        {
            GivenProviders(GivenProvider("First", 0, null), GivenProvider("Second", 1, "https://img/second.jpg"));

            Subject.Find(Search).Source.Should().Be("Second");
        }

        [Test]
        public void should_keep_going_when_a_provider_throws()
        {
            var broken = GivenProvider("Broken", 0, null);
            broken.Setup(p => p.FindCoverUrl(It.IsAny<CoverSearch>())).Throws(new InvalidOperationException("rate limited"));

            GivenProviders(broken, GivenProvider("Working", 1, "https://img/working.jpg"));

            Subject.Find(Search).Source.Should().Be("Working");
        }

        [Test]
        public void should_return_null_when_no_provider_has_a_cover()
        {
            GivenProviders(GivenProvider("First", 0, null), GivenProvider("Second", 1, ""));

            Subject.Find(Search).Should().BeNull();
        }

        private static HttpException GivenHttpFailure(HttpStatusCode statusCode)
        {
            var request = new HttpRequest("https://example.test/api");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, statusCode);

            return statusCode == HttpStatusCode.TooManyRequests ? new TooManyRequestsException(request, response) : new HttpException(request, response);
        }

        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void should_say_the_lookup_was_incomplete_when_a_provider_was_unavailable_and_none_matched(HttpStatusCode statusCode)
        {
            var limited = GivenProvider("Limited", 0, null);
            limited.Setup(p => p.FindCoverUrl(It.IsAny<CoverSearch>())).Throws(GivenHttpFailure(statusCode));
            GivenProviders(limited, GivenProvider("Second", 1, null));

            Action act = () => Subject.Find(Search);

            act.Should().Throw<FallbackLookupUnavailableException>();
        }

        [Test]
        public void should_still_return_a_cover_when_another_provider_was_rate_limited()
        {
            var limited = GivenProvider("Limited", 0, null);
            limited.Setup(p => p.FindCoverUrl(It.IsAny<CoverSearch>())).Throws(GivenHttpFailure(HttpStatusCode.TooManyRequests));
            GivenProviders(limited, GivenProvider("Second", 1, "https://img/second.jpg"));

            Subject.Find(Search).Source.Should().Be("Second");
        }

        [Test]
        public void should_treat_a_client_error_as_no_cover()
        {
            var refusing = GivenProvider("Refusing", 0, null);
            refusing.Setup(p => p.FindCoverUrl(It.IsAny<CoverSearch>())).Throws(GivenHttpFailure(HttpStatusCode.Forbidden));
            GivenProviders(refusing, GivenProvider("Second", 1, null));

            Subject.Find(Search).Should().BeNull();
        }

        [Test]
        public void should_search_for_the_title_without_bracketed_noise()
        {
            var provider = new Mock<ICoverFallbackProvider>();
            provider.SetupGet(p => p.Name).Returns("Only");
            provider.Setup(p => p.FindCoverUrl(It.Is<CoverSearch>(s => s.Title == "In Fury Born"))).Returns("https://img/fury.jpg");
            GivenProviders(provider);

            var result = Subject.Find(new CoverSearch { Title = "In Fury Born (1)", Author = "David Weber", Isbn13 = "9780671721305" });

            result.Url.Should().Be("https://img/fury.jpg");
            provider.Verify(p => p.FindCoverUrl(It.Is<CoverSearch>(s => s.Author == "David Weber" && s.Isbn13 == "9780671721305")), Times.Once());
        }

        [Test]
        public void should_retry_the_first_volume_without_its_number()
        {
            var provider = new Mock<ICoverFallbackProvider>();
            provider.SetupGet(p => p.Name).Returns("Only");
            provider.Setup(p => p.FindCoverUrl(It.Is<CoverSearch>(s => s.Title == "1% Lifesteal"))).Returns("https://img/lifesteal.jpg");
            GivenProviders(provider);

            Subject.Find(new CoverSearch { Title = "1% Lifesteal, Book One", Author = "Robert Blaise" }).Url.Should().Be("https://img/lifesteal.jpg");

            provider.Verify(p => p.FindCoverUrl(It.Is<CoverSearch>(s => s.Title == "1% Lifesteal, Book One")), Times.Once());
        }

        [Test]
        public void should_not_retry_a_provider_that_failed_but_still_try_the_next_one()
        {
            var broken = new Mock<ICoverFallbackProvider>();
            broken.SetupGet(p => p.Name).Returns("Broken");
            broken.SetupGet(p => p.Order).Returns(0);
            broken.Setup(p => p.FindCoverUrl(It.IsAny<CoverSearch>())).Throws(new InvalidOperationException("rate limited"));

            var working = new Mock<ICoverFallbackProvider>();
            working.SetupGet(p => p.Name).Returns("Working");
            working.SetupGet(p => p.Order).Returns(1);
            working.Setup(p => p.FindCoverUrl(It.IsAny<CoverSearch>())).Returns("https://img/ok.jpg");
            GivenProviders(broken, working);

            Subject.Find(new CoverSearch { Title = "Wolfpack, Book 1", Author = "Someone" }).Source.Should().Be("Working");

            broken.Verify(p => p.FindCoverUrl(It.IsAny<CoverSearch>()), Times.Once());
        }
    }
}
