using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // The Calibre integration was removed; nothing reads or writes these columns any more.
    [Migration(044)]
    public class drop_calibre_columns : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Delete.Column("CalibreId").FromTable("BookFiles");
            Delete.Column("IsCalibreLibrary").FromTable("RootFolders");
            Delete.Column("CalibreSettings").FromTable("RootFolders");
        }
    }
}
