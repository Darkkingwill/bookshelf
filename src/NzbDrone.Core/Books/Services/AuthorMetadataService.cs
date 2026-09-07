using System.Collections.Generic;

namespace NzbDrone.Core.Books
{
    public interface IAuthorMetadataService
    {
        bool Upsert(AuthorMetadata author);
        bool UpsertMany(List<AuthorMetadata> authors);
        AuthorMetadata Update(AuthorMetadata author);
    }

    public class AuthorMetadataService : IAuthorMetadataService
    {
        private readonly IAuthorMetadataRepository _authorMetadataRepository;

        public AuthorMetadataService(IAuthorMetadataRepository authorMetadataRepository)
        {
            _authorMetadataRepository = authorMetadataRepository;
        }

        public bool Upsert(AuthorMetadata author)
        {
            return _authorMetadataRepository.UpsertMany(new List<AuthorMetadata> { author });
        }

        public bool UpsertMany(List<AuthorMetadata> authors)
        {
            return _authorMetadataRepository.UpsertMany(authors);
        }

        // Upsert matches rows by ForeignAuthorId, so it can't be used to edit an existing
        // row's own ForeignAuthorId - that would look like a different author's metadata and
        // get inserted as a new, orphaned row instead of updating the one Authors.AuthorMetadataId
        // actually points to. This updates the known row directly by its primary key instead.
        public AuthorMetadata Update(AuthorMetadata author)
        {
            return _authorMetadataRepository.Update(author);
        }
    }
}
