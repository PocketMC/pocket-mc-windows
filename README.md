<div align="center">

<table border="0" cellpadding="16">
  <tr>
    <td align="center" width="200">
      <img src="docs/assets/branding/logo.png" alt="PocketMC" width="180" />
    </td>
    <td align="center">
      <h1 style="border: none; margin-bottom: 10px;">PocketMC Windows</h1>
      <p><b>Local-first Minecraft server management, without the terminal mess.</b></p>
      <p>Create, run, update, monitor, back up, and share Minecraft Java and Bedrock servers from one native Windows desktop app.</p>
      <a href="https://github.com/PocketMC/pocket-mc-windows/actions"><img src="https://img.shields.io/github/actions/workflow/status/PocketMC/pocket-mc-windows/ci.yml?branch=master&style=flat-square&logo=github" alt="Build" /></a>
      <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" /></a>
      <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Windows-10%201809%2B%20%2F%2011-0078D4?style=flat-square&logo=windows" alt="Windows 10 1809+ / Windows 11" /></a>
      <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22C55E?style=flat-square" alt="MIT License" /></a>
      <a href="https://github.com/PocketMC/pocket-mc-windows/releases"><img src="https://img.shields.io/github/v/release/PocketMC/pocket-mc-windows?style=flat-square" alt="Release" /></a>
      <a href="https://discord.gg/mWdMr8Mc2m"><img src="https://img.shields.io/badge/Discord-Join-%235865F2?style=flat-square&logo=discord" alt="Discord" /></a>
      <a href="https://www.reddit.com/r/PocketMC/"><img src="https://img.shields.io/badge/Reddit-r%2FPocketMC-%23FF4500?style=flat-square&logo=reddit" alt="Reddit" /></a>
      <a href="https://www.youtube.com/@OfficialPocketMC"><img src="https://img.shields.io/badge/YouTube-Watch-%23FF0000?style=flat-square&logo=youtube" alt="YouTube" /></a>
      <a href="https://www.buymeacoffee.com/sahaj33"><img src="https://img.shields.io/badge/Donate-BMC-%23FFDD00?style=flat-square&logo=buy-me-a-coffee&logoColor=black" alt="Buy Me A Coffee" /></a>
      <br><br>
      <a href="https://github.com/PocketMC/pocket-mc-windows/releases/latest/download/PocketMC-win-Setup.exe"><img src="https://img.shields.io/badge/Download-Setup.exe-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Download Setup.exe" /></a>
    </td>
  </tr>
</table>

<br>

<video src="https://github.com/user-attachments/assets/e4056674-f1e9-4cfc-be33-96da10109dc1" width="880" autoplay="autoplay"></video>

<br><br>

</div>

---

PocketMC is a native WPF/.NET 8 desktop app for hosting a local Minecraft server. It manages software downloads, isolated instances, app-managed Java and PHP runtimes, startup and shutdown, live metrics, logs, players, backups, cloud replication, add-ons, Playit.gg tunnels and a remote web dashboard all in one polished Windows UI.

Your servers reside on your machine, in the app root you select. PocketMC is not a cloud host, a Minecraft client launcher or a Linux web panel masquerading as a Windows application.

<br>

## Supported server software

<table border="0" align="center" cellpadding="8">
  <tr align="center">
    <td><img src="docs/assets/icons/vanilla.png" alt="Vanilla Java" height="60" /></td>
    <td><img src="docs/assets/icons/papermc.png" alt="Paper" height="60" /></td>
    <td><img src="docs/assets/icons/fabric.png" alt="Fabric" height="60" /></td>
    <td><img src="docs/assets/icons/forge.png" alt="Forge" height="60" /></td>
    <td><img src="docs/assets/icons/neoforge.png" alt="NeoForge" height="60" /></td>
    <td><img src="docs/assets/icons/bds.png" alt="Bedrock Dedicated Server" height="60" /></td>
    <td><img src="docs/assets/icons/pocketmine-mp.png" alt="PocketMine-MP" height="60" /></td>
  </tr>
  <tr align="center" valign="top">
    <td><sub><b>Vanilla Java</b></sub></td>
    <td><sub><b>Paper</b></sub></td>
    <td><sub><b>Fabric</b></sub></td>
    <td><sub><b>Forge</b></sub></td>
    <td><sub><b>NeoForge</b></sub></td>
    <td><sub><b>Bedrock (BDS)</b></sub></td>
    <td><sub><b>PocketMine-MP</b></sub></td>
  </tr>
</table>

<br>

## Features

<details>
<summary><b> &nbsp;Instance lifecycle</b></summary>
<br>

- Create isolated instances from the UI: server type, version, loader, seed, world type, gamemode, difficulty, player limit, EULA, custom world import.
- Start, stop, restart, and hard-kill servers from the dashboard or tray.
- Graceful shutdown via RCON with a console-input fallback.
- Crash detection with sanitized output capture and optional auto-restart with backoff.
- Per-instance port conflict checks before launch.
- Geyser-enabled instances patch Bedrock listener ports before starting.
- Power management: Windows sleep is automatically prevented while a server is running.
- Per-instance auto-start on app launch.

</details>

<details>
<summary><b> &nbsp;Managed runtimes — zero Java headaches</b></summary>
<br>

- App-local Java provisioning through Adoptium: **Java 8, 11, 17, 21, and 25**.
- Automatic version selection based on Minecraft version:
  - 1.0 – 1.16.4 → Java 8
  - 1.16.5 – 1.17.1 → Java 11
  - 1.18 – 1.20.4 → Java 17
  - 1.20.5 – 1.21.1 → Java 21
  - 1.21.2+ → Java 25
- Java 25 downloaded in the background automatically. Older versions pulled only when a selected Minecraft version requires them.
- Custom Java path override for advanced users.
- PocketMine-MP uses an app-managed PHP 8.2 PM5 runtime, nothing to install globally.
- All downloads use retries, partial-file cleanup, safe promotion, and hash verification where upstream hashes exist.
- Missing bundled runtimes prompt for user confirmation before download or repair.

</details>

<details>
<summary><b> &nbsp;Dashboard, metrics, and console</b></summary>
<br>

- Live CPU / RAM / player metrics for running instances.
- Dynamic Geyser/Floodgate and Simple Voice Chat badges.
- Console output is buffered, sanitized, classified, and persisted across sessions.
- Stopped or crashed servers can still open a read-only last-session log view.
- Large logs are tailed, no loading a 500 MB log file into the UI.
- Console tools: filter, search, command input, session history.

</details>

<details>
<summary><b> &nbsp;Public access via Playit.gg</b></summary>
<br>

- Built-in Playit agent provisioning and setup flow (simplified two-step wizard).
- Automatic tunnel discovery for matching instance ports.
- Auto-creates tunnels for Java, Bedrock, Geyser, PocketMine, and Simple Voice Chat when possible.
- Interactive Ports Map with local ports, public addresses, roles, and live tunnel status.
- Clear states for offline agents, invalid tokens, unclaimed agents, pending allocations, and account limits.

</details>

<details>
<summary><b> &nbsp;Java & Bedrock cross-play</b></summary>
<br>

- Geyser and Floodgate are provisioned automatically during server creation for supported Java server types.
- If cross-play setup fails for a given version or loader, the server is still created and cross-play can be added later from Server Settings.
- Simple Voice Chat is detected via JAR presence in `mods/` or `plugins/`, config file, or log parsing, and its port is automatically included in the Playit tunnel map.

</details>

<details>
<summary><b> &nbsp;Mods, plugins, packs, and add-ons</b></summary>
<br>

- Modrinth browser for server-side mods, plugins, and modpacks.
- CurseForge browser via your own API key.
- Poggit integration for PocketMine plugins.
- Map browser for world downloads.
- Dependency resolution where provider metadata supports it.
- Java metadata scanning: Fabric, Quilt, Forge, NeoForge, Bukkit/Paper plugin metadata, icons.
- Add-on inventory: display names, versions, loader types, side-support labels, warnings, update status.
- Enable/disable add-ons without manually renaming files.
- Manual add-on update installation with compatibility checks.
- Bedrock `.mcpack`, `.mcaddon`, `.zip` ingestion with manifest parsing and automatic pack registration.
- Marketplace installs validate staged files, extensions, filenames, hashes, dependency failures, and unsafe modpack overrides.

</details>

<details>
<summary><b> &nbsp;Backups, cloud replication, and safe restore</b></summary>
<br>

- Manual and scheduled backups with retention pruning.
- Live-server backup: RCON save sync first, console save-command fallback.
- Locked and unsafe files (e.g. `session.lock`) skipped gracefully.
- Manifest entries include metadata, size deltas, checksums, and failure state.
- Cloud upload to **Google Drive**, **Dropbox**, and **OneDrive** with upload history and retention handling.
- Per-instance custom local and external-folder backup directories.
- Restore: checks ZIP integrity, validates checksums, extracts to staging, verifies world structure, backs up the current world, rolls back if apply fails.

</details>

<details>
<summary><b> &nbsp;Player and server controls</b></summary>
<br>

- Online player parsing for Java, Bedrock, and PocketMine log formats.
- Ban, op, whitelist, and runtime command actions from the player management page.
- Whitelist support for `whitelist.json` (Java), `allowlist.json` (BDS), and `white-list.txt` (PocketMine).
- Runtime setting application path for safer config changes while a server is running.

</details>

<details>
<summary><b> &nbsp;Instance version updates</b></summary>
<br>

- Offline update workflow with target version selection and Java runtime requirement checks.
- Compatibility warnings for tracked marketplace add-ons.
- Staged artifact replacement with supported add-on migration.
- Pre-update snapshots, journals, and locks for safe application.
- Rollback when an update fails mid-apply.

</details>

<details>
<summary><b> &nbsp;Instance import & export</b></summary>
<br>

- Export a complete instance (server files, world, add-ons, metadata) to a portable archive.
- Import an exported archive to restore or share an instance.
- Cancellation support throughout the import/export flow.
- Java exports include `eula.txt`, server properties, whitelist, ops, bans, and other root files.

</details>

<details>
<summary><b> &nbsp;Remote Control Dashboard</b></summary>
<br>

- Browser-based dashboard for managing servers from any device with light and dark mode support.
- Start, stop, restart, view the live console, send commands, and manage players remotely.
- Secure internet exposure via built-in **Cloudflare Quick Tunnels** or **Playit.gg HTTPS tunnels**, or restrict to LAN only.
- QR code generated for both local and public URLs, scan to open on mobile instantly.
- Configurable host port (default: 25580) and per-feature access controls (console commands, player actions).
- Password-protected sessions with token lifetime configuration and active session visibility.
- **Discord integration:** link your Discord account and receive the public remote control URL directly in your DMs via the PocketMC Discord bot when a tunnel starts.
- Pairing authorization flow with revocable device sessions.

</details>

<details>
<summary><b> &nbsp;AI session summaries</b></summary>
<br>

Generates structured session summaries from server logs using your own API key or a local endpoint.

Supported providers: **Google Gemini**, **OpenAI**, **Anthropic Claude**, **Mistral AI**, **Groq**, **Ollama / compatible local endpoint**

Logs are preprocessed and sanitized (IPs, emails) before being sent. You own the API key and the provider choice. A desktop toast notification is shown when a summary completes, with a direct link to open it.

</details>

<details>
<summary><b> &nbsp;Diagnostics</b></summary>
<br>

- Dependency health monitoring for required services and runtimes.
- Port diagnostics snapshots with conflict classification and recovery recommendations.
- Diagnostic reporting service for structured issue capture.

</details>

<details>
<summary><b> &nbsp;Windows integration and app polish</b></summary>
<br>

- Toast notifications for server online, Playit agent connection, Remote Control availability, and AI summary completion — each individually toggleable in App Settings.
- Tray integration, minimize-to-tray, and start-with-Windows.
- Start minimized to tray on launch option.
- Single-instance enforcement: launching the app a second time brings the existing window to the foreground.
- `pocketmc://` custom URI protocol for deep linking (e.g., Discord bot integration).
- Velopack update integration with automatic updates.
- Windows UWP loopback helper for Minecraft Bedrock local access via `CheckNetIsolation.exe`.
- Mica, Acrylic, Wallpaper Blur, custom background images, custom accent colors, and theme settings.
- Discord Rich Presence: server type, version, player count, uptime, download button.

</details>

<br>

## Screenshots

### Desktop App

| | |
| :---: | :---: |
| **Dashboard**<br><img src="docs/assets/screenshots/screenshot-dashboard.png" width="420" alt="Dashboard" /> | **Console**<br><img src="docs/assets/screenshots/screenshot-console.png" width="420" alt="Console" /> |
| **Server Settings**<br><img src="docs/assets/screenshots/server-settings.png" width="420" alt="Server Settings" /> | **Backups**<br><img src="docs/assets/screenshots/screenshot-backups.png" width="420" alt="Backups" /> |
| **Mod Marketplace**<br><img src="docs/assets/screenshots/mod-marketplace.png" width="420" alt="Mod Marketplace" /> | **Mod Management**<br><img src="docs/assets/screenshots/screenshot-mod-management.png" width="420" alt="Mod Management" /> |
| **Plugin Management**<br><img src="docs/assets/screenshots/screenshot-plugins.png" width="420" alt="Plugin Management" /> | **Java Runtimes**<br><img src="docs/assets/screenshots/java-runtimes.png" width="420" alt="Java Runtimes" /> |
| **Public Tunnels**<br><img src="docs/assets/screenshots/tunnels.png" width="420" alt="Public Tunnels" /> | **Interactive Ports Map**<br><img src="docs/assets/screenshots/ports-map.png" width="420" alt="Interactive Ports Map" /> |
| **Remote Control**<br><img src="docs/assets/screenshots/remote-control.png" width="420" alt="Remote Control" /> | **App Settings**<br><img src="docs/assets/screenshots/app-settings.png" width="420" alt="App Settings" /> |
| **About Page**<br><img src="docs/assets/screenshots/about.png" width="420" alt="About Page" /> | |

### Themes

Custom background images and accent colors with different themes options, customise PocketMC like your imagination.

<img src="docs/assets/screenshots/themes-showcase.png" width="880" alt="PocketMC themes showcase — different accent colors and custom background images" />

### Remote Web Dashboard

| Welcome | Login | Instances |
| :---: | :---: | :---: |
| <img src="docs/assets/screenshots/mobile-welcome.png" width="280" alt="Web Dashboard - Welcome" /> | <img src="docs/assets/screenshots/mobile-login.png" width="280" alt="Web Dashboard - Login" /> | <img src="docs/assets/screenshots/mobile-instances.png" width="280" alt="Web Dashboard - Instances" /> |
| **Home** | **Console** | **Players** |
| <img src="docs/assets/screenshots/mobile-home.png" width="280" alt="Web Dashboard - Home" /> | <img src="docs/assets/screenshots/mobile-console.png" width="280" alt="Web Dashboard - Console" /> | <img src="docs/assets/screenshots/mobile-players.png" width="280" alt="Web Dashboard - Players" /> |

<br>

## Installation

Download [`PocketMC-win-Setup.exe`](https://github.com/PocketMC/pocket-mc-windows/releases/latest) from the latest release and run it.

- Installs per-user, no admin rights required.
- .NET 8 Desktop Runtime is required and prompted automatically if missing.
- Java and PHP do not need to be globally installed. PocketMC provisions its own runtimes.
- Updates are handled automatically through Velopack.

<br>

## Quick start

```
1.  Pick an app root folder      →  Stores instances, runtimes, backups, logs, and settings.
2.  Create an instance           →  Family, version, world settings, player limit, EULA.
3.  Start the server             →  Connect via localhost or LAN IP.
4.  (Optional) Add public access →  Link Playit.gg — PocketMC resolves or creates tunnels.
5.  (Optional) Install content   →  Modrinth, CurseForge, Poggit, or Bedrock add-on import.
6.  (Optional) Protect the world →  Manual / scheduled / external / cloud backups.
7.  (Optional) Remote access     →  Enable Remote Control — share the URL or scan the QR code.
8.  (Optional) Diagnose          →  Diagnostics, persistent logs, AI session summaries.
```

<br>

## System requirements

| | |
|---|---|
| **OS** | Windows 10 build 17763 (1809) or Windows 11 |
| **Architecture** | x64 |
| **RAM** | 4 GB minimum — 8 GB+ recommended |
| **Runtime** | .NET 8 Desktop Runtime |
| **Internet** | Required for first-run downloads, marketplace, cloud backups, updates, Playit.gg |

<br>

## Build from source

<details>
<summary>Expand</summary>
<br>

**Prerequisites:** Windows 10 1809+ or Windows 11 · .NET 8 SDK · Visual Studio 2022 with *Desktop development with .NET*, or JetBrains Rider

```bash
git clone https://github.com/PocketMC/pocket-mc-windows.git
cd pocket-mc-windows

dotnet restore
dotnet build
dotnet test
dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
```

For packaging, PocketMC uses Velopack. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the release packaging flow.


</details>
<br>

## Project structure

<details>
<summary>Expand</summary>
<br>

| Path | Purpose |
|------|---------|
| `PocketMC.Desktop/Composition` | Dependency injection and service registration |
| `PocketMC.Desktop/Core` | Shared interfaces, MVVM utilities, and presentation primitives |
| `PocketMC.Desktop/Infrastructure` | WPF/Windows integrations, dialogs, dispatcher, notifications, updates, loopback helper, single-instance enforcement, protocol registration, power management, filesystem/process helpers, and security utilities |
| `PocketMC.Desktop/Features/Dashboard` | Dashboard cards, metrics, actions, instance state, and quick controls |
| `PocketMC.Desktop/Features/InstanceCreation` | New instance wizard, version loading, EULA flow, world import, Geyser setup, and server downloads |
| `PocketMC.Desktop/Features/Instances` | Metadata, registry, lifecycle, launch config, providers, backups, updates, import/export, worlds, runtime config, and process management |
| `PocketMC.Desktop/Features/Java` | Java resolution, Adoptium provisioning, runtime validation, and custom path support |
| `PocketMC.Desktop/Features/CloudBackups` | Google Drive, Dropbox, OneDrive integrations, OAuth, upload history, retention, and path safety |
| `PocketMC.Desktop/Features/Tunnel` | Playit agent lifecycle, API client, provisioning, discovery, auto-creation, and ports map UI |
| `PocketMC.Desktop/Features/Networking` | Port checks, roles, Simple Voice Chat detection and config patching, tunnel-aware preflight, and diagnostic snapshots |
| `PocketMC.Desktop/Features/Marketplace` | Modrinth, CurseForge, Poggit, map browser, modpack parsing, dependency resolution, and install hardening |
| `PocketMC.Desktop/Features/Mods` | Java add-on inventory, toggles, update checks, Bedrock add-on install/uninstall, pack registration |
| `PocketMC.Desktop/Features/Console` | Filtering, sanitization, classification, persistent history, and display behavior |
| `PocketMC.Desktop/Features/Players` | Parsing, bans, operators, whitelist/allowlist, and player UI |
| `PocketMC.Desktop/Features/Diagnostics` | Dependency health monitoring, port diagnostics, and reporting |
| `PocketMC.Desktop/Features/RemoteControl` | Web dashboard host (ASP.NET Core), REST API, WebSocket console, Cloudflare/Playit tunnel providers, QR codes, Discord integration, and settings UI |
| `PocketMC.Desktop/Features/Settings` | App/server settings, runtime application, cloud config, appearance, notifications, and AI config |
| `PocketMC.Desktop/Features/Shell` | Main shell, navigation, tray, startup options, visual state, and coordination |
| `PocketMC.Desktop/Features/Intelligence` | AI API client (Gemini, OpenAI, Claude, Mistral, Groq, Ollama), session summarization, log preprocessing, markdown rendering, summary storage |
| `PocketMC.Desktop/Features/Setup` | First-run root directory setup, App Settings page, Java Setup page |
| `PocketMC.Desktop.Tests` | xUnit/Moq tests for lifecycle, process, settings, marketplace, tunnel, console, backups, cloud, add-ons, players, startup, and VM logic |

</details>

<br>

## Community & License

**Discord** — [discord.gg/mWdMr8Mc2m](https://discord.gg/mWdMr8Mc2m)  
**Reddit** — [r/PocketMC](https://www.reddit.com/r/PocketMC/)  
**YouTube** — [Watch Tutorials](https://www.youtube.com/@OfficialPocketMC)  
**MIT** © 2026 PocketMC Contributors — see [LICENSE](LICENSE)

<br>

<div align="center">

<a href="https://www.buymeacoffee.com/sahaj33" target="_blank">
  <img src="https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png" alt="Buy Me A Coffee" height="38" />
</a>
<br><br>
<a href="https://deepwiki.com/PocketMC/pocket-mc-windows">
  <img src="https://deepwiki.com/badge.svg" alt="DeepWiki" />
</a>

<br><br>

<sub>PocketMC runs servers on your Windows PC. It does not provide cloud hosting.<br>
Playit.gg, CurseForge, cloud providers, AI summaries, and Discord integration require their own accounts or API keys.</sub>

</div>
