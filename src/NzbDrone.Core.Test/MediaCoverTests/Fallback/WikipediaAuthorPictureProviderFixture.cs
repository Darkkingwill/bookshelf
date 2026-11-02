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
    public class WikipediaAuthorPictureProviderFixture : CoreTest<WikipediaAuthorPictureProvider>
    {
        private const string Thumbnail = "https://upload.wikimedia.org/greg-bear.jpg";

        private static readonly AuthorPictureSearch Search = new AuthorPictureSearch
        {
            Name = "Greg Bear",
            BookTitles = new[] { "Darwin's Radio", "Moving Mars" }
        };

        private void GivenLookup(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("prop=pageimages"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), json, status));
        }

        private void GivenPageText(string title, string text)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("prop=extracts"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), @"{ ""query"": { ""pages"": { ""1"": { ""title"": """ + title + @""", ""extract"": """ + text + @""" } } } }", HttpStatusCode.OK));
        }

        private void GivenGregBearPage()
        {
            GivenLookup(@"{ ""query"": { ""pages"": {
                ""-1"": { ""title"": ""Greg Bear (author)"", ""missing"": """" },
                ""44445"": { ""pageid"": 44445, ""title"": ""Greg Bear"", ""thumbnail"": { ""source"": """ + Thumbnail + @""" } }
            } } }");
        }

        [Test]
        public void should_use_the_lead_photo_of_the_page_that_names_the_person_and_one_of_their_books()
        {
            GivenGregBearPage();
            GivenPageText("Greg Bear", "He is known for Darwin's Radio and Eon.");

            Subject.FindPictureUrl(Search).Should().Be(Thumbnail);
        }

        [Test]
        public void should_ask_for_the_bare_name_and_the_usual_disambiguated_forms_in_one_request()
        {
            GivenGregBearPage();
            GivenPageText("Greg Bear", "Moving Mars");

            Subject.FindPictureUrl(Search);

            Mocker.GetMock<IHttpClient>().Verify(
                c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("Greg%20Bear%7CGreg%20Bear%20(author)%20") || r.Url.FullUri.Contains("Greg%20Bear%7CGreg%20Bear%20%28author%29"))),
                Times.Once());
        }

        [Test]
        public void should_skip_a_page_whose_text_names_none_of_their_books()
        {
            GivenGregBearPage();
            GivenPageText("Greg Bear", "Greg Bear is an unrelated person who sells cars.");

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_skip_a_disambiguation_page()
        {
            GivenLookup(@"{ ""query"": { ""pages"": { ""9"": { ""pageid"": 9, ""title"": ""Greg Bear"", ""pageprops"": { ""disambiguation"": """" },
                ""thumbnail"": { ""source"": """ + Thumbnail + @""" } } } } }");
            GivenPageText("Greg Bear", "Darwin's Radio");

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_skip_a_page_without_a_photo()
        {
            GivenLookup(@"{ ""query"": { ""pages"": { ""9"": { ""pageid"": 9, ""title"": ""Greg Bear"" } } } }");
            GivenPageText("Greg Bear", "Darwin's Radio");

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_skip_a_page_about_somebody_else()
        {
            GivenLookup(@"{ ""query"": { ""pages"": { ""9"": { ""pageid"": 9, ""title"": ""Greg Beard"", ""thumbnail"": { ""source"": """ + Thumbnail + @""" } } } } }");
            GivenPageText("Greg Beard", "Darwin's Radio");

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        // "Robert Galbraith (author)" is a redirect to J. K. Rowling; the page is hers but the book still proves it.
        [Test]
        public void should_follow_a_redirect_from_a_pen_name_to_the_page_of_the_person()
        {
            var search = new AuthorPictureSearch { Name = "Robert Galbraith", BookTitles = new[] { "Troubled Blood" } };

            GivenLookup(@"{ ""query"": {
                ""redirects"": [ { ""from"": ""Robert Galbraith (author)"", ""to"": ""J. K. Rowling"" } ],
                ""pages"": { ""16027"": { ""pageid"": 16027, ""title"": ""J. K. Rowling"", ""thumbnail"": { ""source"": """ + Thumbnail + @""" } } } } }");
            GivenPageText("J. K. Rowling", "As Robert Galbraith she wrote Troubled Blood.");

            Subject.FindPictureUrl(search).Should().Be(Thumbnail);
        }

        [Test]
        public void should_not_use_a_page_it_only_reached_through_an_unrelated_redirect()
        {
            GivenLookup(@"{ ""query"": {
                ""redirects"": [ { ""from"": ""Somebody Else"", ""to"": ""Greg Beard"" } ],
                ""pages"": { ""9"": { ""pageid"": 9, ""title"": ""Greg Beard"", ""thumbnail"": { ""source"": """ + Thumbnail + @""" } } } } }");
            GivenPageText("Greg Beard", "Darwin's Radio");

            Subject.FindPictureUrl(Search).Should().BeNull();
        }

        [Test]
        public void should_return_nothing_when_wikipedia_is_unavailable()
        {
            GivenLookup("{}", HttpStatusCode.ServiceUnavailable);

            Subject.FindPictureUrl(Search).Should().BeNull();
        }
    }
}
