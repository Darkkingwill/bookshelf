using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AuthorServiceUpdateFixture : DbTest<AuthorRepository, Author>
    {
        [Test]
        public void updating_metadata_source_and_foreign_author_id_should_persist_and_be_readable_fresh()
        {
            var authorMetadataRepo = Mocker.Resolve<AuthorMetadataRepository>();
            Mocker.SetConstant<IAuthorRepository>(Subject);
            Mocker.SetConstant<IAuthorMetadataRepository>(authorMetadataRepo);

            var authorMetadataService = Mocker.Resolve<AuthorMetadataService>();
            Mocker.SetConstant<IAuthorMetadataService>(authorMetadataService);

            var authorService = Mocker.Resolve<AuthorService>();

            var metadata = authorMetadataRepo.Insert(Builder<AuthorMetadata>.CreateNew()
                .With(x => x.Id = 0)
                .With(x => x.ForeignAuthorId = "578957")
                .With(x => x.MetadataSource = "hardcover")
                .With(x => x.Name = "Deanna King")
                .Build());

            var author = Subject.Insert(Builder<Author>.CreateNew()
                .With(x => x.Id = 0)
                .With(x => x.AuthorMetadataId = metadata.Id)
                .With(x => x.Metadata = metadata)
                .With(x => x.CleanName = "deannaking")
                .Build());

            // Simulate exactly what AuthorController.UpdateAuthor / AuthorResource.ToModel do:
            // fetch the stored author fresh, build a small change-only Author, apply it in place.
            var storedAuthor = authorService.GetAuthor(author.Id);

            var changes = new Author
            {
                Id = storedAuthor.Id,
                Metadata = new AuthorMetadata
                {
                    MetadataSource = "goodreads",
                    ForeignAuthorId = "19241704",
                    Name = "Deanna King"
                }
            };

            storedAuthor.ApplyChanges(changes);

            authorService.UpdateAuthor(storedAuthor);

            var freshAuthor = authorService.GetAuthor(author.Id);

            freshAuthor.Metadata.Value.MetadataSource.Should().Be("goodreads");
            freshAuthor.Metadata.Value.ForeignAuthorId.Should().Be("19241704");

            var freshMetadata = authorMetadataRepo.Get(metadata.Id);
            freshMetadata.MetadataSource.Should().Be("goodreads");
            freshMetadata.ForeignAuthorId.Should().Be("19241704");
        }
    }
}
