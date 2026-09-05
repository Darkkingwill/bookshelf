using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(041)]
    public class add_series_link_overrides : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("SeriesBookLink").AddColumn("TitleOverride").AsString().Nullable();
            Alter.Table("SeriesBookLink").AddColumn("PositionOverride").AsString().Nullable();
            Alter.Table("SeriesBookLink").AddColumn("IsPrimaryOverride").AsBoolean().Nullable();
        }
    }
}
