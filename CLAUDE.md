# CLAUDE.md

Notes for working in this fork of Sonarr. Upstream conventions still apply; this
file only covers what differs or has bitten us.

## This is a fork

`origin` is `CrovLune/Sonarr`, `upstream` is `Sonarr/Sonarr`. Work lands on
`v5-develop` via PRs against **origin**. `gh` defaults to the upstream repo on
forks, so always pass `--repo CrovLune/Sonarr --base v5-develop`.

Divergences from upstream:

- Trakt (import lists + notifications) and Telegram notifications are removed.
  Their migrations are retained — migrations must never be deleted.
- TMDB is used as a supplementary metadata source for original titles.
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
3. **Dangling references** to removed provider types in `src/`.

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

## Configuration

Fork settings read the database first and fall back to an environment variable,
so they can be set in `docker-compose.yml` without a UI field:

| Setting | Environment variable |
| --- | --- |
| `TmdbApiKey` | `SONARR__TMDB_API_KEY` |
| `OriginalTitleLanguages` | `SONARR__ORIGINAL_TITLE_LANGUAGES` |

**`TmdbApiKey` is returned masked** (`"***" + last4`) by the config API. The base
`ConfigController.SaveConfig` reflects over every resource property and persists
it, and the Media Management settings page round-trips the whole resource even
though it renders no TMDB field — so saving any unrelated setting used to write
`***abcd` over the key and shadow the env var. Both the getter and setter now
reject the mask (`ConfigService.TmdbApiKeyMask`). Apply the same guard to any new
masked secret.

`OriginalTitleLanguages` accepts language names or ISO codes (`Russian`, `ru`,
`rus`). For a listed language the metadata and original titles are **swapped**,
not overwritten: the native title becomes `Series.Title` and the English title is
kept as `OriginalTitle`. That matters because `SeriesRepository.FindByTitle` falls
back to `CleanOriginalTitle` and `ReleaseSearchService` adds `OriginalTitle` to
scene titles, so existing English filenames still match and searches go out under
both names.

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
