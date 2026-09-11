using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    // Tags written as "Author - Series NN - Title" put a prefix on the file title that every
    // book by that author in that series shares. The prefix is most of the string, so it
    // contributes near-identical similarity to every candidate and the actual title - the only
    // discriminating part - is swamped. Observed live on a 13-book series: the correct book
    // scored 0.26388 and a wrong book in the same series scored 0.25390, so the wrong one won
    // by 0.01 and 5 of 13 folders were misfiled essentially at random.
    [TestFixture]
    public class CompoundTagDistanceFixture : CoreTest
    {
        private const string AuthorName = "Robert Enright";
        private const string SeriesName = "Sam Pope";

        private static Edition GivenSeriesEdition(string title, string position)
        {
            var metadata = new AuthorMetadata { Name = AuthorName };

            var book = new Book
            {
                Title = title,
                AuthorMetadata = metadata,
                Author = new Author { Metadata = metadata }
            };

            var edition = new Edition
            {
                Title = title,
                Monitored = true,
                Book = book
            };

            book.Editions = new List<Edition> { edition };
            book.SeriesLinks = new List<SeriesBookLink>
            {
                new SeriesBookLink
                {
                    Book = book,
                    Series = new Series { Title = SeriesName },
                    Position = position,
                    IsPrimary = true
                }
            };

            return edition;
        }

        private static List<LocalBook> GivenTrack(string bookTag, string fileName)
        {
            return new List<LocalBook>
            {
                new LocalBook
                {
                    Path = $"/media/{AuthorName}/{SeriesName}/1 - {fileName}/{fileName}.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        BookTitle = bookTag,
                        Authors = new List<string> { AuthorName }
                    }
                }
            };
        }

        [Test]
        public void compound_tag_should_match_its_own_book_not_a_sibling_in_the_same_series()
        {
            var tracks = GivenTrack($"{AuthorName} - {SeriesName} 01 - The Night Shift", "The Night Shift");

            var correct = GivenSeriesEdition("The Night Shift", "1");
            var sibling = GivenSeriesEdition("Worlds Apart", "9");

            var dCorrect = DistanceCalculator.BookDistance(tracks, correct).NormalizedDistance();
            var dSibling = DistanceCalculator.BookDistance(tracks, sibling).NormalizedDistance();

            Console.WriteLine($"correct 'The Night Shift' = {dCorrect:F5}");
            Console.WriteLine($"sibling 'Worlds Apart'    = {dSibling:F5}");
            Console.WriteLine($"margin = {dSibling - dCorrect:F5}");

            // The same file tagged with just its title - the unambiguous case.
            var clean = GivenTrack("The Night Shift", "The Night Shift");
            var dClean = DistanceCalculator.BookDistance(clean, GivenSeriesEdition("The Night Shift", "1")).NormalizedDistance();

            Console.WriteLine($"clean tag baseline        = {dClean:F5}");

            dCorrect.Should().BeLessThan(dSibling, "the file's own book must win");

            // The prefix carries no information about which book this is, so carrying it must
            // not cost anything: a compound tag should score its own book exactly as well as a
            // clean tag does. Anything worse means the prefix is still diluting the signal.
            dCorrect.Should().BeApproximately(dClean, 0.001, "a shared prefix must not make the file's own book harder to recognise");
        }

        [TestCase("Robert Enright - Sam Pope 01 - The Night Shift", "The Night Shift", "Worlds Apart")]
        [TestCase("Robert Enright - Sam Pope 02 - The Takers", "The Takers", "Worlds Apart")]
        [TestCase("Robert Enright - Sam Pope 05 - The Final Mile", "The Final Mile", "Worlds Apart")]
        [TestCase("Sam Pope 07 - No Way Back", "No Way Back", "Worlds Apart")]
        [TestCase("The Night Shift", "The Night Shift", "Worlds Apart")]
        public void correct_book_should_beat_sibling(string tag, string correctTitle, string siblingTitle)
        {
            var tracks = GivenTrack(tag, correctTitle);

            var dCorrect = DistanceCalculator.BookDistance(tracks, GivenSeriesEdition(correctTitle, "1")).NormalizedDistance();
            var dSibling = DistanceCalculator.BookDistance(tracks, GivenSeriesEdition(siblingTitle, "9")).NormalizedDistance();

            Console.WriteLine($"[{tag}] correct={dCorrect:F5} sibling={dSibling:F5} margin={dSibling - dCorrect:F5}");

            dCorrect.Should().BeLessThan(dSibling);
        }
    }
}
