using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class GrabbedBookFixture
    {
        private static Author GivenAuthor(int metadataId)
        {
            return new Author { AuthorMetadataId = metadataId };
        }

        private static RemoteBook GivenGrab(params Book[] books)
        {
            return new RemoteBook { Books = new List<Book>(books) };
        }

        [Test]
        public void should_return_the_book_a_download_was_grabbed_for()
        {
            var book = new Book { Id = 421, AuthorMetadataId = 5 };

            GrabbedBook.From(GivenGrab(book), GivenAuthor(5)).Should().BeSameAs(book);
        }

        [Test]
        public void should_return_nothing_for_a_grab_that_spans_several_books()
        {
            var grab = GivenGrab(new Book { Id = 1, AuthorMetadataId = 5 }, new Book { Id = 2, AuthorMetadataId = 5 });

            GrabbedBook.From(grab, GivenAuthor(5)).Should().BeNull();
        }

        [Test]
        public void should_return_nothing_when_the_book_belongs_to_a_different_author()
        {
            GrabbedBook.From(GivenGrab(new Book { Id = 1, AuthorMetadataId = 9 }), GivenAuthor(5)).Should().BeNull();
        }

        [Test]
        public void should_return_nothing_without_a_grab()
        {
            GrabbedBook.From(null, GivenAuthor(5)).Should().BeNull();
            GrabbedBook.From(new RemoteBook(), GivenAuthor(5)).Should().BeNull();
        }
    }

    [TestFixture]
    public class FolderNamesBookFixture
    {
        [TestCase("The Big Nowhere", "James Ellroy - The Big Nowhere (new rip)", true)]
        [TestCase("The Big Nowhere", "the.big.nowhere.mp3", true)]
        [TestCase("The Big Nowhere: An LA Quartet Novel", "James Ellroy - The Big Nowhere", true)]
        [TestCase("The Big Nowhere", "James Ellroy collection", false)]
        [TestCase("The Big Nowhere", "Hollywood Nocturnes", false)]
        [TestCase("It", "It Ends With Us", false)]
        [TestCase("Hunt", "The Hunt for Red October", false)]
        public void should_agree_only_when_the_folder_contains_the_title(string title, string folder, bool expected)
        {
            FolderTitleMatcher.FolderNamesBook(title, folder).Should().Be(expected);
        }

        [Test]
        public void should_agree_when_any_of_the_folder_names_contains_the_title()
        {
            FolderTitleMatcher.FolderNamesBook("The Big Nowhere", "CD 1", "James Ellroy - The Big Nowhere (new rip)").Should().BeTrue();
            FolderTitleMatcher.FolderNamesBook("The Big Nowhere", "CD 1", null, string.Empty).Should().BeFalse();
        }
    }
}
