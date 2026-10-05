using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // When the metadata source stops reporting a whole series, the refresh deletes it. Pinned links are
    // manual fixes and must survive that, along with the series that holds them.
    [TestFixture]
    public class RefreshSeriesKeepsPinnedLinksFixture : CoreTest<RefreshSeriesServiceTestHarness>
    {
        private Series _series;
        private SeriesBookLink _pinned;
        private SeriesBookLink _unpinned;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 7, ForeignSeriesId = "goodreads-title:green town", ForeignAuthorId = "5" };
            _pinned = new SeriesBookLink { Id = 1, SeriesId = 7, BookId = 10, Pinned = true };
            _unpinned = new SeriesBookLink { Id = 2, SeriesId = 7, BookId = 11, Pinned = false };
        }

        private void GivenLinks(params SeriesBookLink[] links)
        {
            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(x => x.GetLinksBySeriesAndAuthor(7, "5"))
                .Returns(links.ToList());
        }

        [Test]
        public void should_only_delete_unpinned_links_and_keep_the_series()
        {
            GivenLinks(_pinned, _unpinned);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(x => x.GetLinksBySeries(7))
                .Returns(new List<SeriesBookLink> { _pinned });

            Subject.Delete(_series);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Verify(x => x.DeleteMany(It.Is<List<SeriesBookLink>>(l => l.Count == 1 && l[0].Id == 2)), Times.Once());
            Mocker.GetMock<ISeriesService>().Verify(x => x.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_delete_the_series_when_nothing_is_pinned()
        {
            GivenLinks(_unpinned);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(x => x.GetLinksBySeries(7))
                .Returns(new List<SeriesBookLink>());

            Subject.Delete(_series);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Verify(x => x.DeleteMany(It.Is<List<SeriesBookLink>>(l => l.Count == 1 && l[0].Id == 2)), Times.Once());
            Mocker.GetMock<ISeriesService>().Verify(x => x.Delete(7), Times.Once());
        }
    }
}
