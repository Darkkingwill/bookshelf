using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.M4bConversion;

namespace NzbDrone.Core.Test.MediaFiles.M4bConversionTests
{
    [TestFixture]
    public class M4bConversionTagsFixture
    {
        [TestCase("I'm Thinking of Ending Things by Iain Reid, narrated by Candace Thaxton [ENG / M4B]", "Candace Thaxton")]
        [TestCase("Foe by Iain Reid, narrated by Jacques Roy [ENG / M4B] [VIP]", "Jacques Roy")]
        [TestCase("Some Book by Someone, Narrated by Ann Lee, Bob Ray (Unabridged)", "Ann Lee, Bob Ray")]
        public void should_find_the_narrator_in_a_release_title(string releaseTitle, string expected)
        {
            M4bConversionService.ParseNarrator(releaseTitle).Should().Be(expected);
        }

        [TestCase("Foe by Iain Reid [ENG / EPUB]")]
        [TestCase("")]
        [TestCase(null)]
        public void should_return_nothing_when_a_release_has_no_narrator(string releaseTitle)
        {
            M4bConversionService.ParseNarrator(releaseTitle).Should().BeNull();
        }

        [Test]
        public void should_turn_a_description_into_plain_text_on_one_line()
        {
            var cleaned = M4bConversionService.CleanDescription(
                "<p>Six days ago, <i>astronaut</i> Mark Watney became one of the first people to walk on Mars.</p><br />" +
                "\n" + "Now &amp; here, he is alone.");

            cleaned.Should().Be("Six days ago, astronaut Mark Watney became one of the first people to walk on Mars. Now & here, he is alone.");
        }

        [TestCase("262 pages ; 18 cm")]
        [TestCase("")]
        [TestCase(null)]
        public void should_drop_a_description_that_is_only_a_catalogue_stub(string overview)
        {
            M4bConversionService.CleanDescription(overview).Should().BeNull();
        }

        [Test]
        public void should_cap_a_very_long_description()
        {
            M4bConversionService.CleanDescription(new string('a', 6000)).Should().HaveLength(4000);
        }

        [Test]
        public void should_name_a_chapter_after_the_file_as_it_was_downloaded()
        {
            var originals = new Dictionary<string, string>
            {
                { "/media/Ellroy/The Big Nowhere - 19.mp3", "/downloads/James Ellroy - The Big Nowhere (new rip)/19 - Part 2, Chapter 19.mp3" }
            };

            M4bConversionService.ChapterTitleFor(originals, "/media/Ellroy/The Big Nowhere - 19.mp3").Should().Be("Part 2, Chapter 19");
        }

        [Test]
        public void should_name_a_chapter_after_the_library_file_when_the_original_is_unknown()
        {
            M4bConversionService.ChapterTitleFor(new Dictionary<string, string>(), "/media/Ellroy/The Big Nowhere - 19.mp3").Should().Be("The Big Nowhere - 19");
            M4bConversionService.ChapterTitleFor(null, "/media/Ellroy/The Big Nowhere - 19.mp3").Should().Be("The Big Nowhere - 19");
        }

        [Test]
        public void should_tag_the_year_the_book_was_first_published()
        {
            M4bConversionService.PickYear(new DateTime(1988, 9, 1), new DateTime(1994, 1, 1)).Should().Be(1988);
        }

        [Test]
        public void should_fall_back_to_the_edition_year_and_ignore_nonsense_years()
        {
            M4bConversionService.PickYear(null, new DateTime(1994, 1, 1)).Should().Be(1994);
            M4bConversionService.PickYear(new DateTime(1, 1, 1), null).Should().BeNull();
            M4bConversionService.PickYear(null, null).Should().BeNull();
        }
    }
}
