using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
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
    }
}
