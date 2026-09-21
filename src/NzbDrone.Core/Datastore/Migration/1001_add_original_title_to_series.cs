using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration;

// Fork migrations live at 1000+ so they can never collide with an upstream
// version number again. FluentMigrator applies any unapplied migration
// regardless of how it orders against the highest applied one, so upstream
// migrations added later still run normally.
[Migration(1001)]
public class add_original_title_to_series : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        // This shipped as migration 226 and collided with upstream's own 226.
        // Databases migrated by the older build already have these columns.
        if (!Schema.Table("Series").Column("OriginalTitle").Exists())
        {
            Alter.Table("Series").AddColumn("OriginalTitle").AsString().Nullable();
        }

        if (!Schema.Table("Series").Column("CleanOriginalTitle").Exists())
        {
            Alter.Table("Series").AddColumn("CleanOriginalTitle").AsString().Nullable();
        }
    }
}
