# Changelog

This changelog is organized from newest to oldest and rewritten from release-to-release diffs, changed-file hotspots, commit scope, and the previous release notes. Each entry explains what changed, why it changed, and what users or maintainers should watch during upgrades.

## Diff Analysis Summary

- `v1.9.4...v1.9.5`: 4 commits focused on Google Drive backup proxy reliability, telemetry proxy endpoint simplification, service registration, deprecated Playit setup cleanup, and dynamic instance port updates.
- `v1.9.3...v1.9.4`: 27 commits focused on custom accent colors, animated navigation, remote dashboard dark mode, Forge/NeoForge stabilization, and UI polish.
- `v1.9.2...v1.9.3`: 58 commits focused on Forge and NeoForge server support, Remote Control web dashboard implementation, automated add-on updates, server update rollback safety, telemetry, and desktop shell polish.
- `v1.9.1...v1.9.2`: 12 commits focused on desktop notifications, Discord linking polish, Remote Control permissions, Playit setup simplification, scroll reliability, export completeness, and release workflow stability.
- `v1.9.0...v1.9.1`: 5 commits focused on Discord integration, single-instance enforcement, QR code generation, and clipboard reliability.
- `v1.8.0...v1.9.0`: 36 commits focused on remote control dashboard, instance import/export, Phase 1 security hardening, and decoupled player management.
- `v1.7.7...v1.8.0`: 24 commits focused on safe instance version updates, persistent console history, marketplace hardening, custom backup destinations, Windows startup/tray behavior, and Discord Rich Presence.
- `v1.7.6...v1.7.7`: 10 commits focused on AI provider flexibility, Paper API v3 migration, creation wizard UX, console preprocessing, and dashboard polish.
- `v1.7.5...v1.7.6`: 19 commits focused on security hardening, custom wallpaper backgrounds, markdown/summary rendering, path safety, atomic writes, DPAPI/process safety, and expanded tests.
- `v1.6.2...v1.6.9`: 53 commits focused on player management, server settings profiles, Bedrock/PocketMine parity, add-on update workflows, runtime download gating, console intelligence, Playit agent stability, and production workflow cleanup.
- `v1.4.0...v1.5.4`: 120 commits focused on NeoForge support, marketplace dependency resolution, port reliability, cross-play networking, automated Playit setup, Java runtime lifecycle management, and release infrastructure.
- `v1.0.0...v1.4.0`: 39 commits focused on turning the early desktop shell into a broader multi-protocol server manager with Bedrock, PocketMine, diagnostics, graceful lifecycle handling, Velopack packaging, and stronger infrastructure.

---

## v1.9.5 - Automatic Updates, Clean Architecture & UX Improvements

### Summary

v1.9.5 brings significant architectural improvements by restructuring the application into Clean Architecture with `PocketMC.Domain`, `PocketMC.Application`, and `PocketMC.Infrastructure`. It introduces an automatic update startup flow, adds native detection for Geyser and SimpleVoiceChat, and improves user security by introducing a mandatory password setup prompt when enabling Remote Control. Additionally, the Playit Agent setup has been delayed to not block the initial dashboard experience.

### Diff Basis

The `v1.9.4...v1.9.5` diff focuses on major architectural refactoring (moving to Clean Architecture), automatic updates, Remote Control security enhancements, Bedrock/proxy port detection, image processing refactoring, and Playit setup flow optimizations.

### Added

- **Clean Architecture Restructuring**
  - Restructured the project to follow Clean Architecture principles, separating core business logic into `PocketMC.Domain` and external integrations into `PocketMC.Infrastructure`.
  - Moved AI providers (Gemini, etc.) to the Infrastructure layer.
- **Automatic Updates**
  - Added a lightweight `StartupUpdateWindow` to handle checking and downloading updates synchronously before opening the main application window (if enabled in settings).
- **Remote Control Security**
  - Added a password prompt when toggling Remote Control "ON" for the first time, ensuring users either set a password or explicitly opt-out, preventing accidental insecure tunnel exposure.
- **Bedrock & Voice Chat Support**
  - Integrated `GeyserDetector` and `SimpleVoiceChatDetector` to seamlessly identify Bedrock bridge proxies and voice chat add-ons, automatically configuring their required ports in the Preflight Service and Dashboard.
- **Cloud Backup Reliability**
  - Added a fallback proxy URL for Google Drive backup operations so backup requests can retry through a secondary endpoint if the primary proxy path fails.
  - Introduced a shared proxy-posting helper for backup requests to keep retry behavior consistent across Google Drive backup operations.
- **Application Services**
  - Registered the new `WhatsNewService` in the dependency injection container.
  - Registered `InstancePortUpdateService` so server startup retry flows can update instance ports dynamically.

### Changed

- **Playit Setup Flow**
  - Delayed the Playit setup wizard so it no longer blocks the dashboard on fresh launch. Users can now complete the setup from the Tunnel page when they are ready.
- **Image Processing**
  - Extracted image cropping logic into a dedicated `ImageProcessingService` for reusability and cleaner architecture.
- **Telemetry Proxy Configuration**
  - Updated telemetry to use the latest production proxy base URL.
  - Simplified telemetry posting so telemetry events post directly to the primary proxy endpoint without fallback handling.
- **Server Startup & Port Management**
  - Integrated `InstancePortUpdateService` into dashboard startup retry handling to support dynamic port updates when a server needs to retry startup on a different port.
- **Updates Configuration**
  - Removed the "automatically install updates" feature; it now strictly performs update checks based on user preferences.

### Removed

- **Deprecated Playit Setup Flow**
  - Removed the obsolete `PlayitSetupWizard` navigation route and wizard pages.
  - Removed unit tests tied to the deprecated wizard route.
- **Discord Commit Notifications**
  - Removed the Discord commit notification workflow.

### Reasoning

This release separates reliability needs by service area and greatly improves the maintainability of the codebase through Clean Architecture. Delaying the Playit setup and adding a password prompt for Remote Control directly address user friction and security. Removing the old Playit setup wizard route keeps navigation cleaner, while the new instance port update service lays groundwork for smoother automatic recovery when ports need to change during server startup retries.

### Upgrade Impact

- **Backups:** Google Drive backup operations should be more resilient when the primary proxy endpoint is unavailable.
- **Telemetry:** Telemetry uses the updated production proxy endpoint with simpler request behavior.
- **Remote Control:** Users enabling Remote Control for the first time will be prompted to set a password.
- **Playit Setup:** The setup is no longer forced on first launch.
- **Server Startup:** Startup retries can now participate in dynamic port updates through the newly registered service.

---

## v1.9.4 - Custom Accent Colors, UI Polish & Web Dashboard Enhancements

### Summary

v1.9.4 introduces custom accent colors, animated navigation indicators, and multiple visual refinements across the application. It brings dark/light mode support to the Remote Control web dashboard, promotes Forge and NeoForge out of Beta, and improves instance import flexibility by supporting JSON comments and trailing commas.

### Diff Basis

The `v1.9.3...v1.9.4` diff contains 27 commits. The largest changes are in the custom accent color support, the animated navigation indicator for the sidebar, remote control web dashboard theme support, and the Forge/NeoForge stabilization.

### Added

- **UI & Personalization**
  - Added support for custom accent colors throughout the app.
  - Added an animated navigation indicator for sidebar lists to improve feedback.
  - Added a new default image wallpaper along with a restore button to easily revert to it.
  - The default accent color has been migrated to green.
- **Remote Control Dashboard**
  - Added theme toggle and support for light/dark mode in the web dashboard.
  - Improved caching by adding cache-control headers to static files and using a local favicon.
- **Discord**
  - Added a confirmation dialog before linking a Discord account.
  - Added Discord role mentions to release notifications in CI.

### Changed

- **Forge & NeoForge Support**
  - Removed the Beta badge from Forge and NeoForge server options as they are now stable.
  - Enabled the Addons panel for NeoForge servers.
- **UX Refinements**
  - Moved the "Auto-Update Addons" toggle directly to the addons toolbar for easier access.
  - Updated the application logo, icons, and branding images across the app, documentation, and web dashboard.
  - Improved the description text for telemetry settings to be more clear.
- **Instance Management**
  - The instance import parser now allows JSON comments and trailing commas for better resilience with manual backups.

### Fixed

- Resolved nested `ScrollViewer` issues in the Tunnel page that caused scrolling glitches.
- Fixed an issue where the second row height in `AppDialogWindow` wasn't sizing correctly.
- Updated the telemetry proxy base URL to point to the correct production endpoint.

### Reasoning

This release focuses on personalization and user experience. Custom accent colors and new navigation animations make the app feel more native and polished. Promoting Forge and NeoForge out of beta recognizes their stability, while the dark mode addition to the remote control dashboard brings it to parity with modern web expectations.

### Upgrade Impact

- **Appearance:** The default accent color will switch to green, but users can now fully customize it in settings.
- **Remote Dashboard:** Users will see a new theme toggle and updated branding on the web interface.

---

## v1.9.3 - Forge & NeoForge Support, Remote Dashboard & Add-on Auto-Updates

### Summary

v1.9.3 introduces first-class support for Forge and NeoForge servers, automatic add-on updates on server start, and significant resilience improvements for server updates through a new temporary directory rollback system. It also strengthens the existing Remote Control dashboard by adding password authentication and encrypted credentials, alongside new telemetry features and desktop shell polish.

### Diff Basis

The `v1.9.2...v1.9.3` diff contains 58 commits. The largest additions involve the new `AddonAutoUpdateService`, `InstanceRollbackService` for update safety, Forge/NeoForge mod compatibility scanning, and the new `RemoteAuthenticationService` for the web dashboard. Several documentation and README assets were also reorganized or updated.

### Added

- **Forge & NeoForge Support**
  - Added Forge and NeoForge as Beta server types during instance creation.
  - Implemented engine compatibility checks for mods based on loader type (Forge vs. NeoForge vs. Fabric).
  - Enhanced mod compatibility visibility in the UI so users know which mods match their server type.
- **Remote Control Authentication**
  - Added password authentication to the existing Remote Control web dashboard.
  - Added secure encrypted password storage for remote control settings.
- **Add-on Auto-Updates**
  - Added an `AddonAutoUpdateService` to optionally update installed add-ons automatically when the server starts.
  - Added state text overrides and compatibility checks to prevent updating incompatible plugins or mods.
- **Desktop & Shell Polish**
  - Added configuration options for startup behavior, power management, and telemetry settings.
  - Added `WindowsCornerService` to bring Windows 11-style rounded corners to Windows 10.
  - Enhanced keyboard navigation and shortcuts across the application.
  - Implemented a telemetry service (with final heartbeat on shutdown) for app diagnostics.

### Changed

- **Server Updates & Rollbacks**
  - Improved the instance rollback process to use a temporary directory, making server restoration much safer and fixing directory lock issues during updates.
  - Enhanced installer logging and progress reporting during the server setup process.
- **Add-on Management**
  - Integrated auto-update add-ons functionality directly into the Dashboard and UI.
  - Removed the legacy Poggit service.
  - Implemented Geyser detection natively into the codebase and refactored related references.
- **Documentation & UI**
  - Reorganized `docs/assets/` into structured `branding/`, `icons/`, and `screenshots/` directories.
  - Updated README with visual server software icons, gifs, and additional feature screenshots.

### Fixed

- Unwanted mod type badges no longer appear for Paper plugins.
- Guaranteed application shutdown when the main window closes.
- Restrict whitelist commands to only execute when the server is fully online.
- Console and dashboard agent connection checks now correctly include the 'Connecting' state.

### Reasoning

This release focuses on automation and reliability. Forge and NeoForge support opens up a major segment of modded Minecraft. Adding update rollbacks and add-on auto-updates means server operators spend less time doing manual file management and are protected if a major version upgrade fails mid-installation. Finally, adding password authentication to the Remote Dashboard ensures secure access when exposed via public tunnels.

### Upgrade Impact

- **Remote Control:** Users can now set up passwords to secure their remote control web dashboard access.
- **Add-ons:** Servers configured for auto-updates will safely scan and update their add-ons on launch.
- **Telemetry:** A new telemetry service is active but controllable via the App Settings page.

---

## v1.9.2 - Notifications, Discord Linking, Playit Setup & Scroll Reliability

### Summary

v1.9.2 is a focused polish and reliability release. It adds configurable desktop notifications, completes more of the Discord linking loop, simplifies Playit setup, improves Remote Control settings organization, fixes Java instance exports, and centralizes page scrolling so affected pages no longer need App Settings to be visited first.

### Diff Basis

The `v1.9.1...v1.9.2` diff contains 12 commits touching 36 files. The largest changes are in App Settings notification controls, Windows toast activation, Remote Control settings, Playit setup wizard flow, shared WPF scroll handling, instance export contents, and release notification CI.

### Added

- **Configurable Desktop Notifications**
  - Added App Settings toggles for server online notifications, Playit agent connection notifications, Remote Control availability notifications, and AI summary notifications.
  - Shows a Windows toast when a server reaches the online state, including the server name, loader type, and Minecraft version.
  - Shows a Remote Control toast when the web panel is started and a public tunnel URL is available, with duplicate notifications suppressed for the same tunnel URL.
  - Added AI summary completion toasts with a **View** action.
- **AI Summary Toast Navigation**
  - Clicking an AI summary toast now opens PocketMC, restores the main window if needed, navigates to the relevant server settings page, selects the summaries tab, and opens the latest summary automatically.
- **Discord Linking**
  - After a successful `pocketmc://` Discord link activation, PocketMC now calls the configured Discord API `assign-role` endpoint so linked users can receive their Discord role automatically.
- **Remote Control Settings**
  - Split Remote Control permissions and Discord Direct Message notification settings into their own expandable sections for easier scanning.
  - Added a clearer Discord notification section with linked/not-linked state, join server action, and DM notification toggle.

### Changed

- **Playit Setup Wizard**
  - Simplified Playit setup from four steps to two steps: get the setup code, then paste and connect.
  - Consolidated the browser-side Playit instructions into a single checklist so users do not have to click through multiple internal steps.
- **Navigation & Settings Organization**
  - Reordered the footer navigation so Settings appears before Java Setup.
  - Reordered App Settings so behavior, updates, appearance, notifications, Discord, cloud backups, API configuration, AI, and diagnostics are grouped more naturally.
- **Scroll Handling**
  - Centralized mouse-wheel forwarding in `ScrollViewerHelper` with detachable preview handlers and better child-control handling.
  - Applied the shared scroll behavior to Dashboard, Tunnel list, Ports Map, Remote Control, Java Setup, App Settings, and About.
  - The app now disables the shell-owned navigation scroll host for affected pages directly instead of relying on App Settings to initialize that state incidentally.

### Fixed

- **Java Instance Export Completeness:** Java exports now include `eula.txt` alongside server properties, whitelist, ops, bans, and other root files.
- **AI Provider Settings:** Switching AI providers no longer saves the previous provider's endpoint URL under the newly selected provider.
- **Toast Activation:** Windows toast arguments are parsed more safely for summary navigation.
- **Release CI:** Discord release notifications now pass generated release notes through an environment variable, avoiding PowerShell parsing issues with multiline or special-character changelog text.

### Reasoning

This release removes several small but sharp bits of friction. Users get clearer feedback when servers, tunnels, and summaries are ready; Discord linking now completes the role assignment handoff; Playit setup has fewer steps; and page scrolling is initialized at the shell/page level instead of depending on a side effect from App Settings. The export fix also matters for portability because `eula.txt` is part of a complete Java server instance.

### Upgrade Impact

- Notification settings default to enabled, so users may see new desktop toasts after upgrading. They can disable each category from App Settings.
- Discord-linked users may now trigger an `assign-role` call against the configured Discord API.
- Java instance exports now carry `eula.txt`; restored or shared exports should more closely match the original instance.
- No server data migration is required.

---

## v1.9.1 - Discord Integration, QR Codes & Single-Instance Enforcement

### Summary

v1.9.1 improves the Remote Control experience by adding Discord bot integration for automated public tunnel DMs, QR code generation for quick mobile pairing, and structural desktop improvements like single-instance enforcement and deep-link routing.

### Added

- **Discord Integration & Deep Linking**
  - Registered the `pocketmc://` custom URI protocol in the Windows registry to support external application deep links.
  - Implemented automatic Discord account linking when clicking the connection link from the PocketMC Discord Welcome Bot.
  - Added a **Discord Integration** section to the Remote Control page, tracking the linkage status and allowing users to toggle automated public tunnel Direct Messages.
  - Automatically dispatches a webhook to the PocketMC Discord Bot when a public tunnel initializes, sending the remote control URL straight to the user's DMs.
- **Remote Control QR Codes**
  - Added QR code generation for both Local and Public remote control URLs, letting users effortlessly scan to open the dashboard on their mobile devices without typing out complex addresses.
- **Desktop Architecture**
  - **Single Instance Enforcement:** Launching the app multiple times now forces the existing running instance to the foreground instead of silently spawning a duplicate background process.
  - Incoming custom URIs are now passed gracefully from the second process to the primary running process via Named Pipes.

### Fixed

- **Clipboard Reliability:** Refactored copy-to-clipboard actions (like copying URLs or ports) to use a new asynchronous `ClipboardHelper`. This includes built-in retries and COM exception swallowing, preventing crashes when other background apps temporarily lock the Windows clipboard (e.g., `CLIPBRD_E_CANT_OPEN`).
- Replaced the plaintext Discord invite hyperlink with a properly styled Discord-branded "Join Discord Server" button.

### Reasoning

These additions directly attack friction. Typing long URLs on mobile keyboards is awful, which QR codes solve instantly. Finding the app when you've already launched it or accidentally opening multiple instances is annoying, which single-instance locking fixes. And finally, sending the public remote control URL straight to the user's Discord DMs via the Welcome Bot completely closes the loop on making PocketMC a hands-free, remote-first server environment.

### Upgrade Impact

- Upgrading will register the `pocketmc://` protocol handler in the Windows registry.
- Users who attempt to run the app multiple times will now simply have their original window brought into focus.

---

## v1.9.0 - Remote Control Web Dashboard, Instance Import/Export & Security Hardening

### Summary

v1.9.0 is a major feature and security release bringing a full Remote Control web dashboard, Instance Import/Export capabilities, decoupled player management, manual add-on updates, power management features, and Phase 1 security hardening.

### Changed

- **Remote Control & Dashboard**
  - Comprehensive Remote Control web dashboard to manage server instances remotely via a mobile-friendly web UI.
  - Integrated PlayIt HTTPS tunnel provider to securely expose the remote dashboard effortlessly.
  - Remote control pairing authorization, token lifetime configuration, LAN bypass, and active session visibility options.
- **Instance Management**
  - Complete Instance Import and Export functionality with cancellation support and simplified add-on packaging.
- **Player & Whitelist Management**
  - Decoupled player management from the server process.
  - Live server controls with enhanced internal whitelist and Bedrock allowlist support.
- **Add-on & Mod Management**
  - Added an add-on inventory model for classifying installed mods/plugins/packs by kind, state, and filename policy.
  - Added add-on toggle support (enable/disable without deleting files manually).
  - Added add-on update-check models and manual add-on update installation support.
  - Polymorphic add-on metadata scanning across different Mod Loaders.
  - Refactored `SettingsAddonsVM` to cleanly separate file scanning, UI presentation, and state mutation.
- **Core App & Settings**
  - Added power management sleep prevention to natively keep the system awake automatically when a server is running.
  - Added a `ServerRuntimeSettingApplier` path for safer server configuration application.

### Fixed

- **Phase 1 Security Hardening:** Pinned Playit agent checksum, validated Bedrock world paths for backup restore, and applied atomic write safety to downloads and session locks.
- **Google Drive Backups:** Properly escaped literal paths for Google Drive queries to prevent query parsing faults.
- **Process Stability:** Made server process registration atomic and fixed issues where active session locks and log streams could be prematurely deleted.
- **PlayIt Integration:** Refined the Simple Voice Chat proxy prompt message, added async stop support, and ensured predictable agent binary deletion behaviors.
- **UI Adjustments:** Ensured clear state representation when remote control is disabled or when a tunnel is initializing. Modpack overrides correctly persist metadata.

### Reasoning

The introduction of the Remote Control Dashboard transitions the application from a localized manager to a fully accessible host environment, meaning administrators can finally control server instances on the go via a browser or mobile device securely. The Phase 1 hardening efforts drastically improve the application's ability to safely download unverified payloads (PlayIt agents, add-ons), prevent race conditions on session locks, and sanitize configuration updates. Finally, the add-on component refactor creates cleaner ownership and prepares for more robust updates down the line.

### Upgrade Impact

- **Remote Control defaults to Off:** Users must explicitly start remote control sessions and configure access tokens if they wish to manage the application remotely.
- **Add-On State Management:** Add-ons toggled off now retain local configurations but stay safely excluded from the server runtime.
- **Power Management:** System will no longer sleep natively if a PocketMC instance is actively started, overriding standard Windows idle rules for the lifetime of the process.
- **Backups & Security Checks:** Custom manual backup restorations heavily rely on improved Bedrock world path checks, rejecting invalid payload archives for live servers.

---

## v1.8.0 - Instance Version Updates, Persistent Console History & Desktop Operations

### Summary

v1.8.0 is a major operational reliability release. It adds a real update pipeline for existing server instances, persistent current/last console logs, richer add-on metadata, safer marketplace installs, per-instance backup destinations, Windows startup/tray controls, per-server app-launch auto-start, Discord Rich Presence, and an Interactive Ports Map.

### Diff Basis

The `v1.7.7...v1.8.0` diff contains 24 commits and a wide set of changes across updates, marketplace, console, backups, networking, desktop startup, tests, and UI. The largest new areas are the `Features/Instances/Updates` subsystem, `ConsoleLogHistoryService`, `DiscordRpcService`, marketplace file/install hardening, Java mod metadata scanning, and `PortsMapPage`.

### Added

- Version & Updates workflow for stopped server instances.
- Update planning with target version selection, Java runtime requirements, compatible/incompatible add-on reporting, warnings, backups, snapshots, staging, journals, locks, and rollback support.
- Staged update application for server artifacts and marketplace add-ons.
- Add-on migration planning, staging, and application for tracked marketplace add-ons.
- Persistent console history using current, last-session, archive, and legacy fallback log files.
- Read-only last-session console mode for stopped/crashed servers or app restarts.
- Windows startup, start minimized to tray, minimize to tray on close, and per-instance auto-start settings.
- Discord Rich Presence with active server state, version/type, player count, uptime, and join/download action.
- Interactive Ports Map showing instance ports, Playit tunnel addresses, role/status metadata, and edit/copy/navigation actions.
- Per-instance custom local backup directory support.
- Add-on search, sorting, icons, metadata scanning, side-support badges, and warning-first filtering.
- Java mod/plugin metadata scanning for Fabric, Quilt, Forge, NeoForge, legacy Forge, and Bukkit/Paper plugin metadata.
- Marketplace install risk warnings and safer modpack override inspection.

### Fixed

- Console pages can now open for stopped or historical instances after app restart.
- Previous console session logs are no longer overwritten before users can view them.
- Large console logs now load as tails instead of freezing the UI by reading entire files.
- Dependency resolution no longer continues after required dependency failures.
- Marketplace install/update paths now validate staged files, expected extensions, filenames, hashes, and contained paths.
- Bedrock add-on manifest parsing handles string and array versions.
- Bedrock add-on installs now fail properly when no valid manifest is installed.
- Unsafe modpack overrides are blocked from escaping the instance folder or touching protected files.
- Custom backup directories are now included in integrity checks and manifest cleanup.
- Simple Voice Chat detection now requires an actual mod/plugin JAR instead of trusting stale configs/logs.
- Missing bundled Java runtimes now require user confirmation before download/repair.
- Tray-close behavior still performs graceful shutdown when exiting.
- Windows startup minimized launches intentionally skip Minecraft server auto-start.

### Reasoning

The app moved from “create and run servers” toward “operate servers safely over time.” Existing server updates, persistent logs, rollback, and backup-aware changes are the difference between a toy launcher and a tool people can trust with worlds they actually care about. Marketplace hardening also matters because add-on downloads touch executable code and archive extraction, which are exactly where bad assumptions go to become security incidents.

### Upgrade Impact

- Existing `logs/pocketmc-session.log` remains supported as a fallback.
- Version updates should be run on stopped servers.
- Manual/untracked add-ons are preserved but may not update automatically.
- Bedrock packs are preserved during updates, but automatic Bedrock add-on update migration is not supported.
- Windows startup and tray settings default to off.
- Discord Rich Presence is controlled by App Settings.
- Background Java setup defaults to Java 25; older Java versions are prompted on demand.

---

## v1.7.7 - Custom AI Providers, Creator Wizard Improvements & Paper API v3

### Summary

v1.7.7 improves AI configuration, reduces console summary token waste, modernizes the new instance wizard, adds inline world import and gameplay presets, migrates Paper downloads to the PaperMC v3 API, and polishes dashboard metadata.

### Diff Basis

The `v1.7.6...v1.7.7` diff contains 10 commits. The most visible code changes are in the new instance page, AI client/preprocessor, Paper provider, dashboard card view model, and console history loading.

### Changed

- Added Ollama/local-cloud model support and custom endpoint/model overrides across AI providers.
- Hid the endpoint URL input unless it is relevant to the selected provider.
- Added consecutive log-line deduplication before AI processing.
- Suppressed repeated player-list command spam when loading historical console logs.
- Added level seed, world type, gamemode, difficulty, player limit, and custom world import options during instance creation.
- Refactored the creation wizard into a cleaner two-column layout with responsive collapse and a pinned footer action bar.
- Migrated Paper provider resolution to the PaperMC v3 API.
- Improved dashboard “Last Played” display with relative times and exact tooltips.
- Consolidated card metadata to reduce duplicate RAM/slot display clutter.

### Fixed

- Fault Tolerance card alignment in settings.
- Test flakiness around file locks and time-dependent assertions.

### Reasoning

The AI work reduces wasted tokens and configuration friction. The wizard changes move important server choices earlier, where they belong. The Paper API migration is defensive maintenance: depending on old upstream endpoints is a great way to wake up to broken server creation and a user base holding pitchforks made of bug reports.

### Upgrade Impact

- Users using custom AI endpoints should verify provider/model settings after upgrade.
- Paper server creation should be more resilient against upstream API changes.

---

## v1.7.6 - Appearance Customization, Security Hardening & UI Polish

### Summary

v1.7.6 adds custom wallpaper background support, improves About page visuals, and performs a broad security and robustness hardening pass.

### Diff Basis

The `v1.7.5...v1.7.6` diff contains 19 commits. Most changes are security/test-focused, with major work in path validation, settings writes, markdown rendering, DPAPI/process handling, Bedrock add-on safety, and About/appearance UI.

### Added

- Custom background image support for the Wallpaper Blur theme.
- Browse, clear, use-wallpaper fallback, and preview controls for custom backgrounds.
- High-resolution About page logo while preserving the small Minecraft-compatible logo asset.
- Support/donation card and About page scroll fixes.
- Native markdown viewer and emoji formatting improvements for AI summaries.
- Additional security tests for paths, regex timeouts, DPAPI, Bedrock add-ons, settings, disk writes, process/job objects, RCON, and loopback handling.

### Fixed and Hardened

- Directory traversal protections across backup, summary, cloud, add-on, and extraction paths.
- Atomic config/manifest writes to reduce corruption risk during shutdown or power loss.
- Regex operations now use timeouts to reduce ReDoS risk.
- DPAPI credential path handling is stricter.
- UWP loopback checks avoid sync-context deadlocks.
- Java runtime validation and process cleanup paths are more robust.
- Bedrock add-on installer now rejects unsafe or malformed archive paths more consistently.
- AI summary formatting handles markdown and emoji output more reliably.

### Reasoning

This release was about making the app less trusting. That is good engineering. A local server manager downloads files, extracts archives, stores credentials, edits configs, and launches processes. Every one of those verbs is a tiny invitation for chaos if guardrails are weak.

### Upgrade Impact

- Custom background images should be treated as UI preferences only, not core runtime state.
- Security changes may reject paths or add-ons that previously worked only because validation was too permissive.

---

## v1.7.5 - Playit Agent Safety & Java Provisioning Tweaks

### Summary

v1.7.5 hardens Playit agent controls and improves Java runtime ordering and cache correctness.

### Changed

- Added a confirmation dialog before disconnecting the Playit agent, since that action wipes the local secret key.
- Added a Delete Agent action for removing the local `playit.exe`, only enabled when the agent is stopped.
- Removed Simple Voice Chat tunnel IP display from dashboard cards to reduce confusion.
- Fixed dashboard skeleton loaders disappearing before Bedrock/Playit addresses fully resolve.
- Changed Java provisioning order to prioritize Java 25 first, then older versions.
- Updated Java Setup ordering to show newer runtimes first.

### Fixed

- Manually deleted Java runtimes no longer appear as installed after tab switching or refresh.
- Duplicate Java Setup download icon issue.

### Reasoning

This release reduces accidental destructive actions and fixes runtime state lying to the user. “Installed” should mean installed, not “we remembered it fondly from before deletion.”

### Upgrade Impact

- Java runtime state is more filesystem-backed and less cache-trusting.
- Users who intentionally deleted runtimes should see more accurate status.

---

## v1.7.4 - System-Wide Backdrops, Wallpaper Blur & Startup Safeguards

### Summary

v1.7.4 introduces the unified window backdrop system, Wallpaper Blur, improved Acrylic behavior, dynamic light theme support, and safer server startup state handling.

### Changed

- Added Wallpaper Blur theme for Windows 10 and Windows 11 using pre-rendered blurred wallpaper.
- Unified Mica, Acrylic, Wallpaper Blur, Solid Dark, and Solid Light into a single background selector.
- Reworked native Acrylic handling with DWM/PInvoke-backed dark mode attributes.
- Improved unfocused-window behavior so frosted effects remain visually consistent.
- Added dynamic light theme support instead of forcing dark UI everywhere.
- Added `ServerState.SettingUp` to block duplicate startup attempts during preflight work.
- Improved cancel/error handling so aborted preflight flows revert server state correctly.
- Simplified confirmation dialog button layouts.
- Enlarged clickable player metric target on dashboard cards.

### Reasoning

This was half UX polish, half state safety. Visual themes matter because the app is a desktop product, but `SettingUp` matters more: double-click startup races are how users get duplicate processes, broken ports, and bug reports that read like haunted-house transcripts.

### Upgrade Impact

- Theme settings become more explicit.
- Startup flow should be safer during Playit, memory, or Simple Voice Chat preflight prompts.

---

## v1.7.3 - Tunnel Display Fix

### Summary

v1.7.3 fixes Playit tunnel addresses disappearing from server cards even when tunnels are active.

### Fixed

- Tunnel addresses now remain visible while resolving.
- Server cards show pending/error states when tunnels exist but addresses are not allocated yet.
- Playit API parsing supports multiple response shapes.
- Java, Bedrock/Geyser, and Simple Voice Chat tunnel resolution paths have expanded coverage.

### Reasoning

Blank connection cards are not “minimal UI.” They are silent failure wearing a clean shirt. This patch makes tunnel state visible and actionable.

### Upgrade Impact

- Users should see clearer dashboard tunnel state during address allocation or tunnel-limit conditions.

---

## v1.7.2 - Simple Voice Chat Tunneling & Networking Refinements

### Summary

v1.7.2 adds first-class Simple Voice Chat support with automatic detection, configuration tracking, and one-click tunnel provisioning. It also refactors tunnel orchestration for multi-tunnel scenarios.

### Added

- Detection for Simple Voice Chat across mod/plugin JARs, configs, and logs.
- Simple Voice Chat port/config tracking.
- Non-blocking first-run prompt for creating a voice tunnel.
- Dashboard status for voice tunnel health and public address availability.
- Metadata fields for voice config path, port, tunnel ID, address, warning state, and status.
- Tests for detection, parsing, first-run prompts, and tunnel lifecycle behavior.

### Changed

- Refactored `InstanceTunnelOrchestrator` to handle primary server and voice tunnel flows separately.
- Improved Playit API request/response handling and token refresh fallbacks.
- Added richer port binding role classification for diagnostics and preflight checks.
- Improved port conflict detection for auxiliary ports.

### Reasoning

Simple Voice Chat is not just “another port.” It has first-run config timing, UDP behavior, server-mod detection, and tunnel lifecycle problems. Treating it as a real feature instead of a manual footnote makes cross-network voice setups much less miserable.

### Upgrade Impact

- Existing servers with Simple Voice Chat may prompt for tunnel creation.
- Voice chat may still require the mod/plugin to generate config on first launch before final tunnel patching.

---

## v1.7.1 - Backup Intelligence, Health Monitoring & UI Polish

### Summary

v1.7.1 upgrades backups from plain archives into versioned, metadata-rich records with integrity checks, health warnings, and better About page links.

### Added

- Sequential backup version badges.
- Manual/scheduled trigger labels.
- Size delta tracking between backups.
- Backup cards with timestamp, size, server type, and Minecraft version.
- Editable backup labels stored in `backup-manifest.json`.
- SHA-256 integrity verification per backup.
- Disk-space, failed-backup, overdue-schedule, and summary-bar warnings.
- Feedback/bug report card and native GitHub button in the About page.

### Fixed

- Server icon caching issue that caused custom icons to revert visually after settings reload.

### Reasoning

Backups only matter if users can trust, identify, and restore the right one. Metadata, checksums, labels, and health warnings turn backups into operational history instead of a folder full of mystery ZIPs.

### Upgrade Impact

- Backup manifests become more important for display and integrity features.
- Existing backups without metadata may show less historical detail than new backups.

---

## v1.7.0 - Robust Cloud Backups, Secure OAuth Proxy & One-Click Restore

### Summary

v1.7.0 adds native cloud backup and restore support for Google Drive, Dropbox, and OneDrive using a hybrid OAuth architecture.

### Added

- One-click cloud restore for remote backups.
- Restore write-safety guards that disable restore while servers are running.
- Direct provider integrations for Dropbox, Google Drive, and OneDrive.
- DPAPI encryption for stored cloud access and refresh tokens.
- Google OAuth helper proxy for secure token exchange without exposing client secrets in the open-source app.
- PKCE public-client flows for Dropbox and OneDrive.
- Verbose OAuth/provider diagnostic handling.
- Cloud backup documentation and Playit tunnel integration documentation.

### Reasoning

Cloud backups are high-value, but OAuth secrets in an open-source desktop client are a trap with neon lights. The hybrid proxy approach keeps Google secrets out of the repo while letting Dropbox/OneDrive use public-client flows properly.

### Upgrade Impact

- Users must authenticate providers before cloud backup/restore.
- Restore is intentionally blocked for running servers to prevent world corruption.
- Developer setup requires correct OAuth/proxy configuration.

---

## v1.6.9 - Playit.gg Connectivity & Versioning Resilience

### Summary

v1.6.9 fixes Playit agent provisioning failures by pinning a stable signed agent and correcting version fallback behavior.

### Fixed

- Pinned Playit agent downloads to signed `v0.17.1` instead of floating latest.
- Fixed fallback behavior where missing executable version metadata could incorrectly use the PocketMC app version.
- Deprecated local `playit.exe` files are refreshed on upgrade.

### Reasoning

Network integration cannot depend on whatever upstream labels “latest” today. Pinning a known-good agent reduces random breakage from upstream release churn, humanity’s favorite way to outsource chaos.

### Upgrade Impact

- Existing Playit agent binaries may be replaced automatically.
- Playit registration should avoid `AgentVariantVersionNotFound` caused by incorrect version fallback.

---

## v1.6.8 - Server Settings, Player Management & Dashboard Polish

### Summary

v1.6.8 expands server settings with render/simulation distance controls, redesigns Player Management, and improves dashboard loading indicators.

### Added

- Render and simulation distance sliders for Java, Bedrock, and PocketMine engines.
- Player Management sidebar navigation.
- OP status ToggleSwitch with live updates.
- Engine-aware dashboard skeleton loading rows.

### Fixed

- OP status binding bug caused by switching to non-ListView containers.
- Geyser IP rows no longer show before addresses resolve.
- Remaining build warnings were cleaned up.
- Test suite updated for slider behavior.

### Reasoning

This release improves day-to-day administration. Settings should map to the server engine users actually run, and dashboard loading states should not imply nonexistent addresses.

### Upgrade Impact

- Player management UI behavior changes visually but keeps the same administrative goal.
- Engine-specific settings are more accurate.

---

## v1.6.7 - AI Intelligence Fixes & Marketplace Upgrades

### Summary

v1.6.7 fixes AI summary timing, improves markdown rendering, and adds installed add-on update workflows.

### Added

- Installed add-on update checks.
- Individual update and Update All actions for marketplace add-ons.
- Rich markdown rendering for AI summaries.
- “Total Online Time” header in summaries.

### Fixed

- Session duration timezone bug that produced hardcoded offset-like values.
- AI summary rendering for emojis, soft line breaks, and list formatting.
- Add-on reinstall dialog preselection.
- Removed legacy modpack ZIP UI from the new instance wizard.

### Reasoning

AI summaries are only useful if the time math is not nonsense. Add-on update support also reduces maintenance friction for servers that rely on marketplace-installed content.

### Upgrade Impact

- Older corrupted summary durations may be recalculated.
- Add-on updates still depend on provider metadata quality and loader/version compatibility.

---

## v1.6.6 - Server Settings Polish & Bedrock Integration

### Summary

v1.6.6 improves server settings behavior for Bedrock and PocketMine and removes Java-centric UI assumptions from non-Java engines.

### Added

- Vanilla add-ons empty state explaining that Vanilla servers do not support mods/plugins.
- Player Management refresh now reloads live OP status and gamemodes.
- Dedicated Bedrock/PocketMine MOTD/server-name workflow.

### Fixed

- Bedrock/PocketMine world imports support `.mcworld` and ZIP archives more reliably.
- PocketMine Addons tab correctly enables marketplace/local file behavior.
- Gameplay Rules separator rendering issue.
- Test regressions from Java player-name quoting changes.

### Reasoning

The app supports more than Java now, so Java-only assumptions in settings become bugs. This release tightens engine-aware UX instead of making Bedrock users navigate Java-shaped nonsense.

### Upgrade Impact

- Bedrock/PocketMine settings should show fewer irrelevant Java-only controls.
- World import behavior is more engine-specific.

---

## v1.6.5 - Cross-Play & PocketMine Player Management Fixes

### Summary

v1.6.5 fixes ban/op handling for Floodgate players and PocketMine banned-player parsing.

### Fixed

- Java servers now format Floodgate Bedrock player names correctly for ban/op commands.
- PocketMine `banned-players.txt` pipe-delimited format is parsed correctly.
- PocketMine unban operations now work from the UI.

### Reasoning

Cross-play administration fails fast when player names are formatted incorrectly. This patch fixes real admin commands, not decorative UI dust.

### Upgrade Impact

- Cross-play player moderation should be more reliable after upgrade.
- PocketMine ban lists should display accurately.

---

## v1.6.4 - Player Management, Console Intelligence & Bedrock Profiles

### Summary

v1.6.4 adds the first major Player Management suite, console log classification/filtering, and engine-aware settings profiles.

### Added

- Player Management UI for operators, banned players, banned IPs, and whitelists.
- State-file-backed player administration services.
- Console log classification and severity/origin filtering.
- Engine-aware settings profiles.
- Native Bedrock settings for Bedrock-specific properties.

### Fixed and Improved

- Backup service dynamically resolves Bedrock/PocketMine world folders instead of assuming `world/`.
- Production release-note extraction in CI was improved.
- Legacy tunnel limit dialog classes and redundant settings dependencies were cleaned up.

### Reasoning

This is where PocketMC becomes more server-admin tool than launcher. Player controls, logs, and engine-aware settings are core operational features.

### Upgrade Impact

- Settings and backup behavior become more engine-specific.
- Existing Bedrock/PocketMine instances benefit from more accurate world targeting.

---

## v1.6.3 - Installation Lifecycle & UI Safety

### Summary

v1.6.3 adds cancellation, safety locks, and better install state tracking for server software downloads and setup flows.

### Added

- Safe cancellation for server downloads with cleanup of partial files and pending folders.
- Sidebar navigation lock during downloads.
- Close confirmation during active downloads.
- Explicit `Installing` status for installer-processing states.
- Java runtime download progress dialog.
- Runtime provisioning gate.
- More resilient tunnel polling.

### Fixed

- Console anti-freeze output throttling for high-volume logs.
- First-time Playit dashboard connection flow redirects correctly.
- Bedrock backups dynamically resolve the configured world folder.
- Floodgate installs for Paper/Spigot use official GeyserMC API routing.
- Tunnel page scrollbar polish.

### Reasoning

Install flows are vulnerable to half-finished state. Cancel buttons, locks, and explicit statuses stop the app from pretending a server exists before the files and setup actually make sense.

### Upgrade Impact

- Interrupted installs should clean up more reliably.
- Users should see clearer progress when runtime downloads are required.

---

## v1.6.2 - LAN Connectivity & Resilient Tunneling

### Summary

v1.6.2 adds LAN address visibility and improves Playit tunnel creation failure handling.

### Added

- LAN IP and port display on instance cards.
- Copy button for LAN addresses.
- Better port metadata persistence for offline display.
- API-driven Playit tunnel limit handling.
- Persistent manual tunnel creation dialog after failures.
- Non-blocking auto-provisioning failure notifications.

### Reasoning

Not every server connection needs a public tunnel. Showing LAN addresses helps local play immediately, while better tunnel-limit handling turns vague failure into something users can act on.

### Upgrade Impact

- Dashboard cards now show local and public connection paths more clearly.
- Tunnel-limit failures should be less cryptic.

---

## v1.6.1 - Cross-Play & UI Tweaks

### Summary

v1.6.1 fixes cross-play config patching and improves Bedrock dashboard presentation.

### Fixed

- Geyser config port patching for Fabric, Forge, and NeoForge by searching the correct `config/` path.
- Cross-play Bedrock hostname copy button no longer copies numeric IP by mistake.
- Dedicated Bedrock cards use a more appropriate icon.

### Reasoning

Small patch, real pain removed. Copy buttons should copy what they say, which is apparently a bar modern software still trips over.

### Upgrade Impact

- Cross-play connection info should be more accurate.
- Non-Spigot Geyser port patching should behave correctly.

---

## v1.6.0 - Dynamic Theming & Advanced Tunnel Management

### Summary

v1.6.0 adds user-selectable themes and replaces manual tunnel guidance with automated Playit tunnel management.

### Added

- Light, Dark, and System theme selection.
- Dynamic resources for readable dashboard/tunnel UI across themes.
- Automated tunnel provisioning on server start.
- Tunnel rename, local port edit, enable/disable, and delete controls.
- Explicit Save states for tunnel edits.
- Skeleton loading and clearer error states during tunnel resolution.

### Reasoning

This release removes manual tunnel setup friction. Users want to run a server, not perform a small networking ritual under fluorescent lighting.

### Upgrade Impact

- Tunnel management becomes app-driven instead of guide-driven.
- Theme preference becomes configurable.

---

## v1.5.4 - Playit.gg Connection Gate & Settings Refactor

### Summary

v1.5.4 adds a pre-start Playit agent warning gate and cleans up server settings.

### Added

- Pre-start warning dialog when the Playit agent is disconnected or awaiting setup.
- Centralized agent/tunnel state using `AgentProvisioningService`.
- Better handling for pending states such as `AwaitingSetupCode`.
- Network reliability test helper.
- Additional server launch configurator tests.

### Removed

- Legacy Software Updates expander from Server Settings.

### Reasoning

Starting a server while the tunnel agent is clearly not ready creates avoidable confusion. This release makes connection readiness visible before startup instead of after users wonder why nobody can join.

### Upgrade Impact

- Server startup may show a warning if Playit is disconnected or not linked.

---

## v1.5.3 - Java Management, Networking & Setup Fixes

### Summary

v1.5.3 improves Playit setup UX, Java runtime lifecycle controls, and port conflict messaging.

### Added

- Four-step Playit setup wizard.
- Per-runtime Download controls for missing Java versions.
- User-removed Java runtime intent tracking.
- Protection against deleting Java runtimes used by running servers.
- Atomic deletion state handling with rollback on filesystem failure.

### Fixed

- Offline servers no longer permanently reserve ports.
- Port conflict dialogs better identify external processes and likely fixes.
- Removed legacy Secret Key and Provisioning Backend fields.

### Reasoning

This release respects user intent around Java runtimes and makes setup less error-prone. If users delete a runtime, the app should not immediately resurrect it like a cursed folder.

### Upgrade Impact

- Java provisioning becomes more selective.
- Playit setup becomes guided and safer.

---

## v1.5.2 - Intelligence Safeguards & Under-the-Hood Fixes

### Summary

v1.5.2 adds AI summary cost safeguards, improves summarization prompts, fixes Bedrock IPv6 port conflicts, and simplifies console UI.

### Added

- Large-log warning before AI summarization for sessions above roughly 1.5 MB.
- Improved summarization prompt that ignores PII and focuses on performance/configuration issues.

### Fixed

- Bedrock IPv6 port is assigned from IPv4 port + 1 to avoid multi-instance conflicts.
- Removed command suggestion overlay from the console to reduce accidental commands and heavy parsing.

### Reasoning

AI features should not surprise users with cost or privacy risk. Console suggestions were also doing too much for too little benefit.

### Upgrade Impact

- Users may see warnings before summarizing large sessions.
- Bedrock multi-instance port behavior should be safer.

---

## v1.5.1 - Setup UX Fixes & Active Server Indicators

### Summary

v1.5.1 improves first-time setup and dashboard visibility.

### Fixed and Improved

- Default setup directory is created automatically if missing.
- Continue button can be enabled for the default directory.
- Running server cards receive an emerald highlight border.

### Reasoning

First-run setup should not fail because the default folder does not exist yet. That is the software equivalent of forgetting to open the door after inviting someone in.

### Upgrade Impact

- First-time users should have a smoother setup path.
- Running instances are easier to spot.

---

## v1.5.0 - Modloader Expansion, Marketplace Intelligence & Instance Management

### Summary

v1.5.0 adds NeoForge support, loader-aware marketplace filtering, dependency resolution, instance renaming, and stronger release/documentation infrastructure.

### Added

- NeoForge server creation support.
- Loader-aware Modrinth and CurseForge search filtering.
- `EngineCompatibility` model for marketplace compatibility.
- Dependency resolver and add-on manifest tracking.
- Instance renaming with collision detection and case-safe directory moves.
- Instance name and description editing.
- Minimum version filtering in providers.
- Issue templates, contributing docs, license, funding link, and CI release publishing improvements.

### Fixed

- Modrinth relaxed fallback no longer returns arbitrary incompatible loaders.
- Spurious incompatible-loader warnings reduced.
- Multi-monitor window persistence task removed due to layout issues.
- `.devin` directory removed.

### Reasoning

This release formalizes compatibility. Marketplace installs without loader/version checks are just random JAR roulette with extra steps.

### Upgrade Impact

- Marketplace results should be more relevant.
- Instance directory changes are safer, but renaming still needs filesystem permissions.

---

## v1.4.3 - Port Engine, Cross-Play & UI Enhancements

### Summary

v1.4.3 introduces a port reliability engine, configurable Geyser Bedrock ports, dashboard UI modernization, and release workflow improvements.

### Added

- Port reliability engine with tests.
- Configurable per-instance Geyser Bedrock UDP ports.
- Numeric IP display alongside hostnames.
- Separate copy buttons for precise address copying.
- Automatic Geyser config patching.
- Fabric API auto-download during Geyser setup for Fabric servers.
- Modern dashboard grid layout and enhanced connection blocks.

### Fixed

- Duplicate config-level ports no longer block startup unless another instance is actually running.
- Session log handle leaks after server exit.
- Clipboard crashes during IP copy.
- Build locking during publishing.
- XAML symbol rendering crashes.
- Console scrolling behavior.

### Reasoning

The networking model needed to become explicit. Multiple instances, cross-play, Geyser, and tunnels cannot be managed reliably with “hope port 25565 is free.”

### Upgrade Impact

- Users running multiple cross-play servers should see fewer port collisions.
- Dashboard connection information becomes more detailed.

---

## v1.4.2 - UI Modernization & Observatory Hardening

### Summary

v1.4.2 improves AI summary presentation, console filtering/search, command history, and Geyser/Floodgate provisioning.

### Added

- Emerald-themed AI Summary panel.
- `Markdig.Wpf` markdown rendering.
- Severity filters for console logs.
- Regex console search.
- Command history navigation and command suggestions.
- CI/CD workflow hardening.
- Single-source versioning sync across project files, changelog, and CI.

### Fixed

- Geyser/Floodgate provisioning migrated to Modrinth builds to resolve Fabric download failures.
- Partial plugin download cleanup and retry behavior improved.

### Reasoning

This release improves observability. Servers fail in noisy ways, so log filtering, regex search, and readable summaries help users find the actual problem instead of drowning in console soup.

### Upgrade Impact

- AI and console UI should be easier to scan.
- Cross-play setup should be more reliable for Fabric servers.

---

## v1.4.0 - Bedrock & PocketMine Protocol Expansion

### Summary

v1.4.0 expands PocketMC from Java-only management into multi-protocol hosting with native Bedrock Dedicated Server and PocketMine-MP support.

### Added

- Bedrock Dedicated Server version discovery via community manifest.
- One-click BDS installation.
- Bedrock `.mcpack` and `.mcaddon` import support.
- Automatic `world_behavior_packs.json` and `world_resource_packs.json` updates.
- UWP loopback helper for Minecraft for Windows local connections.
- PocketMine-MP support with app-managed PHP runtime.
- Poggit marketplace support for PocketMine plugins.
- PocketMine generator sanity patching.
- Engine-aware Addons tab filtering.
- Native Bedrock/PocketMine dashboard adjustments.

### Fixed

- BDS provisioning now uses safe temp paths to reduce access-denied failures.
- Native Bedrock duplicate Bedrock IP row is suppressed.

### Reasoning

This is a milestone release because it changes the product boundary. PocketMC stops being only a Java server helper and becomes a local-first Minecraft server manager across Java, Bedrock, and PocketMine.

### Upgrade Impact

- Users can create native Bedrock and PocketMine servers.
- Bedrock local connections may require UWP loopback exemption.

---

## v1.3.0 - Architectural Hardening & Observability

### Summary

v1.3.0 is an architecture and reliability hardening release. It adds artifact verification, graceful shutdown, PII scrubbing, RCON-based command execution, dependency health monitoring, support bundles, and major modular refactoring.

### Added

- SHA1/SHA256 artifact verification in downloads.
- Graceful app/server lifecycle shutdown via `IApplicationLifecycleService`.
- Extended PII scrubbing for logs before AI summaries.
- Managed RCON client for Java process interaction.
- Dependency health monitor for Adoptium, Playit.gg, and Modrinth.
- Support bundle export with masked sensitive properties.
- External backup replication directory support.
- `IAssetProvider` abstraction.
- Moved resource monitoring into the instance/services architecture.

### Reasoning

This release attacks prototype debt. It adds integrity checks, safer shutdowns, less private log leakage, and better failure diagnostics, which are the boring pieces users only notice when missing.

### Upgrade Impact

- Shutdown behavior becomes more deliberate.
- Diagnostics and support bundles should make bug reports easier to act on.
- Internal structure changes affect maintainers more than users.

---

## v1.2.5 - Health Monitoring, Support Bundles & Lifecycle Safety

### Summary

v1.2.5 adds dependency health checks, external backup replication, support bundle export, RCON support, hash verification, PII redaction, graceful shutdown extraction, and update-check initialization.

### Changed

- Added dependency health monitoring.
- Added external backup replication.
- Added support bundle export.
- Added RCON client, download hash verification, and PII redaction.
- Extracted graceful shutdown into `IApplicationLifecycleService`.
- Moved `ResourceMonitorService` and introduced `IAssetProvider`.
- Initialized update checks on startup.
- Refreshed settings button state and added pack icon support.

### Reasoning

This release laid the foundation for the larger v1.3.0 hardening work. It begins separating operational concerns from UI glue.

### Upgrade Impact

- Diagnostics and update status become more visible.
- Backup replication can be configured separately from local backups.

---

## v1.2.4 - Community, Release & Packaging Guidance

### Summary

v1.2.4 improves community/support links and release packaging documentation.

### Changed

- Added Discord/community support references in the app and README.
- Added release and packaging guidance for Velopack.
- Updated release workflow notes to use `RELEASE_PAT`.

### Reasoning

A project needs a support path and a repeatable release process. Otherwise every release becomes a hand-crafted little disaster.

### Upgrade Impact

- Mainly documentation and release process impact.

---

## v1.2.3 - Velopack Migration

### Summary

v1.2.3 migrates installation and update packaging from Inno Setup to Velopack.

### Added

- Velopack startup bootstrapping before WPF startup.
- Automatic update checks in the shell layer.
- GitHub Actions publishing for `win-x64` output.
- Velopack packing and release asset upload.

### Removed

- Inno Setup installer script.

### Reasoning

Velopack gives the app a cleaner path toward auto-updates and modern Windows distribution. Inno Setup is fine until update UX matters, and then the suffering begins.

### Upgrade Impact

- Packaging and update behavior changes.
- Maintainers should use the Velopack release workflow instead of the removed Inno script.

---

## v1.0.0 - Initial Stable Release

### Summary

v1.0.0 is the initial stable PocketMC Windows desktop release.

### Included

- Core WPF desktop shell.
- Minecraft server instance management.
- Dashboard, console, settings, and backup pages.
- Java setup support.
- Playit.gg tunneling integration.
- Windows notifications and basic app shell behavior.

### Reasoning

This version established the core product loop: create a server, run it locally, manage it from a GUI, and expose it through tunneling without requiring users to live inside a terminal like it is a character-building exercise.

### Upgrade Impact

- Baseline release.
