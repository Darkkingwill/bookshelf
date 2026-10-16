using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.MediaFiles
{
    // Wraps a tag save on an MP4/M4B: keeps a copy of the file beforehand, and afterwards re-reads the result and checks
    // that every box fits its parent, that nothing outside the tags changed and that the chapters and chunk offsets
    // survived. If anything is off the original bytes are put back.
    internal class Mp4SaveGuard : IDisposable
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Mp4SaveGuard));
        private static readonly string[] Extensions = { ".m4b", ".m4a", ".mp4", ".m4v", ".aax" };

        private readonly string _path;
        private readonly string _backup;
        private readonly Mp4Snapshot _before;
        private readonly DateTime _modifiedUtc;
        private bool _saveStarted;
        private bool _done;

        private Mp4SaveGuard(string path, string backup, Mp4Snapshot before)
        {
            _path = path;
            _backup = backup;
            _before = before;
            _modifiedUtc = File.GetLastWriteTimeUtc(path);
        }

        public static bool IsMp4(string path)
        {
            return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }

        // Returns null when the file is not an MP4 or its layout cannot be read: there is then nothing to compare
        // against and the save goes ahead unguarded.
        public static Mp4SaveGuard Begin(string path)
        {
            if (!IsMp4(path))
            {
                return null;
            }

            Mp4Snapshot before;

            try
            {
                before = Mp4Snapshot.Capture(path);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Could not read the MP4 layout of {path}, saving tags without a safety check");
                return null;
            }

            var backup = $"{path}.{Guid.NewGuid():N}.tagbak";
            File.Copy(path, backup);

            return new Mp4SaveGuard(path, backup, before);
        }

        // Call just before the save, so that a save that throws part way through is rolled back too.
        public void SaveStarting()
        {
            _saveStarted = true;
        }

        // Call after the save, once the TagLib file is closed. Returns false (after restoring the original) if the
        // saved file is damaged.
        public bool Commit()
        {
            string problem;

            try
            {
                problem = _before.Compare(Mp4Snapshot.Capture(_path), _path);
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }

            if (problem == null)
            {
                _done = true;
                return true;
            }

            Logger.Error($"Writing tags damaged {_path} ({problem}); restoring the original file");
            Restore();
            return false;
        }

        public void Dispose()
        {
            if (_saveStarted && !_done)
            {
                Restore();
            }

            try
            {
                if (File.Exists(_backup))
                {
                    File.Delete(_backup);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Could not delete the temporary backup {_backup}");
            }
        }

        private void Restore()
        {
            File.Copy(_backup, _path, true);
            File.SetLastWriteTimeUtc(_path, _modifiedUtc);
            _done = true;
        }
    }
}
