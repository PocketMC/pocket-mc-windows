### A. Executive summary
- **What the project does:** PocketMC is a Windows desktop application built with C#/.NET 8 that manages Minecraft Java, Bedrock, and Cross-play servers. It features automatic runtime provisioning, a dashboard with metrics, public tunneling via playit.gg, and built-in plugin/addon marketplace integration.
- **Overall maturity:** The application is architecturally mature and feature-rich. It utilizes modern WPF design, uses Dependency Injection heavily, has excellent process management (e.g. JobObjects for process tree cleanup), robust networking integrations (playit API, RCON clients), and extensive AI/Intelligence logging hooks.
- **Biggest risks:** The biggest risks stem from unvalidated edge cases with file extraction and data cleanup paths, race conditions in process handling, and weak dependency health resilience. Specifically, the backup creation logic reads files dynamically without locking safeguards if the server is actively modifying them. Additionally, paths loaded for instance setups and Mod/Addon manifest extraction are susceptible to Zip-Slip attacks in several unvalidated spots. Finally, Playit tunnel error handling assumes network conditions always resolve rather than aggressively bounding retries for rate limits.

### B. Bug and risk findings

**1. Zip-Slip Vulnerability in Bedrock Addon Installer**
- **Severity:** High
- **Evidence:** `PocketMC.Desktop/Features/Mods/BedrockAddonInstaller.cs` extracts `.mcaddon`/`.mcpack` ZIPs directly using `ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true)`. Unlike `BackupService.cs` which uses the safe `SafeZipExtractor`, the Addon installer trusts user-uploaded addon packs entirely.
- **Why it matters:** An attacker could craft a malicious `.mcpack` with traversal paths (e.g., `..\..\..\Windows\System32\...`) to overwrite sensitive files when the user imports an addon.
- **Suggested fix:** Change `ZipFile.ExtractToDirectory` to use `SafeZipExtractor.ExtractAsync` in `BedrockAddonInstaller.cs`.

**2. Missing Lock Avoidance in World Backup Replication**
- **Severity:** Medium
- **Evidence:** `BackupService.cs` attempts to copy the generated ZIP to the external directory via `File.Copy(zipPath, destinationPath, true);`. However, if the replication directory points to an active sync folder (like Google Drive) or is locked by another process, `File.Copy` will throw an unhandled exception, causing the backup thread to fail and potentially skip pruning.
- **Why it matters:** Users relying on external backups will silently stop getting new backups if the destination gets locked or has permission issues.
- **Suggested fix:** Wrap the `File.Copy` replication in a retry loop or standard try-catch that logs the warning but still completes the backup metadata save and pruning.

**3. Incomplete PlayIt Limit Handling in Instance Tunnel Orchestrator**
- **Severity:** Medium
- **Evidence:** `InstanceTunnelOrchestrator.cs` correctly catches `TunnelResolutionResult.TunnelStatus.LimitReached` but shows an error dialog *inside a background loop dispatch*. If a user tries to start 10 servers and hits the limit, they will be spammed with 10 error dialogs concurrently.
- **Why it matters:** Causes severe UX friction and UI thread blocking if auto-start instances trigger the limit.
- **Suggested fix:** Implement a debouncer or application-level state flag in `InstanceTunnelOrchestrator.cs` to track if the limit was reached recently, preventing redundant dialogs.

**4. Partial Path Traversal checking in FileUtils**
- **Severity:** Low
- **Evidence:** `FileUtils.DeleteDirectoryWithRetryAsync` and other file methods rely heavily on string manipulation.
- **Why it matters:** Although `PathSafety.cs` exists, it is not consistently applied to all file inputs.
- **Suggested fix:** Audit file handling services to ensure `PathSafety.ValidateContainedPath` is used correctly.

**5. Obsolete PluginScanner is referenced in tests**
- **Severity:** Low
- **Evidence:** Building the test project shows warnings that `PluginScanner` is obsolete and replaced by `IAddonManager` implementations.
- **Why it matters:** Technical debt; tests are verifying deprecated code.
- **Suggested fix:** Remove or update `PluginScannerTests.cs` to use the new implementations.

### C. Testing audit
- **What is tested well:** Port collision detection, networking rules (`PortPreflightServiceTests`, `PortProbeServiceTests`), and basic navigation stacks.
- **What is untested:** RCON client edge cases, JobObject process killing, File extraction safety, AI Summarization resilience.
- **Missing test categories:** E2E instance creation lifecycle (mocking the JAR download), Bedrock provider JSON parsing logic.
- **Highest priority tests to add:** Tests for `BedrockAddonInstaller` extraction, and `BackupService` edge cases (e.g., empty zip handling, external replication failure).

### D. Product and UX issues
- **Friction points for users:** Java installer output for Forge/NeoForge can be overwhelming, even with the 50-line throttling. If it fails, the user just gets a generic "installer failed" without seeing the exact Java stack trace easily.
- **Confusing behaviors:** "Fix Bedrock LAN" uses `checknetisolation` which requires UAC elevation. If the user denies UAC, the app might not cleanly report why LAN isn't working.
- **Missing flags, settings, or recovery flows:** There's no obvious way to clear the "Runtime Cache" (downloaded Java/PHP binaries) from the UI if a download corrupts but passes initial length checks.

### E. Release readiness
- **Blockers before production use:** Zip-Slip in Bedrock addons MUST be fixed.
- **Nice-to-have improvements:** Switch obsolete test classes.
- **Any packaging, install, or CI gaps:** The `.NET` test runner fails in standard Ubuntu environments because the project requires `Microsoft.WindowsDesktop.App`. CI must run on Windows agents (which `production-build.yml` likely does, based on the badge).

### F. Action plan
1. Replace `ZipFile.ExtractToDirectory` in `BedrockAddonInstaller.cs` with `SafeZipExtractor.ExtractAsync`.
2. Add rate-limit state to `InstanceTunnelOrchestrator.cs` to prevent dialog spam.

**Manual QA Checklist (Windows):**
- [ ] Create a Vanilla Bedrock instance.
- [ ] Import a maliciously crafted `.mcpack` containing `../` paths; verify it rejects or cleans paths.
- [ ] Setup external backups to a locked directory; verify backup still prunes local copies.
- [ ] Hit Playit free tier tunnel limit and start 2 servers; verify only one dialog appears.