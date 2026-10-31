using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class ImageResizerFixture : CoreTest<ImageResizer>
    {
        private const string Source = "/covers/logo.png";
        private const string Destination = "/covers/logo-170.jpg";

        private void GivenFfmpegResult(int exitCode, bool destinationExists, string error = null)
        {
            var output = new ProcessOutput { ExitCode = exitCode };
            if (error != null)
            {
                output.Lines.Add(new ProcessOutputLine(ProcessOutputLevel.Error, error));
            }

            Mocker.GetMock<IProcessProvider>()
                  .Setup(v => v.StartAndCapture("ffmpeg", It.IsAny<string>(), null))
                  .Returns(output);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(Destination))
                  .Returns(destinationExists);
        }

        [Test]
        public void should_scale_to_the_height_keeping_the_aspect_ratio()
        {
            var args = ImageResizer.BuildArgs(Source, Destination, 170);

            args.Should().Contain("-i \"/covers/logo.png\"");
            args.Should().Contain("scale=-2:170");
            args.Should().EndWith("\"/covers/logo-170.jpg\"");
        }

        [TestCase("/covers/logo-170.jpg")]
        [TestCase("/covers/logo-170.JPEG")]
        public void should_set_jpeg_quality_for_jpeg_thumbnails(string destination)
        {
            ImageResizer.BuildArgs(Source, destination, 170).Should().Contain("-q:v 2");
        }

        [Test]
        public void should_not_set_jpeg_quality_for_other_formats()
        {
            ImageResizer.BuildArgs(Source, "/covers/logo-170.png", 170).Should().NotContain("-q:v");
        }

        [Test]
        public void should_keep_the_resized_file_when_ffmpeg_succeeds()
        {
            GivenFfmpegResult(0, true);

            Subject.Resize(Source, Destination, 170);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_file_and_throw_if_ffmpeg_fails()
        {
            GivenFfmpegResult(1, true, "Invalid data found when processing input");

            var exception = Assert.Throws<InvalidOperationException>(() => Subject.Resize(Source, Destination, 170));

            exception.Message.Should().Contain("Invalid data found when processing input");
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(Destination), Times.Once());
        }

        [Test]
        public void should_throw_if_ffmpeg_writes_nothing()
        {
            GivenFfmpegResult(0, false);

            Assert.Throws<InvalidOperationException>(() => Subject.Resize(Source, Destination, 170));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_resize_a_real_image_with_ffmpeg()
        {
            if (!FfmpegIsInstalled())
            {
                Assert.Ignore("ffmpeg is not installed on this machine");
            }

            Mocker.SetConstant<IProcessProvider>(Mocker.Resolve<ProcessProvider>());
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns<string>(s => File.Exists(s));

            var mainFile = Path.Combine(TempFolder, "logo.png");
            var resizedFile = Path.Combine(TempFolder, "logo-170.png");

            File.Copy(GetTestPath("Files/1024.png"), mainFile);

            Subject.Resize(mainFile, resizedFile, 170);

            var fileInfo = new FileInfo(resizedFile);
            fileInfo.Exists.Should().BeTrue();
            fileInfo.Length.Should().BeInRange(1000, 30000);

            // width and height are the two big-endian ints at byte 16 of a PNG's IHDR chunk
            var header = new byte[24];
            using (var stream = File.OpenRead(resizedFile))
            {
                stream.Read(header, 0, header.Length);
            }

            var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

            height.Should().Be(170);
            width.Should().Be(170);
        }

        private static bool FfmpegIsInstalled()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });

                process.WaitForExit(10000);
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
