using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    [TestFixture]
    public class GoodreadsProxySeriesFixture
    {
        // Real title/title_without_series pairs captured live from Gregg Hurwitz's Goodreads
        // author/show response (id 82570) - author/show has no separate series structure at
        // all, so series membership has to be extracted from the title convention Goodreads
        // uses everywhere. Book 8 is deliberately missing the comma Goodreads normally puts
        // before the "#" ("(Orphan X #8)" instead of "(Orphan X, #8)"), which is real,
        // inconsistent Goodreads data, not a typo here.
        private static readonly (long WorkId, string Title, string TitleWithoutSeries)[] OrphanXBooks =
        {
            (1, "Orphan X (Orphan X, #1)", "Orphan X"),
            (2, "The Nowhere Man (Orphan X, #2)", "The Nowhere Man"),
            (3, "Hellbent (Orphan X, #3)", "Hellbent"),
            (4, "Out of the Dark (Orphan X, #4)", "Out of the Dark"),
            (5, "Into the Fire (Orphan X, #5)", "Into the Fire"),
            (6, "Prodigal Son (Orphan X, #6)", "Prodigal Son"),
            (7, "Dark Horse (Orphan X, #7)", "Dark Horse"),
            (8, "The Last Orphan (Orphan X #8)", "The Last Orphan"),
            (9, "Lone Wolf (Orphan X, #9)", "Lone Wolf"),
            (10, "Nemesis (Orphan X, #10)", "Nemesis")
        };

        private static BookResource BuildBookResource(long workId, string title, string titleWithoutSeries)
        {
            var xml = new XElement("book",
                new XElement("id", workId),
                new XElement("title", title),
                new XElement("title_without_series", titleWithoutSeries),
                new XElement("work", new XElement("id", workId)));

            var resource = new BookResource();
            resource.Parse(xml);
            return resource;
        }

        private static Book BuildBook(long workId)
        {
            return new Book { ForeignBookId = workId.ToString() };
        }

        [Test]
        public void should_group_all_books_into_one_series_from_title_convention()
        {
            var resources = OrphanXBooks.Select(b => BuildBookResource(b.WorkId, b.Title, b.TitleWithoutSeries)).ToList();
            var books = OrphanXBooks.Select(b => BuildBook(b.WorkId)).ToList();

            var series = GoodreadsProxy.BuildSeriesFromTitles(resources, books);

            series.Should().HaveCount(1);
            series[0].Title.Should().Be("Orphan X");
            series[0].WorkCount.Should().Be(10);
            series[0].LinkItems.Value.Should().HaveCount(10);
        }

        [Test]
        public void should_tolerate_missing_comma_before_hash()
        {
            var resources = OrphanXBooks.Select(b => BuildBookResource(b.WorkId, b.Title, b.TitleWithoutSeries)).ToList();
            var books = OrphanXBooks.Select(b => BuildBook(b.WorkId)).ToList();

            var series = GoodreadsProxy.BuildSeriesFromTitles(resources, books);

            var book8Link = series[0].LinkItems.Value.Single(l => l.Book.Value.ForeignBookId == "8");
            book8Link.Position.Should().Be("8");
            book8Link.SeriesPosition.Should().Be(8);
        }

        [Test]
        public void should_link_each_book_back_to_its_series()
        {
            var resources = OrphanXBooks.Select(b => BuildBookResource(b.WorkId, b.Title, b.TitleWithoutSeries)).ToList();
            var books = OrphanXBooks.Select(b => BuildBook(b.WorkId)).ToList();

            GoodreadsProxy.BuildSeriesFromTitles(resources, books);

            books.Should().OnlyContain(b => b.SeriesLinks.Value.Count == 1);
        }

        [Test]
        public void should_not_create_series_for_standalone_books()
        {
            var resources = new List<BookResource>
            {
                BuildBookResource(1, "The Tower", "The Tower")
            };
            var books = new List<Book> { BuildBook(1) };

            var series = GoodreadsProxy.BuildSeriesFromTitles(resources, books);

            series.Should().BeEmpty();
            books[0].SeriesLinks.Value.Should().BeEmpty();
        }
    }
}
