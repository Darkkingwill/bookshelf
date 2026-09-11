using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    /// <summary>
    /// This class models the best book in a work, as defined by the Goodreads API.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public sealed class AuthorBookListResource : GoodreadsResource
    {
        public override string ElementName => "author";

        public List<BookResource> List { get; private set; }

        /// <summary>
        /// The 1-based index of the last book on this page, and the total number of
        /// books across all pages - both from the &lt;books start end total&gt; attributes.
        /// Only meaningful for the author/list endpoint (author/show's embedded list
        /// doesn't paginate and leaves these at 0).
        /// </summary>
        public int End { get; private set; }

        public int Total { get; private set; }

        public override void Parse(XElement element)
        {
            var results = element.Descendants("books");
            if (results.Count() == 1)
            {
                var booksElement = results.First();
                List = booksElement.ParseChildren<BookResource>();

                int.TryParse(booksElement.Attribute("end")?.Value, out var end);
                int.TryParse(booksElement.Attribute("total")?.Value, out var total);
                End = end;
                Total = total;
            }
        }
    }
}
