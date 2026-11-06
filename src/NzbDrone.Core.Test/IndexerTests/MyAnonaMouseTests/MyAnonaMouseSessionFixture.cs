using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.MyAnonaMouse;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonaMouseTests
{
    // MyAnonaMouse replaces the mam_id session cookie on every authenticated response. These tests cover the
    // indexer keeping the replacement, which is what stops the pasted session from going stale.
    [TestFixture]
    public class MyAnonaMouseSessionFixture : CoreTest<MyAnonaMouse>
    {
        private const string OldId = "old-session-id-0123456789abcdefghijklmnopqrstuvwxyz";
        private const string NewId = "new-session-id-0123456789abcdefghijklmnopqrstuvwxyz";
        private const string NewerId = "newer-session-id-0123456789abcdefghijklmnopqrstuvwxyz";
        private const string EmptyResult = "{\"data\":[]}";
        private const string NothingFound = "{\"error\":\"Nothing returned, out of 0\"}";

        private IndexerDefinition _stored;
        private string _userDataCookie;

        [SetUp]
        public void Setup()
        {
            _userDataCookie = null;

            // The user-class lookup that follows a successful search goes through the HTTP client.
            var httpClient = new Mock<IHttpClient>();
            httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, UserDataHeaders(), "{\"classname\":\"User\"}"));
            Mocker.SetConstant(httpClient.Object);

            // What the database holds, kept apart from the definition the running search is using.
            _stored = NewDefinition(2, OldId);

            Mocker.GetMock<IIndexerRepository>()
                .Setup(r => r.Find(2))
                .Returns(() => _stored);

            Subject.Definition = NewDefinition(2, OldId);
        }

        [Test]
        public void should_store_the_session_id_the_site_sends_back()
        {
            Parse(Response(EmptyResult, $"mam_id={NewId}; path=/"));

            StoredId().Should().Be(NewId);
            RunningId().Should().Be(NewId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Once());
        }

        [Test]
        public void should_store_the_session_id_when_the_search_found_nothing()
        {
            Parse(Response(NothingFound, $"mam_id={NewId}; path=/"));

            StoredId().Should().Be(NewId);
        }

        [Test]
        public void should_use_the_new_session_id_for_the_next_request_of_the_same_search()
        {
            Parse(Response(EmptyResult, $"mam_id={NewId}; path=/"));

            var generator = (MyAnonaMouseRequestGenerator)Subject.GetRequestGenerator();

            generator.GetCookies()["mam_id"].Should().Be(NewId);
        }

        [Test]
        public void should_store_a_newer_session_id_sent_with_the_user_data_request()
        {
            _userDataCookie = NewerId;

            Parse(Response(EmptyResult, $"mam_id={NewId}; path=/"));

            StoredId().Should().Be(NewerId);
            RunningId().Should().Be(NewerId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Exactly(2));
        }

        [Test]
        public void should_not_write_when_the_session_id_is_unchanged()
        {
            Parse(Response(EmptyResult, $"mam_id={OldId}; path=/"));

            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        [Test]
        public void should_not_write_when_no_cookie_is_sent()
        {
            Parse(Response(EmptyResult));

            StoredId().Should().Be(OldId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        [TestCase("deleted")]
        [TestCase("short")]
        [TestCase("")]
        public void should_ignore_a_cleared_or_implausible_session_id(string value)
        {
            Parse(Response(EmptyResult, $"mam_id={value}; path=/"));

            StoredId().Should().Be(OldId);
            RunningId().Should().Be(OldId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        [Test]
        public void should_keep_a_session_id_the_user_pasted_while_the_search_was_running()
        {
            _stored = NewDefinition(2, "pasted-by-the-user-0123456789abcdefghijklmnopqrstuvwxyz");

            Parse(Response(EmptyResult, $"mam_id={NewId}; path=/"));

            StoredId().Should().Be("pasted-by-the-user-0123456789abcdefghijklmnopqrstuvwxyz");
            RunningId().Should().Be(OldId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        [Test]
        public void should_not_touch_the_database_for_a_definition_that_was_never_saved()
        {
            Subject.Definition = NewDefinition(0, OldId);

            Parse(Response(EmptyResult, $"mam_id={NewId}; path=/"));

            RunningId().Should().Be(NewId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Find(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        [Test]
        public void should_not_store_anything_when_the_site_answers_with_an_error()
        {
            Assert.Throws<IndexerException>(() => Parse(Response(EmptyResult, $"mam_id={NewId}; path=/", HttpStatusCode.InternalServerError)));

            StoredId().Should().Be(OldId);
            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        [Test]
        public void should_still_report_a_rejected_session_as_such()
        {
            Assert.Throws<ApiKeyException>(() => Parse(Response("", null, HttpStatusCode.Forbidden)));

            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }

        private static IndexerDefinition NewDefinition(int id, string mamId)
        {
            return new IndexerDefinition
            {
                Id = id,
                Name = "MyAnonaMouse",
                Implementation = "MyAnonaMouse",
                Settings = new MyAnonaMouseSettings
                {
                    BaseUrl = "https://www.myanonamouse.net/",
                    MamId = mamId,
                    MinimumSeeders = 1
                }
            };
        }

        private static IndexerResponse Response(string body, string setCookie = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            var request = new IndexerRequest("https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php", HttpAccept.Json);
            var headers = new HttpHeader { ContentType = "application/json" };

            if (setCookie != null)
            {
                headers.Add("Set-Cookie", setCookie);
            }

            return new IndexerResponse(request, new HttpResponse(request.HttpRequest, headers, Encoding.UTF8.GetBytes(body), status));
        }

        private HttpHeader UserDataHeaders()
        {
            var headers = new HttpHeader { ContentType = "application/json" };

            if (_userDataCookie != null)
            {
                headers.Add("Set-Cookie", $"mam_id={_userDataCookie}; path=/");
            }

            return headers;
        }

        private void Parse(IndexerResponse response)
        {
            Subject.GetParser().ParseResponse(response);
        }

        private string StoredId()
        {
            return ((MyAnonaMouseSettings)_stored.Settings).MamId;
        }

        private string RunningId()
        {
            return ((MyAnonaMouseSettings)Subject.Definition.Settings).MamId;
        }
    }
}
