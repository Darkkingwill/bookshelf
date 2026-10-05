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
    }
}
