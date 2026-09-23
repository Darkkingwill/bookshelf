using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // A refresh pairs each remote edition with a local one. For a book with exactly one local edition
    // carrying a file, a fallback re-targets that edition when its id changed after a metadata source
    // switch. It used to fire for ANY unmatched remote edition, so the first English edition in the
    // list claimed the file's Ukrainian edition, and the Ukrainian remote edition then went down as
    // new with an id that was still in the table - the insert hit the unique index and the refresh
    // failed. Seen live on "All These Worlds": the English editions could never be pulled in.
    [TestFixture]
    public class RefreshBookEditionMatchingFixture : CoreTest<RefreshBookService>
    {
        private static readonly MethodInfo MatchMethod = typeof(RefreshBookService)
            .GetMethod("GetMatchingExistingChildren", BindingFlags.Instance | BindingFlags.NonPublic);

        private Edition _localWithFile;

        [SetUp]
        public void Setup()
        {
            _localWithFile = new Edition { Id = 1, ForeignEditionId = "253954908", Language = "ukr", Monitored = true };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(1))
                .Returns(new List<BookFile> { new BookFile { Id = 44262, EditionId = 1 } });
        }

        private Edition Match(List<Edition> local, Edition remote, List<Edition> remotes)
        {
            var result = (Tuple<Edition, List<Edition>>)MatchMethod.Invoke(Subject, new object[] { local, remote, remotes });
            return result.Item1;
        }

        // Replays SortChildren's loop: which local edition (if any) each remote edition lands on.
        private Dictionary<string, Edition> Pair(List<Edition> local, List<Edition> remotes)
        {
            return remotes.ToDictionary(r => r.ForeignEditionId, r => Match(local, r, remotes));
        }

        [Test]
        public void should_not_relabel_an_edition_the_remote_still_reports()
        {
            var local = new List<Edition> { _localWithFile };
            var remotes = new List<Edition>
            {
                new Edition { ForeignEditionId = "35506021", Language = "eng", Title = "All These Worlds" },
                new Edition { ForeignEditionId = "35663086", Language = "eng", Title = "All These Worlds" },
                new Edition { ForeignEditionId = "253954908", Language = "ukr", Title = "Усі ці світи" }
            };

            var pairs = Pair(local, remotes);

            pairs["35506021"].Should().BeNull("an English edition must be added as new, not claim the Ukrainian one");
            pairs["35663086"].Should().BeNull();
            pairs["253954908"].Should().BeSameAs(_localWithFile, "the Ukrainian edition must match itself by id");
        }

        [Test]
        public void should_still_retarget_when_the_local_id_is_gone_after_a_source_switch()
        {
            var local = new List<Edition> { _localWithFile };
            var remotes = new List<Edition>
            {
                new Edition { ForeignEditionId = "hc:12345", Language = "eng", Title = "All These Worlds" }
            };

            var pairs = Pair(local, remotes);

            pairs["hc:12345"].Should().BeSameAs(_localWithFile, "the file-bearing edition should follow its book to the new id");
        }

        [Test]
        public void should_retarget_only_once_when_the_local_id_is_gone()
        {
            var local = new List<Edition> { _localWithFile };
            var remotes = new List<Edition>
            {
                new Edition { ForeignEditionId = "hc:12345", Language = "eng" },
                new Edition { ForeignEditionId = "hc:67890", Language = "eng" }
            };

            var pairs = Pair(local, remotes);

            pairs.Values.Count(x => x != null).Should().Be(1);
        }
    }
}
