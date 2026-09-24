using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Metadata;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface IIdentificationService
    {
        List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config);
    }

    public class IdentificationService : IIdentificationService
    {
        // The distance a folder-title match is given. It has to be an honest, low number because
        // CloseBookMatchSpecification rejects anything above 0.50 further down the line.
        private const double FolderTitleMatchDistance = 0.10;

        private readonly ITrackGroupingService _trackGroupingService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAugmentingService _augmentingService;
        private readonly ICandidateService _candidateService;
        private readonly IMetadataProfileRepository _metadataProfileRepository;
        private readonly Logger _logger;

        public IdentificationService(ITrackGroupingService trackGroupingService,
                                     IMetadataTagService metadataTagService,
                                     IAugmentingService augmentingService,
                                     ICandidateService candidateService,
                                     IMetadataProfileRepository metadataProfileRepository,
                                     Logger logger)
        {
            _trackGroupingService = trackGroupingService;
            _metadataTagService = metadataTagService;
            _augmentingService = augmentingService;
            _candidateService = candidateService;
            _metadataProfileRepository = metadataProfileRepository;
            _logger = logger;
        }

        public List<LocalEdition> GetLocalBookReleases(List<LocalBook> localTracks, bool singleRelease)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<LocalEdition> releases;
            if (singleRelease)
            {
                releases = new List<LocalEdition> { new LocalEdition(localTracks) };
            }
            else
            {
                releases = _trackGroupingService.GroupTracks(localTracks);
            }

            _logger.Debug($"Sorted {localTracks.Count} tracks into {releases.Count} releases in {watch.ElapsedMilliseconds}ms");

            foreach (var localRelease in releases)
            {
                try
                {
                    _augmentingService.Augment(localRelease);
                }
                catch (AugmentingFailedException)
                {
                    _logger.Warn($"Augmentation failed for {localRelease}");
                }
            }

            return releases;
        }

        public List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            // 1 group localTracks so that we think they represent a single release
            // 2 get candidates given specified author, book and release.  Candidates can include extra files already on disk.
            // 3 find best candidate
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Starting book identification");

            var releases = GetLocalBookReleases(localTracks, config.SingleRelease);
            var languages = new EditionLanguagePreference(_metadataProfileRepository.All());

            var i = 0;
            foreach (var localRelease in releases)
            {
                i++;
                _logger.ProgressInfo($"Identifying book {i}/{releases.Count}");
                _logger.Debug($"Identifying book files:\n{localRelease.LocalBooks.Select(x => x.Path).ConcatToString("\n")}");

                try
                {
                    IdentifyRelease(localRelease, idOverrides, config, languages);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error identifying release");
                }
            }

            watch.Stop();

            _logger.Debug($"Track identification for {localTracks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return releases;
        }

        private List<LocalBook> ToLocalTrack(IEnumerable<BookFile> trackfiles, LocalEdition localRelease)
        {
            var scanned = trackfiles.Join(localRelease.LocalBooks, t => t.Path, l => l.Path, (track, localTrack) => localTrack);
            var toScan = trackfiles.ExceptBy(t => t.Path, scanned, s => s.Path, StringComparer.InvariantCulture);
            var localTracks = scanned.Concat(toScan.Select(x => new LocalBook
            {
                Path = x.Path,
                Size = x.Size,
                Modified = x.Modified,
                FileTrackInfo = _metadataTagService.ReadTags((FileInfoBase)new FileInfo(x.Path)),
                ExistingFile = true,
                AdditionalFile = true,
                Quality = x.Quality
            }))
            .ToList();

            localTracks.ForEach(x => _augmentingService.Augment(x, true));

            return localTracks;
        }

        private void IdentifyRelease(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, EditionLanguagePreference languages)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var usedRemote = false;

            IEnumerable<CandidateEdition> candidateReleases = _candidateService.GetDbCandidatesFromTags(localBookRelease, idOverrides, config.IncludeExisting);

            // A candidate carries every file already attributed to it, and those files get folded into the
            // set that BookDistance scores by majority vote.  A candidate holding more files than the folder
            // being identified would therefore outvote that folder and absorb it, so restrict the extra files
            // to the folders we are actually identifying.  Partial and multi-disc re-imports still work
            // (their existing files are in the same folders), but cross-folder contamination cannot happen.
            var localFolders = new HashSet<string>(
                localBookRelease.LocalBooks
                    .Select(x => Path.GetDirectoryName(x.Path))
                    .Where(x => x.IsNotNullOrWhiteSpace()),
                PathEqualityComparer.Instance);

            // convert all the TrackFiles that represent extra files to List<LocalTrack>
            // local candidates are actually a list so this is fine to enumerate
            var allLocalTracks = ToLocalTrack(candidateReleases
                .SelectMany(x => x.ExistingFiles)
                .Where(x => localFolders.Contains(Path.GetDirectoryName(x.Path) ?? string.Empty))
                .DistinctBy(x => x.Path), localBookRelease);

            _logger.Debug($"Retrieved {allLocalTracks.Count} possible tracks in {watch.ElapsedMilliseconds}ms");

            if (!candidateReleases.Any())
            {
                _logger.Debug("No local candidates found, trying remote");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);
                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => x.Edition.Book.Value.Id > 0 && x.Edition.Book.Value.AuthorId > 0);
                }

                usedRemote = true;
            }

            GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, languages, out var seenCandidate);

            if (!seenCandidate)
            {
                // nothing from the tags - the folder name may still say what this is
                if (TryFolderTitleMatch(localBookRelease, idOverrides, config, languages, allLocalTracks))
                {
                    localBookRelease.PopulateMatch(config.KeepAllEditions);
                    return;
                }

                // can't find any candidates even after using remote search
                // populate the overrides and return
                foreach (var localTrack in localBookRelease.LocalBooks)
                {
                    localTrack.Edition = idOverrides.Edition;
                    localTrack.Book = idOverrides.Book;
                    localTrack.Author = idOverrides.Author;
                }

                return;
            }

            // If the result isn't great and we haven't tried remote candidates, try looking for remote candidates
            // Goodreads may have a better edition of a local book
            if (localBookRelease.Distance.NormalizedDistance() > 0.15 && !usedRemote)
            {
                _logger.Debug("Match not good enough, trying remote candidates");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);

                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => x.Edition.Book.Value.Id > 0);
                }

                GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, languages, out _);
            }

            _logger.Debug($"Best release found in {watch.ElapsedMilliseconds}ms");

            // an exact folder-title match to a different book takes over from the tags
            TryFolderTitleMatch(localBookRelease, idOverrides, config, languages, allLocalTracks);

            localBookRelease.PopulateMatch(config.KeepAllEditions);

            _logger.Debug($"IdentifyRelease done in {watch.ElapsedMilliseconds}ms");
        }

        // Falls back to the name of the book's folder when the tags gave no match, or a match to a different book.
        // Only an exact, unique title match qualifies (see CandidateService.GetDbCandidatesFromFolder), so it is
        // safe to let it replace a tag match. A forced book or edition is never overridden.
        private bool TryFolderTitleMatch(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, EditionLanguagePreference languages, List<LocalBook> allLocalTracks)
        {
            if (idOverrides?.Edition != null || idOverrides?.Book != null)
            {
                return false;
            }

            var current = localBookRelease.Edition;

            // No distance cut-off on the tag match: a candidate that already holds other files is scored partly by
            // those files' tags, so it can look like a near-perfect match to a file that belongs to a different book
            // in the same series (seen live: "Currency" scoring 0.03 against "The System of the World"). An exact,
            // unique folder-title match to a *different* book is stronger evidence than that.
            var candidates = _candidateService.GetDbCandidatesFromFolder(localBookRelease, config.IncludeExisting);

            if (candidates == null || !candidates.Any())
            {
                return false;
            }

            if (current != null && candidates.Any(x => x.Edition.BookId == current.BookId))
            {
                // already on that book
                return false;
            }

            var previousDistance = localBookRelease.Distance;

            // pick the closest edition of the folder-title book
            localBookRelease.Edition = null;
            GetBestRelease(localBookRelease, candidates, allLocalTracks, languages, out _);

            if (localBookRelease.Edition == null)
            {
                // every edition scored as a complete mismatch; the folder title still decides
                localBookRelease.Edition = candidates.First().Edition;
                localBookRelease.ExistingTracks = new List<LocalBook>();
            }

            var distance = new Distance();
            distance.Add("folder_title", FolderTitleMatchDistance);
            localBookRelease.Distance = distance;

            _logger.Debug("Matched {0} to {1} by its folder name (was {2}, distance {3})",
                          localBookRelease,
                          localBookRelease.Edition,
                          current?.ToString() ?? "no match",
                          previousDistance.NormalizedDistance());

            return true;
        }

        private void GetBestRelease(LocalEdition localBookRelease, IEnumerable<CandidateEdition> candidateReleases, List<LocalBook> extraTracksOnDisk, EditionLanguagePreference languages, out bool seenCandidate)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Matching {0} track files against candidates", localBookRelease.TrackCount);
            _logger.Trace("Processing files:\n{0}", string.Join("\n", localBookRelease.LocalBooks.Select(x => x.Path)));

            var bestDistance = localBookRelease.Edition != null ? localBookRelease.Distance.NormalizedDistance() : 1.0;
            seenCandidate = false;

            foreach (var candidateRelease in candidateReleases)
            {
                seenCandidate = true;

                var release = candidateRelease.Edition;
                _logger.Debug($"Trying Release {release}");
                var rwatch = System.Diagnostics.Stopwatch.StartNew();

                var extraTrackPaths = candidateRelease.ExistingFiles.Select(x => x.Path).ToList();
                var extraTracks = extraTracksOnDisk.Where(x => extraTrackPaths.Contains(x.Path)).ToList();
                var allLocalTracks = localBookRelease.LocalBooks.Concat(extraTracks).DistinctBy(x => x.Path).ToList();

                var distance = DistanceCalculator.BookDistance(allLocalTracks, release, languages.For(release));
                var currDistance = distance.NormalizedDistance();

                rwatch.Stop();
                _logger.Debug("Release {0} has distance {1} vs best distance {2} [{3}ms]",
                              release,
                              currDistance,
                              bestDistance,
                              rwatch.ElapsedMilliseconds);
                if (currDistance < bestDistance)
                {
                    bestDistance = currDistance;
                    localBookRelease.Distance = distance;
                    localBookRelease.Edition = release;
                    localBookRelease.ExistingTracks = extraTracks;
                    if (currDistance == 0.0)
                    {
                        break;
                    }
                }
            }

            watch.Stop();
            _logger.Debug($"Best release: {localBookRelease.Edition} Distance {localBookRelease.Distance.NormalizedDistance()} found in {watch.ElapsedMilliseconds}ms");
        }
    }
}
