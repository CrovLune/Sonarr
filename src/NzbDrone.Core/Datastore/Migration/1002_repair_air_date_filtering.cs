using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(1002)]
public class repair_air_date_filtering : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        // Databases migrated while the fork's original-title migration was also
        // numbered 226 recorded version 226 against it, so upstream's
        // add_air_date_filtering_to_release_profiles is treated as applied and
        // will never run. Add its columns here when they are missing.
        if (!Schema.Table("ReleaseProfiles").Column("AirDateRestriction").Exists())
        {
            Alter.Table("ReleaseProfiles").AddColumn("AirDateRestriction").AsBoolean().WithDefaultValue(false);
        }

        if (!Schema.Table("ReleaseProfiles").Column("AirDateGracePeriod").Exists())
        {
            Alter.Table("ReleaseProfiles").AddColumn("AirDateGracePeriod").AsInt32().WithDefaultValue(0);
        }
    }
}
