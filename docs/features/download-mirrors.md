# Download Mirrors

PocketMC routes downloads through mirror tunnels when the official host is unreachable or slow.
This is what keeps version downloads, mods, and runtime archives working behind restricted networks.

## How it works

`DownloadMirrorHandler` (a `DelegatingHandler`) sits in front of every download HTTP client
(`PocketMC.Downloads` plus the provider typed clients). For GET requests it builds an ordered
candidate list from `DownloadMirrorService`:

1. **Custom tunnel** — user-defined template from Settings.
2. **jsDelivr** — raw repository files mapped to `https://cdn.jsdelivr.net/gh/<owner>/<repo>@<ref>/<path>`.
3. **GitHub tunnels** — ordered templates from `AppConfig.DownloadMirrorTunnels`
   (`pocketmc.yml` → `download_mirror_tunnels:`), applied to GitHub hosts only.
4. **BMCLAPI** — Mojang launcher metadata and object storage mapped to
   `https://bmclapi2.bangbang93.com`.
5. **Direct URL** — the original address, always tried last.

A candidate is sent with a short response-header timeout (15 s direct, 20 s mirror). On timeout,
connection error, `403`, `408`, `429`, or any `5xx` the next candidate is tried. `404` is treated
as authoritative and returned immediately. A tunnel that fails is put on a 5-minute cooldown so
subsequent downloads skip it; the list still contains it as a last resort.

## Settings

Settings → Downloads & Mirrors:

- **Use download mirrors** — master switch (`AppSettings.UseDownloadMirrors`, default `true`).
- **Custom download mirror** — optional template (`AppSettings.CustomDownloadMirror`):
  - `{url}` placeholder: `https://ghfast.top/{url}`
  - trailing slash prefix: `https://mirror.example/gh/`

Invalid templates (no placeholder/prefix, non-http scheme) are rejected when the field loses focus.

## Config (`pocketmc.yml`)

```yaml
download_mirror_tunnels:
  - "https://ghfast.top/{url}"
  - "https://gh-proxy.com/{url}"
  - "https://ghproxy.net/{url}"
  - "https://hub.gitmirror.com/{url}"
  - "https://gh.llkk.cc/{url}"
  - "https://mirror.ghproxy.com/{url}"
  - "https://github.moeyy.xyz/{url}"
  - "https://cf.ghproxy.cc/{url}"
```

The list is part of the remote-overridable config, so tunnels can be swapped without a release.

## Scope and limitations

- GET only; uploads and API `POST`s go straight to the origin.
- Loopback, raw IP, and `.local` hosts are never rewritten.
- Only GitHub hosts receive GitHub tunnel templates; Paper/Fabric/Forge/NeoForge/PocketMine
  endpoints go direct unless a custom tunnel matches.
- Range requests keep their `Range` header, so resume still works through mirrors.
- SHA-1/256/512 verification in `DownloaderService` is unchanged — a corrupted mirror response is
  rejected and retried.

## Tests

`PocketMC.Infrastructure.Tests/Storage/DownloadMirrorServiceTests.cs` covers candidate ordering,
template parsing, BMCLAPI mapping, cooldown behaviour, and localhost bypass.
