using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class add_author_metadata_source : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Every author is pinned to whichever metadata source last established their
            // identity (hardcover, goodreads, googlebooks, openlibrary, audible,
            // rreadingglasses). Refreshes and adds always resolve through this source rather
            // than whatever's globally configured as primary, so an id from one source's
            // namespace can never be misinterpreted against another's. Existing authors were
            // all effectively resolved through Hardcover, so that's the default.
            Alter.Table("AuthorMetadata").AddColumn("MetadataSource").AsString().NotNullable().WithDefaultValue("hardcover");
        }
    }
}
