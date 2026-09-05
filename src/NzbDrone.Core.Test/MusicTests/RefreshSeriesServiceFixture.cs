using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshSeriesServiceFixture : CoreTest<RefreshSeriesService>
    {
        private Author _author;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.Series = new List<Series>())
                .Build();

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthorMetadataId(It.IsAny<int>()))
                .Returns(new List<Book>());

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.FindById(It.IsAny<List<string>>()))
                .Returns(new List<Series>());
        }

        [Test]
        public void should_not_delete_existing_series_when_metadata_source_returns_none()
        {
            var existingSeries = Builder<Series>.CreateListOfSize(2).BuildList();

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.GetByAuthorMetadataId(It.IsAny<int>()))
                .Returns(existingSeries);

            var result = Subject.RefreshSeriesInfo(_author.AuthorMetadataId, new List<Series>(), _author, false, false, null);

            result.Should().BeFalse();

            Mocker.GetMock<ISeriesBookLinkService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<SeriesBookLink>>()), Times.Never());

            Mocker.GetMock<ISeriesService>()
                .Verify(s => s.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_refresh_normally_when_metadata_source_returns_no_series_and_none_existed()
        {
            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.GetByAuthorMetadataId(It.IsAny<int>()))
                .Returns(new List<Series>());

            var result = Subject.RefreshSeriesInfo(_author.AuthorMetadataId, new List<Series>(), _author, false, false, null);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<SeriesBookLink>>()), Times.Never());
        }
    }
}
