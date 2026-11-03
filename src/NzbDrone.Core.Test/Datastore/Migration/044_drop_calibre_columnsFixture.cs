using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class drop_calibre_columnsFixture : MigrationTest<drop_calibre_columns>
    {
        [Test]
        public void should_drop_the_calibre_columns_and_keep_the_rows()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("RootFolders").Row(new
                {
                    Path = "/media/audiobooks",
                    Name = "Audiobooks",
                    IsCalibreLibrary = false
                });

                c.Insert.IntoTable("BookFiles").Row(new
                {
                    EditionId = 7,
                    CalibreId = 0,
                    Quality = "{}",
                    Size = 1234L,
                    DateAdded = new DateTime(2026, 1, 2),
                    Path = "/media/audiobooks/a/b.m4b",
                    Part = 1
                });
            });

            var rootFolderColumns = db.Query<TableColumn44>("SELECT \"name\" AS \"Name\" FROM pragma_table_info('RootFolders')").Select(x => x.Name).ToList();
            var bookFileColumns = db.Query<TableColumn44>("SELECT \"name\" AS \"Name\" FROM pragma_table_info('BookFiles')").Select(x => x.Name).ToList();

            rootFolderColumns.Should().NotContain(new[] { "IsCalibreLibrary", "CalibreSettings" });
            rootFolderColumns.Should().Contain(new[] { "Path", "Name", "DefaultTags" });
            bookFileColumns.Should().NotContain("CalibreId");
            bookFileColumns.Should().Contain(new[] { "EditionId", "Path", "Part", "IndexerFlags" });

            var rootFolders = db.Query<RootFolder44>("SELECT \"Path\", \"Name\" FROM \"RootFolders\"");
            rootFolders.Should().HaveCount(1);
            rootFolders.First().Path.Should().Be("/media/audiobooks");
            rootFolders.First().Name.Should().Be("Audiobooks");

            var bookFiles = db.Query<BookFile44>("SELECT \"EditionId\", \"Path\", \"Size\" FROM \"BookFiles\"");
            bookFiles.Should().HaveCount(1);
            bookFiles.First().EditionId.Should().Be(7);
            bookFiles.First().Path.Should().Be("/media/audiobooks/a/b.m4b");
            bookFiles.First().Size.Should().Be(1234L);
        }
    }

    public class TableColumn44
    {
        public string Name { get; set; }
    }

    public class RootFolder44
    {
        public string Path { get; set; }
        public string Name { get; set; }
    }

    public class BookFile44
    {
        public int EditionId { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
    }
}
