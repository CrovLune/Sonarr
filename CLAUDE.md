# CLAUDE.md

Notes for working in this fork of Sonarr. Upstream conventions still apply; this
file only covers what differs or has bitten us.

## This is a fork

`origin` is `CrovLune/Sonarr`, `upstream` is `Sonarr/Sonarr`. Work lands on
`v5-develop` via PRs against **origin**. `gh` defaults to the upstream repo on
forks, so always pass `--repo CrovLune/Sonarr --base v5-develop`.

Sync from **`upstream/develop`**. Upstream's `v5-develop` was deleted, so a local
`upstream/v5-develop` ref is stale and lists nothing new. Merge rather than rebase:
`v5-develop` is published and the deployed images are built from it.

Divergences from upstream:

- Trakt (import lists + notifications) and Telegram notifications are removed.
  Their migrations are retained — migrations must never be deleted.
- `OriginalTitle` keeps the English title when a series' metadata language is its
  original language (`OriginalTitleSelection`), so English and native names both
  match releases. `ReleaseSearchService` also searches under `OriginalTitle`.
- Nothing is grabbed automatically from a season until every episode in it has
  aired (`SeasonFullyAiredSpecification`, all series types and specials).
  Interactive searches and a user-invoked single-episode search skip the rule.
  Because RSS never revisits old posts, `SeasonFinaleSearchService` runs hourly
  and queues a season search for each season whose finale aired since its last run.
- Docker packaging lives in the repo (`Dockerfile`, `docker-compose.yml`,
  `build-and-push.sh`).

## Migrations: fork migrations live at 1000+

Fork migrations use version **1000 and above**. Upstream uses the low numbers,
and a collision is fatal at startup:

```
FluentMigrator.Exceptions.DuplicateMigrationException: Duplicate migration version 226.
SonarrStartupException: Sonarr failed to start: Error creating main database
```

This took production down once: the fork's original-title migration shipped as
226, then an upstream sync added its own 226. FluentMigrator applies any
unapplied migration regardless of how its version orders against the highest
applied one, so a high fork range costs nothing.

Fork migrations should guard their `AddColumn` calls:

```csharp
if (!Schema.Table("Series").Column("OriginalTitle").Exists())
```

Databases migrated before the renumber recorded 226 against the *fork's*
migration, so upstream's 226 is treated as applied and will never run.
`1002_repair_air_date_filtering` exists to add those columns after the fact.
Any future renumber needs the same treatment.

The reverse also happens: an upstream migration can add a column the fork added
first. Upstream's `208_metadata_translations` adds `Series.OriginalTitle` and
`CleanOriginalTitle`, which production already had from `1001`, so 208 carries the
same guards. Without them it fails at startup with a duplicate column.

## After merging upstream, check these

1. **Duplicate migration versions** — this is the one that causes outages:
   ```bash
   ls -1 src/NzbDrone.Core/Datastore/Migration/ | grep -oE "^[0-9]+" | sort -n | uniq -d
   ```
2. **Orphaned test fixtures** for removed providers. Deleting a provider but
   leaving its fixture stops the whole `Core.Test` project compiling, which
   silently disables every test in it:
   ```bash
   grep -rln --include='*.cs' -E "Notifications\.Telegram|Notifications\.Trakt|ImportLists\.Trakt" src/*.Test*/
   ```
3. **Dangling references** to removed provider types in `src/`. Upstream keeps
   adding Trakt providers (e.g. the Trakt smart list), so delete any new ones.
4. **New upstream migrations that add columns the fork already has** (see 208
   above). Then test the migration against a copy of the production database.

## Testing

The repo targets .NET 10. If the host lacks that SDK, run the suite in a
container rather than skipping it:

```bash
docker run --rm -v "$PWD":/src -w /src \
  -e DOTNET_CLI_HOME=/tmp -e HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  --user "$(id -u):$(id -g)" mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj \
    -p:TreatWarningsAsErrors=false -p:NuGetAudit=false
```

`build-and-push.sh` runs this before pushing; `SKIP_TESTS=1` overrides it.

`SkyHookProxy` is only reachable through integration tests that call the live
skyhook API, so logic worth testing belongs in a pure helper beside it (see
`OriginalTitleSelection`).

## Metadata language

Native-language titles, episode titles and overviews come from upstream's
per-series metadata language (`Series.Language`) and the "Preferred metadata
language" setting. Skyhook v2 serves them directly. The fork's earlier TMDB
pipeline (`TmdbApiKey`, `OriginalTitleLanguages`) was removed in favour of it.

If a setting is ever returned masked by the config API, reject the mask in its
setter too. `ConfigController.SaveConfig` persists every resource property, and the
settings pages send the whole resource back, so a masked value would otherwise
overwrite the real one.

## Deploying

`./build-and-push.sh` builds `linux/amd64`, pushes to
`ghcr.io/crovlune/sonarr:{latest,<version>}`, then on the host:

```bash
cd ~/containers/sonarr && docker compose pull && docker compose up -d
```

The host's `docker-compose.yml` is **not** the one in this repo; it has real
volumes and its own environment. Back it up before editing.

Verify a migration against a copy of the production database before deploying
schema changes — it catches what compiling does not:

```bash
docker cp sonarr:/config/sonarr.db /tmp/migtest/sonarr.db
docker run --rm --network none --user 1000:1000 -v /tmp/migtest:/config <image>
```
