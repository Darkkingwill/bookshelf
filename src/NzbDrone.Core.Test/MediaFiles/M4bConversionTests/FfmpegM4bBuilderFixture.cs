using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.M4bConversion;

namespace NzbDrone.Core.Test.MediaFiles.M4bConversionTests
{
    [TestFixture]
    public class FfmpegM4bBuilderFixture
    {
        private static M4bTags FullTags()
        {
            return new M4bTags
            {
                Title = "The Martian",
                Album = "The Martian",
                Artist = "Andy Weir",
                AlbumArtist = "Andy Weir",
                Year = 2011,
                Narrator = "R. C. Bray",
                Description = "Six days ago, astronaut Mark Watney became one of the first people to walk on Mars."
            };
        }

        [Test]
        public void should_write_the_tags_and_the_chapters_into_the_metadata_file()
        {
            var file = FfmpegM4bBuilder.BuildMetadataFile(
                new List<double> { 1000, 2500 },
                new List<string> { "Part 1", "Part 2" },
                FullTags());

            file.Should().StartWith(";FFMETADATA1");
            file.Should().Contain("title=The Martian");
            file.Should().Contain("artist=Andy Weir");
            file.Should().Contain("album_artist=Andy Weir");
            file.Should().Contain("album=The Martian");
            file.Should().Contain("date=2011");
            file.Should().Contain("composer=R. C. Bray");
            file.Should().Contain("description=Six days ago");
            file.Should().Contain("synopsis=Six days ago");
            file.Should().Contain("genre=Audiobook");
            file.Should().Contain("START=0");
            file.Should().Contain("END=1000");
            file.Should().Contain("START=1000");
            file.Should().Contain("END=3500");
        }

        [Test]
        public void should_leave_out_tags_that_have_no_value()
        {
            var file = FfmpegM4bBuilder.BuildMetadataFile(
                new List<double> { 1000 },
                new List<string> { "Part 1" },
                new M4bTags { Title = "Foe", Artist = "Iain Reid" });

            file.Should().Contain("title=Foe");
            file.Should().NotContain("composer=");
            file.Should().NotContain("date=");
            file.Should().NotContain("description=");
            file.Should().NotContain("album=");
        }

        [Test]
        public void should_escape_characters_that_would_break_the_metadata_file()
        {
            var tags = new M4bTags
            {
                Title = "Book 3: Hash # Tag; and = sign",
                Description = "line one" + "\n" + "line two"
            };

            var file = FfmpegM4bBuilder.BuildMetadataFile(new List<double>(), new List<string>(), tags);

            file.Should().Contain("title=Book 3: Hash \\# Tag\\; and \\= sign");
            file.Should().Contain("description=line one\\" + "\n" + "line two");
        }

        [Test]
        public void should_not_put_tags_on_the_command_line()
        {
            var args = FfmpegM4bBuilder.BuildArgs(
                new List<string> { "/in/1.mp3", "/in/2.mp3" },
                "/tmp/chapters.txt",
                "/tmp/cover.jpg",
                "/out/book.m4b");

            args.Should().Contain("-map_metadata 2");
            args.Should().NotContain("-metadata ");
            args.Should().Contain("concat=n=2");
            args.Should().Contain("attached_pic");
        }
    }
}
