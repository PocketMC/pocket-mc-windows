<div align="center">

<table border="0" cellpadding="16">
  <tr>
    <td align="center" width="200">
      <img src="docs/assets/logo.png" alt="PocketMC" width="180" />
    </td>
    <td align="center">
      <h1 style="border: none; margin-bottom: 10px;">PocketMC</h1>
      <p><b>A local-first Windows desktop app for creating, running, monitoring, backing up, and sharing Minecraft servers.</b></p>
      <a href="https://github.com/PocketMC/pocket-mc-windows/actions"><img src="https://img.shields.io/github/actions/workflow/status/PocketMC/pocket-mc-windows/production-build.yml?branch=master&style=flat-square&logo=github" alt="Build" /></a>
      <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET" /></a>
      <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Windows-10%201809%2B%20%2F%2011-0078D4?style=flat-square&logo=windows" alt="Platform" /></a>
      <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22C55E?style=flat-square" alt="License" /></a>
      <a href="https://github.com/PocketMC/pocket-mc-windows/releases"><img src="https://img.shields.io/github/v/release/PocketMC/pocket-mc-windows?style=flat-square" alt="Release" /></a>
      <a href="https://discord.gg/h27uNCaxPH"><img src="https://img.shields.io/badge/Discord-Join-%235865F2?style=flat-square&logo=discord" alt="Discord" /></a>
      <a href="https://www.buymeacoffee.com/sahaj33"><img src="https://img.shields.io/badge/Donate-BMC-%23FFDD00?style=flat-square&logo=buy-me-a-coffee&logoColor=black" alt="Buy Me A Coffee" /></a>
    </td>
  </tr>
</table>

<img src="docs/assets/screenshot-dashboard.png" alt="PocketMC Dashboard" width="860" style="border-radius: 10px; margin-top: 16px;" />

</div>

---

## What PocketMC is

PocketMC is a **Windows WPF desktop app** for managing local Minecraft server instances without turning server setup into a command-line chore.

It manages server downloads, app-local runtimes, startup/shutdown, console output, player state, backups, diagnostics, and Playit.gg tunnels from one desktop UI. Your servers, worlds, runtimes, backups, logs, and tunnel files live under the root folder you choose on first launch.

## What PocketMC is not

- Not a cloud hosting provider. Servers run on your Windows PC.
- Not a Minecraft game launcher. It manages servers, not the Minecraft client.
- Not a Linux/Docker web panel. The app is built for local Windows desktop use.
- Not a replacement for upstream accounts, API keys, EULAs, or provider limits.

---

## Supported server software

PocketMC resolves available versions from upstream APIs/manifests where possible, so exact version availability depends on those providers.

| Server family | Support in PocketMC |
|---|---|
| Vanilla Java | Official Mojang server JARs, Minecraft 1.8.8+ |
| Paper | PaperMC builds, Minecraft 1.8.8+ |
| Fabric | Fabric server JARs, Minecraft 1.14+ |
| Forge | Forge installer-based server setup, Minecraft 1.8.8+ |
| NeoForge | NeoForge installer-based server setup from Maven metadata |
| Bedrock Dedicated Server | Windows BDS ZIPs from the community Bedrock server manifest, including releases and previews when available |
| PocketMine-MP | PocketMine-MP `.phar` releases with managed PHP runtime support |
| Cross-play | Geyser/Floodgate provisioning for Java instances that need Bedrock access |

---

## Features

<details>
<summary><b>Instance lifecycle</b></summary>

- Create isolated Minecraft server instances from the desktop UI.
- Download server artifacts through provider-specific download pipelines.
- Start, stop, restart, and kill instances from the dashboard or tray flow.
- Graceful shutdown uses RCON when configured, then falls back to standard console input.
- Crash detection captures recent sanitized output and can trigger automatic restart with backoff.
- Per-instance port preflight checks, probing, lease tracking, and recovery messaging help avoid local port conflicts.
- Geyser-enabled instances patch the Bedrock listener port before launch.

</details>

<details>
<summary><b>Managed runtimes</b></summary>

- App-local Java provisioning through Adoptium.
- Bundled Java runtime targets: **Java 8, 11, 17, 21, and 25**.
- Runtime selection is based on the Minecraft version, with optional custom Java path support.
- PocketMine-MP uses an app-managed PHP 8.2 PM5 runtime from the official PocketMine PHP binaries.
- Downloads use retries, partial-file handling, safe promotion, and hash verification when upstream hashes are available.

</details>

<details>
<summary><b>Dashboard, console, and player tracking</b></summary>

- Dashboard cards show instance state and live server context.
- Resource monitoring tracks CPU/RAM usage for running instances.
- Console output is buffered, sanitized, classified, and written to `logs/pocketmc-session.log`.
- Player count and online player names are parsed from Java, Bedrock, and PocketMine-style output.
- Console tools include filtering, search-oriented log handling, command input, and session history support.

</details>

<details>
<summary><b>Public access with Playit.gg</b></summary>

- Built-in Playit.gg agent provisioning and setup flow.
- Existing tunnel discovery for matching local ports.
- Automatic tunnel creation for Java and Bedrock-style server ports when possible.
- Dashboard-ready public and numeric tunnel addresses.
- Clear handling for offline agents, invalid tokens, unclaimed agents, and Playit account tunnel limits.

</details>

<details>
<summary><b>Mods, plugins, add-ons, and content</b></summary>

- Modrinth browser for server-side mods, plugins, and modpacks.
- CurseForge browser support when the user provides a CurseForge API key in app settings.
- Poggit/PocketMine plugin integration through the marketplace services.
- Dependency resolution support for marketplace installs where the upstream API provides dependency metadata.
- Bedrock `.mcpack`, `.mcaddon`, and `.zip` ingestion with manifest parsing.
- Bedrock behavior/resource packs are copied into the correct BDS folders and registered in the active world's JSON pack lists.

</details>

<details>
<summary><b>Backups and recovery</b></summary>

- Manual and scheduled world backups.
- Live-server backup flow attempts RCON save synchronization first, then falls back to console save commands.
- Backups tolerate locked files and skip known unsafe files such as `session.lock`.
- Backup retention pruning keeps old archives under control.
- Restore uses safe ZIP extraction to avoid path traversal issues.
- Optional external backup replication copies archives into a user-selected folder, useful with Google Drive, Dropbox, OneDrive, or another sync client.

</details>

<details>
<summary><b>Diagnostics, updates, and quality-of-life</b></summary>

- Dependency health checks for Playit.gg, Adoptium, and Modrinth.
- Diagnostic reporting and support-bundle style data collection are wired into the settings flow.
- Windows toast notifications and tray integration.
- Velopack startup/update integration.
- Windows UWP loopback helper for Minecraft Bedrock local loopback access through `CheckNetIsolation.exe`.
- Mica/theme settings for Windows UI polish.

</details>

<details>
<summary><b>AI session summaries</b></summary>

PocketMC can generate structured server-session summaries from `pocketmc-session.log` using a user-supplied API key.

Supported providers:

- Google Gemini
- OpenAI
- Anthropic Claude
- Mistral AI
- Groq

Logs are preprocessed before summarization. The app sanitizes obvious personal data such as IP addresses and emails before storing/exporting console output, but AI summaries still send processed log content to the provider you select.

</details>

---

## Installation

Download `Setup.exe` from the [latest release](https://github.com/PocketMC/pocket-mc-windows/releases/latest) and run it.

- Installs per-user; admin rights are not required for normal installation.
- .NET 8 Desktop Runtime is required and should be prompted if missing.
- Java does not need to be installed globally. PocketMC manages its own Java runtimes.
- PHP does not need to be installed globally for PocketMine-MP. PocketMC provisions the required runtime.
- Updates are handled through Velopack.

---

## Quick start

1. **Choose an app root folder.** This is where PocketMC stores instances, runtimes, backups, logs, and tunnel files.
2. **Create an instance.** Open **New Instance**, select a server family and version, configure basic settings, accept the Minecraft EULA when required, then create/download the server.
3. **Start the server.** Use the dashboard start button. Connect locally with `localhost` or from LAN using your PC's local IP.
4. **Optional: enable public access.** Link Playit.gg in the tunnel flow and let PocketMC resolve or create a matching tunnel for the instance.
5. **Optional: configure extras.** Add Modrinth/CurseForge/Poggit content, set backup schedules, configure AI summaries, or tune server properties.

---

## Screenshots

| Dashboard | Server Console |
|-----------|---------------|
| ![Dashboard](docs/assets/screenshot-dashboard.png) | ![Console](docs/assets/screenshot-console.png) |

| Server Settings | App Settings | Backups |
|-----------------|-----------------|---------|
| ![ServerSettings](docs/assets/server-settings.png) | ![Settings](docs/assets/screenshot-settings.png) | ![Backups](docs/assets/screenshot-backups.png) |

| Plugin Browser | Mod Marketplace |
|-----------------|----------------|
| ![Plugins](docs/assets/screenshot-plugins.png) | ![Marketplace](docs/assets/mod-marketplace.png) |

| Public Tunnels | Managed Runtimes | About |
|-----------------|----------------|-------|
| ![Tunnels](docs/assets/tunnels.png) | ![Runtimes](docs/assets/runtimes.png) | ![About](docs/assets/about.png) |


---

## System requirements

| Requirement | Minimum |
|---|---|
| OS | Windows 10 1809, build 17763, or Windows 11 |
| Architecture | x64 |
| RAM | 4 GB minimum, 8 GB+ recommended |
| Runtime | .NET 8 Desktop Runtime |
| Internet | Required for first-run runtime/server downloads, provider metadata, marketplace browsing, updates, and Playit.gg |

---

## Build from source

### Prerequisites

- Windows 10 1809+ or Windows 11
- .NET 8 SDK
- Visual Studio 2022 with **Desktop development with .NET**, or JetBrains Rider

### Commands

```bash
git clone https://github.com/PocketMC/pocket-mc-windows.git
cd pocket-mc-windows

dotnet restore
dotnet build
dotnet test
dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
```

For packaging, PocketMC uses Velopack. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the release packaging flow.

---

## Project structure

| Path | Purpose |
|---|---|
| `PocketMC.Desktop/Composition` | Dependency injection and service registration |
| `PocketMC.Desktop/Core` | Shared interfaces, MVVM utilities, and presentation primitives |
| `PocketMC.Desktop/Infrastructure` | WPF/Windows integrations, dialogs, dispatcher, notifications, updates, loopback helper, and process helpers |
| `PocketMC.Desktop/Features/Instances` | Instance metadata, registry, server lifecycle, launch configuration, providers, backups, world/config handling, runtime support |
| `PocketMC.Desktop/Features/Java` | Java version resolution, Adoptium runtime provisioning, runtime validation |
| `PocketMC.Desktop/Features/Tunnel` | Playit.gg agent lifecycle, API client, tunnel setup, discovery, and auto-creation |
| `PocketMC.Desktop/Features/Marketplace` | Modrinth, CurseForge, Poggit, modpack parsing, dependency resolution, marketplace models |
| `PocketMC.Desktop/Features/Mods` | Bedrock add-on install/uninstall and pack registration |
| `PocketMC.Desktop/Features/Console` | Console filtering, sanitization, classification, and display behavior |
| `PocketMC.Desktop/Features/Players` | Player parsing, bans, allowlist/operator sidecar management, player UI |
| `PocketMC.Desktop/Features/Diagnostics` | Dependency health and diagnostic reporting |
| `PocketMC.Desktop/Features/Settings` | App/server settings view models and settings pages |
| `PocketMC.Desktop/Features/Shell` | Main window, navigation shell, tray UI, startup coordination, visual state |
| `PocketMC.Desktop.Tests` | xUnit/Moq tests for lifecycle, process, settings, marketplace, tunnel, console, and view-model logic |

---

## Important notes

- Playit.gg public access requires a working Playit account/agent link. Playit tunnel limits still apply.
- CurseForge browsing requires your own CurseForge API key.
- AI summaries require your own provider API key and send processed logs to that provider.
- External backup replication copies archives to a selected folder. Cloud sync depends on whatever sync tool owns that folder.
- Forge and NeoForge use installer-based flows, so their setup is more complex than a single vanilla JAR download.
- Provider availability depends on upstream services such as Mojang, PaperMC, Fabric, Forge, NeoForge, GitHub, Modrinth, CurseForge, Poggit, Adoptium, and Playit.gg.

---

## Contributing

Fork the repo, branch off `master`, and open a pull request with a clear explanation of what changed and why.

Before opening a PR:

```bash
dotnet build
dotnet test
```

For larger changes, especially around process lifecycle, runtime provisioning, tunnel orchestration, backup safety, marketplace installs, or filesystem security, open an issue first.

---

## Community

**Discord:** [discord.gg/h27uNCaxPH](https://discord.gg/h27uNCaxPH)

---

## License

MIT © 2024 PocketMC Contributors — see [LICENSE](LICENSE).

---

<div align="center">

<a href="https://www.buymeacoffee.com/sahaj33" target="_blank">
  <img src="https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png" alt="Buy Me A Coffee" height="41" />
</a>

</div>
