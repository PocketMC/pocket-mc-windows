# Remote News

PocketMC reads self-contained plain-text news files from the repository's `news/` directory. Publishing an item requires only adding a `.txt` file and pushing it to `master`; there is no repository index or remote JSON news document.

Each file begins with YAML-like frontmatter between `---` lines:

```text
---
id: unique-permanent-id
type: announcement
priority: normal
popup: false
published: 2026-09-29T18:00:00Z
expires:
minVersion:
maxVersion:
---
title: Announcement title

heading: Section heading

paragraph:
Plain text content. Blank lines separate content blocks.

list:
- First item
- Second item

numbered-list:
1. First step
2. Second step

warning:
Important plain-text notice.

important:
Additional plain-text notice.

code:
Displayed command or example; PocketMC never executes it.

link: PocketMC website | https://pocketmc.github.io/

divider:
```

Supported `type` values are `announcement`, `quick-fix`, `critical`, `maintenance`, `update`, and `security`. Supported priorities are `normal`, `important`, and `critical`. `published` must be UTC. `expires`, `minVersion`, and `maxVersion` are optional. Version limits are inclusive. IDs must be unique, permanent, and independent of the filename.

The app lists repository TXT files and uses Git blob hashes as a local discovery cursor. It reads bounded frontmatter first, then downloads full text only for relevant new or changed items. Items are ordered by `published`; a failed read or download does not advance past unfinished newer news. Invalid files are logged and ignored until their repository contents change.

The original TXT and one small state file are stored under `%LOCALAPPDATA%\PocketMC\news\`, alongside PocketMC's existing `settings.json` data directory. Cache and acknowledgement state survive application updates; cached news remains readable offline. A popup stays eligible until the user acknowledges it with **Got it**.

Remote content is rendered as native text and list controls. Only HTTP and HTTPS links are openable. News files cannot define application styling, markup, scripts, or executable actions.