# Remote News Publishing Guide

PocketMC news is published as self-contained plain-text files in the repository's `news/` directory. There is no central index or remote metadata file. To publish, add one `.txt` file, commit it, and get it onto the repository's `master` branch. A file pushed only to a feature branch is not visible to installed apps until it reaches `master`.

## Quick Publish

1. Create `news/<descriptive-name>.txt`, for example `news/2026-10-02-server-maintenance.txt`.
2. Add the required metadata and content using the template below.
3. Validate the file locally with the news parser/test, then commit and push or merge it to `master`.

Filename rules: use a simple `.txt` filename containing letters, numbers, dots, underscores, or hyphens. Filenames do not define news identity or ordering; `id` and `published` do.

## Template

```text
---
id: server-maintenance-2026-10-02
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

- `id` [required]: Permanent unique identifier, independent of the filename. Use letters, numbers, `.`, `_`, or `-`; maximum 128 characters. Do not reuse or rename an ID after publishing.
- `type` [required]: One of `announcement`, `quick-fix`, `critical`, `maintenance`, `update`, or `security`. Type is descriptive; it does not alone decide popup behavior.
- `priority` [required]: One of `normal`, `important`, or `critical`. The app displays priority independently from type; it does not by itself decide whether a popup appears.
- `popup` [required]: `true` or `false`. `true` makes the item eligible for a popup until that user reads/acknowledges it; set it independently of `type` and `priority`.
- `published` [required]: Publication time in UTC, written as an ISO timestamp ending in `Z`, such as `2026-10-02T18:00:00Z`. This controls ordering, not the filename.

These metadata fields are optional:

- `expires` [optional]: UTC timestamp later than `published`. An expired item is not an active notice and will not popup; previously cached history is retained. Leave blank for no expiry.
- `minVersion` [optional]: Inclusive minimum PocketMC version, for example `1.9.9`. Leave blank for no minimum.
- `maxVersion` [optional]: Inclusive maximum PocketMC version, for example `1.9.9.1`. Leave blank for no maximum.

When both version limits are present, `minVersion` must not exceed `maxVersion`. Versions are compared numerically. An item without either limit applies to all PocketMC versions.

Every metadata key must occur at most once. Unknown keys, invalid values, missing required fields, invalid UTC timestamps, and reversed version ranges cause the file to be skipped and logged.

## Content Elements

Content is plain text, not Markdown or HTML. Use only these directives; PocketMC chooses the visual style.

- `title: Text` [required, exactly once]: The title can be inline after the colon or on following lines.
- `subtitle: Text` [optional]: Short supporting line.
- `heading: Text` [optional, repeatable]: Section heading.
- `paragraph:` [optional, repeatable]: Normal prose follows on the next lines and can span multiple lines.
- `list:` [optional, repeatable]: Bullet list. Each item is a separate line beginning `- `.
- `numbered-list:` [optional, repeatable]: Ordered steps. Each item is a separate line beginning `1. `, `2. `, and so on.
- `warning:` [optional, repeatable]: Caution callout.
- `important:` [optional, repeatable]: Important-notice callout.
- `code:` [optional, repeatable]: Monospaced displayed text. It is never executed.
- `link: Label | https://example.com` [optional, repeatable]: Clickable link. Only absolute `http` and `https` links are accepted.
- `divider:` [optional, repeatable]: Horizontal separator; no value is allowed after the colon.

Separate content blocks with a blank line. Text blocks continue until a blank line or the next directive. Lists consume consecutive matching bullet/number lines. Every file must contain exactly one title; other content blocks are optional. Unsupported directives, duplicate titles, and HTML-like markup in text blocks are rejected.

## Fetching, Cache, and Read State

PocketMC lists `.txt` files through the GitHub Contents API and checks Git blob hashes. It reads a bounded metadata prefix for new or changed files, then downloads and verifies full text only for eligible items. Items are processed by `published` time, with ID as a deterministic tie-breaker. A failed fetch does not move the cursor past unfinished news.

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