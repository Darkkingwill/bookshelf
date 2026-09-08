using System.Collections.Generic;

namespace Readarr.Api.V1.Books
{
    public class MergeBooksResource
    {
        public int TargetBookId { get; set; }
        public List<int> SourceBookIds { get; set; }
    }
}
