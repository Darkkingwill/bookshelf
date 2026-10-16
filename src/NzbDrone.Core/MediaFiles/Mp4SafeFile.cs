using System.Collections.Generic;
using System.IO;
using System.Linq;
using TagLib;
using TagLib.Mpeg4;

namespace NzbDrone.Core.MediaFiles
{
    // TagLib# reads the body of boxes it does not know (such as the 'chpl' chapter list an ffmpeg-made M4B keeps in
    // moov.udta) from the wrong place: the box header reader leaves the stream 32 bytes into the box and the body is
    // read from there without seeking back. Saving rewrites the whole udta, so those boxes came out shifted by 24 bytes
    // (and cut short when they sat at the end of the file). This re-reads them correctly before anything is saved.
    internal class Mp4SafeFile : TagLib.Mpeg4.File
    {
        public Mp4SafeFile(string path)
            : base(path)
        {
            RepairUnknownUserDataBoxes();
        }

        private static string Signature(IEnumerable<(string Type, long Size)> children)
        {
            return string.Join(",", children.Select(c => $"{c.Type}:{c.Size}"));
        }

        private void RepairUnknownUserDataBoxes()
        {
            if (UdtaBoxes == null || UdtaBoxes.Count == 0)
            {
                return;
            }

            using (var stream = new FileStream(Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var parsed = Mp4BoxReader.Flatten(Mp4BoxReader.Read(stream, out _))
                    .Where(b => b.Type == "udta")
                    .ToList();

                foreach (var udta in UdtaBoxes)
                {
                    var children = udta.Children?.ToList() ?? new List<Box>();

                    if (!children.Any(c => c is UnknownBox))
                    {
                        continue;
                    }

                    var signature = Signature(children.Select(c => (c.BoxType.ToString(), (long)c.Size)));
                    var match = parsed.FirstOrDefault(p => Signature(p.Children.Select(c => (c.Type, c.Size))) == signature);

                    if (match == null)
                    {
                        throw new CorruptFileException($"Could not match the user data box in {Name} to the file layout; not safe to rewrite it");
                    }

                    parsed.Remove(match);

                    for (var i = 0; i < children.Count; i++)
                    {
                        if (children[i] is UnknownBox unknown)
                        {
                            var source = match.Children[i];
                            unknown.Data = new ByteVector(Mp4BoxReader.ReadBytes(stream, source.BodyOffset, source.BodySize));
                        }
                    }
                }
            }
        }
    }
}
