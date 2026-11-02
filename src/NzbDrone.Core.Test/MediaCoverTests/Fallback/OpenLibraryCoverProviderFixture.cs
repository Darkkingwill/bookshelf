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
    public class OpenLibraryCoverProviderFixture : CoreTest<OpenLibraryCoverProvider>
    {
        private readonly CoverSearch _search = new CoverSearch { Title = "Inspired by Murder", Author = "Audrey J. Cole" };

        private void GivenResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), json, status));
        }

        [Test]
        public void should_return_the_large_cover_of_the_first_matching_work()
        {
            GivenResponse(@"{ ""docs"": [
              { ""title"": ""Inspired by Murder Study Guide"", ""author_name"": [ ""Audrey J. Cole"" ], ""cover_i"": 1 },
              { ""title"": ""Inspired by Murder"",             ""author_name"": [ ""Audrey J. Cole"" ], ""cover_i"": 13769906 }
            ] }");

            Subject.FindCoverUrl(_search).Should().Be("https://covers.openlibrary.org/b/id/13769906-L.jpg");
        }

        [Test]
        public void should_skip_a_work_without_a_cover()
        {
            GivenResponse(@"{ ""docs"": [ { ""title"": ""Inspired by Murder"", ""author_name"": [ ""Audrey J. Cole"" ] } ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_skip_a_work_by_another_author()
        {
            GivenResponse(@"{ ""docs"": [ { ""title"": ""Inspired by Murder"", ""author_name"": [ ""Somebody Else"" ], ""cover_i"": 5 } ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        private void GivenEditionLanguage(string editionJson)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/books/"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), editionJson, HttpStatusCode.OK));
        }

        [Test]
        public void should_skip_a_cover_that_belongs_to_a_translated_edition()
        {
            GivenResponse(@"{ ""docs"": [ { ""title"": ""Inspired by Murder"", ""author_name"": [ ""Audrey J. Cole"" ], ""cover_i"": 7,
                                           ""cover_edition_key"": ""OL1M"", ""language"": [ ""tur"", ""eng"" ] } ] }");
            GivenEditionLanguage(@"{ ""languages"": [ { ""key"": ""/languages/tur"" } ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_use_a_cover_that_belongs_to_an_english_edition()
        {
            GivenResponse(@"{ ""docs"": [ { ""title"": ""Inspired by Murder"", ""author_name"": [ ""Audrey J. Cole"" ], ""cover_i"": 7,
                                           ""cover_edition_key"": ""OL1M"", ""language"": [ ""tur"", ""eng"" ] } ] }");
            GivenEditionLanguage(@"{ ""languages"": [ { ""key"": ""/languages/eng"" } ] }");

            Subject.FindCoverUrl(_search).Should().Be("https://covers.openlibrary.org/b/id/7-L.jpg");
        }

        [Test]
        public void should_use_a_cover_when_its_edition_does_not_say_which_language_it_is()
        {
            GivenResponse(@"{ ""docs"": [ { ""title"": ""Inspired by Murder"", ""author_name"": [ ""Audrey J. Cole"" ], ""cover_i"": 7,
                                           ""cover_edition_key"": ""OL1M"", ""language"": [ ""eng"" ] } ] }");
            GivenEditionLanguage(@"{ ""title"": ""Inspired by Murder"" }");

            Subject.FindCoverUrl(_search).Should().Be("https://covers.openlibrary.org/b/id/7-L.jpg");
        }

        [Test]
        public void should_skip_a_work_that_was_never_published_in_english()
        {
            GivenResponse(@"{ ""docs"": [ { ""title"": ""Inspired by Murder"", ""author_name"": [ ""Audrey J. Cole"" ], ""cover_i"": 7, ""language"": [ ""tur"", ""por"" ] } ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_open_library_returns_an_error()
        {
            GivenResponse("{}", HttpStatusCode.ServiceUnavailable);

            Subject.FindCoverUrl(_search).Should().BeNull();
        }
    }
}
