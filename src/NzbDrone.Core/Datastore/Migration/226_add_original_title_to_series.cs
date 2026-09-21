using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(226)]
public class add_original_title_to_series : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("Series").AddColumn("OriginalTitle").AsString().Nullable();
        Alter.Table("Series").AddColumn("CleanOriginalTitle").AsString().Nullable();
    }
}
