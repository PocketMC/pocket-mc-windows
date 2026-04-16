<div align="center">

<img src="docs/assets/logo.png" alt="PocketMC Logo" width="200" />

**Run Minecraft Java & Bedrock servers from your Windows PC — without any mess** 
---
PocketMC is a modern Windows-native server manager for Java (Vanilla, Paper, Fabric, Forge) and Bedrock (BDS, PocketMine-MP) servers. It helps you create, launch, monitor, back up, and share servers from your own machine with a polished GUI. Supports automatic Java and PHP provisioning, Playit.gg public tunneling, live server metrics, backups, and in-app plugin/mod workflows.

[![Build](https://img.shields.io/github/actions/workflow/status/PocketMC/pocket-mc-windows/production-build.yml?branch=main&style=flat-square&logo=github)](https://github.com/PocketMC/pocket-mc-windows/actions)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-22C55E?style=flat-square)](LICENSE)
[![Release](https://img.shields.io/github/v/release/PocketMC/pocket-mc-windows?style=flat-square)](https://github.com/PocketMC/pocket-mc-windows/releases)
[![PocketMC Discord](https://img.shields.io/badge/PocketMC-Discord-%235865F2.svg)](https://discord.gg/h27uNCaxPH)

---

<!-- Replace with your actual screenshot -->
<img src="docs/assets/screenshot-dashboard.png" alt="PocketMC Dashboard" width="860" style="border-radius: 12px;" />

</div>

---

## Why PocketMC?

Hosting a Minecraft server on Windows usually means juggling Java versions, server jars, folder layouts, backups, and public connectivity.

PocketMC fixes that with a desktop-first workflow:

- **Create multiple server instances** with isolated folders and metadata. Supports **Vanilla, Paper, Fabric, Forge, Bedrock (BDS), and PocketMine-MP**. 
- **Automatic Runtime provisioning**: Managed Java (JRE) and PHP setups for required Minecraft versions — no need to pre-install anything manually. 
- **Public server access via Playit.gg** with guided in-app flow and per-start tunnel resolution. 
- **Live CPU, RAM, and player metrics** on a dashboard built for real use, not just setup. 
- **Backups and restore workflows** designed for live servers and safer world management. 
- **Addon, Plugin, and Mod workflows** with in-app browsing, Poggit integration, and .mcpack importers. 

---

## Key Features

### Server Instance Management
Create and manage multiple Minecraft Java & Bedrock server instances side-by-side. Supports **Vanilla**, **Paper**, **Fabric**, **Forge**, **Bedrock (BDS)**, and **PocketMine-MP** server types with guided creation and version selection.

### Automatic Java Setup
PocketMC downloads and manages its own Java runtimes for supported Minecraft versions, so you don’t have to deal with system-wide Java conflicts or manual installation steps.

### Public Access with Playit.gg
PocketMC integrates **Playit.gg** for public server sharing. The app can guide first-time tunnel setup, refresh tunnel addresses on server start, and surface the active public address directly in the UI.

### Live Dashboard & Console
Monitor each server with live **CPU**, **RAM**, and **player count** metrics, then jump into a dedicated console with colorized logs, filtering, search, crash visibility, and command suggestions.

### World, Plugin, Mod, and Modpack Workflows
Import worlds from ZIPs, browse plugins/mods from supported sources, and manage modded setups with compatibility-aware tooling and safer file handling.

### Backup and Restore
Create manual backups or schedule automatic ones. PocketMC is built around real-world server operations, including safer world compression, restore workflows, and retention handling.

### Crash-Safe Process Management
PocketMC uses Windows-native process handling and cleanup patterns to reduce the chance of orphaned background processes when servers or tunnels crash.

### AI Session Summaries
Optionally generate structured summaries of server sessions using external AI providers for highlights, events, and session review.

---

## Screenshots

| Dashboard | Server Console |
|-----------|---------------|
| ![Dashboard](docs/assets/screenshot-dashboard.png) | ![Console](docs/assets/screenshot-console.png) |

| Server Settings | Plugin Browser |
|-----------------|----------------|
| ![Settings](docs/assets/screenshot-settings.png) | ![Plugins](docs/assets/screenshot-plugins.png) |

---

## System Requirements

| Requirement | Minimum |
|---|---|
| OS | Windows 10 1809 (build 17763) or Windows 11 |
| Architecture | x64 |
| RAM | 4 GB (8 GB+ recommended for running servers) |
| .NET Runtime | .NET 8 Desktop Runtime |
| Internet | Required for first-run JRE download and Playit.gg tunneling |

> Java does **not** need to be pre-installed. PocketMC manages its own JRE stack.
> Velopack can prompt for missing desktop runtime prerequisites during install or update.

---

## Installation

### Velopack Setup (Recommended)

1. Download `Setup.exe` from the [latest release](https://github.com/PocketMC/pocket-mc-windows/releases/latest).
2. Run the Velopack installer. No admin rights are required and it installs per-user.
3. Launch PocketMC Desktop from the Start Menu or desktop shortcut.
4. On first run, choose a root folder where server instances and runtimes will be stored.
5. Future updates are handled automatically through Velopack.

### Build from Source

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), [Git](https://git-scm.com/)

```bash
git clone https://github.com/PocketMC/pocket-mc-windows.git
cd pocket-mc-desktop
dotnet build PocketMC.Desktop.sln --configuration Release
dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
```

To build the Velopack release locally, install the Velopack CLI and pack the published output:

```powershell
dotnet tool install -g vpk
dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj -c Release -r win-x64 -o ./publish
vpk pack -u PocketMC -v 1.2.4 -p ./publish -e PocketMC.Desktop.exe
```

The installer and release feed files will be written to `Releases/`.

Quick local test (fast): publish and run the built exe to verify UI and basic flows before tagging and pushing a release.

```powershell
dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj -c Release -r win-x64 -o ./publish
Start-Process -FilePath .\publish\PocketMC.Desktop.exe
# Or run from an elevated console if needed:
# & .\publish\PocketMC.Desktop.exe
```

Release & versioning notes:

- Use a semver tag like `v1.2.4` when creating a release. The CI uses the tag name as the release version.

```powershell
# Create a tag and push it
git tag v1.2.4
git push origin v1.2.4
```

When the CI runs on the pushed tag it will publish for `win-x64`, run `vpk pack` with the tag as the version, and attach `Setup.exe` to the GitHub Release automatically.

If you want the workflow to create the GitHub Release automatically, add a repository secret named `RELEASE_PAT` with release/write access. Secret names cannot start with `GITHUB_`.

Full release history: [CHANGELOG.md](CHANGELOG.md)

---

## Getting Started

### 1. First Launch

PocketMC will prompt you to select an **App Root Folder**. This is where everything lives:

```
<AppRoot>/
├── servers/          # One subfolder per server instance
├── runtimes/         # Managed JREs (java11, java17, java21, etc.) and PHP
└── tunnel/           # Playit.gg agent binary and logs
```

On first run, PocketMC downloads the required Java runtimes automatically. This is a one-time setup.

### 2. Create a Server Instance

1. Click **New Instance** on the Dashboard.
2. Enter a name, description, and select server type (Vanilla, Paper, Fabric, or Forge).
3. Choose a Minecraft version from the live version list.
4. Accept the [Minecraft EULA](https://aka.ms/MinecraftEULA).
5. Click **Create & Download**, the server JAR downloads automatically.

### 3. Start Your Server

Hit **Start** on the instance card. The server boots, and you'll see live status, CPU, RAM, and player count update in real time.

### 4. Enable Public Access (Optional)

PocketMC integrates with [Playit.gg](https://playit.gg) for free public tunneling:

1. On first start, PocketMC will open a browser and guide you through linking your Playit.gg account.
2. After approval, a tunnel is automatically resolved for your server's port.
3. The public address appears as a **copyable pill** on the instance card.

> Playit.gg free accounts support up to 4 simultaneous tunnels.

---

## Community & Support

Join our community on Discord to get help, ask questions, and connect with other PocketMC users:

- Discord: https://discord.gg/h27uNCaxPH

## Contributing

Contributions are welcome. Before opening a PR:

1. Fork the repo and create a feature branch off `main`.
2. Follow the existing code style, no unnecessary abstractions, self-documenting names.
3. Test process lifecycle edge cases manually (crash recovery, orphan process cleanup).
4. Open a PR with a clear description of what changed and why.

For significant architectural changes, open an issue first to discuss the approach.

---

## Roadmap

- [x] Bedrock Edition (BDS) & PocketMine support
- [ ] In-app whitelist and op management
- [ ] System tray minimization with running-server indicator
- [ ] Multi-monitor window persistence
- [ ] Forge — full 1.17+ bootstrapper stability pass
- [ ] Modpack import progress UI (streaming install feedback)
- [ ] Player activity charts and historical metrics

---

## License

MIT © 2024 PocketMC Contributors — see [LICENSE](LICENSE) for full terms.

---

<div align="center">

Built for people who want to run a Minecraft server,  
not maintain a Linux box.

</div>

<a href="https://www.buymeacoffee.com/sahaj33" target="_blank"><img src="https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png" alt="Buy Me A Coffee" style="height: 41px !important;width: 174px !important;box-shadow: 0px 3px 2px 0px rgba(190, 190, 190, 0.5) !important;-webkit-box-shadow: 0px 3px 2px 0px rgba(190, 190, 190, 0.5) !important;" ></a>
