using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(043)]
    public class add_series_link_pinned : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("SeriesBookLink").AddColumn("Pinned").AsBoolean().WithDefaultValue(false);
        }
    }
}
