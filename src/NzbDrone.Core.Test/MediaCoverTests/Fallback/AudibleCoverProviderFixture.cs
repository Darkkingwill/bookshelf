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
    public class AudibleCoverProviderFixture : CoreTest<AudibleCoverProvider>
    {
        private readonly CoverSearch _search = new CoverSearch { Title = "Project Hail Mary", Author = "Andy Weir" };

        private void GivenResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), json, status));
        }

        [Test]
        public void should_return_the_english_listing_that_matches_title_and_author()
        {
            GivenResponse(@"{ ""products"": [
              { ""title"": ""Project Hail Mary"", ""language"": ""german"",  ""authors"": [ { ""name"": ""Andy Weir"" } ], ""product_images"": { ""500"": ""https://img/de.jpg"" } },
              { ""title"": ""Project Hail Mary"", ""language"": ""english"", ""authors"": [ { ""name"": ""Andy Weir"" } ], ""product_images"": { ""500"": ""https://img/en.jpg"" } }
            ] }");

            Subject.FindCoverUrl(_search).Should().Be("https://img/en.jpg");
        }

        [Test]
        public void should_skip_a_different_book_by_the_same_author()
        {
            GivenResponse(@"{ ""products"": [
              { ""title"": ""The Martian"", ""language"": ""english"", ""authors"": [ { ""name"": ""Andy Weir"" } ], ""product_images"": { ""500"": ""https://img/martian.jpg"" } }
            ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_skip_the_same_title_by_another_author()
        {
            GivenResponse(@"{ ""products"": [
              { ""title"": ""Project Hail Mary"", ""language"": ""english"", ""authors"": [ { ""name"": ""Somebody Else"" } ], ""product_images"": { ""500"": ""https://img/x.jpg"" } }
            ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_match_when_the_real_title_is_the_subtitle()
        {
            GivenResponse(@"{ ""products"": [
              { ""title"": ""Dark Tower I"", ""subtitle"": ""The Gunslinger"", ""language"": ""english"", ""authors"": [ { ""name"": ""Stephen King"" } ], ""product_images"": { ""500"": ""https://img/gunslinger.jpg"" } }
            ] }");

            Subject.FindCoverUrl(new CoverSearch { Title = "The Gunslinger", Author = "Stephen King" }).Should().Be("https://img/gunslinger.jpg");
        }

        [Test]
        public void should_skip_a_listing_without_an_image()
        {
            GivenResponse(@"{ ""products"": [
              { ""title"": ""Project Hail Mary"", ""language"": ""english"", ""authors"": [ { ""name"": ""Andy Weir"" } ] }
            ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_audible_returns_an_error()
        {
            GivenResponse("{}", HttpStatusCode.InternalServerError);

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_not_call_audible_without_a_title_or_author()
        {
            Subject.FindCoverUrl(new CoverSearch { Title = "Artemis", Author = null }).Should().BeNull();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
