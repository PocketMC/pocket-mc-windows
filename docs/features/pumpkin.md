# Pumpkin support

Choose **Pumpkin (Java + Bedrock)** when creating an instance. The version selector shows Pumpkin release tags rather than Mojang version numbers. Each upstream tag identifies the client versions supported by that build; Pumpkin does not provide every Minecraft version or complete vanilla feature parity.

PocketMC downloads the official Windows binary for the host architecture and launches `pumpkin.exe` directly. No Java, PHP, Geyser, or Floodgate installation is needed. Existing Java mods, Bukkit/Paper plugins, PocketMine plugins, and Bedrock add-on packages are not compatible with Pumpkin's plugin API and are not offered through PocketMC's add-on browsers.

## Client ports

- Java defaults to TCP **25565**.
- Bedrock uses Pumpkin's **NetherNet** implementation, with TCP and UDP on **19132** by default. Pumpkin also reserves the following UDP port for IPv6.
- The ports can be changed during creation and in Server Settings. Leave room for the following Bedrock UDP port; its primary port must be at most 65534.
- Standard Playit Bedrock tunnels carry RakNet and cannot transport Pumpkin's NetherNet traffic. PocketMC must not advertise them as a working Pumpkin Bedrock address. Java TCP tunnels can still be used for Java clients.
- Playit now offers NetherNet tunnels on its free tier with agent **1.2.0 or later**. The new service requires **NXS signaling**: configure the endpoint `https://nethernet.playit.gg` and the tunnel token in a compatible server, disable STUN discovery, and leave Proxy Protocol at None. Clients use the tunnel hostname on port **19132**, regardless of the allocated UDP port shown in the tunnel page. Premium is no longer a requirement for NetherNet itself.
- Playit's 1.2.0 release documents Geyser and WaterdogPE setups. The pinned Pumpkin release has no NXS endpoint/token configuration, so PocketMC continues to show its Bedrock LAN address and does not automatically create an incompatible public tunnel. Updating the Playit agent alone does not establish Pumpkin compatibility. A normal RakNet tunnel cannot be substituted. Native public Bedrock support requires a verified compatible upstream implementation or bridge.
- When another instance holds the Bedrock port, recovery changes the Bedrock listener and its following IPv6 UDP port together. The Java port stays unchanged. Choose a separate Bedrock port for each running Pumpkin instance.

Bedrock clients must support the protocol version identified by the selected Pumpkin release. Actual remote access depends on the network and upstream transport behavior.

## Configuration

Pumpkin uses a single **`pumpkin.toml`** in the instance directory, rather than `server.properties`. PocketMC maps shared controls including MOTD, players, ports, online mode, gamemode, difficulty, seed, view/simulation distance, PvP, and whitelist to native Pumpkin settings. The Advanced tab uses the same key/value rows and append/remove controls as other server types. Native keys use dotted paths, such as `networking.java.enabled`; values use TOML syntax, including quoted strings, booleans, numbers, and arrays. Settings managed by the other tabs are read-only in this editor.

TOML is parsed before changes are saved or a server is launched. Unknown settings are retained when shared controls are saved; formatting and comments may be normalized. PocketMC maintains console input (`commands.use_console=true`, `commands.use_tty=false`) and plain log output so console commands and graceful shutdown continue working.

On launch, PocketMC points Pumpkin's favicon setting at the instance's `server-icon.png` when present. This applies the bundled PocketMC logo or the custom icon chosen in Visual Identity to the Minecraft server list. Restart the server after changing its icon. Pumpkin's add-ons page shows an unsupported state because PocketMC's current Java/PocketMine add-on tools cannot manage Pumpkin's native plugins.

Pumpkin's configuration schema has changed between upstream releases. This integration targets the single-file schema used by official release 0.2.1 and compatible builds. Review the upstream release notes before upgrading or editing advanced fields. The upstream server may reject invalid configuration values even when TOML syntax is valid.

## Worlds and backups

Pumpkin uses its own world storage and has changed its default chunk format. PocketMC does not offer Java/Bedrock world ZIP imports or community map installation into Pumpkin instances. Use a compatible Pumpkin world or upstream conversion guidance when migrating worlds.

Stop Pumpkin before taking a backup. Offline backups avoid copying files while the native server is writing them. Legacy RCON is not available in current Pumpkin releases; console commands handle normal shutdown. Updating the server requires it to be stopped and uses PocketMC's existing staged update and recovery workflow.

Portable exports retain the configured chunk format and its storage settings, including Anvil, Linear, or Pump. Default exports rebuild a safe configuration without management credentials; access-control JSON in `data/` and native plugin files require the sensitive-configuration option. Imported folders with an unknown release must be updated to a known Pumpkin release before they can be exported for exact reconstruction.

## Player identities

Pumpkin stores operators, bans, and whitelist entries in `data/`. Java and Bedrock identities must be resolved by the running native server; PocketMC does not invent a Mojang or offline UUID for a Bedrock player. Start Pumpkin and use `whitelist add <player>` in its console, then refresh the player list. Existing known whitelist entries can be viewed and removed while stopped.

## Upstream references

- [Official project and releases](https://github.com/Pumpkin-MC/Pumpkin)
- [Release 0.2.1 source configuration](https://github.com/Pumpkin-MC/Pumpkin/blob/0.2.1%2B26.3-26.51/crates/pumpkin-config/src/lib.rs)
- [Java networking configuration](https://github.com/Pumpkin-MC/Pumpkin/blob/0.2.1%2B26.3-26.51/crates/pumpkin-config/src/networking/java.rs)
- [Bedrock networking configuration](https://github.com/Pumpkin-MC/Pumpkin/blob/0.2.1%2B26.3-26.51/crates/pumpkin-config/src/networking/bedrock.rs)
- [Playit tunnel availability and Premium requirements](https://playit.gg/pricing/)
- [Playit agent releases](https://github.com/playit-cloud/playit-agent/releases)
- [Playit 1.2.0 NXS setup requirements](https://github.com/playit-cloud/playit-agent/releases/tag/v1.2.0)
