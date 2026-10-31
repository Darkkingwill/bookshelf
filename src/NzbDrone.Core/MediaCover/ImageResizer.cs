using System;
using System.IO;
using System.Linq;
using System.Text;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaCover
{
    public interface IImageResizer
    {
        void Resize(string source, string destination, int height);
    }

    // Thumbnails are made with ffmpeg, which the audiobook conversion already requires, so cover resizing needs no
    // image library of its own.
    public class ImageResizer : IImageResizer
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IProcessProvider _processProvider;

        public ImageResizer(IDiskProvider diskProvider, IProcessProvider processProvider)
        {
            _diskProvider = diskProvider;
            _processProvider = processProvider;
        }

        public void Resize(string source, string destination, int height)
        {
            try
            {
                var output = _processProvider.StartAndCapture("ffmpeg", BuildArgs(source, destination, height));

                if (output.ExitCode != 0)
                {
                    var errorTail = string.Join("\n", output.Error.Select(l => l.Content).TakeLast(10));
                    throw new InvalidOperationException($"ffmpeg exited with code {output.ExitCode} while resizing {Path.GetFileName(source)}:\n{errorTail}");
                }

                if (!_diskProvider.FileExists(destination))
                {
                    throw new InvalidOperationException($"ffmpeg did not write the resized image for {Path.GetFileName(source)}");
                }
            }
            catch
            {
                if (_diskProvider.FileExists(destination))
                {
                    _diskProvider.DeleteFile(destination);
                }

                throw;
            }
        }

        internal static string BuildArgs(string source, string destination, int height)
        {
            // ffmpeg writes its banner and progress to stderr, which the process provider logs at Error level.
            var args = new StringBuilder("-y -hide_banner -nostats -loglevel error ");

            // A width of -2 keeps the aspect ratio and rounds to an even number of pixels, like Resize(0, height) did.
            args.Append($"-i \"{source}\" -frames:v 1 -vf \"scale=-2:{height}\" ");

            // Thumbnails don't need super high quality
            var extension = Path.GetExtension(destination);
            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                args.Append("-q:v 2 ");
            }

            args.Append($"\"{destination}\"");

            return args.ToString();
        }
    }
}
