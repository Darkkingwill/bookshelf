using NzbDrone.Core.Books;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public static class GrabbedBook
    {
        // The one book a download was grabbed for, so the import can start from the book the user picked instead of
        // guessing it from tags. Only a grab for exactly one book of the expected author counts: a collection, or a
        // release that spans several books, says nothing certain about which book a given file is.
        public static Book From(RemoteBook remoteBook, Author author)
        {
            var books = remoteBook?.Books;

            if (books == null || books.Count != 1)
            {
                return null;
            }

            var book = books[0];

            if (author != null && book.AuthorMetadataId != author.AuthorMetadataId)
            {
                return null;
            }

            return book;
        }
    }
}
