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
    public class AuthorPictureFinderFixture : CoreTest<AuthorPictureFinder>
    {
        private static readonly AuthorPictureSearch Search = new AuthorPictureSearch { Name = "Greg Bear", BookTitles = new[] { "Darwin's Radio" } };

        private Mock<IAuthorPictureProvider> GivenProvider(string name, int order, string url)
        {
            var provider = new Mock<IAuthorPictureProvider>();
            provider.SetupGet(p => p.Name).Returns(name);
            provider.SetupGet(p => p.Order).Returns(order);
            provider.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Returns(url);

            return provider;
        }

        private void GivenProviders(params Mock<IAuthorPictureProvider>[] providers)
        {
            Mocker.SetConstant<IEnumerable<IAuthorPictureProvider>>(Array.ConvertAll(providers, p => p.Object));
        }

        [Test]
        public void should_try_providers_in_order_and_stop_at_the_first_picture()
        {
            var second = GivenProvider("Second", 1, "https://img/second.jpg");
            var first = GivenProvider("First", 0, "https://img/first.jpg");
            GivenProviders(second, first);

            var result = Subject.Find(Search);

            result.Source.Should().Be("First");
            result.Url.Should().Be("https://img/first.jpg");
            second.Verify(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>()), Times.Never());
        }

        [Test]
        public void should_fall_through_to_the_next_provider_when_one_has_nothing()
        {
            GivenProviders(GivenProvider("First", 0, null), GivenProvider("Second", 1, "https://img/second.jpg"));

            Subject.Find(Search).Source.Should().Be("Second");
        }

        [Test]
        public void should_carry_on_when_a_provider_throws()
        {
            var broken = GivenProvider("Broken", 0, null);
            broken.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Throws(new InvalidOperationException("boom"));
            GivenProviders(broken, GivenProvider("Second", 1, "https://img/second.jpg"));

            Subject.Find(Search).Source.Should().Be("Second");
        }

        [Test]
        public void should_return_nothing_when_no_provider_has_a_picture()
        {
            GivenProviders(GivenProvider("First", 0, null), GivenProvider("Second", 1, null));

            Subject.Find(Search).Should().BeNull();
        }

        private static HttpException GivenHttpFailure(HttpStatusCode statusCode)
        {
            var request = new HttpRequest("https://example.test/api");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, statusCode);

            return statusCode == HttpStatusCode.TooManyRequests ? new TooManyRequestsException(request, response) : new HttpException(request, response);
        }

        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void should_say_the_lookup_was_incomplete_when_a_provider_was_unavailable_and_none_matched(HttpStatusCode statusCode)
        {
            var limited = GivenProvider("Limited", 0, null);
            limited.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Throws(GivenHttpFailure(statusCode));
            GivenProviders(limited, GivenProvider("Second", 1, null));

            Action act = () => Subject.Find(Search);

            act.Should().Throw<FallbackLookupUnavailableException>();
        }

        [Test]
        public void should_say_the_lookup_was_incomplete_when_a_provider_timed_out_and_none_matched()
        {
            var slow = GivenProvider("Slow", 0, null);
            slow.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Throws(new TimeoutException());
            GivenProviders(slow, GivenProvider("Second", 1, null));

            Action act = () => Subject.Find(Search);

            act.Should().Throw<FallbackLookupUnavailableException>();
        }

        [Test]
        public void should_still_return_a_picture_when_another_provider_was_rate_limited()
        {
            var limited = GivenProvider("Limited", 0, null);
            limited.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Throws(GivenHttpFailure(HttpStatusCode.TooManyRequests));
            GivenProviders(limited, GivenProvider("Second", 1, "https://img/second.jpg"));

            Subject.Find(Search).Source.Should().Be("Second");
        }

        // A 403, a 404 or an unreadable answer says nothing about whether the author has a picture: it counts as "no picture".
        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.NotFound)]
        public void should_treat_a_client_error_as_no_picture(HttpStatusCode statusCode)
        {
            var refusing = GivenProvider("Refusing", 0, null);
            refusing.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Throws(GivenHttpFailure(statusCode));
            GivenProviders(refusing, GivenProvider("Second", 1, null));

            Subject.Find(Search).Should().BeNull();
        }

        [Test]
        public void should_treat_an_unexpected_failure_as_no_picture()
        {
            var broken = GivenProvider("Broken", 0, null);
            broken.Setup(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>())).Throws(new InvalidOperationException("boom"));
            GivenProviders(broken, GivenProvider("Second", 1, null));

            Subject.Find(Search).Should().BeNull();
        }

        [TestCase("Rhett C. Bruno; M.B. Vance")]
        [TestCase("Anonymous")]
        [TestCase("anonymous ")]
        [TestCase("")]
        [TestCase(null)]
        public void should_not_search_for_a_name_that_is_not_one_person(string name)
        {
            var provider = GivenProvider("First", 0, "https://img/first.jpg");
            GivenProviders(provider);

            Subject.Find(new AuthorPictureSearch { Name = name, BookTitles = new[] { "A Title" } }).Should().BeNull();

            provider.Verify(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>()), Times.Never());
        }

        [Test]
        public void should_not_search_for_an_author_without_books_to_confirm_the_match()
        {
            var provider = GivenProvider("First", 0, "https://img/first.jpg");
            GivenProviders(provider);

            Subject.Find(new AuthorPictureSearch { Name = "Landon Scott", BookTitles = new string[0] }).Should().BeNull();

            provider.Verify(p => p.FindPictureUrl(It.IsAny<AuthorPictureSearch>()), Times.Never());
        }
    }
}
