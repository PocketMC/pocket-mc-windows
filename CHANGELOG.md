# Changelog

This file summarizes the Pocket MC Desktop release line from `v1.0.0` to `v1.6.2`.

## v1.6.2 - LAN Connectivity & Resilient Tunneling

This release introduces LAN IP visibility to the dashboard for easier local play and overhauls the Playit.gg tunnel creation logic to be more resilient and informative.

### 🏠 LAN Connectivity
* **Dashboard LAN IP Display**: You can now view your server's Local Area Network (LAN) IP and port directly on the instance cards. This makes it easier to connect from other devices on the same network without needing a public tunnel.
* **One-Click Copy**: Added a dedicated copy button for LAN addresses, mirroring the functionality for public tunnel addresses.
* **Port Persistence**: Improved internal metadata tracking to ensure server ports are accurately cached and displayed even when the server is offline.

### 🌐 Resilient Playit.gg Integration
* **API-Driven Limit Handling**: Moved away from hardcoded tunnel limits. PocketMC now communicates directly with the Playit API to determine if you've reached your tunnel quota, providing real-time feedback.
* **Actionable Error Messaging**: If a tunnel cannot be created due to account limits, the application now displays a clear, actionable message with a direct link to upgrade or manage your tunnels, rather than a generic failure.
* **Persistent Creation Dialog**: The manual "Create Tunnel" dialog now remains open if a creation attempt fails, allowing you to fix configuration issues or view error details without losing your progress.
* **Non-Blocking Auto-Provisioning**: Failures during automatic tunnel creation (at server startup) are now handled as non-blocking notifications. Your server will still start, and you'll be notified of the connectivity issue to resolve it later.

### 🛠️ Internal Improvements
* **Metadata Reliability**: Refactored `InstanceMetadata` to better track primary networking ports across different server engines.
* **Standardized Failure Paths**: Unified error handling between manual tunnel creation and automatic background provisioning for a more consistent user experience.

## v1.6.1 - Cross-Play & UI Tweaks

This is a minor patch release focusing on fixing cross-play port configuration and polishing the dashboard UI for Bedrock servers.

### ✨ Enhancements & Fixes
* **Geyser Config Port Patching**: Fixed an issue where the Geyser config port was not being properly patched on startup for non-Spigot loaders (Fabric, Forge, NeoForge). The patching engine now correctly searches the `config/` directory.
* **Bedrock IP Copy Behavior**: Fixed a bug where clicking the "Copy" button next to a cross-play server's Bedrock Hostname incorrectly copied the numeric IP address instead of the hostname.
* **Dedicated Bedrock UI Icons**: Updated the Dashboard UI to display the correct native Bedrock icon (Cube) instead of the default Desktop icon for dedicated Bedrock instances, ensuring visual consistency across all Bedrock connection types.

## v1.6.0 - Dynamic Theming & Advanced Tunnel Management

### 🎨 Dynamic Theming & UI Enhancements
* **User-Selectable Themes**: Added a new application theme toggle in App Settings allowing you to freely switch between Light, Dark, and System Default themes without needing to restart the application.
* **Intelligent Color Adaptability**: Overhauled dashboard and tunnel interfaces to use dynamic resources, ensuring high readability and vibrant aesthetics regardless of the active theme.

### 🌐 Advanced Playit.gg Tunnel Management
* **Automated Provisioning**: Eliminated the manual tunnel guide. Tunnels are now seamlessly and automatically provisioned in the background the moment you start your server.
* **Full Tunnel Controls**: The Tunnel Page has been upgraded with a comprehensive management suite. You can now rename tunnels, change local ports, toggle them on/off, and delete them directly from the PocketMC UI.
* **Explicit Editing States**: Refactored the tunnel renaming and port editing fields to require explicit "Save" actions, preventing accidental misconfigurations.
* **Connection Transparency**: Added dynamic skeleton loading indicators and clearer error states to the Dashboard's instance cards, providing immediate visual feedback while tunnel addresses are being resolved or if they hit rate limits.

## v1.5.4 - Playit.gg Connection Gate & Settings Refactor

### 🚀 Playit.gg Connectivity Gate
* **Pre-Start Warning Dialog**: Added a non-blocking `PreStartAgentWarningWindow` that intercepts server startups if the Playit.gg agent is disconnected or pending setup, guiding users to resolve the issue before launching.
* **Centralized State Management**: Integrated `AgentProvisioningService` to act as the single source of truth for tunnel states globally across the application.
* **Smart State Handling**: Accurately categorizes pending states like `AwaitingSetupCode` to trigger the warning gate, providing a smoother connection setup flow.

### 🛠️ UI & Settings Refactor
* **Removed Legacy Software Updates**: Removed the redundant "Software Updates" expander from the Server Settings page, cleaning up the UI and relying on the application's core update mechanisms.

### 🧪 Test Coverage Improvements
* **Network Reliability**: Introduced `PortReliabilityTestWorkspace` helper class to facilitate robust network-related component testing.
* **JVM & File Handling Tests**: Added comprehensive unit tests for `ServerLaunchConfigurator` to verify version-specific JVM arguments and proper file management during server startup.

## v1.5.3 - Java Management, Networking & Setup Fixes

### 🔒 Playit.gg Security & UX Hardening
* **New Guided Setup Wizard**: Replaced the manual setup flow with a 4-step guided wizard that simplifies agent linking and removes the need for users to manually manage secret keys.
* **UI Cleanup**: Removed legacy fields for "Secret Key" and "Provisioning Backend" to provide a cleaner, more secure dashboard experience.

### ☕ Java Runtime Lifecycle Management
* **User Intent Persistence**: Added a "User Removed" flag to the app configuration. Manually deleted runtimes will no longer be automatically re-downloaded at startup, respecting your decision to save disk space.
* **Active Instance Protection**: The system now blocks the deletion of any Java runtime that is currently being used by a running server instance to prevent unexpected crashes.
* **Individual Restore Controls**: Added a "Download" button to each missing runtime row. You can now restore specific Java versions individually without being forced to download all missing bundled runtimes.
* **Atomic Deletion Logic**: Implemented safe-guarding logic that ensures your intent flag is only saved if the deletion is successful, with automatic rollback on file-system errors.

### 🌐 Networking & Port Resolution
* **Dynamic Port Reservation**: Fixed a bug where offline servers would permanently block ports. Ports are now dynamically released when an instance is stopped, allowing other servers to use them.
* **Improved Error Messaging**: Updated port conflict dialogs to be more actionable. The app now specifically identifies when an external process is holding a port and provides clearer guidance on how to resolve the conflict.

### 🛠️ Internal Improvements
* **Settings Architecture**: Expanded `AppSettings` to support complex state tracking for managed runtimes.
* **Provisioning Flow**: Refined `JavaProvisioningService` to distinguish between background maintenance and manual user-triggered repairs.

## v1.5.2 - Intelligence Safeguards & Under-the-Hood Fixes

This release introduces safety guardrails for the AI summarization feature to protect against unexpected token costs, fixes native Bedrock port conflicts, and removes console command suggestions for a cleaner, faster experience.

### ✨ Enhancements & Fixes

- **AI Token Safeguards:** Added a size threshold check for server session logs. Generating an AI summary for extremely large sessions (>1.5MB of logs) will now prompt a warning dialog to prevent accidental token over-consumption.
- **Enhanced AI Prompt:** The backend summarization prompt has been fully overhauled to instruct the model to ignore Personally Identifiable Information (PII) like IPs and emails, and to focus more heavily on performance metrics and configuration issues.
- **Bedrock Port Conflict Fix:** IPv6 ports (`server-portv6`) are now automatically bound and assigned based on the selected IPv4 port (`server-port + 1`) when editing Server Settings. This resolves the `ipv6 port conflict` crashes entirely when running multiple native instances on the same host.
- **Console Streamlining**: Slashed out the bulky command suggestion overlay from the server console. This provides a cleaner UI, stops accidental command misfires, and prevents the application from constantly parsing massive log text streams for new command syntaxes.

---

## v1.5.1 - Setup UX Fixes & Active Server Indicators

This is a minor patch release focusing on improving the first-time user experience and minor dashboard UI tweaks.

### ✨ Enhancements & Fixes

- **Setup Directory Auto-Creation:** Fixed an issue during the first-time setup where selecting the default directory (`Documents/PocketMC`) would fail if it did not already exist. The application now intelligently pre-creates this directory and enables the "Continue" button by default.
- **Active Instance Indicators:** Added a dynamic emerald-green highlight border around running server cards on the Dashboard, allowing you to easily spot online servers.

---

## v1.5.0 - Modloader Expansion, Marketplace Intelligence & Instance Management

This release adds NeoForge as a supported modloader, introduces engine-aware addon management with full dependency resolution in the marketplace, and ships a suite of instance management improvements including renaming, customization, and minimum version filtering. Loader-aware filtering for Modrinth and CurseForge is now fully enforced, and several false-warning and fallback bugs are resolved.

### 🧩 Modloader Expansion

- **NeoForge Support:** NeoForge is now a fully supported modloader option in the instance creation workflow, joining Fabric and Forge.
- **Loader-Aware Filtering:** Modrinth and CurseForge search results are now filtered by the active loader (Fabric / Forge / NeoForge) to prevent incompatible addon installs (#17).
- **Modrinth Fallback Hardening:** The Modrinth relaxed query no longer falls back to an arbitrary loader. When a version is not found, a clear message is returned instead of a misleading result (#18).

### 🛒 Marketplace Intelligence

- **EngineCompatibility Model:** Introduced a new `EngineCompatibility` model so addons in the marketplace are resolved and displayed based on the active server engine.
- **Dependency Resolution:** The marketplace now automatically resolves addon dependencies and manages addon manifests, reducing manual setup for complex modpacks.
- **False Warning Reduction:** Eliminated spurious incompatible-loader warnings that appeared during addon install even when the loader was valid.

### 📁 Instance Management

- **Instance Renaming:** Instances can now be renamed directly from the UI, with collision detection and case-safe directory moves to prevent filesystem conflicts.
- **Name & Description Customization:** Added support for editing instance names and descriptions from within the instance settings panel.
- **Minimum Version Filtering:** Server providers now respect minimum version constraints during instance creation, with testability improvements to the underlying filtering logic.

### 📦 Infrastructure & Documentation

- **Project Rename:** The project name has been corrected from `PocketMC` to `Pocket MC` across the application and repository.
- **CI Improvements:** Added portable test release publishing with artifact upload to the CI pipeline. Production build workflow now includes version checks and improved release logic.
- **Documentation:** Added detailed documentation for Pocket MC Desktop. README structure and content revised to reflect current features.
- **Funding:** Added a Buy Me a Coffee link to the repository for community support.

### 🗑️ Removed

- **Multi-Monitor Window Persistence:** Removed the multi-monitor window persistence task, which caused layout issues across display configurations.
- **`.devin` Directory:** Removed the `.devin` directory from the repository.

---

## v1.4.3 - Port Engine, Cross-Play, & UI Enhancements

This release introduces a new port reliability engine to improve server startup stability, adds configurable Geyser Bedrock port support, modernizes the dashboard UI, and streamlines release workflows.

### 🔌 Port Reliability Engine

- **Robust Port Resolution:** Added a new port reliability engine and comprehensive tests to prevent port binding conflicts and improve server startup resilience.
- **Conflict Logic Patch:** Updated preflight checks to only block startup if another instance is actively running, ignoring duplicate config-level ports.

### 🎮 Cross-Play & Networking

- **Configurable Geyser Port:** Added support for per-instance Geyser Bedrock UDP ports, allowing concurrent cross-play servers.
- **Dual-IP Display:** Integrated numeric IP addresses alongside hostnames for all tunnel types, with separate copy buttons for precision.
- **Tunnel Auto-Patching:** Geyser configuration files are now automatically patched with the correct Bedrock UDP port on startup.
- **Fabric API Automation:** Added automatic `fabric-api` downloading during Geyser setup for Fabric servers to resolve mod dependency crashes.

### 💎 Dashboard & UI Polish

- **Modern Grid Layout:** Replaced the legacy list view with a clean, responsive layout featuring fixed headers and scrollable instance areas.
- **Enhanced IP Blocks:** Redesigned connection sections to cleanly display both hostnames and numeric IPs, with state-aware visibility (IPs hide when server is stopped).
- **Compact Actions:** Reorganized dashboard buttons into a balanced 2x2 grid for improved accessibility and a cleaner look.
- **Scroll Precision:** Fixed console scrolling behavior, disabling unreliable "smooth" scroll for standard high-performance WPF scrolling.

### 🐛 Bug Fixes & Stability

- **Log Handle Leaks:** Fixed a critical issue where session log handles remained open after server exit, preventing instance deletion and causing filesystem locking errors.
- **Clipboard Resilience:** Implemented robust clipboard handling with retries to prevent application crashes during IP copy actions.
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
- **Dashboard Grid Layout:** Modernized the instance card layout with improved metrics scannability and connection clarity.

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
