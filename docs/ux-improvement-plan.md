# PocketMC UI/UX Improvement Plan

This document captures the highest-impact UI/UX changes needed to make PocketMC easier, safer, and more confidence-building for users managing Minecraft servers on Windows.

The focus is on problems that can block users, cause destructive mistakes, or make the app feel confusing even when the underlying feature works correctly.

## Goals

- Make the first server creation flow beginner-friendly without removing advanced power.
- Make server connection status obvious: local play, public play, Java, and Bedrock should not feel like a scavenger hunt.
- Reduce destructive mistakes around deleting servers, worlds, agents, and configuration.
- Make start failures, crashes, port conflicts, and tunnel issues actionable.
- Improve responsive layouts for smaller windows and high-DPI Windows scaling.
- Use user-facing terminology consistently: prefer **server** over **instance** in UI copy unless the context is technical.

## Priority P0: User-blocking UX issues

### 1. Dashboard server clarity

Current dashboard cards expose a lot of powerful state, but users mainly need to answer: **Can my friends join?**

Recommended changes:

- Rename dashboard heading from `Instances` to `Servers`.
- Rename `New Instance` to `New Server`.
- Add a dashboard empty state when no servers exist:

```text
No servers yet
Create your first Minecraft server or import an existing one.
[Create Server] [Import Server]
```

- Add a clear connection summary for running servers:

```text
Join address for friends outside your Wi-Fi
abc.playit.gg:12345 [Copy]

LAN address for players on the same Wi-Fi
192.168.1.5:25565 [Copy]

Bedrock address
1.2.3.4:19132 [Copy]
```

- Add one primary action: `Copy Invite Message`.

Suggested invite message format:

```text
Join my Minecraft server:
Java: abc.playit.gg:12345
Bedrock: 1.2.3.4:19132
Version: 1.21.5
```

### 2. Server health panel

Add a health checklist visible from dashboard cards and console:

```text
Server Health
✅ Server process running
✅ Java ready
✅ Enough RAM
✅ Port available
✅ Public address ready
⚠ Backup recommended
❌ Last crash detected

[Fix issues]
```

This should be powered by existing checks where possible:

- server lifecycle state
- Java configuration
- memory availability
- port reliability checks
- tunnel/agent state
- latest crash report detection
- backup availability

### 3. Safer delete flow

Delete confirmation should clearly explain what is being deleted and require stronger intent.

Recommended copy:

```text
Delete "Survival Server" permanently?

This deletes:
- World files
- Server config
- Installed mods/plugins
- Logs

Type DELETE to confirm.
```

Rules:

- Keep delete actions visually separated in a `Danger Zone`.
- Use danger styling only for destructive actions.
- Never place `Delete Agent` beside routine tunnel actions like `Connect` or `Refresh`.

### 4. Version loading recovery

When loading versions fails, the user should not only see a raw exception.

Recommended UI:

```text
Could not load Paper versions from PaperMC.
Check your internet connection or try again.

[Retry] [Use cached versions] [Open diagnostics]
```

Implementation notes:

- Keep raw exception text behind `Details`.
- Display the provider that failed.
- Allow retry from the same page without changing server type.
- Prefer cached versions if available.

### 5. Stage-based creation progress

Replace vague progress with visible stages:

```text
1/6 Creating folder
2/6 Downloading server software
3/6 Writing server.properties
4/6 Installing optional addons
5/6 Importing world
6/6 Final checks
```

Special cases:

- Forge/NeoForge should show: `Running Forge installer. This can take several minutes.`
- Bedrock BDS should show extraction progress separately from download progress.
- Geyser/Floodgate provisioning should have its own stage when cross-play is enabled.

## Priority P1: Reduce beginner confusion

### 6. Convert server creation into a guided flow

The current creation page exposes basics, server type, version, snapshots, loader version, cross-play, world settings, custom world upload, and EULA at once.

Recommended flow:

1. **Choose server goal**
2. **Choose version**
3. **Optional settings**
4. **Review and create**

Suggested first step options:

```text
Vanilla survival with friends
Plugins server
Mods server
Bedrock server
Import existing server
```

Map those choices to sensible defaults:

- Vanilla survival with friends -> Paper or Vanilla, depending on product decision.
- Plugins server -> Paper.
- Mods server -> Fabric/Forge/NeoForge picker.
- Bedrock server -> Bedrock BDS or PocketMine choice.
- Import existing server -> import flow.

### 7. Explain server type choices

Replace bare server type names with helpful labels:

```text
Vanilla - official Minecraft server
Paper - plugins and better performance
Fabric - lightweight Java mods
Forge - classic Java mods
NeoForge - newer Forge-style mods
Bedrock BDS - official Bedrock server
PocketMine - Bedrock plugin server
```

Add a `Not sure?` helper:

```text
Want plugins? Choose Paper.
Want Java mods? Choose Fabric or Forge.
Want Bedrock players? Choose Bedrock BDS or enable Cross-play.
Want the safest beginner option? Choose Paper.
```

### 8. Improve import discoverability

Import is currently reachable from the new instance flow, but it should also be visible from the dashboard.

Recommended dashboard actions:

```text
[Create Server] [Import Server]
```

### 9. Pre-flight start checklist

Before starting a server, show a compact checklist when there are warnings or blockers:

```text
Java installed
Enough RAM available
Server file exists
EULA accepted
Port available
Tunnel ready
World folder valid
```

Turn existing start-time dialogs into visible checks where practical.

### 10. Better port conflict UX

When a port conflict blocks start/restart, show cause and fixes:

```text
Port 25565 is already in use.

Likely cause:
Another Minecraft server or Java process is running.

Fix options:
[Use another port]
[Find process using port]
[Open advanced help]
```

Avoid exposing low-level binding text as the primary message.

## Priority P2: Console, tunnel, and settings polish

### 11. Console responsiveness

The console top bar has many controls. On smaller windows, collapse secondary controls into overflow.

Primary controls:

```text
Back | Search logs | Stop Server | Send command
```

Overflow controls:

```text
Auto-scroll
Copy logs
Filters
Regex
Players
Restart
AI Summary
```

Collapse log filters into one control:

```text
Filter: All / Chat / Info / Warnings / Errors / System
```

### 12. Console read-only affordance

When showing last session logs:

- Change title to `Console: Last session`.
- Change command placeholder to `Start the server to send commands`.
- Keep the existing informational banner.

### 13. AI privacy disclosure

Before first AI log analysis or AI summary, show:

```text
This sends selected log text to your configured AI provider.
Do not send private IPs, tokens, or sensitive chat logs.
```

Add a setting:

```text
Redact IP addresses and player UUIDs before AI analysis
```

### 14. Tunnel simple mode

Most users do not think in terms of agents and origins. First-run tunnel UX should be task-based:

```text
Make my server public
Step 1: Download Playit agent
Step 2: Sign in / claim agent
Step 3: Create address
Step 4: Copy address to friends
```

Terminology changes:

- `Agent Status` -> `Public connection status`
- `Tunnel` -> `Public connection`
- Hide agent executable path under advanced details.
- Move `Delete Agent` into a danger zone.

State-dependent actions:

- Missing agent -> `Download Agent`
- Downloaded but not setup -> `Setup Agent`
- Setup but disconnected -> `Connect`
- Connected -> `Disconnect`

### 15. Settings hierarchy and search

The server settings page has many sections. Add settings search and group navigation into clearer buckets.

Recommended groups:

```text
Basic
- Identity
- Performance
- Gameplay

Content
- World & Files
- Addons
- Version Updates

Safety & Advanced
- Backups
- Fault Tolerance
- AI Summaries
- Advanced Editor
```

Add:

```text
Search settings...
```

Search should match terms like RAM, backup, difficulty, port, icon, MOTD, mods, plugins, AI, crash, and update.

### 16. Separate settings save from update actions

Do not mix global `Save Changes` with version update actions in the same visual group.

Recommended:

- Global footer: `Discard changes` and `Save settings`.
- Version tab card: `Preview update`, `Apply update`, `Rollback previous update`.

## Crash recovery improvements

When a crash is detected, show actionable recovery options:

```text
Server crashed
Likely cause: Missing mod dependency / Java mismatch / port conflict / out of memory

[View crash report]
[Copy report]
[Open mods folder]
[Restore backup]
[Ask AI to explain]
```

Also surface crash reason on the dashboard card, not only inside console.

## Modded server safe mode

For Fabric, Forge, and NeoForge servers, add recovery actions:

```text
Start without mods
Disable recently added mods
Open mods folder
Restore backup
```

This should be especially visible after a crash caused by mod loading errors.

## Backup prompts before risky operations

Prompt users to create a backup before:

- Minecraft version update
- loader version change
- bulk mod/plugin changes
- world replacement/import
- advanced config edits
- deleting world files

Recommended prompt:

```text
Create backup first?
Recommended before this change.

[Create backup and continue]
[Continue without backup]
```

Do not show this for safe edits like description changes.

## Terminology standards

Prefer user-facing language:

| Current | Preferred |
| --- | --- |
| Instance | Server |
| New Instance | New Server |
| Import Instance | Import Server |
| Server Configurations | Server Settings |
| Agent | Playit connection helper |
| Tunnel | Public connection |
| Public Address | Join address |

Use `instance` only in code, logs, and advanced/internal contexts.

## Implementation checklist

### Safe first PR candidates

- [ ] Rename dashboard visible copy from `Instances` to `Servers`.
- [ ] Add dashboard empty state.
- [ ] Add `Import Server` action near `Create Server`.
- [ ] Add clearer copy labels around tunnel/LAN/Bedrock addresses.
- [ ] Add copy feedback for address buttons.
- [ ] Strengthen delete confirmation copy.
- [ ] Add retry button for version loading failures.
- [ ] Add first-run AI privacy notice.

### Larger follow-up PRs

- [ ] Server creation wizard.
- [ ] Server health panel.
- [ ] Pre-flight start checklist.
- [ ] Console responsive overflow toolbar.
- [ ] Tunnel simple mode.
- [ ] Settings search.
- [ ] Crash recovery flow.
- [ ] Modded server safe mode.

## Acceptance criteria

- A first-time user can create a recommended server without understanding Paper/Fabric/Forge internals.
- A running server card clearly shows which address to share with friends.
- Destructive actions require deliberate confirmation.
- Common failures provide fixes, not just errors.
- Console and settings remain usable on smaller windows and high-DPI displays.
- AI-powered features disclose what data may be sent before first use.
