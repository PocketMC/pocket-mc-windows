<div align="center">

<table border="0" cellpadding="16">
  <tr>
    <td align="center" width="200">
      <img src="docs/assets/logo.png" alt="PocketMC" width="180" />
    </td>
    <td align="center">
      <h1 style="border: none; margin-bottom: 10px;">PocketMC Windows</h1>
      <p><b>Local-first Minecraft server management, without the terminal mess.</b></p>
      <p>Create, run, update, monitor, back up, and share Minecraft Java and Bedrock servers from one native Windows desktop app.</p>
      <a href="https://github.com/PocketMC/pocket-mc-windows/actions"><img src="https://img.shields.io/github/actions/workflow/status/PocketMC/pocket-mc-windows/production-build.yml?branch=master&style=flat-square&logo=github" alt="Build" /></a>
      <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" /></a>
      <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Windows-10%201809%2B%20%2F%2011-0078D4?style=flat-square&logo=windows" alt="Windows 10 1809+ / Windows 11" /></a>
      <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22C55E?style=flat-square" alt="MIT License" /></a>
      <a href="https://github.com/PocketMC/pocket-mc-windows/releases"><img src="https://img.shields.io/github/v/release/PocketMC/pocket-mc-windows?style=flat-square" alt="Release" /></a>
      <a href="https://discord.gg/h27uNCaxPH"><img src="https://img.shields.io/badge/Discord-Join-%235865F2?style=flat-square&logo=discord" alt="Discord" /></a>
      <a href="https://www.reddit.com/r/PocketMC/"><img src="https://img.shields.io/badge/Reddit-r%2FPocketMC-%23FF4500?style=flat-square&logo=reddit" alt="Reddit" /></a>
      <a href="https://www.youtube.com/@OfficialPocketMC"><img src="https://img.shields.io/badge/YouTube-Watch-%23FF0000?style=flat-square&logo=youtube" alt="YouTube" /></a>
      <a href="https://www.buymeacoffee.com/sahaj33"><img src="https://img.shields.io/badge/Donate-BMC-%23FFDD00?style=flat-square&logo=buy-me-a-coffee&logoColor=black" alt="Buy Me A Coffee" /></a>
    </td>
  </tr>
</table>


<br>
<video src="https://github.com/user-attachments/assets/1555998d-8f5c-4371-9caa-cf751f6b4f57" width="880" autoplay="autoplay"></video>
<br>

<br>

</div>

---

PocketMC is a native WPF/.NET 8 desktop app for local Minecraft server hosting; software downloads, isolated instances, app-managed Java/PHP runtimes, startup/shutdown, live metrics, logs, players, backups, cloud replication, add-ons, and Playit.gg tunnels, all in one polished Windows UI.

Your servers live on your machine, in the app root you choose. PocketMC is not a cloud host, not a Minecraft client launcher, and not a Linux web panel in a Windows costume.

<br>

## What changed without PocketMC vs. with it

| Before | After |
|--------|-------|
| Hunt the right JAR. Guess the Java version. Edit configs by hand. | Server software and runtimes managed entirely by the app. |
| Scattered terminal scripts and folders across the disk. | Isolated instances under one root — clean, auditable, portable. |
| Manual Playit tunnel setup on every machine. | Built-in agent provisioning, tunnel creation, and live status. |
| Backups when someone remembers to run a script. | Manual, scheduled, external-folder, and cloud backup flows. |
| Replacing server files and hoping the update holds. | Staged updates with planning, snapshots, journals, and rollback. |
| Staring at raw logs until something makes sense. | Persistent history, diagnostics, and optional AI session summaries. |

<br>

## Supported server software

<table border="0" align="center" cellpadding="8">
  <tr align="center">
    <td><img src="docs/assets/vanilla.png" alt="Vanilla Java" height="60" /></td>
    <td><img src="docs/assets/papermc.png" alt="Paper" height="60" /></td>
    <td><img src="docs/assets/fabric.png" alt="Fabric" height="60" /></td>
    <td><img src="docs/assets/forge.png" alt="Forge" height="60" /></td>
    <td><img src="docs/assets/neoforge.png" alt="NeoForge" height="60" /></td>
    <td><img src="docs/assets/bds.png" alt="Bedrock Dedicated Server" height="60" /></td>
    <td><img src="docs/assets/pocketmine-mp.png" alt="PocketMine-MP" height="60" /></td>
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

<details open>
<summary><b>🖥️ &nbsp;Instance lifecycle</b></summary>
<br>

- Create isolated instances from the UI: server type, version, loader, seed, world type, gamemode, difficulty, player limit, EULA, custom world import.
- Start, stop, restart, and hard-kill servers from the dashboard or tray.
- Graceful shutdown via RCON with a console-input fallback.
- Crash detection with sanitized output capture and optional auto-restart with backoff.
- Per-instance port conflict checks before launch.
- Geyser-enabled instances patch Bedrock listener ports before starting.

</details>

<details>
<summary><b>☕ &nbsp;Managed runtimes — zero Java headaches</b></summary>
<br>

- App-local Java provisioning through Adoptium: **Java 8, 11, 17, 21, and 25**.
- Java 25 downloaded by default in the background. Older versions pulled only when a selected Minecraft version requires them.
- Custom Java path override for advanced users.
- PocketMine-MP uses an app-managed PHP 8.2 PM5 runtime — nothing to install globally.
- All downloads use retries, partial-file cleanup, safe promotion, and hash verification where upstream hashes exist.

</details>

<details>
<summary><b>📊 &nbsp;Dashboard, metrics, and console</b></summary>
<br>

- Live CPU / RAM / player metrics for running instances.
- Dynamic Geyser/Floodgate and Simple Voice Chat badges.
- Console output is buffered, sanitized, classified, and persisted across sessions.
- Stopped or crashed servers can still open a read-only last-session log view.
- Large logs are tailed — no loading a 500 MB log file into the UI.
- Console tools: filter, search, command input, session history.

</details>

<details>
<summary><b>🌐 &nbsp;Public access via Playit.gg</b></summary>
<br>

- Built-in Playit agent provisioning and setup flow.
- Automatic tunnel discovery for matching instance ports.
- Auto-creates tunnels for Java, Bedrock, Geyser, PocketMine, and Simple Voice Chat when possible.
- Interactive Ports Map with local ports, public addresses, roles, and live tunnel status.
- Clear states for offline agents, invalid tokens, unclaimed agents, pending allocations, and account limits.

</details>

<details>
<summary><b>🧩 &nbsp;Mods, plugins, packs, and add-ons</b></summary>
<br>

- Modrinth browser for server-side mods, plugins, and modpacks.
- CurseForge browser via your own API key.
- Poggit integration for PocketMine plugins.
- Dependency resolution where provider metadata supports it.
- Java metadata scanning: Fabric, Quilt, Forge, NeoForge, Bukkit/Paper plugin metadata, icons.
- Add-on inventory: display names, versions, loader types, side-support labels, warnings, update status.
- Enable/disable add-ons without manually renaming files.
- Bedrock `.mcpack`, `.mcaddon`, `.zip` ingestion with manifest parsing and automatic pack registration.
- Marketplace installs validate staged files, extensions, filenames, hashes, dependency failures, and unsafe modpack overrides.

</details>

<details>
<summary><b>💾 &nbsp;Backups, cloud replication, and safe restore</b></summary>
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
<summary><b>👥 &nbsp;Player and server controls</b></summary>
<br>

- Online player parsing for Java, Bedrock, and PocketMine log formats.
- Ban, op, whitelist, and runtime command actions from the player management page.
- Whitelist support for `whitelist.json` (Java), `allowlist.json` (BDS), and `white-list.txt` (PocketMine).
- Runtime setting application path for safer config changes while a server is running.

</details>

<details>
<summary><b>🔄 &nbsp;Instance version updates</b></summary>
<br>

- Offline update workflow with target version selection and Java runtime requirement checks.
- Compatibility warnings for tracked marketplace add-ons.
- Staged artifact replacement with supported add-on migration.
- Pre-update snapshots, journals, and locks for safe application.
- Rollback when an update fails mid-apply.

</details>

<details>
<summary><b>🖱️ &nbsp;Remote Control Dashboard</b></summary>
<br>

- Browser-based dashboard for managing servers from any device.
- Start, stop, restart, view the live console, send commands, and manage players remotely.
- Secure internet exposure via built-in **Cloudflare Quick Tunnels** or **Playit.gg HTTPS tunnels**, or restrict to LAN only.
- Configurable host port and per-feature access controls to prevent unauthorized actions.

</details>

<details>
<summary><b>🤖 &nbsp;AI session summaries</b></summary>
<br>

Generates structured session summaries from server logs using your own API key or a local endpoint.

Supported providers: **Google Gemini**, **OpenAI**, **Anthropic Claude**, **Mistral AI**, **Groq**, **Ollama / compatible local endpoint**

Logs are preprocessed and sanitized (IPs, emails) before being sent. You own the API key and the provider choice.

</details>

<details>
<summary><b>🪟 &nbsp;Windows integration and app polish</b></summary>
<br>

- Toast notifications, tray integration, minimize-to-tray, and start-with-Windows.
- Start minimized to tray on launch option.
- Velopack update integration.
- Windows UWP loopback helper for Minecraft Bedrock local access via `CheckNetIsolation.exe`.
- Mica, Acrylic, Wallpaper Blur, custom background image, and theme settings.
- Discord Rich Presence: server type, version, player count, uptime, download button.

</details>

<br>

## Screenshots

### Desktop App

| | |
| :---: | :---: |
| **Dashboard**<br><img src="docs/assets/screenshot-dashboard.png" width="420" alt="Dashboard" /> | **Console**<br><img src="docs/assets/screenshot-console.png" width="420" alt="Console" /> |
| **Server Settings**<br><img src="docs/assets/server-settings.png" width="420" alt="Server Settings" /> | **Backups**<br><img src="docs/assets/screenshot-backups.png" width="420" alt="Backups" /> |
| **Mod Marketplace**<br><img src="docs/assets/mod-marketplace.png" width="420" alt="Mod Marketplace" /> | **Public Tunnels**<br><img src="docs/assets/tunnels.png" width="420" alt="Public Tunnels" /> |
| **Interactive Ports Map**<br><img src="docs/assets/ports-map.png" width="420" alt="Interactive Ports Map" /> | **Java Runtimes**<br><img src="docs/assets/java-runtimes.png" width="420" alt="Java Runtimes" /> |
| **Remote Control**<br><img src="docs/assets/remote-control.png" width="420" alt="Remote Control" /> | **App Settings**<br><img src="docs/assets/app-settings.png" width="420" alt="App Settings" /> |

### Remote Web Dashboard for your friend

|Dashboard | Home | Console | Players |
| :---: | :---: | :---: | :---: |
| <img src="docs/assets/mobile-instances.png" width="280" alt="Web Dashboard - Players" /> | <img src="docs/assets/mobile-home.png" width="280" alt="Web Dashboard - Home" /> | <img src="docs/assets/mobile-console.png" width="280" alt="Web Dashboard - Console" /> | <img src="docs/assets/mobile-players.png" width="280" alt="Web Dashboard - Players" /> |

<br>

## Installation

Download [`Setup.exe`](https://github.com/PocketMC/pocket-mc-windows/releases/latest) from the latest release and run it.

- Installs per-user — no admin rights required.
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
7.  (Optional) Diagnose          →  Diagnostics, persistent logs, AI summaries.
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

<br>

## Project structure

<details>
<summary>Expand</summary>
<br>

| Path | Purpose |
|------|---------|
| `PocketMC.Desktop/Composition` | Dependency injection and service registration |
| `PocketMC.Desktop/Core` | Shared interfaces, MVVM utilities, and presentation primitives |
| `PocketMC.Desktop/Infrastructure` | WPF/Windows integrations, dialogs, dispatcher, notifications, updates, loopback helper, filesystem/process helpers, and security utilities |
| `PocketMC.Desktop/Features/Dashboard` | Dashboard cards, metrics, actions, instance state, and quick controls |
| `PocketMC.Desktop/Features/InstanceCreation` | New instance wizard, version loading, EULA flow, world import, Geyser setup, and server downloads |
| `PocketMC.Desktop/Features/Instances` | Metadata, registry, lifecycle, launch config, providers, backups, updates, worlds, runtime config, and process management |
| `PocketMC.Desktop/Features/Java` | Java resolution, Adoptium provisioning, runtime validation, and custom path support |
| `PocketMC.Desktop/Features/CloudBackups` | Google Drive, Dropbox, OneDrive integrations, OAuth, upload history, retention, and path safety |
| `PocketMC.Desktop/Features/Tunnel` | Playit agent lifecycle, API client, provisioning, discovery, auto-creation, and ports map UI |
| `PocketMC.Desktop/Features/Networking` | Port checks, roles, Simple Voice Chat parsing, tunnel-aware preflight, and diagnostic snapshots |
| `PocketMC.Desktop/Features/Marketplace` | Modrinth, CurseForge, Poggit, modpack parsing, dependency resolution, and install hardening |
| `PocketMC.Desktop/Features/Mods` | Java add-on inventory, toggles, update checks, Bedrock add-on install/uninstall, pack registration |
| `PocketMC.Desktop/Features/Console` | Filtering, sanitization, classification, persistent history, and display behavior |
| `PocketMC.Desktop/Features/Players` | Parsing, bans, operators, whitelist/allowlist, and player UI |
| `PocketMC.Desktop/Features/Diagnostics` | Dependency health, port diagnostics, and reporting |
| `PocketMC.Desktop/Features/Settings` | App/server settings, runtime application, cloud config, appearance, and AI config |
| `PocketMC.Desktop/Features/Shell` | Main shell, navigation, tray, startup options, visual state, and coordination |
| `PocketMC.Desktop/Features/Intelligence` | AI API client, session summarization, log preprocessing, markdown rendering, summary storage |
| `PocketMC.Desktop.Tests` | xUnit/Moq tests for lifecycle, process, settings, marketplace, tunnel, console, backups, cloud, add-ons, players, startup, and VM logic |

</details>

<br>

## Contributing

Fork the repo, branch off `master`, open a pull request with a clear explanation of what changed and why.

Before opening a PR:

```bash
dotnet build
dotnet test
```

For substantial changes touching process lifecycle, runtime provisioning, tunnel orchestration, update/rollback flows, backup safety, cloud uploads, marketplace installs, or filesystem security — open an issue first.

<br>

## Community & License

**Discord** — [discord.gg/h27uNCaxPH](https://discord.gg/h27uNCaxPH)  
**Reddit** — [r/PocketMC](https://www.reddit.com/r/PocketMC/)  
**YouTube** — [Watch Tutorials](https://www.youtube.com/@OfficialPocketMC)  
**MIT** © 2026 PocketMC Contributors — see [LICENSE](LICENSE)

<br>

<div align="center">

<a href="https://www.buymeacoffee.com/sahaj33" target="_blank">
  <img src="https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png" alt="Buy Me A Coffee" height="38" />
</a>
<br>
<a href="https://deepwiki.com/PocketMC/pocket-mc-windows">
  <img src="https://deepwiki.com/badge.svg" alt="DeepWiki" />
</a>

<br><br>

<sub>PocketMC runs servers on your Windows PC. It does not provide cloud hosting.<br>Playit.gg, CurseForge, cloud providers, and AI summaries require their own accounts/keys.</sub>

</div>
