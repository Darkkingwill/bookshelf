using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover.Fallback;

namespace NzbDrone.Core.Test.MediaCoverTests.Fallback
{
    [TestFixture]
    public class AuthorPictureMatcherFixture
    {
        [TestCase("Greg Bear", "Greg Bear")]
        [TestCase("Greg Bear", "greg bear")]
        [TestCase("Cixin Liu", "Liu Cixin")]
        [TestCase("J. K. Rowling", "Joanne Rowling")]
        public void should_match_the_same_person(string local, string candidate)
        {
            CoverMatcher.SameAuthor(local, candidate).Should().BeTrue();
        }

        // A wrong face is worse than none, so a near miss must not count the way it does for a book's author.
        [TestCase("Jack Carr", "Jack Karr")]
        [TestCase("Andy Weir", "Anthony Weir")]
        [TestCase("David Ellis", "David Ellis (scriptwriter)")]
        [TestCase("David Ellis", "")]
        [TestCase("", "David Ellis")]
        public void should_not_match_a_different_person(string local, string candidate)
        {
            CoverMatcher.SameAuthor(local, candidate).Should().BeFalse();
        }

        [Test]
        public void should_match_any_of_the_authors_titles_against_any_candidate_title()
        {
            CoverMatcher.AnyTitleMatches(new[] { "Zfinity", "Darwin's Radio" }, new[] { "Anvil of Stars", "Darwin's Radio" }).Should().BeTrue();
        }

        [Test]
        public void should_not_match_when_no_title_agrees_or_there_are_no_candidates()
        {
            CoverMatcher.AnyTitleMatches(new[] { "Darwin's Radio" }, new[] { "Darwin's Children" }).Should().BeFalse();
            CoverMatcher.AnyTitleMatches(new[] { "Darwin's Radio" }, new string[0]).Should().BeFalse();
            CoverMatcher.AnyTitleMatches(new[] { "Darwin's Radio" }, null).Should().BeFalse();
        }

        [Test]
        public void should_find_a_title_in_the_text_of_a_page()
        {
            CoverMatcher.MentionsAnyTitle("His novels include Darwin's Radio (1999) and Moving Mars.", new[] { "Moving Mars" }).Should().BeTrue();
        }

        [Test]
        public void should_find_a_title_without_its_series_subtitle_or_volume_number()
        {
            CoverMatcher.MentionsAnyTitle("The first book, The Final Empire, was published in 2006.", new[] { "Mistborn: The Final Empire" }).Should().BeTrue();
            CoverMatcher.MentionsAnyTitle("In the series, Apocalypse Challenge was the first.", new[] { "Apocalypse Challenge, Book One" }).Should().BeTrue();
        }

        // "Timeline" or "Destiny" appear in any biography, so one short word proves nothing about who the page is about.
        [Test]
        public void should_not_count_a_short_single_word_title_as_evidence()
        {
            CoverMatcher.MentionsAnyTitle("The timeline of his career and his destiny.", new[] { "Timeline", "Destiny" }).Should().BeFalse();
        }

        [Test]
        public void should_count_a_long_single_word_title_as_evidence()
        {
            CoverMatcher.MentionsAnyTitle("Her debut was Prisoner of the Mind and then Cryptonomicon.", new[] { "Cryptonomicon" }).Should().BeTrue();
        }

        [Test]
        public void should_not_find_a_title_inside_a_longer_word_or_phrase()
        {
            CoverMatcher.MentionsAnyTitle("The Moving Marshmallow Story", new[] { "Moving Mars" }).Should().BeFalse();
        }
    }
}
