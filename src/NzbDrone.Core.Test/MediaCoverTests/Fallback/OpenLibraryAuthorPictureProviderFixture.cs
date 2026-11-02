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
    public class OpenLibraryAuthorPictureProviderFixture : CoreTest<OpenLibraryAuthorPictureProvider>
    {
        private static readonly AuthorPictureSearch Search = new AuthorPictureSearch
        {
            Name = "Jack Carr",
            BookTitles = new[] { "The Terminal List", "Only the Dead" }
        };

        private void GivenSearch(string json)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/search/authors.json"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), json, HttpStatusCode.OK));
        }

        private void GivenWorks(string authorKey, params string[] titles)
        {
            var entries = string.Join(",", System.Array.ConvertAll(titles, t => @"{ ""title"": """ + t + @""" }"));

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/authors/" + authorKey + "/works.json"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), @"{ ""entries"": [" + entries + "] }", HttpStatusCode.OK));
        }

        private void GivenPhoto(HttpStatusCode status)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Head(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, status));
        }

        [Test]
        public void should_return_the_photo_of_the_author_who_wrote_one_of_their_books()
        {
            GivenSearch(@"{ ""docs"": [ { ""key"": ""OL1706955A"", ""name"": ""Jack Carr"" } ] }");
            GivenWorks("OL1706955A", "Only the Dead", "Targeted");
            GivenPhoto(HttpStatusCode.OK);

            Subject.FindPictureUrl(Search).Should().Be("https://covers.openlibrary.org/a/olid/OL1706955A-L.jpg?default=false");
        }

        // Authors share names: the 1944 Jack Carr wrote about the Pacific Rim, not a thriller.
        [Test]
        public void should_skip_a_namesake_who_wrote_none_of_their_books()
        {
            GivenSearch(@"{ ""docs"": [
                { ""key"": ""OL15129931A"", ""name"": ""Jack Carr"" },
                { ""key"": ""OL1706955A"",  ""name"": ""Jack Carr"" } ] }");
            GivenWorks("OL15129931A", "Canada and the Pacific Rim");
            GivenWorks("OL1706955A", "The Terminal List");
            GivenPhoto(HttpStatusCode.OK);

            Subject.FindPictureUrl(Search).Should().Contain("OL1706955A");
        }

        [Test]
        public void should_skip_an_author_with_a_similar_but_different_name()
        {
            GivenSearch(@"{ ""docs"": [ { ""key"": ""OL9A"", ""name"": ""Jack Karr"" } ] }");
            GivenWorks("OL9A", "The Terminal List");
            GivenPhoto(HttpStatusCode.OK);

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_match_an_alternate_name()
        {
            GivenSearch(@"{ ""docs"": [ { ""key"": ""OL7A"", ""name"": ""John Carr"", ""alternate_names"": [ ""Jack Carr"" ] } ] }");
            GivenWorks("OL7A", "The Terminal List");
            GivenPhoto(HttpStatusCode.OK);

            Subject.FindPictureUrl(Search).Should().Contain("OL7A");
        }

        [Test]
        public void should_return_nothing_when_the_author_has_no_photo()
        {
            GivenSearch(@"{ ""docs"": [ { ""key"": ""OL1706955A"", ""name"": ""Jack Carr"" } ] }");
            GivenWorks("OL1706955A", "The Terminal List");
            GivenPhoto(HttpStatusCode.NotFound);

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_return_nothing_when_no_author_is_found()
        {
            GivenSearch(@"{ ""docs"": [] }");

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_return_nothing_when_open_library_is_unavailable()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "{}", HttpStatusCode.ServiceUnavailable));

            Subject.FindPictureUrl(Search).Should().BeNull();
        }
    }
}
