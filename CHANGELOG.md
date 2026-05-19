# Changelog

This file summarizes the Pocket MC Desktop release line from `v1.0.0` to `v1.7.3`.

## v1.7.3 - Tunnel Display Fix (#40)

This patch fixes a regression where Playit.gg tunnel IPs were not displaying on server cards despite tunnels being active.

### 🐛 Bug Fixes
* **Tunnel IP Display**: Fixed tunnel addresses now staying visible while resolving, preventing blank cards during resolution.
* **API Response Parsing**: Added support for both Playit API response shapes to ensure tunnel data is correctly parsed regardless of API version.
* **Pending States**: Server cards now show proper pending/error states when tunnels exist but addresses aren't yet available, instead of appearing blank.
* **Multi-Tunnel Support**: Improved resolution for Java, Bedrock/Geyser, and Simple Voice Chat tunnels with expanded test coverage.

## v1.7.2 - Simple Voice Chat Tunneling & Networking Refinements

This release adds first-class support for Simple Voice Chat with automatic detection, port configuration management, and seamless one-click tunnel provisioning. The networking and tunnel architecture receives significant improvements for enhanced stability and better diagnostic visibility.

### 🎤 Simple Voice Chat Support
* **Automatic Detection**: PocketMC now automatically detects Simple Voice Chat installations across Java, Fabric, Forge, and other modloaders. Detection works via config files, mod/plugin JARs, and console logs for comprehensive coverage.
* **One-Click Tunnel Provisioning**: Once detected, Simple Voice Chat ports are automatically included in the tunnel provisioning workflow. Create tunnels for voice communication with a single click, just like standard server tunnels.
* **Real-Time Port Configuration**: The application monitors Simple Voice Chat configuration files and dynamically tracks port, bind address, and voice host settings. Changes are detected and applied without requiring an app restart.
* **Status Monitoring Dashboard**: The dashboard now displays Simple Voice Chat tunnel status alongside primary server connections, with indicators for tunnel health and public address availability.
* **First-Run Setup Prompt**: When Simple Voice Chat is detected on a running server without an active tunnel, a non-blocking prompt guides users through one-click tunnel creation to enable voice chat connectivity.
* **Configuration Path Tracking**: Metadata now persists Simple Voice Chat configuration paths, enabling consistent state tracking across sessions and seamless tunnel management.

### 🌐 Tunnel Architecture Refinements
* **Enhanced API Communication**: Rebuilt the PlayitApiClient with improved request/response handling and better error recovery. Token refresh logic is now more robust and includes fallback mechanisms for edge cases.
* **Tunneling Orchestration Overhaul**: Completely refactored `InstanceTunnelOrchestrator` to elegantly handle multiple simultaneous tunnel types (primary server + Simple Voice Chat). The orchestration engine now cleanly separates tunnel lifecycle management, state tracking, and provisioning workflows.
* **Improved Port Binding Logic**: Enhanced port preflight and diagnostics services with refined binding role detection. The system now more accurately distinguishes between primary server ports, Geyser ports, and auxiliary ports like Simple Voice Chat.
* **Port Role Classification**: Introduced structured port binding role classification to provide granular visibility into why a port is in use and what component owns it (server, tunnel, voice chat, etc.).

### 🧪 Test Coverage Expansion
* **Simple Voice Chat Test Suite**: Added comprehensive unit tests covering detection logic, configuration parsing, first-run prompts, and lifecycle management. Tests validate behavior across Java, Fabric, and Forge scenarios.
* **Tunnel Service Tests**: Expanded tunnel service test coverage with 400+ new assertions. Tests now validate multi-tunnel scenarios, error recovery, and API edge cases.
* **Port Diagnostics Tests**: Enhanced port diagnostics and preflight service tests to cover new binding role scenarios and improved conflict detection.

### 🛠️ Internal Improvements
* **Application State Management**: Expanded `ApplicationState` to track Simple Voice Chat configuration and tunnel provisioning state throughout the application lifecycle.
* **Dialog Service Refactoring**: Improved `WpfDialogService` and dialog infrastructure for better handling of non-blocking prompts and improved UI responsiveness during tunnel operations.
* **Instance Metadata Expansion**: Extended `InstanceMetadata` with Simple Voice Chat–specific fields for comprehensive state persistence (port, tunnel ID, tunnel address, config path, warning dismissal state, status).

### 🐛 Bug Fixes & Stability
* **Tunnel State Synchronization**: Fixed edge cases where tunnel state could become desynchronized between the UI and backend service, especially during rapid server start/stop cycles.
* **Port Conflict Resolution**: Improved port conflict detection to handle auxiliary ports (like Simple Voice Chat) without incorrectly blocking server startup.

## v1.7.1 - Backup Intelligence, Health Monitoring & UI Polish

This release transforms the backup system from a simple archive list into a fully versioned, metadata-rich history with proactive health monitoring. It also fixes a server icon caching bug and adds a feedback section to the About page.

### 📦 Backup Versioning UI
* **Sequential Version Badges**: Every backup now receives a versioned identifier (v1, v2, v3…) displayed as a prominent blue badge, making it easy to track backup progression over time.
* **Trigger Identification**: Each backup card clearly indicates whether it was created manually or by the automated scheduler via a labeled badge.
* **Size Delta Tracking**: The size difference from the previous backup is displayed inline (e.g. "+2.3 MB"), giving immediate visibility into world growth between snapshots.

### 📋 Backup Metadata & History Viewer
* **Rich Backup Cards**: Each backup now displays its creation timestamp, file size, server type, and Minecraft version — all captured at the moment of backup creation.
* **Editable Labels**: Users can annotate any backup with a custom label (e.g. "Before Wither fight" or "Pre-update snapshot") that persists across sessions via the backup manifest.
* **SHA-256 Integrity Verification**: A dedicated shield button per backup runs a full SHA-256 checksum validation against the original hash recorded at creation time, immediately alerting if a backup file has become corrupted.
* **Persistent Manifest**: All metadata is stored in a `backup-manifest.json` file alongside backup archives, surviving app restarts and providing a complete audit trail.

### 🛡️ Backup Health Warnings
* **Disk Space Monitoring**: The backup tab now monitors available drive space in real-time — amber warning at <2 GB remaining, critical red alert at <1 GB.
* **Failed Backup Alerts**: If a scheduled or manual backup fails, the failure reason and timestamp are recorded and surfaced as a prominent banner on the next visit.
* **Overdue Schedule Detection**: When a scheduled backup misses its window by more than 1 hour, an overdue warning is displayed with the exact delay.
* **Backup Stats Summary Bar**: A new metrics strip at the top of the Backups tab shows total backup size, time since last backup, and remaining disk space at a glance.

### 🐛 Bug Fixes
* **Server Icon Persistence**: Fixed a WPF image caching issue where the custom server icon reverted to the default Pocket MC logo after reloading the Server Settings page, despite the file being correctly saved to disk. Added `BitmapCreateOptions.IgnoreImageCache` to both `SettingsGeneralVM` and `ImageCropPage`.

### 🎨 About Page Improvements
* **Feedback & Bug Reports Section**: Added a new expandable card linking to the Google Forms feedback/bug report form, accessible via a single-click button.
* **Native GitHub Button**: Replaced the inline hyperlink for the GitHub repository with a proper native Fluent UI button for visual consistency.
* **Full-Width Card Layout**: Removed the 600px max-width constraint from About page cards so they stretch to match the full application window, consistent with all other pages.

## v1.7.0 - Robust Cloud Backups, Secure OAuth Proxy & One-Click Restore

This major release introduces native cloud backups and one-click cloud restorations for **Google Drive**, **Dropbox**, and **OneDrive**, backed by a secure hybrid proxy architecture that shields API secrets from the open-source client.

### ☁️ Cloud Backups & One-Click Restore
* **One-Click Cloud Restoration**: Restoring server worlds from the cloud is now fully integrated. Click the new "Restore" button next to any remote backup to download, extract, and automatically apply files directly to your server.
* **Write-Safety Guards**: To prevent write conflicts and world data corruption, the cloud restore action is strictly gated. The UI automatically disables the Restore button while the Minecraft server is actively running.
* **Direct Provider Integration**: Fully integrated compiled API clients (`Dropbox.Api`, `Google.Apis.Drive.v3`, and `Microsoft.Identity.Client` via MSAL.NET) for direct, robust communication with cloud servers without local file-sync utilities.
* **DPAPI Token Protection**: All user access and refresh tokens are securely encrypted using Windows Data Protection API (DPAPI) at rest and locked to your local Windows user profile.

### 🔒 Secure Hybrid OAuth Architecture
* **Google OAuth Helper Proxy**: Built and deployed a stateless ASP.NET Core helper proxy backend to handle Google Drive OAuth token exchanges and refresh requests. This securely shields the Google client secret from the open-source repository while offering 100% transparency.
* **PKCE Public Client Flow**: Integrated native PKCE (Proof Key for Code Exchange) auth flows for Dropbox and OneDrive, communicating directly from the desktop application to the providers with zero middle-man involvement.
* **Verbose Diagnostic Handlers**: Rebuilt the cloud provider exception handlers to catch and report full HTTP response bodies from OAuth servers, simplifying troubleshooting for developer setup validation (e.g., Google's testing-state consent constraints).

### 📖 Enhanced Project Documentation
* **Comprehensive Documentation Port**: Added a brand-new guide detailing the Playit.gg Tunnel Integration (**docs/playit-tunnels.md**) with Mermaid sequencing and error diagnosis.
* **Upgraded Backup Guides**: Redesigned **docs/cloud-backups.md** to fully document the secure token proxy architecture, restore steps, and Google/Dropbox/OneDrive developer console setup configurations.

## v1.6.9 - Playit.gg Connectivity & Versioning Resilience

This release fixes a critical Playit.gg agent connection failure by pinning to a stable, signed agent binary and correcting the partner version resolution logic to prevent upstream API registration errors.

### 🌐 Resilient Playit.gg Tunneling
* **Pinned Signed Agent Binary**: Changed the playit-agent download mechanism to target a stable, signed release version (**v0.17.1**) instead of the floating `latest` GitHub release. This prevents provisioning failures caused by experimental or broken upstream releases.
* **Agent Version Fallback Hardening**: Fixed a bug where downloaded playit executables lacking Windows file version resources would trigger fallback to the PocketMC app version (`1.6.8` / `1.6.9`), resulting in upstream Playit API `AgentVariantVersionNotFound` registration errors. The resolver now falls back gracefully to the registered Playit agent version `0.17.1`.
* **Automatic Agent Refresh**: Automated the deletion of deprecated local `playit.exe` installations on upgrade, forcing a clean background pull of the new signed v0.17.1 release.


## v1.6.8 - Server Settings, Player Management & Dashboard Polish

This release adds render/simulation distance controls across all server engines, completely redesigns the Player Management UI with a modern sidebar layout, and refines the dashboard loading experience with engine-aware skeleton placeholders.

### ✨ New Features & Enhancements
* **Render & Simulation Distance Sliders**: Added production-grade render distance and simulation distance sliders to Server Settings for all supported engines (Java, Bedrock, PocketMine). Each engine maps to its native config property (`view-distance`, `server-authoritative-movement`, `chunk-ticking.tick-radius`, etc.) with engine-appropriate value ranges and defaults.
* **Player Management Sidebar Navigation**: Replaced the legacy top-level TabControl with a modern NavigationView sidebar, consistent with the Server Settings page design. "Online Players" and "Ban List" tabs now live in a clean sidebar with slide-in transition animations.
* **OP Status Toggle Switch**: Replaced the ToggleButton for operator status with a native Windows ToggleSwitch slider control, providing a cleaner visual indicator of persistent OP state with live updates.
* **Engine-Aware Skeleton Loading**: Dashboard instance cards now show the correct number of skeleton placeholder rows during IP resolution based on server type — Java: 1 row, Bedrock/PM: 2 rows, Java+Geyser: 3 rows. LAN IP is excluded from skeletons since it appears instantly.

### 🛠️ Fixes
* **OP Status Binding Fix**: Restored ListView containers (from ItemsControl) in the Player Management page to fix a WPF binding issue where clicking the OP toggle would permanently disconnect the `IsChecked` OneWay binding due to local value precedence.
* **Geyser IP Visibility**: Fixed Bedrock/Geyser IP rows appearing prematurely on crossplay server cards with empty addresses. The Geyser hostname row now waits until the tunnel address is actually resolved before becoming visible.
* **CI Warning Cleanup**: Resolved all remaining build warnings (CS0618 deprecated usage in tests, CS8625 null parameter mismatches) ensuring a zero-warning production build.
* **Test Suite Stability**: All 183 tests passing with updated view-distance test assertions matching the new slider implementations.

## v1.6.7 - AI Intelligence Fixes & Marketplace Upgrades

This release solidifies the AI summarization engine with major timezone and rendering fixes, introduces powerful new addon update capabilities to the marketplace, and continues the modernization of the app's Fluent shell.

### ✨ AI Summarization & Intelligence
* **Session Duration Accuracy**: Fixed a critical timezone calculation bug that caused AI session summaries to incorrectly report a hardcoded offset (e.g., 5h 30m) instead of the actual elapsed session uptime. The UI now dynamically recalibrates duration using strict UTC bounds, retroactively repairing older corrupted summary records.
* **Rich Markdown Engine**: Integrated a custom Markdown pipeline to bring advanced extensions to the Server Console and Settings UI. AI summaries now perfectly render emojis, soft line breaks, and enhanced list formatting.
* **Duration Visibility**: Injected a highly visible "Total Online Time" header directly into the AI summary markdown view.

### 🛒 Addon Marketplace Enhancements
* **Update Engine**: Introduced a complete update workflow for installed addons. You can now seamlessly check for updates on individual mods/plugins or use the new "Update All" action to upgrade all your installed addons simultaneously, fully preserving your modloader and version parity constraints.
* **Reinstallation Fixes**: Resolved UI selection glitches within the Addon Reinstall dialog. The prompt now correctly pre-selects and lists the targeted modification required for manual re-installations.
* **Modpack UI Cleanup**: Streamlined the "Create New Instance" wizard by deprecating and removing legacy modpack ZIP import elements, focusing the UX on the native marketplace engine.

## v1.6.6 - Server Settings Polish & Bedrock Integration

This release brings significant refinement to the server settings experience, properly adapting UI workflows (such as world imports and marketplace integrations) to natively support Bedrock and PocketMine server engines without visual glitches or incorrect capabilities.

### ✨ New Features & Enhancements
* **Vanilla Addons Empty State**: Added a clear empty state warning in the Addons tab for Vanilla servers, explaining that Vanilla does not support plugins/mods and guiding users to change their server engine.
* **Player Management Refresh**: The refresh button in the Player Management panel now actively re-fetches live OP status and gamemodes for players across all server engine types.
* **Unified Bedrock Configurations**: Cleaned up the server settings configurations for Bedrock and PocketMine instances. Removed the interactive dummy server card preview (which is primarily Java-focused) and replaced it with a dedicated, reliable MOTD/Server Name text field. Hidden the Java-exclusive "Browse Maps" button for these engines.

### 🛠️ Fixes
* **Engine-Aware World Imports**: Improved the world import functionality to be fully engine-aware for Bedrock and PocketMine servers, seamlessly supporting `.mcworld` exports alongside standard ZIP archives.
* **PocketMine Addon Marketplace**: Fixed a UI glitch in PocketMine's Addons section where the Plugin Marketplace button was erroneously disabled and the Local File import button incorrectly opened the Poggit web browser.
* **Gameplay Rules UI Rendering**: Resolved a rendering glitch involving an overlapping separator line in the Gameplay Rules tab of the server settings.
* **Test Suite Stabilization**: Fixed test regressions caused by recent updates to player name quoting logic for Java servers.

## v1.6.5 - Cross-Play & PocketMine Player Management Fixes

This patch release resolves critical player management issues for cross-play (Floodgate) and PocketMine environments, ensuring that bans and operator commands work reliably across all player types.

### 🛠️ Fixes & Enhancements
* **Cross-Play Ban & Op Reliability**: Fixed an issue where Java Edition servers would fail to ban or op Bedrock players connected via Floodgate. PocketMC now correctly formats player names without quotes for Java servers, preventing "Player does not exist" errors when interacting with Bedrock players (e.g., `.PlayerName`).
* **PocketMine Ban Parsing**: Fixed a bug where PocketMine's `banned-players.txt` was not being read correctly due to its pipe-delimited format. The player list UI now accurately displays banned PocketMine players and allows for successful unbanning.

## v1.6.4 - Player Management, Console Intelligence & Bedrock Profiles

This release introduces a new player management suite, intelligent console log filtering, and engine-aware settings profiles to provide native configuration options for Bedrock servers.

### ✨ New Features
* **Player Management Suite**: Introduced a comprehensive player management UI powered by a new state file service. Administrators can now natively view and manage operators, banned players, banned IPs, and whitelists directly from the UI without editing JSON files.
* **Console Log Intelligence**: Added a new log classification engine to intelligently parse, classify, and filter server console logs by severity and origin, reducing log spam and improving readability.
* **Engine-Aware Settings Profiles**: Re-architected the settings backend with a new profiling system. The application now provides engine-specific UI configurations and dropdowns (e.g., Gamemodes, Level Types) tailored to the active server's engine.
* **Native Bedrock Settings**: Added a dedicated Bedrock settings view exposing Bedrock-exclusive properties including `server-portv6`, `allow-cheats`, `texturepack-required`, `force-gamemode`, `default-player-permission-level`, and `tick-distance`.

### 🛠️ Fixes & Enhancements
* **Dynamic World Backups**: Refactored the backup service to parse server properties files dynamically. Backups now correctly identify and target the active world folder name for Bedrock and Pocketmine instead of assuming the default `world/` directory.
* **CI Changelog Extraction**: Updated the production build GitHub workflow to improve release note extraction algorithms during automated deployments.
* **Settings Cleanup**: Removed legacy tunnel limit dialog classes and cleaned up redundant dependencies across multiple settings panels.

## v1.6.3 - Installation Lifecycle & UI Safety

This release brings major stability enhancements to the server installation process, introducing download cancellation, safety locks, and better real-time status tracking across the application.

### 🔒 Safety & Installation Control
* **Download Cancellation**: You can now safely cancel server software downloads mid-way. Doing so instantly cleans up all partially downloaded files and deletes the pending server folder automatically.
* **Navigation Lock & Prompts**: The application now intelligently locks sidebar navigation while a server is downloading. Attempting to close PocketMC during an active download will now trigger a safety confirmation dialog.
* **Explicit Installing State**: Introduced a dedicated `Installing` status. This provides accurate visual feedback when servers are processing installers (like Forge/Fabric) and allows you to halt the installation if it hangs.

### 📈 Performance & Networking
* **Console Anti-Freeze**: Added advanced output throttling and a defensive buffer drain to the server console. This completely prevents UI freezes and memory crashes when high-volume installer logs or spam errors flood the terminal.
* **Live Tunnel Updates**: The tunnel management UI has been rebuilt to support real-time state tracking without visual flickering. Tunnels waiting for a public address are now continuously polled in the background.
* **Wizard Redirection**: Fixed the dashboard connection flow to properly redirect first-time users straight to the Playit setup wizard.

### 🛠️ Ecosystem Enhancements & Fixes
* **Java Download Dialog**: Added a dedicated visual progress dialog when downloading missing Java runtimes, keeping you informed rather than freezing the background.
* **Dynamic Bedrock Backups**: The backup engine now dynamically reads your Bedrock server's configuration to locate the exact world folder name, ensuring your backups never miss their target.
* **Floodgate Fixes**: Fixed an issue where Floodgate (cross-play addon) failed to install on Paper and Spigot servers by routing the download directly through the GeyserMC official API.
* **UI Polish**: Tightened the Tunnel Page layout by disabling unnecessary scrollbars.

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
