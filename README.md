<div align="center">

<table border="0">
  <tr>
    <td align="center" width="200">
      <img src="docs/assets/logo.png" alt="PocketMC" width="180" />
    </td>
    <td align="left">
      <h2 style="border: none; margin-bottom: 10px;">PocketMC</h2>
      <p><b>Run Minecraft Java, Bedrock, and Cross-play servers on Windows.<br> No terminal. No Java headaches. No mess.</b></p>
      <a href="https://github.com/PocketMC/pocket-mc-windows/actions"><img src="https://img.shields.io/github/actions/workflow/status/PocketMC/pocket-mc-windows/production-build.yml?branch=master&style=flat-square&logo=github" alt="Build" /></a>
      <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET" /></a>
      <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows" alt="Platform" /></a>
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
## What PocketMC is NOT

- Not a hosting service — runs locally on your PC
- Not a Linux server panel — no Docker, no SSH, no sysadmin skills needed
- Not a Minecraft launcher — it manages servers, not the game
- Not a script pack — replaces manual setup entirely


---


## What it does

PocketMC is a Windows desktop app for creating and managing Minecraft server instances — Java and Bedrock — without touching a command line. Java is bundled automatically. Public sharing is one click via Playit.gg. Without dealing with Java installs, port forwarding, or command-line setup.

Supported server types: **Vanilla · Paper · Fabric · Forge · Bedrock (BDS) · PocketMine-MP**

---

## Features

### 🎮 Server Management

- **Multi-Protocol Support** — Run Minecraft Java, native Bedrock Edition (BDS), and PocketMine-MP instances side-by-side. Includes one-click installation for 45+ Bedrock versions via community manifests.
- **Managed Runtimes** — PocketMC automatically provisions isolated JRE and PHP 8.x runtimes. Nothing touches your system Java/PHP installation.
- **Add-on & Plugin Browser** — Native support for Bedrock `.mcpack`/`.mcaddon` and Poggit (PocketMine) integration. Browse and install Java plugins from Modrinth/CurseForge.
- **Public Tunneling** — Integrated Playit.gg with guided setup. Public addresses are shown as copyable links on the dashboard.
- **Instance Isolation** — Each server runs in its own folder with independent configs, runtimes, and world files.

### 🔭 Observability & Intel

- **Live Metrics** — Real-time CPU, RAM, and player count tracking per instance.
- **Dependency Health Dashboard** — Live status monitoring for Adoptium, Playit.gg, and Modrinth microservices directly in the settings.
- **Modern AI Console** — Colorized logs with multi-keyword search, Regex filtering, command history, intelligent command auto-suggestions and crashes and errors AI analysis.
- **AI Session Summaries** — Generate structured summaries of your server sessions using Google Gemini, OpenAI, Anthropic Claude, Mistral AI, or Groq.
- **Disaster Recovery** — Automated backups with optional off-site replication to Google Drive/Dropbox sync directories and one-click support bundle export.

### 🛡️ Technical Excellence

- **RCON Client Engine** — Standard I/O is deprecated in favor of a robust managed RCON client, eliminating synchronization deadlocks on high-load servers.
- **Artifact Integrity** — Deep SHA1/SHA256 verification for all downloads (Playit daemon, server binaries) ensures your local files are never corrupted or tampered with.
- **Graceful Lifecycle** — A custom 15-second shutdown loop ensures all players are kicked and worlds are saved correctly before the app exits.
- **PII Scrubbing** — Automated RegEx pipelines scrub personal data (IPv4, emails) from console logs before they are processed by AI or exported.
- **UWP Loopback Automation** — One-click "Fix Bedrock LAN" tool to handle UWP network isolation, allowing local connections to Bedrock servers.

---

## Comparison

| Tool | Type | Hosting | Cost | Java | Bedrock | Mods/Plugins | 1-Click Install | Backups | Live Metrics | No Port-Forward | Open Source |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **PocketMC** | Windows desktop app | Self-hosted | Free | ✅ Vanilla/Paper/Fabric/Forge | ✅ BDS + PocketMine-MP + Geyser | ✅ CurseForge/Modrinth/Poggit | ✅ | ✅ Scheduled + manual | ✅ CPU/RAM/players | ✅ Playit.gg built-in | ✅ MIT |
| SquidServers | Desktop app | Self-hosted | Free | ✅ Vanilla/Paper/Fabric | ✅ via Geyser | ✅ Mods | ✅ | ✅ | ✅ | ✅ | ❌ |
| auto-mcs | Desktop + Docker | Self-hosted | Free | ✅ Paper/Purpur/Fabric/Forge/Spigot/Vanilla | ✅ via Geyser | ✅ Modrinth | ✅ | ✅ Auto | ✅ | ✅ playit.gg | ✅ AGPL-3.0 |
| MCSManager | Web panel | Self-hosted | Free | ✅ | ✅ | ✅ | ✅ marketplace | ✅ scheduled | ✅ | ❌ | ✅ Apache-2.0 |
| Pterodactyl | Web panel | Self-hosted | Free | ✅ | ✅ via eggs | ✅ | ✅ | ⚠️ manual scripts | ✅ | ❌ | ✅ MIT |
| fork.gg | Windows GUI | Self-hosted | Free | ✅ Vanilla/Paper/Waterfall | ❌ | ⚠️ manual jar replace | ✅ | ❌ | ❌ | ❌ | ✅ |
| Apex Hosting | Managed host | Cloud | ~$4.49+/mo | ✅ | ✅ | ✅ 1-click modpacks | ✅ | ✅ daily | ✅ graphs | N/A | ❌ |
| Aternos | Managed host | Cloud | Free (ads) | ✅ | ✅ | ⚠️ CurseForge/Modrinth only, no upload | ✅ | ✅ Google Drive | ❌ | N/A | ❌ |
| CubeCoders AMP | Web panel | Self-hosted | £7.50+ one-time | ✅ | ✅ | ✅ local + S3 | ✅ + analytics | ❌ | ❌ | N/A | ❌ |
| e4mc | Tunnel mod | — | Free | ✅ | ❌ | N/A | N/A | ❌ | ❌ | ✅ | ✅ |
| Essential Mod | Client mod (P2P) | Self-hosted | Free | ✅ | ❌ | ⚠️ must match mods | ✅ Host World | ❌ | ❌ | ✅ | ❌ |
| Minehut | Managed host | Cloud | Free / ~$4-12 | ✅ | ✅ (beta crossplay) | ✅ upload jars | ✅ | ✅ 2/GB RAM | ❌ | N/A | ❌ |
| playit.gg | Tunnel service | — | Free / $3 mo | ✅ | ✅ | N/A | N/A | ❌ | ✅ tunnel stats | ✅ | ❌ |

---

## Installation

Download `Setup.exe` from the [latest release](https://github.com/PocketMC/pocket-mc-windows/releases/latest) and run it.

- No admin rights required — installs per-user.
- .NET 8 Desktop Runtime is prompted automatically if missing.
- Java does **not** need to be pre-installed. PocketMC manages its own JRE stack.
- Updates are handled automatically via Velopack.

---

## Quick Start

**1. Pick a root folder** on first launch. Everything — servers, runtimes, tunnel — lives here.

**2. Create an instance.** Hit **New Instance**, choose a server type and version, accept the EULA, click **Create & Download**. The JAR fetches automatically.

**3. Start your server.** Hit **Start**. Metrics go live. Connect from Minecraft at `localhost` or your LAN IP.

**Optional: Enable public access.** Open the instance, enable Playit.gg tunneling, and follow the one-time account link flow. Your public address appears on the dashboard.

---
✔ Fully isolated — Java and PHP are managed internally
✔ No system setup — no environment variables, no conflicts
✔ Runs multiple servers safely side-by-side

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

| | Minimum |
|---|---|
| OS | Windows 10 1809 (build 17763) or Windows 11 |
| Architecture | x64 |
| RAM | 4 GB (8 GB+ recommended) |
| .NET | .NET 8 Desktop Runtime (auto-prompted on install) |
| Internet | Required for first-run JRE download and Playit.gg |

---

## Roadmap

- [ ] In-app whitelist and op management
- [ ] Forge 1.17+ bootstrapper stability pass
- [ ] Modpack install progress UI
- [ ] Player activity charts and historical metrics

---

## Contributing

Fork the repo, branch off `main`, and open a PR with a clear description of what changed and why. For significant architecture changes, open an issue first.

When testing locally, cover process lifecycle edge cases — crash recovery, orphan process cleanup, tunnel teardown. The full build guide is in [`CONTRIBUTING.md`](CONTRIBUTING.md).

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
