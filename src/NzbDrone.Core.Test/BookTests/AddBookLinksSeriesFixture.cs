using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests
{
    // The author feed often has no series, so a book added from a search result has to get its series
    // link at add time, otherwise the first import files it under Author/Title instead of the series folder.
    [TestFixture]
    public class AddBookLinksSeriesFixture : CoreTest<AddBookService>
    {
        private const int AddedBookId = 42;

        private Author _author;
        private Book _metadataBook;
        private SeriesBookLink _inserted;
        private List<Series> _insertedSeries;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .With(s => s.Metadata = Builder<AuthorMetadata>.CreateNew().Build())
                .Build();

            _metadataBook = new Book
            {
                ForeignBookId = "89117450",
                Title = "The Apollo Murders",
                Editions = new List<Edition>
                {
                    new Edition { ForeignEditionId = "edition", Monitored = true }
                }
            };

            _inserted = null;
            _insertedSeries = new List<Series>();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo("89117450", It.IsAny<string>()))
                .Returns(() => Tuple.Create(_author.Metadata.Value.ForeignAuthorId,
                                            _metadataBook,
                                            new List<AuthorMetadata> { _author.Metadata.Value }));

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.FindById(It.IsAny<string>()))
                .Returns(_author);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.AddBook(It.IsAny<Book>(), It.IsAny<bool>()))
                .Callback<Book, bool>((b, r) => b.Id = AddedBookId)
                .Returns<Book, bool>((b, r) => b);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(s => s.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns(new List<SeriesBookLink>());

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(s => s.Insert(It.IsAny<SeriesBookLink>()))
                .Callback<SeriesBookLink>(l => _inserted = l)
                .Returns<SeriesBookLink>(l => l);

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.InsertMany(It.IsAny<IList<Series>>()))
                .Callback<IList<Series>>(s =>
                {
                    foreach (var item in s)
                    {
                        item.Id = 7;
                        _insertedSeries.Add(item);
                    }
                });
        }

        private void GivenMetadataSeries(string title, string position, int seriesPosition)
        {
            var series = new Series { Id = 0, ForeignSeriesId = "369266", Title = title };
            var link = new SeriesBookLink { Series = series, Position = position, SeriesPosition = seriesPosition };

            var links = _metadataBook.SeriesLinks?.IsLoaded == true ? _metadataBook.SeriesLinks.Value : new List<SeriesBookLink>();
            links.Add(link);
            _metadataBook.SeriesLinks = links;
        }

        private Book BookToAdd()
        {
            return new Book
            {
                ForeignBookId = "89117450",
                Editions = new List<Edition> { new Edition { ForeignEditionId = "edition", Monitored = true } },
                AuthorMetadata = new AuthorMetadata { ForeignAuthorId = _author.Metadata.Value.ForeignAuthorId }
            };
        }

        [Test]
        public void should_create_series_and_pinned_link_when_book_has_a_series()
        {
            GivenMetadataSeries("Apollo Murders", "1", 1);

            Subject.AddBook(BookToAdd());

            _insertedSeries.Should().ContainSingle();
            _insertedSeries[0].ForeignSeriesId.Should().Be("goodreads-title:apollo murders");
            _insertedSeries[0].Title.Should().Be("Apollo Murders");

            _inserted.Should().NotBeNull();
            _inserted.BookId.Should().Be(AddedBookId);
            _inserted.SeriesId.Should().Be(7);
            _inserted.Position.Should().Be("1");
            _inserted.SeriesPosition.Should().Be(1);
            _inserted.IsPrimary.Should().BeFalse();
            _inserted.Pinned.Should().BeTrue();
        }

        [Test]
        public void should_reuse_existing_series_with_the_same_foreign_id()
        {
            GivenMetadataSeries("Apollo Murders", "1", 1);

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.FindById("goodreads-title:apollo murders"))
                .Returns(new Series { Id = 99, ForeignSeriesId = "goodreads-title:apollo murders", Title = "Apollo Murders" });

            Subject.AddBook(BookToAdd());

            _insertedSeries.Should().BeEmpty();
            _inserted.SeriesId.Should().Be(99);
        }

        [Test]
        public void should_reuse_existing_series_with_the_same_title()
        {
            GivenMetadataSeries("Apollo Murders", "1", 1);

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.FindByTitle("Apollo Murders"))
                .Returns(new Series { Id = 55, ForeignSeriesId = "369266", Title = "Apollo Murders" });

            Subject.AddBook(BookToAdd());

            _insertedSeries.Should().BeEmpty();
            _inserted.SeriesId.Should().Be(55);
            _inserted.Pinned.Should().BeTrue();
        }

        [Test]
        public void should_use_the_lowest_series_position_like_the_search_result_does()
        {
            GivenMetadataSeries("Big Universe", "5", 5);
            GivenMetadataSeries("Apollo Murders", "1", 1);

            Subject.AddBook(BookToAdd());

            _insertedSeries.Should().ContainSingle();
            _insertedSeries[0].Title.Should().Be("Apollo Murders");
            _inserted.SeriesPosition.Should().Be(1);
        }

        [Test]
        public void should_not_link_anything_when_the_book_has_no_series()
        {
            Subject.AddBook(BookToAdd());

            _insertedSeries.Should().BeEmpty();
            _inserted.Should().BeNull();
        }

        [Test]
        public void should_not_touch_a_book_that_already_has_series_links()
        {
            GivenMetadataSeries("Apollo Murders", "1", 1);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(s => s.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns(new List<SeriesBookLink> { new SeriesBookLink { Id = 3, BookId = AddedBookId, SeriesId = 1 } });

            Subject.AddBook(BookToAdd());

            _insertedSeries.Should().BeEmpty();
            _inserted.Should().BeNull();
        }

        [Test]
        public void should_still_add_the_book_when_linking_the_series_fails()
        {
            GivenMetadataSeries("Apollo Murders", "1", 1);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(s => s.Insert(It.IsAny<SeriesBookLink>()))
                .Throws(new InvalidOperationException("db is locked"));

            var book = Subject.AddBook(BookToAdd());

            book.Id.Should().Be(AddedBookId);
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
