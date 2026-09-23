using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    [TestFixture]
    public class GoodreadsLinkParserFixture : CoreTest
    {
        [TestCase("https://www.goodreads.com/book/show/656.War_and_Peace", "edition:656")]
        [TestCase("https://www.goodreads.com/book/show/656-war-and-peace", "edition:656")]
        [TestCase("https://www.goodreads.com/book/show/656", "edition:656")]
        [TestCase("http://goodreads.com/book/show/656", "edition:656")]
        [TestCase("goodreads.com/book/show/656", "edition:656")]
        [TestCase("https://m.goodreads.com/book/show/656", "edition:656")]
        [TestCase("https://www.goodreads.com/book/show/656.War_and_Peace?ac=1&from_search=true&qid=abc&rank=1", "edition:656")]
        [TestCase("  <https://www.goodreads.com/book/show/656>  ", "edition:656")]
        [TestCase("HTTPS://WWW.GOODREADS.COM/BOOK/SHOW/656", "edition:656")]
        [TestCase("https://www.goodreads.com/work/editions/4912783-war-and-peace", "work:4912783")]
        [TestCase("https://www.goodreads.com/work/show/4912783", "work:4912783")]
        [TestCase("https://www.goodreads.com/author/show/128382.Leo_Tolstoy", "author:128382")]
        [TestCase("https://www.goodreads.com/author/list/128382.Leo_Tolstoy", "author:128382")]
        public void should_translate_goodreads_links(string input, string expected)
        {
            GoodreadsLinkParser.Normalize(input).Should().Be(expected);
        }

        [TestCase("goodreads:656", "edition:656")]
        [TestCase("Goodreads: 656", "edition:656")]
        [TestCase("GOODREADS:656", "edition:656")]
        public void should_translate_goodreads_prefix(string input, string expected)
        {
            GoodreadsLinkParser.Normalize(input).Should().Be(expected);
        }

        [TestCase("War and Peace")]
        [TestCase("edition:656")]
        [TestCase("isbn:067003469X")]
        [TestCase("goodreads:abc")]
        [TestCase("https://www.goodreads.com/review/list/12345")]
        [TestCase("https://www.amazon.com/dp/B00JCDK5ME")]
        [TestCase("Goodreads Choice Awards")]
        [TestCase("")]
        [TestCase(null)]
        public void should_leave_other_terms_alone(string input)
        {
            GoodreadsLinkParser.Normalize(input).Should().Be(input);
        }
    }
}
