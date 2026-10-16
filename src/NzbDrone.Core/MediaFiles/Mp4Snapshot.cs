using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NzbDrone.Core.MediaFiles
{
    // What an MP4 looks like apart from its tags: the box layout, the content of every user data box other than 'meta',
    // and the chunk offset tables. Two snapshots (before and after a tag save) can be compared to see if the save
    // damaged anything.
    internal class Mp4Snapshot
    {
        private readonly List<string> _layout = new List<string>();
        private readonly List<List<string>> _userData = new List<List<string>>();
        private readonly List<List<long>> _chunkOffsets = new List<List<long>>();
        private readonly List<long> _mdatOffsets = new List<long>();
        private long _trailingBytes;

        public static Mp4Snapshot Capture(string path)
        {
            var snapshot = new Mp4Snapshot();

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var boxes = Mp4BoxReader.Read(stream, out var trailing);
                snapshot._trailingBytes = trailing;

                if (!boxes.Any(b => b.Type == "moov"))
                {
                    throw new InvalidDataException("No moov box");
                }

                snapshot.AddLayout(boxes);

                foreach (var box in Mp4BoxReader.Flatten(boxes))
                {
                    if (box.Type == "mdat" && box.Path == "/mdat")
                    {
                        snapshot._mdatOffsets.Add(box.Offset);
                    }
                    else if (box.Type == "udta")
                    {
                        snapshot._userData.Add(UserDataFingerprint(stream, box));
                    }
                    else if (box.Type == "stco" || box.Type == "co64")
                    {
                        snapshot._chunkOffsets.Add(ReadChunkOffsets(stream, box));
                    }
                }
            }

            return snapshot;
        }

        // Returns null when the saved file is consistent with this snapshot, otherwise what is wrong with it.
        public string Compare(Mp4Snapshot after, string path)
        {
            if (after._trailingBytes != _trailingBytes)
            {
                return "bytes after the last box changed";
            }

            if (!_layout.SequenceEqual(after._layout))
            {
                return "the box layout outside the tags changed";
            }

            if (_userData.Count != after._userData.Count)
            {
                return "the number of user data boxes changed";
            }

            for (var i = 0; i < _userData.Count; i++)
            {
                var lost = _userData[i].Except(after._userData[i]).ToList();

                if (lost.Any())
                {
                    return $"user data box content was altered or lost ({lost.First().Split('|')[0]})";
                }
            }

            if (_chunkOffsets.Count != after._chunkOffsets.Count || _mdatOffsets.Count != after._mdatOffsets.Count)
            {
                return "the chunk offset tables changed";
            }

            for (var i = 0; i < _chunkOffsets.Count; i++)
            {
                if (_chunkOffsets[i].Count != after._chunkOffsets[i].Count)
                {
                    return "a chunk offset table changed length";
                }

                for (var j = 0; j < _chunkOffsets[i].Count; j++)
                {
                    if (after._chunkOffsets[i][j] != _chunkOffsets[i][j] + MediaShift(_chunkOffsets[i][j], after))
                    {
                        return "chunk offsets no longer point at the media data";
                    }
                }
            }

            try
            {
                using (var file = TagLib.File.Create(path))
                {
                    _ = file.Tag?.Title;
                    _ = file.Properties?.Duration;
                }
            }
            catch (Exception ex)
            {
                return $"the file can no longer be read back ({ex.Message})";
            }

            return null;
        }

        private static List<string> UserDataFingerprint(Stream stream, Mp4Box udta)
        {
            var result = new List<string>();

            using (var sha = SHA256.Create())
            {
                // 'meta' holds the tags themselves and is expected to change, 'free' is padding TagLib reshuffles
                foreach (var child in udta.Children.Where(c => c.Type != "meta" && c.Type != "free"))
                {
                    var body = Mp4BoxReader.ReadBytes(stream, child.BodyOffset, child.BodySize);
                    result.Add($"{child.Type}|{Convert.ToBase64String(sha.ComputeHash(body))}");
                }
            }

            return result;
        }

        private static List<long> ReadChunkOffsets(Stream stream, Mp4Box box)
        {
            var header = Mp4BoxReader.ReadBytes(stream, box.BodyOffset, 8);
            var count = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
            var width = box.Type == "co64" ? 8 : 4;

            if (count < 0 || 8 + ((long)count * width) > box.BodySize)
            {
                throw new InvalidDataException($"{box.Type} at {box.Offset} claims {count} entries but is only {box.BodySize} bytes");
            }

            var data = Mp4BoxReader.ReadBytes(stream, box.BodyOffset + 8, (long)count * width);
            var offsets = new List<long>(count);

            for (var i = 0; i < count; i++)
            {
                long value = 0;

                for (var b = 0; b < width; b++)
                {
                    value = (value << 8) | data[(i * width) + b];
                }

                offsets.Add(value);
            }

            return offsets;
        }

        // How far the media data holding this offset moved
        private long MediaShift(long oldOffset, Mp4Snapshot after)
        {
            for (var m = 0; m < _mdatOffsets.Count; m++)
            {
                var end = m + 1 < _mdatOffsets.Count ? _mdatOffsets[m + 1] : long.MaxValue;

                if (oldOffset >= _mdatOffsets[m] && oldOffset < end)
                {
                    return after._mdatOffsets[m] - _mdatOffsets[m];
                }
            }

            return 0;
        }

        // The layout minus what tagging legitimately changes: the size of every box that contains a udta, and the
        // inside of a udta (compared by content instead).
        private void AddLayout(IEnumerable<Mp4Box> boxes)
        {
            foreach (var box in boxes)
            {
                if (box.Type == "udta")
                {
                    _layout.Add(box.Path);
                }
                else if (box.Children.Any())
                {
                    _layout.Add(box.Path);
                    AddLayout(box.Children);
                }
                else
                {
                    _layout.Add($"{box.Path}|{box.Size}");
                }
            }
        }
    }
}
