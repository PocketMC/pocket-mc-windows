# Changelog

This file summarizes the PocketMC Desktop release line from `v1.0.0` to `v1.3.0`.

## v1.3.0 - Architectural Hardening & Observability

This release focuses entirely on massive under-the-hood structural improvements designed to make PocketMC safer, significantly more resilient to failures, and vastly easier to debug. Known internally as "Phase 1 & 2 of the Architecture Audit," this brings PocketMC from a prototype state into production-ready territory!

### 🛡️ Security & Integrity Engine (Phase 1)

- **Artifact Verification:** Implemented deep SHA1/SHA256 signature verification directly into the `DownloaderService`. Any Playit daemon or Paper/Vanilla jar you pull from external networks is now heavily hashed to detect silent corruption or man-in-the-middle tammpering.
- **Graceful Lifecycle System:** Hardened the exit behaviors! Instead of blindly closing and triggering unrecorded player kicks, exiting the app now yields a custom 15-second `IApplicationLifecycleService.GracefulShutdownAsync()` loop that saves worlds and closes network tunnels correctly before quitting. 
- **PII Scrubbing:** Heavily extended the `LogSanitizer`. PocketMC will now procedurally scrub personal metadata (like IPv4 strings and emails) from console captures using advanced RegEx pipelines before your crash logs ever touch an AI summary model.
- **RCON Client Engine:** StandardInput has been officially deprecated for interacting with Java child processes. PocketMC has fully migrated to a robust managed `RconClient` handling `try/catch` and direct socket control to eliminate standard I/O synchronization deadlocks on high server loads.

### 🔭 Diagnostic & Recovery Engine (Phase 2)

- **External Dependency Orchestrator:** Added a dynamic background thread loop (`DependencyHealthMonitor`) that constantly polls external microservices. Your settings page now features a **live dashboard** monitoring native latency status against **Adoptium**, **Playit.gg**, and **Modrinth**. You'll instantly know if a server failure is on your end or theirs. 
- **Disaster Recovery (Off-site Replications):** Significantly expanded the local automated snapshot tool. You can now configure an external sync directory (e.g., Google Drive/Dropbox sync folder) inside your Settings menu. Upon completing a local ZIP backup, PocketMC will autonomously replicate that payload identically to your secondary disk.
- **"One-Click" Support Bundles:** Implemented an asynchronous `DiagnosticReportingService`. With a single click inside Settings, PocketMC packages your system specs, Java variables, global app logs, masked properties, and native crash-reports into one dense support ZIP on your desktop—completely wiping all clear-text passwords (like `rcon.password`) out of the bundle before it drops!
- **UI Modernization Refactors:** Abstracted away huge layers of tech-debt by decoupling the `ResourceMonitorService` and abstracting logic into `IAssetProvider`, eliminating major background memory leaks. 

### 🔧 Internal Refactors

- Rebuilt architecture directory hierarchies shifting away from clustered `Providers` into a clean modular format (`Features/Instances`).
- Added graceful fallbacks to the new Update Engine banner checking systems.
- Handled UI context cleanup for settings panels and fixed missing null validation reference warnings.

## v1.2.5
- Add dependency health monitoring and external backup replication.
- Support bundle export to settings page.
- RCON client, download hash verification, and PII redaction.
- Extract graceful shutdown into IApplicationLifecycleService.
- Move ResourceMonitorService and add IAssetProvider abstraction.
- Initialize update check on startup, refresh settings button state, and add pack icon.

## v1.2.4
- Added Discord/community support in the app and README.
- Added release and packaging guidance for Velopack.
- Updated the release workflow notes to use a repository secret named `RELEASE_PAT`.

## v1.2.3
- Migrated installation and update packaging from Inno Setup to Velopack.
- Added Velopack startup bootstrapping before WPF application startup.
- Added automatic update checks in the shell layer.
- Updated GitHub Actions to publish `win-x64` output, pack Velopack releases, and upload release assets.
- Removed `installer.iss` and updated the build/install documentation.

## v1.0.0
- Initial stable PocketMC Desktop release.
- Core WPF desktop shell for managing Minecraft server instances, dashboard, console, settings, backups, Java setup, Playit.gg tunneling, and notifications.