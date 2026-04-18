# Changelog

This file summarizes the PocketMC Desktop release line from `v1.0.0` to `v1.4.3`.

## v1.4.3 - Port Engine, Cross-Play, & UI Enhancements

This release introduces a new port reliability engine to improve server startup stability, adds configurable Geyser Bedrock port support, modernizes the dashboard UI, and streamlines release workflows.

### 🔌 Port Reliability Engine

- **Robust Port Resolution:** Added a new port reliability engine and comprehensive tests to prevent port binding conflicts and improve server startup resilience.
- **Conflict Logic Patch:** Updated preflight checks to only block startup if another instance is actually running, ignoring duplicate config-level ports.

### 🎮 Cross-Play & Networking

- **Configurable Geyser Port:** Added support for per-instance Geyser Bedrock UDP ports, allowing concurrent cross-play servers.
- **Dual-IP Display:** Integrated numeric IP addresses alongside hostnames for all tunnel types, with separate copy buttons for precision.
- **Tunnel Auto-Patching:** Geyser configuration files are now automatically patched with the correct Bedrock UDP port on startup.

### 💎 Dashboard & UI Polish

- **Modern Grid Layout:** Replaced the legacy list view with a clean, responsive `UniformGrid` layout.
- **Enhanced IP Blocks:** Redesigned connection sections to cleanly display both hostnames and numeric IPs.

### 🐛 Bug Fixes & Stability

- **Log Handle Leaks:** Fixed an issue where `_sessionLogWriter` would leak file handles if server configuration or startup failed. It is now properly disposed and nulled during cleanup.
- **Build Locking:** Fixed file access errors during publishing by ensuring clean process termination.
- **Crash Resilience:** Fixed UI-related rendering crashes caused by invalid XAML symbols.

### 📦 Infrastructure & Documentation

- **Automated Deployments:** Enhanced CI workflows with package cleanup and integrated GitHub Release creation.
- **Documentation:** Updated README and screenshots to reflect current UI and networking features.

## v1.4.2 - UI Modernization & Observatory Hardening

This release focuses on bringing a premium, high-impact visual experience to PocketMC while significantly enhancing the diagnostic tools and cross-play stability.

### ✨ Modernized Observatory

- **Emerald-Themed Intelligence:** Rebuilt the AI Summary panel with better markdown styling system.
- **Rich Markdown Rendering:** Integrated `Markdig.Wpf` to render structured AI session summaries with support for bold, italics, and formatted lists.

### 📟 Advanced Console Features

- **Smart Log Filtering:** Added high-performance UI toggles to filter logs by severity (Chat, Info, Warn, Error, System).
- **Regex Search Engine:** Integrated a powerful Regular Expression search bar for the console, allowing advanced users to isolate specific server events with surgical precision.
- **Command Intelligence:** Implemented command history navigation (Up/Down keys) and intelligent auto-suggestions for Minecraft commands based on real-time server output.

### 🌐 Cross-Play Reliability

- **Modrinth API Migration:** Overhauled the Geyser and Floodgate provisioning pipeline. PocketMC now fetches builds directly from Modrinth, resolving critical download failures for Fabric servers.
- **Failure Resilience:** Improved error handling during instance creation, ensuring that partial plugin downloads are cleaned up and retried automatically.

### 📦 Infrastructure

- **CI/CD Workflow Hardening:** Refactored the GitHub Actions production pipeline to ensure consistent versioning and more reliable Velopack release distribution.
- **Versioning Single-Source:** Synchronized project versions across `.csproj`, `CHANGELOG.md`, and CI variables.

## v1.4.0 - Bedrock & PocketMine Protocol Expansion

This milestone transforms PocketMC into a multi-protocol powerhouse, adding first-class support for native Bedrock Edition (BDS) and PocketMine-MP engines alongside Java!

### 🟢 Bedrock Dedicated Server (BDS) Support

- **Full Version Discovery:** Integrated the kittizz community manifest, enabling one-click installation for 45+ versions of Bedrock (including stable and preview releases).
- **Bedrock Add-on Management:** Native support for `.mcpack` and `.mcaddon` files. Importing an addon automatically handles file extraction and updates `world_behavior_packs.json` / `world_resource_packs.json` for you.
- **Fixed Provisioning Failures:** Rebuilt the BDS download pipeline to use system temp directories, resolving "Access Denied" errors during instance creation.
- **UWP Loopback Automation:** Added a hardware-level "Fix Bedrock LAN" tool in settings that automates `CheckNetIsolation.exe` loopback exemptions, allowing you to connect to local servers from Minecraft for Windows.

### 🔵 PocketMine-MP Support

- **PHP Runtime Orchestrator:** PocketMC now automatically provisions and manages sandboxed PHP 8.x runtimes for PocketMine instances.
- **Poggit Marketplace:** Integrated Poggit browsing for PocketMine plugins. The "Plugin Marketplace" button now intelligently switches sources based on your server engine.
- **Auto-Generator Patching:** Implemented a world-generator sanity check that automatically patches `server.properties` (e.g., `minecraft:normal` → `DEFAULT`) to prevent common "Unknown generator" startup crashes.

### ✨ Dashboard & UI Polish

- **Engine-Aware Settings:** The Addons tab now dynamically filters content. Java-only sections (like Modrinth/Forge) are hidden when managing Bedrock or PocketMine instances.
- **IP Duplicate Suppression:** The dashboard card now intelligently hides the secondary "Bedrock IP" row for native Bedrock servers to reduce clutter.
- **Config Core Keys:** Expanded the core property list to include Bedrock-specific networking keys (`server-portv6`, `allow-cheats`, etc.) for easier configuration.

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
