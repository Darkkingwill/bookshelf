using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover.Fallback;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class CoverMatcherFixture
    {
        [TestCase("Project Hail Mary", "Project Hail Mary")]
        [TestCase("Project Hail Mary", "project hail mary!")]
        [TestCase("The Martian", "Martian")]
        [TestCase("Wolfpack 1", "Wolfpack")]
        [TestCase("Death Row", "Death Row (Unabridged)")]
        [TestCase("Mistborn: The Final Empire", "The Final Empire")]
        public void should_match_the_same_title(string local, string candidate)
        {
            CoverMatcher.TitlesMatch(local, candidate).Should().BeTrue();
        }

        [Test]
        public void should_match_a_title_that_audible_lists_as_the_subtitle()
        {
            CoverMatcher.TitlesMatch("The Gunslinger", "Dark Tower I", "The Gunslinger").Should().BeTrue();
        }

        // A wrong cover is worse than none, so a sequel, a spin-off or a translation must never match.
        [TestCase("The Perfect Run", "The Perfect Run 2")]
        [TestCase("Skyward", "Skyward Flight")]
        [TestCase("Artemis", "Artemisa")]
        [TestCase("It", "It Ends with Us")]
        [TestCase("Dune", "Dune Messiah")]
        [TestCase("Project Hail Mary", "The Martian")]
        public void should_not_match_a_different_book(string local, string candidate)
        {
            CoverMatcher.TitlesMatch(local, candidate).Should().BeFalse();
        }

        [TestCase("Andy Weir", "Andy Weir")]
        [TestCase("J.R.R. Tolkien", "J. R. R. Tolkien")]
        [TestCase("Cixin Liu", "Liu Cixin")]
        [TestCase("Rebecca Sharp", "Dr. Rebecca Sharp")]
        [TestCase("Stephen King", "stephen king")]
        public void should_match_the_same_author(string local, string candidate)
        {
            CoverMatcher.AuthorsMatch(local, new[] { "Somebody Else", candidate }).Should().BeTrue();
        }

        [TestCase("Stephen King", "Stephen Kingsley")]
        [TestCase("Stephen King", "Joe Hill")]
        [TestCase("Andy Weir", "Anthony Weir")]
        public void should_not_match_a_different_author(string local, string candidate)
        {
            CoverMatcher.AuthorsMatch(local, new[] { candidate }).Should().BeFalse();
        }

        [Test]
        public void should_not_match_when_there_is_no_author_or_title()
        {
            CoverMatcher.AuthorsMatch("Andy Weir", null).Should().BeFalse();
            CoverMatcher.AuthorsMatch(null, new[] { "Andy Weir" }).Should().BeFalse();
            CoverMatcher.TitlesMatch("Artemis", null).Should().BeFalse();
        }
    }
}
