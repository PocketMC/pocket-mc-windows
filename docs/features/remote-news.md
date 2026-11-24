# Remote News Publishing Guide

PocketMC news is published as self-contained plain-text files in the repository's `news/` directory. There is no central index or remote metadata file. To publish, add one `.txt` file, commit it, and get it onto the repository's `master` branch. A file pushed only to a feature branch is not visible to installed apps until it reaches `master`.

## Quick Publish

1. Create `news/<descriptive-name>.txt`, for example `news/2026-10-02-server-maintenance.txt` (or with a language tag like `news/2026-10-02-server-maintenance.zh-CN.txt`).
2. Add the required metadata and content using the template below.
3. Validate the file locally with the news parser/test, then commit and push or merge it to `master`.

Filename rules: use a simple `.txt` filename containing letters, numbers, dots, underscores, or hyphens. Filenames do not define news ordering; `published` does. If the filename ends with a valid language tag before `.txt` (e.g., `.zh-CN.txt`), PocketMC automatically infers the language tag when `lang` is omitted in the header.

## Template

```text
---
id: server-maintenance-2026-10-02
lang:
type: maintenance
priority: important
popup: true
published: 2026-10-02T18:00:00Z
expires: 2026-10-05T00:00:00Z
minVersion:
maxVersion:
---
title: Scheduled Server Maintenance

subtitle: PocketMC service notice

heading: What to expect

paragraph:
PocketMC news delivery will be unavailable during the maintenance window.
Your locally cached news and Minecraft servers will continue to work.

heading: Details

list:
- Starts at 18:00 UTC
- Expected duration is 30 minutes

warning:
No action is required from PocketMC users.

link: Service status | https://status.example.com/
```

Do not add a `read` field to repository news. PocketMC adds `read: true` only to the user's local cached copy when the user reads or acknowledges the item.

In the field lists below, `[required]` and `[optional]` are documentation labels only. Do not type the bracketed labels into a news file. For optional metadata, keep the key and leave its value empty when unused, as shown in the template.

## Metadata Fields

These metadata fields are required in every remote news file:

- `id` [required]: Permanent unique identifier, independent of the filename. Use letters, numbers, `.`, `_`, or `-`; maximum 128 characters. Do not reuse or rename an ID after publishing. All translations of the same announcement share the same `id`.
- `type` [required]: One of `announcement`, `quick-fix`, `critical`, `maintenance`, `update`, or `security`. Type is descriptive; it does not alone decide popup behavior.
- `priority` [required]: One of `normal`, `important`, or `critical`. The app displays priority independently from type; it does not by itself decide whether a popup appears.
- `popup` [required]: `true` or `false`. `true` makes the item eligible for a popup until that user reads/acknowledges it; set it independently of `type` and `priority`.
- `published` [required]: Publication time in UTC, written as an ISO timestamp ending in `Z`, such as `2026-10-02T18:00:00Z`. This controls ordering, not the filename.

These metadata fields are optional:

- `lang` or `language` [optional]: BCP-47 language tag, such as `en-US`, `zh-CN`, `ja-JP`, `es-ES`. If omitted in the header, PocketMC checks if the filename contains a language suffix (e.g. `*.zh-CN.txt`). If neither is present, the item is treated as the default universal/English edition.
- `expires` [optional]: UTC timestamp later than `published`. An expired item is not an active notice and will not popup; previously cached history is retained. Leave blank for no expiry.
- `minVersion` [optional]: Inclusive minimum PocketMC version, for example `1.9.9`. Leave blank for no minimum.
- `maxVersion` [optional]: Inclusive maximum PocketMC version, for example `1.9.9.1`. Leave blank for no maximum.

When both version limits are present, `minVersion` must not exceed `maxVersion`. Versions are compared numerically. An item without either limit applies to all PocketMC versions.

Every metadata key must occur at most once. Unknown keys, invalid values, missing required fields, invalid UTC timestamps, invalid language tags, and reversed version ranges cause the file to be skipped and logged.

## Multi-Language & Translations

PocketMC supports publishing announcements in multiple languages while keeping articles unified:

1. **Shared `id`**:
   All translations of the same news item must use the exact same `id`. For example:
   * `news/2026-10-02-server-maintenance.txt` (`id: server-maintenance-2026-10-02`, default/English)
   * `news/2026-10-02-server-maintenance.zh-CN.txt` (`id: server-maintenance-2026-10-02`, `lang: zh-CN`)
2. **Authoring Localized News**:
   You can specify the language via the `lang:` / `language:` metadata header or by appending the language code to the filename before `.txt` (e.g. `.zh-CN.txt`).
3. **Resolution & Fallback**:
   When a user opens News:
   * PocketMC matches the user's active language first (e.g., `zh-CN`).
   * If an exact regional match is unavailable, it checks for a language family match (e.g., `zh` matching `zh-TW`).
   * If no localized version exists, it cleanly falls back to the default/English edition so users never miss important updates.
4. **Single Read State & Notification**:
   Reading an article marks its logical `id` as read across all translations. Users receive only one popup and one unread count badge per announcement, displayed in their active language.
5. **Live Language Switching**:
   When a user switches their application language in Settings, the News page immediately re-renders all article listings, content, badges, and timestamps in real time.

## Content Elements

Content is plain text, not Markdown or HTML. Use only these directives; PocketMC chooses the visual style.

- `title: Text` [required, exactly once]: The title can be inline after the colon or on following lines.
- `subtitle: Text` [optional]: Short supporting line.
- `heading: Text` [optional, repeatable]: Section heading.
- `paragraph:` [optional, repeatable]: Normal prose follows on the next lines and can span multiple lines.
- `list:` [optional, repeatable]: Bullet list. Each item is a separate line beginning `- `.
- `numbered-list:` [optional, repeatable]: Ordered steps. Each item is a separate line beginning `1. `, `2. `, and so on.
- `warning:` [optional, repeatable]: Caution callout. Callout badges localize dynamically in the app.
- `important:` [optional, repeatable]: Important-notice callout. Callout badges localize dynamically in the app.
- `code:` [optional, repeatable]: Selectable, wrapping monospace text. It is never executed and does not create a nested scrollbar.
- `link: Label | https://example.com` [optional, repeatable]: Clickable link. Only absolute `http` and `https` links are accepted.
- `divider:` [optional, repeatable]: Horizontal separator; no value is allowed after the colon.

Separate content blocks with a blank line. Text blocks continue until a blank line or the next directive. Lists consume consecutive matching bullet/number lines. Every file must contain exactly one title; other content blocks are optional. Unsupported directives, duplicate titles, and HTML-like markup in text blocks are rejected.

## Fetching, Cache, and Read State

PocketMC lists `.txt` files through the GitHub Contents API and checks Git blob hashes. When download mirrors are enabled (`UseDownloadMirrors: true`), news sync requests automatically route through GitHub API mirror tunnels to ensure reliable access in regions with network restrictions.

It reads a bounded metadata prefix for new or changed files, then downloads and verifies full text only for eligible items. Items are processed by `published` time, with ID as a deterministic tie-breaker. A failed fetch does not move the cursor past unfinished news.

Sync runs in the background at startup and every six hours. Users can also select **Refresh** on the News page. Cached news remains available offline.

The local cache is under `%LOCALAPPDATA%\PocketMC\news\`, beside the existing PocketMC `settings.json` directory:

```text
%LOCALAPPDATA%\PocketMC\news\
	state.json
	cache\
		<published-file>.txt
```

`state.json` stores synchronization progress and remote blob hashes, not read/acknowledgement IDs. When an item is read, PocketMC adds `read: true` to the cached TXT frontmatter only. The repository's published file is never modified; the cached read marker survives app restarts and updates. An unread popup is offered again until the user presses **Got it**.

Remote news is untrusted input: no scripts, HTML rendering, executable content, or automatic commands are supported. Links open through the system browser only after the HTTP/HTTPS scheme is checked.