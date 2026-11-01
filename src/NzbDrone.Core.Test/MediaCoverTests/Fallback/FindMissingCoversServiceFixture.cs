using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class FindMissingCoversServiceFixture : CoreTest<FindMissingCoversService>
    {
        private List<Book> _books;

        [SetUp]
        public void Setup()
        {
            _books = Builder<Book>.CreateListOfSize(3).Build().ToList();

            Mocker.GetMock<IBookService>().Setup(s => s.GetAllBooks()).Returns(_books);
        }

        [Test]
        public void should_look_for_a_cover_for_every_book()
        {
            Subject.Execute(new FindMissingCoversCommand());

            Mocker.GetMock<IFillMissingCovers>().Verify(f => f.FillMissingCover(It.IsAny<Book>()), Times.Exactly(3));
        }

        [Test]
        public void should_carry_on_when_one_book_fails()
        {
            Mocker.GetMock<IFillMissingCovers>().Setup(f => f.FillMissingCover(_books[0])).Throws(new InvalidOperationException("boom"));

            Subject.Execute(new FindMissingCoversCommand());

            Mocker.GetMock<IFillMissingCovers>().Verify(f => f.FillMissingCover(_books[1]), Times.Once());
            Mocker.GetMock<IFillMissingCovers>().Verify(f => f.FillMissingCover(_books[2]), Times.Once());

            // the failure is logged as a warning on purpose
            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }
    }
}
