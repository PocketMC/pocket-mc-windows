# PocketMC Release Readiness Checklist

Use this checklist before publishing a production release. Do not treat a green build as the whole release process. Computers lie less than humans, but they still miss things.

## Version and changelog

- [ ] `VERSION` in `.github/workflows/production-build.yml` matches the intended release version.
- [ ] `PocketMC.Desktop/PocketMC.Desktop.csproj` version fields match the intended release version.
- [ ] `CHANGELOG.md` contains a `## vX.Y.Z` section for the release.
- [ ] Release notes describe user-visible changes, migrations, bug fixes, and known issues.

## Build and test gates

- [ ] `dotnet restore` succeeds.
- [ ] `dotnet build PocketMC.Desktop.sln -c Debug` succeeds.
- [ ] `dotnet build PocketMC.Desktop.sln -c Release` succeeds.
- [ ] `dotnet test PocketMC.Desktop.sln --no-build` succeeds.
- [ ] `dotnet list PocketMC.Desktop.sln package --vulnerable --include-transitive` has no unresolved critical/high vulnerabilities.
- [ ] Warnings are reviewed. New nullable warnings require a fix or explicit justification.

## Installer and package checks

- [ ] Portable artifact is produced.
- [ ] Velopack package is produced when the version is new.
- [ ] Setup executable exists.
- [ ] `RELEASES`, `releases.win.json`, and `assets.win.json` exist.
- [ ] Clean install tested on Windows 10 1809+ or equivalent VM.
- [ ] Clean install tested on Windows 11.
- [ ] Update from previous release tested.
- [ ] Portable build launches.

## Data safety checks

- [ ] Existing instances load correctly.
- [ ] Existing `settings.json` loads correctly.
- [ ] Existing `.pocket-mc.json` metadata loads correctly.
- [ ] Backup creation tested for Java.
- [ ] Backup restore tested for Java.
- [ ] Backup creation tested for Bedrock when applicable.
- [ ] Backup restore tested for Bedrock when applicable.
- [ ] Restore rollback path tested after simulated failure.
- [ ] `world/session.lock` is not deleted automatically.

## Runtime and networking checks

- [ ] Java runtime detection works.
- [ ] Managed Java download works.
- [ ] Bedrock/PocketMine runtime flows work where applicable.
- [ ] Playit agent download path works.
- [ ] Playit agent validation works.
- [ ] Tunnel creation/listing works.
- [ ] Tunnel limit and missing-address states are displayed clearly.

## Marketplace checks

- [ ] Modrinth search/install works.
- [ ] CurseForge search/install works with configured API key.
- [ ] Poggit search/install works for PocketMine.
- [ ] Missing provider/network failure shows a clear UI error.
- [ ] Add-on manifest updates after install/update.

## Privacy and support

- [ ] Crash report path is known and documented.
- [ ] Logs do not contain API keys, OAuth tokens, or Playit secrets.
- [ ] AI summary privacy note is accurate.
- [ ] Cloud backup privacy note is accurate.
- [ ] Support bundle/export docs are up to date if implemented.

## Final decision

- [ ] Release approved.
- [ ] Known risks documented.
- [ ] Rollback plan exists.
