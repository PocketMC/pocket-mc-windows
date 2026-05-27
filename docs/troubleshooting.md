# PocketMC Troubleshooting Guide

This guide helps users and maintainers collect useful debugging information without exposing secrets. The goal is to fix issues, not accidentally publish someone's API keys to the internet, because apparently we still have to say that.

## Basic information to include in bug reports

Include:

- PocketMC version
- Windows version
- Minecraft server type: Vanilla, Paper, Fabric, Forge, NeoForge, Bedrock Dedicated Server, or PocketMine-MP
- Minecraft version
- Whether the server was imported or created by PocketMC
- Whether Playit, Geyser, Floodgate, Simple Voice Chat, or cloud backups are enabled
- What action failed: create, start, stop, backup, restore, install addon, update server, create tunnel, etc.
- Screenshot of the visible error if available

Do not include:

- API keys
- OAuth tokens
- Playit agent secret keys
- Full `settings.json`
- Private tunnel secrets
- Personal cloud provider tokens

## Common checks

### Server will not start

1. Check whether another process is using the server port.
2. Confirm the correct Java version is installed or managed by PocketMC.
3. Check the latest server console output.
4. Check whether `world/session.lock` already exists.
5. If `session.lock` exists, do **not** delete it blindly. Stop other servers/tools using the world first.

### Backup failed

1. Confirm the server folder still exists.
2. Confirm there is enough disk space.
3. Confirm the world folder exists:
   - Java: `world`
   - Bedrock Dedicated Server: `worlds/<level-name>`
   - PocketMine: `worlds`
4. If cloud backup is enabled, test local backup first.
5. Check cloud provider authentication if upload failed after local backup succeeded.

### Restore failed

1. Confirm the backup ZIP exists.
2. Confirm the backup ZIP is readable.
3. Confirm the server is stopped before restore.
4. Check whether restore left a `.restore-stage-*` or `.restore-backup-*` folder.
5. Do not manually delete rollback folders until you know which folder contains the current valid world.

### Marketplace install failed

1. Confirm network access.
2. Confirm CurseForge API key if using CurseForge.
3. Confirm the addon supports the selected Minecraft version and loader.
4. For Paper plugins, confirm the destination is the plugin folder.
5. For Fabric/Forge/NeoForge mods, confirm the destination is the mods folder.

### Playit tunnel failed

1. Confirm Playit agent exists in the PocketMC tunnel folder.
2. Restart PocketMC.
3. Check Playit account/tunnel limit.
4. If a tunnel has no allocated address, treat it as a provider/account state issue rather than a local Minecraft server issue.

## Logs and crash reports

PocketMC may write crash reports or local logs under the user's local app data directory.

Typical locations:

```text
%LOCALAPPDATA%\PocketMC\
%LOCALAPPDATA%\PocketMC\CrashReports\
%LOCALAPPDATA%\PocketMC\logs\
```

Before sharing logs, remove:

- API keys
- OAuth tokens
- Playit secrets
- private cloud paths if sensitive
- personal access tokens

## Good bug report template

```text
PocketMC version:
Windows version:
Server type:
Minecraft version:
Created or imported server:
Enabled integrations:
Action that failed:
Expected behavior:
Actual behavior:
Steps to reproduce:
Relevant log excerpt:
Screenshot/video if useful:
```

## Maintainer triage checklist

- [ ] Reproduction steps are clear.
- [ ] User did not include secrets.
- [ ] Issue is assigned to the correct subsystem.
- [ ] Data-loss risk is assessed first.
- [ ] Backup/restore issues are prioritized above cosmetic issues.
- [ ] Networking issues distinguish local server failure from tunnel provider failure.
- [ ] Marketplace issues distinguish no compatible version from provider/API failure.
