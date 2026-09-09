using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.M4bConversion
{
    public interface IFfmpegM4bBuilder
    {
        // inputFiles must already be in the order they should play/appear as chapters.
        // chapterTitles must be the same length as inputFiles. coverImagePath is optional.
        void BuildM4b(List<string> inputFiles, List<string> chapterTitles, string title, string artist, string album, string coverImagePath, string outputPath);
    }

    // Everything ffmpeg/ffprobe-specific lives here so the orchestrating service (which owns
    // the BookFile/database lifecycle) doesn't need to know how the merge actually happens.
    // Uses the concat *filter* (not the concat demuxer) because source files in a real-world
    // multi-part download are not guaranteed to share a codec/sample rate - the demuxer
    // requires that and fails opaquely when it doesn't hold; the filter re-decodes each input
    // so mismatched sources still merge correctly.
    public class FfmpegM4bBuilder : IFfmpegM4bBuilder
    {
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public FfmpegM4bBuilder(IProcessProvider processProvider, Logger logger)
        {
            _processProvider = processProvider;
            _logger = logger;
        }

        public void BuildM4b(List<string> inputFiles, List<string> chapterTitles, string title, string artist, string album, string coverImagePath, string outputPath)
        {
            if (inputFiles.Count != chapterTitles.Count)
            {
                throw new ArgumentException("inputFiles and chapterTitles must be the same length");
            }

            var durationsMs = inputFiles.Select(GetDurationMs).ToList();

            var workDir = Path.Combine(Path.GetTempPath(), "m4b-convert-" + Guid.NewGuid());
            Directory.CreateDirectory(workDir);

            try
            {
                var chaptersPath = Path.Combine(workDir, "chapters.txt");
                File.WriteAllText(chaptersPath, BuildChapterMetadata(durationsMs, chapterTitles), new UTF8Encoding(false));

                var hasCover = coverImagePath.IsNotNullOrWhiteSpace() && File.Exists(coverImagePath);

                var args = BuildArgs(inputFiles, chaptersPath, hasCover ? coverImagePath : null, title, artist, album, outputPath);

                _logger.Debug("Running ffmpeg for M4B conversion");

                var output = _processProvider.StartAndCapture("ffmpeg", args);

                if (output.ExitCode != 0)
                {
                    var errorTail = string.Join("\n", output.Error.Select(l => l.Content).TakeLast(30));
                    throw new InvalidOperationException($"ffmpeg exited with code {output.ExitCode} while building M4B:\n{errorTail}");
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(workDir, true);
                }
                catch
                {
                    // Best-effort cleanup of the temp chapter metadata file; leaving it behind
                    // in the OS temp dir is harmless.
                }
            }
        }

        private double GetDurationMs(string path)
        {
            var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"";
            var output = _processProvider.StartAndCapture("ffprobe", args);

            if (output.ExitCode != 0)
            {
                var errorTail = string.Join("\n", output.Error.Select(l => l.Content).TakeLast(10));
                throw new InvalidOperationException($"ffprobe failed to read duration for {path}:\n{errorTail}");
            }

            var text = string.Join(string.Empty, output.Standard.Select(l => l.Content)).Trim();

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                throw new InvalidOperationException($"ffprobe returned an unparseable duration '{text}' for {path}");
            }

            return seconds * 1000.0;
        }

        private static string BuildChapterMetadata(List<double> durationsMs, List<string> chapterTitles)
        {
            var sb = new StringBuilder();
            sb.AppendLine(";FFMETADATA1");

            double cursor = 0;

            for (var i = 0; i < durationsMs.Count; i++)
            {
                var start = (long)Math.Round(cursor);
                var end = (long)Math.Round(cursor + durationsMs[i]);

                sb.AppendLine();
                sb.AppendLine("[CHAPTER]");
                sb.AppendLine("TIMEBASE=1/1000");
                sb.AppendLine($"START={start}");
                sb.AppendLine($"END={end}");
                sb.AppendLine($"title={EscapeMetadata(chapterTitles[i])}");

                cursor += durationsMs[i];
            }

            return sb.ToString();
        }

        // FFMETADATA1 requires '=', ';', '#', '\' and newlines within a value to be
        // backslash-escaped, or the file fails to parse (or worse, parses into the wrong
        // field) the moment a chapter title contains one of them - book/series titles
        // routinely do (colons are fine, but "Book 3: A Novel" can still carry a stray '#'
        // from an uploader's naming, and box sets sometimes use ';').
        private static string EscapeMetadata(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("=", "\\=")
                .Replace(";", "\\;")
                .Replace("#", "\\#")
                .Replace("\n", "\\\n");
        }

        private static string BuildArgs(List<string> inputFiles, string chaptersPath, string coverImagePath, string title, string artist, string album, string outputPath)
        {
            var args = new StringBuilder("-y ");

            foreach (var file in inputFiles)
            {
                args.Append($"-i \"{file}\" ");
            }

            var chaptersInputIndex = inputFiles.Count;
            args.Append($"-i \"{chaptersPath}\" ");

            var hasCover = coverImagePath.IsNotNullOrWhiteSpace();
            var coverInputIndex = -1;
            if (hasCover)
            {
                coverInputIndex = chaptersInputIndex + 1;
                args.Append($"-i \"{coverImagePath}\" ");
            }

            var concatInputs = string.Join(string.Empty, Enumerable.Range(0, inputFiles.Count).Select(i => $"[{i}:a]"));
            args.Append($"-filter_complex \"{concatInputs}concat=n={inputFiles.Count}:v=0:a=1[outa]\" ");

            args.Append("-map \"[outa]\" ");

            if (hasCover)
            {
                args.Append($"-map {coverInputIndex}:v -c:v copy -disposition:v:0 attached_pic ");
            }

            args.Append($"-map_metadata {chaptersInputIndex} ");
            args.Append($"-metadata title=\"{EscapeArg(title)}\" ");
            args.Append($"-metadata artist=\"{EscapeArg(artist)}\" ");
            args.Append($"-metadata album=\"{EscapeArg(album)}\" ");
            args.Append("-metadata genre=\"Audiobook\" ");

            args.Append("-c:a aac -b:a 128k -ar 44100 ");
            args.Append("-movflags +faststart ");
            args.Append($"\"{outputPath}\"");

            return args.ToString();
        }

        private static string EscapeArg(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\\\"");
        }
    }
}
