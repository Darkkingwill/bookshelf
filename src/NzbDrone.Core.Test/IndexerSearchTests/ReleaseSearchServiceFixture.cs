using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
        public class ReleaseSearchServiceFixture : CoreTest<ReleaseSearchService>
    {
        private Mock<IIndexer> _mockIndexer;
        private Author _author;
        private Book _firstBook;

        [SetUp]
        public void SetUp()
        {
            _mockIndexer = Mocker.GetMock<IIndexer>();
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition { Id = 1 });
            _mockIndexer.SetupGet(s => s.SupportsSearch).Returns(true);

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.AutomaticSearchEnabled(true))
                  .Returns(new List<IIndexer> { _mockIndexer.Object });

            Mocker.GetMock<IMakeDownloadDecision>()
                .Setup(s => s.GetSearchDecision(It.IsAny<List<Parser.Model.ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>()))
                .Returns(new List<DownloadDecision>());

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .Build();

            _firstBook = Builder<Book>.CreateNew()
                .With(e => e.Author = _author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = _firstBook)
                .With(e => e.Monitored = true)
                .Build();

            _firstBook.Editions = new List<Edition> { edition };

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);
        }

        private List<SearchCriteriaBase> WatchForSearchCriteria()
        {
            var result = new List<SearchCriteriaBase>();

            _mockIndexer.Setup(v => v.Fetch(It.IsAny<BookSearchCriteria>()))
                .Callback<BookSearchCriteria>(s => result.Add(s))
                .Returns(Task.FromResult<IList<Parser.Model.ReleaseInfo>>(new List<Parser.Model.ReleaseInfo>()));

            return result;
        }

        // A refresh or a manual add can leave a book with no monitored edition; the search must still run.
        [Test]
        public async Task should_search_on_the_first_edition_when_no_edition_is_monitored()
        {
            _firstBook.Editions.Value.ForEach(e =>
            {
                e.Monitored = false;
                e.Title = "Edition Title";
            });

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            allCriteria.OfType<BookSearchCriteria>().Single().BookTitle.Should().Be("Edition Title");
        }

        [Test]
        public async Task should_search_on_the_book_title_when_the_book_has_no_editions()
        {
            _firstBook.Title = "Project Hail Mary";
            _firstBook.Editions = new List<Edition>();

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            allCriteria.OfType<BookSearchCriteria>().Single().BookTitle.Should().Be("Project Hail Mary");
        }

        [Test]
        public async Task should_search_on_the_first_monitored_edition_when_several_are_monitored()
        {
            _firstBook.Editions.Value.ForEach(e => e.Title = "First Edition Title");
            _firstBook.Editions.Value.Add(new Edition { Title = "Second Edition Title", Monitored = true });

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            allCriteria.OfType<BookSearchCriteria>().Single().BookTitle.Should().Be("First Edition Title");
        }

        [Test]
        public async Task Tags_IndexerTags_AuthorNoTags_IndexerNotIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 3 }
            });

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(0);
        }

        [Test]
        public async Task Tags_IndexerNoTags_AuthorTags_IndexerIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1
            });

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 3 })
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
        }

        [Test]
        public async Task Tags_IndexerAndAuthorTagsMatch_IndexerIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 1, 2, 3 }
            });

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 3, 4, 5 })
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
        }

        [Test]
        public async Task Tags_IndexerAndAuthorTagsMismatch_IndexerNotIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 1, 2, 3 }
            });

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 4, 5, 6 })
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(0);
        }
    }
}
