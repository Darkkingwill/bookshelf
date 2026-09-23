using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // A book whose id the metadata provider no longer knows made GetSkyhookData return null, and
    // RefreshBookInfo(Book) dereferenced it, so a manual refresh died with a NullReferenceException.
    [TestFixture]
    public class RefreshBookMissingUpstreamFixture : CoreTest<RefreshBookService>
    {
        [Test]
        public void should_skip_refresh_when_book_is_gone_upstream()
        {
            var metadata = new AuthorMetadata { Id = 5, ForeignAuthorId = "1", Name = "Billy Bob Holland", MetadataSource = "goodreads" };
            var book = new Book
            {
                Id = 429,
                ForeignBookId = "115107",
                Title = "Heartwood",
                AuthorMetadataId = metadata.Id,
                Author = new Author { Id = 1, Metadata = metadata }
            };

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new BookNotFoundException("115107"));

            Func<bool> refresh = () => Subject.RefreshBookInfo(book);

            refresh.Should().NotThrow();
            refresh().Should().BeFalse();
        }
    }
}
