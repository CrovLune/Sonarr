using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(208)]
    public class metadata_translations : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportLists").AddColumn("Language").AsInt32().Nullable().WithDefaultValue(1);
            Alter.Table("Series").AddColumn("Language").AsInt32().Nullable().WithDefaultValue(1);

            // Fork: the original-title migration (now 1001) added these columns first, so a
            // fork database already has them and an unguarded AddColumn fails at startup.
            if (!Schema.Table("Series").Column("OriginalTitle").Exists())
            {
                Alter.Table("Series").AddColumn("OriginalTitle").AsString().Nullable();
            }

            if (!Schema.Table("Series").Column("CleanOriginalTitle").Exists())
            {
                Alter.Table("Series").AddColumn("CleanOriginalTitle").AsString().Nullable();
            }

            Alter.Table("Series").AddColumn("SeasonType").AsString().Nullable().WithDefaultValue("official");
            Alter.Table("Series").AddColumn("SeasonTypes").AsString().Nullable().WithDefaultValue("[]");

            if (!Schema.Table("Series").Index("IX_Series_CleanOriginalTitle").Exists())
            {
                Create.Index().OnTable("Series").OnColumn("CleanOriginalTitle");
            }

            Create.TableForModel("SeriesTranslations")
                .WithColumn("SeriesId").AsInt32()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("CleanTitle").AsString().Nullable()
                .WithColumn("Overview").AsString().Nullable()
                .WithColumn("Language").AsInt32();

            Create.Index().OnTable("SeriesTranslations").OnColumn("SeriesId");

            Update.Table("Series")
                .Set(new { LastInfoSync = "2026-01-01 00:00:00" })
                .AllRows();
        }
    }
}
