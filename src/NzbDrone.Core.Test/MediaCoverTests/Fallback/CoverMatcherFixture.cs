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
        [TestCase("1% Lifesteal, Book One", "1% Lifesteal")]
        [TestCase("Wolfpack, Book 1", "Wolfpack")]
        [TestCase("In Fury Born (1)", "In Fury Born")]
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
        [TestCase("The Perfect Run, Book Two", "The Perfect Run")]
        [TestCase("Wolfpack, Book 2", "Wolfpack")]
        public void should_not_match_a_different_book(string local, string candidate)
        {
            CoverMatcher.TitlesMatch(local, candidate).Should().BeFalse();
        }

        [Test]
        public void should_search_for_the_title_without_bracketed_noise()
        {
            CoverMatcher.SearchTitles("In Fury Born (1)").Should().Equal("In Fury Born");
            CoverMatcher.SearchTitles("Death Row [Unabridged]").Should().Equal("Death Row");
        }

        [Test]
        public void should_also_search_for_the_first_volume_without_its_number()
        {
            CoverMatcher.SearchTitles("1% Lifesteal, Book One").Should().Equal("1% Lifesteal, Book One", "1% Lifesteal");
            CoverMatcher.SearchTitles("Wolfpack: Volume 1").Should().Equal("Wolfpack: Volume 1", "Wolfpack");
        }

        [Test]
        public void should_not_drop_the_number_of_a_later_volume()
        {
            CoverMatcher.SearchTitles("The Perfect Run, Book Two").Should().Equal("The Perfect Run, Book Two");
            CoverMatcher.SearchTitles("Wolfpack 2").Should().Equal("Wolfpack 2");
        }

        [Test]
        public void should_search_for_nothing_without_a_title()
        {
            CoverMatcher.SearchTitles(null).Should().BeEmpty();
            CoverMatcher.SearchTitles("  (1) ").Should().BeEmpty();
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
