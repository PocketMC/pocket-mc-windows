# Security and recovery corrections

This change addresses the 24 findings reviewed against commit `daacbd3`. It preserves Windows/.NET 8 application targeting while aligning build tooling with the .NET 10 SDK in `global.json`.

## User-visible changes

- Add-on removal accepts only recognized inventory entries under add-on folders, requires an offline server, and cannot delete arbitrary instance files or sibling directories.
- Remote settings validate values and reject control characters. Properties files preserve Java escaping, literal hashes, Unicode escapes, and continuations; a hash within a value is no longer interpreted as an inline comment.
- Password/username changes invalidate existing cookies. Deleted or restricted users lose established console connections. Sign out revokes the session and closes its console connections.
- Public-tunnel modes bind the origin to loopback and issue Secure cookies. LAN-only mode remains explicit HTTP access; use it only on a trusted network. File-manager and private-export permissions confer access to sensitive configuration and executable server content.
- Backups include the configured Java world, sibling dimensions, and detected additional world roots. New archives contain a world-layout manifest. Custom backup folders can be shared: names and retention are scoped by immutable instance IDs.
- An unreadable required world file aborts the backup. A partial candidate is never promoted. A checksum does not label a backup as restore-verified.
- Live Java backups require RCON and a confirmed `save-all flush`. Bedrock and PocketMine backups require an offline server until an engine-specific snapshot protocol is validated. An inability to resume saving is reported as a critical failure.
- Restores and world imports use staging, durable rollback journals, and same-volume promotion. Nested legacy single-world archives are normalized to their actual world root. Existing legacy flat backups remain readable. Legacy Java backups do not contain missing dimensions retroactively.
- Updates atomically commit artifacts and metadata with rollback. Each staging operation has a unique directory. Stopping during runtime setup cancels startup before a child can launch.
- Exports default to sharing mode: plugin/configuration secrets and player administration files are excluded, RCON secrets are removed, and RCON is disabled. The explicit **Private backup** checkbox includes sensitive configuration. Unknown configuration embedded in world data or executable add-ons can still be sensitive; inspect before sharing.
- Cloud folders now use immutable instance IDs. Listing also discovers legacy name-ID folders, including folders from before a rename, and follows pagination.
- Root relocation drains coordinated operations, blocks new starts/mutations, rewrites contained instance paths, and verifies persisted settings before deleting original data.

## Finding-to-fix map

| Review finding | Resolution | Verification |
|---|---|---|
| 1. Add-on deletion boundary | Inventory lookup, shared containment and offline operation gate | Real HTTP regression cases for siblings, config, root and add-on root |
| 2. Property injection | DTO validation and properties escaping | Real HTTP rejection; round-trip codec tests |
| 3. Incomplete verified backups | Required-file failures abort; atomic candidate promotion; no restore-verification claim | Locked region regression |
| 4. Missing world roots | Shared WorldInventory used by backup, export and primary-world resolution | Custom world + Paper dimensions |
| 5. Unconfirmed live snapshot | Confirm RCON disable/flush; reject unsupported live modes; verify resume | Snapshot contract and offline regression coverage; native engine validation still needed |
| 6. Backup overlap/collision | Shared operation gate, unique names and ID-scoped retention | Two instances sharing one destination |
| 7. Startup after Stop | Propagated cancellation and locked pre-launch check; conditional process removal | Delayed provisioning regression |
| 8. Destructive world import | Stage replacement and durable rollback transaction | Multi-root failure and crash-journal recovery |
| 9. Secret-bearing export | Shareable default plus explicit private mode | Inspect both generated ZIP variants |
| 10. Password reset sessions | Cookie credential-version binding | Old cookie receives unauthorized |
| 11. Revoked console socket | Revalidate before sends and periodically | Real established WebSocket revocation |
| 12. Links/junctions | Reject reparse components and linked recursive entries | Symlink/junction archive regression |
| 13. Download integrity | Range/length/empty checks, resource binding, digest-gated resume, atomic replacement, cancellation propagation | Invalid range and empty-response regressions |
| 14. Nontransactional update | Journaled artifacts + metadata commit and unique staging | Shared transaction failure/recovery tests; Windows build |
| 15. Nested restore | Normalize an unambiguous legacy world root | Nested archive restore regression |
| 16. Telemetry opt-out | Gate country, install and heartbeat requests before sending | First-run opt-out makes no HTTP requests |
| 17. Lost cloud history | Lock the entire read/modify/write | Concurrent history writers |
| 18. Rename-dependent cloud lookup | Immutable ID identity and legacy discovery | Build/provider contracts; real cloud account validation remains required |
| 19. Thread-affine power API | Dedicated persistent owner thread | Calls from different callers use one owner |
| 20. Root relocation | Drain operations, invalidate queued stale work, migrate paths and verify persistence | Maintenance gate tests; Windows workflow validation remains required |
| 21. Wrong Java requirement | Java 1.21.x resolves to Java 21; calendar releases use 25 | Version table regression |
| 22. Incorrect properties parsing | Java logical-line, separator and escape semantics | Literal hash and escaped-value round trips |
| 23. SDK mismatch | Valid SDK baseline and matching setup-dotnet; pinned vpk tool | Cross-build and Windows CI |
| 24. Release commit race | Checkout tested head SHA; push/repository guard and serialized publication | Workflow inspection and CI |

## Recovery behavior

File transactions write `.pocketmc-file-transaction.json` before moving any original. Uncommitted journals restore original entries on the next start or managed file operation. Committed journals only finish cleanup. Do not delete `.pocketmc-rollback-*` directories manually while a journal exists. If journal validation fails, preserve the files and investigate from desktop logs.

Lexical and reparse-point checks reject existing unsafe links. They do not sandbox an executable plugin or a malicious process running as the same Windows account; such a process already has that account's filesystem privileges.

A complete archive, matching hash, successful extraction and a verified server boot are separate checks. Perform periodic restore drills on a disposable instance. Cloud replication is not a replacement for a valid local recovery point.

## Validation limits

Linux can execute the pure/application layers and embedded ASP.NET host, and cross-compile the Windows desktop application. DPAPI, job objects, Windows file locking, WPF interaction, sleep/resume, physical BDS/PocketMine behavior and actual cloud accounts require Windows or provider-specific validation. The unmodified baseline already has Linux failures in those Windows-specific suites; compare against the baseline rather than labeling them fixed or skipping them silently.

The application still targets .NET 8. Plan a supported runtime upgrade before its 2026-11-10 support end date; that migration is separate from these behavioral corrections.
