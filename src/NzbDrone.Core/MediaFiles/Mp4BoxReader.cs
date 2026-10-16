using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NzbDrone.Core.MediaFiles
{
    // One ISO base media (MP4) box, located by walking the file ourselves rather than through TagLib.
    internal class Mp4Box
    {
        public string Type { get; set; }
        public string Path { get; set; }
        public long Offset { get; set; }
        public int HeaderSize { get; set; }
        public long Size { get; set; }
        public List<Mp4Box> Children { get; } = new List<Mp4Box>();

        public long End => Offset + Size;
        public long BodyOffset => Offset + HeaderSize;
        public long BodySize => Size - HeaderSize;
    }

    // A strict walk of the boxes that matter for tagging. Unlike TagLib it rejects any box that overruns its parent,
    // which is how a bad size fix-up shows up.
    internal static class Mp4BoxReader
    {
        // 'meta' is deliberately a leaf: it is a full box in iTunes files and a plain one in QuickTime files.
        private static readonly HashSet<string> Containers = new HashSet<string> { "moov", "trak", "mdia", "minf", "stbl", "dinf", "edts", "udta" };

        public static List<Mp4Box> Read(Stream stream, out long trailingBytes)
        {
            var boxes = ReadLevel(stream, 0, stream.Length, string.Empty, true, out trailingBytes);
            return boxes;
        }

        public static IEnumerable<Mp4Box> Flatten(IEnumerable<Mp4Box> boxes)
        {
            foreach (var box in boxes)
            {
                yield return box;

                foreach (var child in Flatten(box.Children))
                {
                    yield return child;
                }
            }
        }

        public static byte[] ReadBytes(Stream stream, long offset, long count)
        {
            var buffer = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);

            var read = 0;
            while (read < count)
            {
                var n = stream.Read(buffer, read, (int)(count - read));
                if (n <= 0)
                {
                    throw new InvalidDataException($"Unexpected end of file reading {count} bytes at {offset}");
                }

                read += n;
            }

            return buffer;
        }

        private static List<Mp4Box> ReadLevel(Stream stream, long start, long end, string parentPath, bool topLevel, out long trailingBytes)
        {
            var boxes = new List<Mp4Box>();
            var position = start;

            while (end - position >= 8)
            {
                var header = ReadBytes(stream, position, 8);
                var size = (long)ToUInt32(header, 0);
                var type = Encoding.GetEncoding("ISO-8859-1").GetString(header, 4, 4);
                var headerSize = 8;

                if (size == 1)
                {
                    var large = ReadBytes(stream, position + 8, 8);
                    var value = ToUInt64(large, 0);

                    if (value > long.MaxValue)
                    {
                        throw new InvalidDataException($"Box '{type}' at {position} has an impossible size");
                    }

                    size = (long)value;
                    headerSize = 16;
                }
                else if (size == 0)
                {
                    size = end - position;
                }

                if (size < headerSize || position + size > end)
                {
                    throw new InvalidDataException($"Box '{type}' at {position} has size {size} but only {end - position} bytes remain in its parent '{parentPath}'");
                }

                var box = new Mp4Box
                {
                    Type = type,
                    Path = parentPath + "/" + type,
                    Offset = position,
                    HeaderSize = headerSize,
                    Size = size
                };

                if (Containers.Contains(type))
                {
                    box.Children.AddRange(ReadLevel(stream, box.BodyOffset, box.End, box.Path, false, out _));
                }

                boxes.Add(box);
                position += size;
            }

            // fewer than 8 bytes left: tolerated as padding (a QuickTime udta ends with a 4 byte zero terminator)
            trailingBytes = end - position;
            return boxes;
        }

        private static uint ToUInt32(byte[] bytes, int index)
        {
            return ((uint)bytes[index] << 24) | ((uint)bytes[index + 1] << 16) | ((uint)bytes[index + 2] << 8) | bytes[index + 3];
        }

        private static ulong ToUInt64(byte[] bytes, int index)
        {
            return ((ulong)ToUInt32(bytes, index) << 32) | ToUInt32(bytes, index + 4);
        }
    }
}
