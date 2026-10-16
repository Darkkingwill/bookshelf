using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Retagging a downloaded M4B rewrites the file in place, so these run the real writer on a real audiobook-style
    // M4B (AAC audio, two chapters, an embedded cover, the uploader's comment) and check nothing but the tags changed.
    [TestFixture]
    public class AudioTagAudiobookFixture
    {
        private static readonly string TestDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media");

        private string _original;
        private string _path;

        [SetUp]
        public void Setup()
        {
            _original = Path.Combine(TestDir, "audiobook.m4b");
            _path = Path.Combine(Path.GetTempPath(), $"audiobook-{Guid.NewGuid()}.m4b");
            File.Copy(_original, _path);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static AudioTag NewTags(AudioTag current)
        {
            return new AudioTag
            {
                Title = "The Big Nowhere",
                Performers = new[] { "James Ellroy" },
                BookAuthors = new[] { "James Ellroy" },
                Book = "The Big Nowhere",
                Track = 1,
                TrackCount = 1,
                Disc = current.Disc,
                DiscCount = current.DiscCount,
                Media = current.Media,
                Date = new DateTime(1988, 9, 1),
                Year = 1988,
                OriginalReleaseDate = new DateTime(1988, 9, 1),
                OriginalYear = 1988,
                Genres = new[] { "Audiobook" },
                Narrator = "Jason Culp",
                Description = "This work is set in 1950s, Los Angeles. A city of angels and of death."
            };
        }

        // The sample data of an MP4 file: the payload of its top-level 'mdat' box.
        private static byte[] MediaData(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var position = 0;

            while (position + 8 <= bytes.Length)
            {
                long size = (bytes[position] << 24) | (bytes[position + 1] << 16) | (bytes[position + 2] << 8) | bytes[position + 3];
                var type = System.Text.Encoding.ASCII.GetString(bytes, position + 4, 4);

                if (size == 0)
                {
                    size = bytes.Length - position;
                }

                if (type == "mdat")
                {
                    return bytes.Skip(position + 8).Take((int)size - 8).ToArray();
                }

                position += (int)size;
            }

            return new byte[0];
        }

        // The whole 'chpl' chapter box (header included) as it sits in the file.
        private static byte[] ChapterBox(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var type = System.Text.Encoding.ASCII.GetBytes("chpl");

            for (var i = 4; i + 4 <= bytes.Length; i++)
            {
                if (bytes[i] == type[0] && bytes[i + 1] == type[1] && bytes[i + 2] == type[2] && bytes[i + 3] == type[3])
                {
                    var size = (bytes[i - 4] << 24) | (bytes[i - 3] << 16) | (bytes[i - 2] << 8) | bytes[i - 1];
                    return bytes.Skip(i - 4).Take(size).ToArray();
                }
            }

            return new byte[0];
        }

        [Test]
        public void should_keep_the_chapter_list_in_the_user_data_byte_for_byte()
        {
            // ffmpeg keeps its chapters in a 'chpl' box next to the tags; TagLib reads such boxes 24 bytes late
            var chaptersBefore = ChapterBox(_original);
            chaptersBefore.Length.Should().BeGreaterThan(8);

            NewTags(new AudioTag(_path)).Write(_path).Should().BeTrue();

            ChapterBox(_path).Should().Equal(chaptersBefore);
        }

        [Test]
        public void should_catch_and_undo_a_save_that_damages_the_file()
        {
            var originalBytes = File.ReadAllBytes(_path);

            // saving through plain TagLib is what shortens and shifts the chapter box
            using (var guard = Mp4SaveGuard.Begin(_path))
            {
                guard.Should().NotBeNull();
                guard.SaveStarting();

                using (var file = TagLib.File.Create(_path))
                {
                    file.Tag.Title = "A much longer title than the one the file had to begin with";
                    file.Save();
                }

                ChapterBox(_path).Should().NotEqual(ChapterBox(_original));

                guard.Commit().Should().BeFalse();
            }

            File.ReadAllBytes(_path).Should().Equal(originalBytes);
        }

        [Test]
        public void should_undo_a_save_whose_sizes_do_not_add_up()
        {
            var originalBytes = File.ReadAllBytes(_path);

            using (var guard = Mp4SaveGuard.Begin(_path))
            {
                guard.SaveStarting();

                // make the user data box a few bytes too small so the chapter box overruns it
                var bytes = File.ReadAllBytes(_path);
                var udta = System.Text.Encoding.ASCII.GetBytes("udta");
                var at = 0;

                for (var i = 4; i + 4 <= bytes.Length; i++)
                {
                    if (bytes[i] == udta[0] && bytes[i + 1] == udta[1] && bytes[i + 2] == udta[2] && bytes[i + 3] == udta[3])
                    {
                        at = i - 4;
                        break;
                    }
                }

                bytes[at + 3] -= 24;
                File.WriteAllBytes(_path, bytes);

                guard.Commit().Should().BeFalse();
            }

            File.ReadAllBytes(_path).Should().Equal(originalBytes);
        }

        [Test]
        public void should_leave_no_backup_files_behind()
        {
            NewTags(new AudioTag(_path)).Write(_path).Should().BeTrue();

            Directory.GetFiles(Path.GetDirectoryName(_path), Path.GetFileName(_path) + ".*.tagbak").Should().BeEmpty();
        }

        [Test]
        public void should_not_let_removing_the_tags_damage_the_chapters()
        {
            var chaptersBefore = ChapterBox(_original);

            new AudioTagService(null, null, null, null, null, null, null, null, NLog.LogManager.GetCurrentClassLogger()).RemoveAllTags(_path);

            ChapterBox(_path).Should().Equal(chaptersBefore);
        }

        [Test]
        public void should_write_the_narrator_and_description_to_an_m4b()
        {
            var before = new AudioTag(_path);

            NewTags(before).Write(_path);

            var after = new AudioTag(_path);

            after.Title.Should().Be("The Big Nowhere");
            after.Performers.Should().Equal("James Ellroy");
            after.Book.Should().Be("The Big Nowhere");
            after.Narrator.Should().Be("Jason Culp");
            after.Description.Should().StartWith("This work is set in 1950s");
            after.Genres.Should().Contain("Audiobook");
            after.Year.Should().Be(1988);
        }

        [Test]
        public void should_leave_the_audio_chapters_and_cover_untouched()
        {
            var before = new AudioTag(_path);
            var mediaBefore = MediaData(_original);

            NewTags(before).Write(_path);

            var after = new AudioTag(_path);

            // the audio, cover and chapter samples are byte for byte what they were
            MediaData(_path).Should().Equal(mediaBefore);
            mediaBefore.Should().NotBeEmpty();

            after.Duration.TotalSeconds.Should().BeApproximately(before.Duration.TotalSeconds, 0.05);

            // no image was supplied, so the embedded cover is kept
            after.ImageSize.Should().Be(before.ImageSize);
        }

        [Test]
        public void should_keep_the_uploaders_own_comment()
        {
            NewTags(new AudioTag(_path)).Write(_path);

            using (var file = TagLib.File.Create(_path))
            {
                file.Tag.Comment.Should().Be("Read by Someone Original");
            }
        }

        [Test]
        public void should_not_wipe_the_narrator_or_description_when_they_are_unknown()
        {
            var tags = NewTags(new AudioTag(_path));
            tags.Write(_path);

            var withoutThem = NewTags(new AudioTag(_path));
            withoutThem.Narrator = null;
            withoutThem.Description = null;
            withoutThem.Write(_path);

            var after = new AudioTag(_path);
            after.Narrator.Should().Be("Jason Culp");
            after.Description.Should().StartWith("This work is set in 1950s");
        }

        [Test]
        public void should_report_the_narrator_and_description_as_changes_only_when_they_differ()
        {
            var current = new AudioTag(_path);
            var wanted = NewTags(current);

            var diff = current.Diff(wanted);

            diff.Keys.Should().Contain(new List<string> { "Narrator", "Description" });

            wanted.Write(_path);

            new AudioTag(_path).Diff(wanted).Keys.Should().NotContain(new List<string> { "Narrator", "Description" });
        }

        [Test]
        public void should_not_call_a_missing_narrator_a_change()
        {
            var current = new AudioTag(_path);
            var wanted = NewTags(current);
            wanted.Narrator = null;
            wanted.Description = null;

            current.Diff(wanted).Keys.Should().NotContain(new List<string> { "Narrator", "Description" });
        }
    }
}
