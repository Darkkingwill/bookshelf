using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover.Fallback;
using NzbDrone.Core.MetadataSource.Providers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class GoogleBooksCoverProviderFixture : CoreTest<GoogleBooksCoverProvider>
    {
        private readonly CoverSearch _search = new CoverSearch { Title = "Hardfought", Author = "Greg Bear" };

        private void GivenApiKey(string key)
        {
            var settings = new Dictionary<string, string>();

            if (key != null)
            {
                settings["apiKey"] = key;
            }

            Mocker.GetMock<IConfigService>()
                  .Setup(c => c.GetMetadataProviderConfigs())
                  .Returns(new List<MetadataProviderConfig> { new MetadataProviderConfig { Key = "googlebooks", Settings = settings } });
        }

        private void GivenResponse(string json)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), json));
        }

        [Test]
        public void should_not_call_google_without_an_api_key()
        {
            GivenApiKey(null);

            Subject.FindCoverUrl(_search).Should().BeNull();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_send_the_key_and_return_a_plain_https_thumbnail()
        {
            GivenApiKey("KEY123");
            GivenResponse(@"{ ""items"": [ { ""volumeInfo"": {
              ""title"": ""Hardfought"", ""authors"": [ ""Greg Bear"" ],
              ""imageLinks"": { ""thumbnail"": ""http://books.google.com/books/content?id=X&zoom=1&edge=curl&source=gbs_api"" } } } ] }");

            Subject.FindCoverUrl(_search).Should().Be("https://books.google.com/books/content?id=X&zoom=1&source=gbs_api");

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.ToString().Contains("key=KEY123"))), Times.Once());
        }

        [Test]
        public void should_skip_a_volume_with_a_different_title()
        {
            GivenApiKey("KEY123");
            GivenResponse(@"{ ""items"": [ { ""volumeInfo"": { ""title"": ""Nightfall"", ""authors"": [ ""Greg Bear"" ], ""imageLinks"": { ""thumbnail"": ""https://x/y.jpg"" } } } ] }");

            Subject.FindCoverUrl(_search).Should().BeNull();
        }
    }
}
