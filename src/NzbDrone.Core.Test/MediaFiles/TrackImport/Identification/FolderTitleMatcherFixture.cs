using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class FolderTitleMatcherFixture : CoreTest
    {
        [TestCase("/media/audiobooks/Neal Stephenson/The Baroque Cycle/7 - Currency/Currency.m4b", "Currency")]
        [TestCase("/media/audiobooks/Craig Alanson/Expeditionary Force/7.5 - Homefront/Homefront.m4b", "Homefront")]
        [TestCase("/media/audiobooks/Kyla Stone/- Edge of Collapse/- Edge of Collapse.m4b", "Edge of Collapse")]
        [TestCase("/media/audiobooks/Stephen King/Fairy Tale/Fairy Tale.m4b", "Fairy Tale")]
        [TestCase("/media/audiobooks/Kyla Stone/The Last Sanctuary/1-5 - The Last Sanctuary Omnibus/x.m4b", "The Last Sanctuary Omnibus")]
        public void should_take_the_book_folder_without_its_series_position(string path, string expected)
        {
            FolderTitleMatcher.GetFolderTitle(path).Should().Be(expected);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("file.m4b")]
        public void should_return_nothing_when_there_is_no_folder(string path)
        {
            FolderTitleMatcher.GetFolderTitle(path).Should().BeEmpty();
        }

        [TestCase("The Neon Rain", "Neon Rain", true)]
        [TestCase("Neon Rain", "The Neon Rain", true)]
        [TestCase("Solomon’s Gold", "Solomon's Gold", true)]
        [TestCase("Everything's Eventual: 5 Dark Tales", "Everything's Eventual", true)]
        [TestCase("Hardfought (Novella)", "Hardfought", true)]
        [TestCase("Currency", "currency", true)]
        [TestCase("Tower Apocalypse 3", "Tower Apocalypse 2", false)]
        [TestCase("Super Powereds: Year 3", "Super Powereds: Year 1", false)]
        [TestCase("The System of the World", "Currency", false)]
        [TestCase("Marilyn Manson", "The Last Days of Marilyn Monroe", false)]
        public void should_only_match_a_title_exactly(string bookTitle, string folderTitle, bool expected)
        {
            FolderTitleMatcher.MatchesTitle(bookTitle, folderTitle).Should().Be(expected);
        }

        [TestCase("It", "It")]
        [TestCase("UR", "UR")]
        [TestCase("Us", "Us")]
        public void should_not_match_titles_too_short_to_trust(string bookTitle, string folderTitle)
        {
            FolderTitleMatcher.MatchesTitle(bookTitle, folderTitle).Should().BeFalse();
        }
    }
}
