using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BookTests
{
    // Single(x => x.Monitored) throws when a book has no monitored edition, which a refresh, a manual add or a metadata
    // provider can all produce. GetMonitoredEdition replaces it on the paths that must not crash.
    [TestFixture]
    public class EditionExtensionsFixture
    {
        [Test]
        public void should_prefer_the_monitored_edition()
        {
            var editions = new List<Edition>
            {
                new Edition { Id = 1, Monitored = false },
                new Edition { Id = 2, Monitored = true },
                new Edition { Id = 3, Monitored = false }
            };

            editions.GetMonitoredEdition().Id.Should().Be(2);
        }

        [Test]
        public void should_fall_back_to_the_first_edition_when_none_is_monitored()
        {
            var editions = new List<Edition>
            {
                new Edition { Id = 7, Monitored = false },
                new Edition { Id = 8, Monitored = false }
            };

            editions.GetMonitoredEdition().Id.Should().Be(7);
        }

        [Test]
        public void should_return_the_first_monitored_edition_when_several_are_monitored()
        {
            var editions = new List<Edition>
            {
                new Edition { Id = 4, Monitored = false },
                new Edition { Id = 5, Monitored = true },
                new Edition { Id = 6, Monitored = true }
            };

            editions.GetMonitoredEdition().Id.Should().Be(5);
        }

        [Test]
        public void should_return_null_when_there_are_no_editions()
        {
            new List<Edition>().GetMonitoredEdition().Should().BeNull();
        }

        [Test]
        public void should_return_null_for_a_null_list()
        {
            ((List<Edition>)null).GetMonitoredEdition().Should().BeNull();
        }
    }
}
