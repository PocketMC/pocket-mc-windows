# PocketMC Codebase Review & Risk Audit

## A. Executive Summary
- **What the project does:** PocketMC is a Windows-focused desktop app for creating and managing Minecraft servers (Java, Bedrock, Cross-play) without needing CLI usage. It features public tunneling via Playit.gg, managed JRE/PHP runtimes, real-time observability metrics, backup management, and plugin/mod installation capabilities. It aims for a zero-config, "just works" experience for casual users, while retaining depth for power users.
- **Overall maturity:** The codebase shows a high degree of maturity for a desktop application. It utilizes modern .NET 8, a clean architecture, dependency injection, and robust Windows-specific implementations like Job Objects for process tree management and safe file handling abstractions.
- **Biggest risks:** The biggest immediate risks are process handle leaks and resource exhaustion caused by un-disposed `Process` objects (such as those used for launching URLs or explorer). A high-severity security risk exists via a potential Zip Slip vulnerability in Bedrock Add-on handling. Lastly, race conditions could corrupt critical JSON and text configurations since standard `File.WriteAllText` is still used in several places instead of the atomic file writing utilities present in the codebase.

## B. Bug and risk findings

### 1. Process handle and resource leaks
- **Severity:** High
- **Evidence:** Dozens of instances where `Process.Start(...)` is invoked without a `using` block or explicit `.Dispose()` call. Examples include `NewInstancePage.xaml.cs:563`, `DashboardActionsVM.cs:261`, `AboutPage.xaml.cs`, `TunnelPage.xaml.cs`, etc.
- **Why it matters:** In a long-running desktop application, leaking handles for `explorer.exe` or web browsers eventually causes system resource exhaustion, potentially crashing the application or the OS environment over time.
- **Suggested fix:** Wrap all `Process.Start` calls in `using var proc = Process.Start(...)` or implement a centralized shell execution utility that automatically cleans up the handles.

### 2. Potential Zip Slip Vulnerability in Add-on Extraction
- **Severity:** Critical
- **Evidence:** `BedrockAddonInstaller.cs` on line 71 executes `ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true)`.
- **Why it matters:** A malicious Bedrock `.mcpack` or `.mcaddon` archive could use relative paths (`../../`) to write executable files into arbitrary directories (like the Windows Startup folder), bypassing the extraction bounds. The repo has a `SafeZipExtractor` which prevents this, but it is not used here.
- **Suggested fix:** Replace `ZipFile.ExtractToDirectory` with `await SafeZipExtractor.ExtractAsync(sourceFilePath, tempDir)`.

### 3. Race conditions and corruption during file writes
- **Severity:** Medium
- **Evidence:** `File.WriteAllText` is used in multiple locations like `BedrockAddonInstaller.cs` (lines 254, 269), `GeyserProvisioningService.cs` (line 243), `DiagnosticReportingService.cs` (lines 62, 73), `PlayitAgentService.cs` (line 385).
- **Why it matters:** Standard text writing is not atomic. If the application crashes, loses power, or a race condition occurs from multiple threads saving settings/worlds concurrently, these configuration files may become truncated, empty, or corrupted.
- **Suggested fix:** Standardize the use of `FileUtils.AtomicWriteAllText` and `FileUtils.AtomicWriteAllTextAsync` across the entire project for all configuration and JSON file modifications.

### 4. Unhandled Exception on Missing Playit.gg Secrets
- **Severity:** Low
- **Evidence:** `PlayitAgentService.cs` (line 385) writes to a `.toml` file without ensuring the directory structure exists or handling potential locked file exceptions from the playit daemon.
- **Why it matters:** Flaky file interactions with the Playit daemon configuration might leave the user unable to connect to the tunnel, breaking a core networking feature without clear feedback.
- **Suggested fix:** Add robust exception handling, directory creation, and atomic writes when managing the Playit TOML configuration.

## C. Testing audit
- **What is tested well:** Core business logic, networking robustness (port diagnostics, lease registries), path traversal safety (`ModpackPathTraversalTests`), and log sanitization for PII (`LogSanitizerTests`).
- **What is untested:** The `Process.Start` leakages and UI interaction behaviors. Furthermore, no automated integration tests interact directly with external binaries (like launching real `CheckNetIsolation.exe` or `java.exe`).
- **Missing test categories:** UI Automation tests (using tools like FlaUI), integration tests mapping the real file system and job object behavior, and specific Roslyn analyzer checks for `IDisposable` leaks on `System.Diagnostics.Process`.
- **Highest priority tests to add:**
  1. Unit tests around `BedrockAddonInstaller` ensuring malicious archives are caught.
  2. Roslyn or build-time analyzer enforcements ensuring `Process.Start` results are assigned to a `using` variable.

## D. Product and UX issues
- **Friction points for users:** Add-on extraction silently logs warnings if an invalid zip/manifest is processed instead of actively warning the user via UI dialogs. Non-technical users will assume it installed properly but experience nothing in-game.
- **Confusing behaviors:** UWP loopback uses `CheckNetIsolation.exe` but if the user lacks permissions or Defender blocks it, the error surfacing may be opaque to casual users.
- **Missing flags, settings, or recovery flows:** Missing a clear "Repair/Reset" UI flow for corrupted instance configurations (like damaged `server.properties`).

## E. Release readiness
- **Blockers before production use:**
  - Fix the Zip Slip vulnerability in `BedrockAddonInstaller.cs`.
  - Fix the `Process.Start` resource leaks.
- **Nice-to-have improvements:** Replace all generic `File.WriteAllText` instances with the existing `FileUtils.AtomicWriteAllText` to drastically improve configuration reliability.
- **Any packaging, install, or CI gaps:** The CI requires `-p:EnableWindowsTargeting=true` for non-Windows agents building the project, but GitHub Actions runner environments (like `windows-latest`) might mask cross-platform .NET build issues. Installer scripts or actions (like Velopack) should incorporate code signing (Authenticode) prior to distribution to avoid Windows SmartScreen warnings.

## F. Action plan

**Top 5 fixes in priority order:**
1. **Fix Zip Slip:** Replace `ZipFile.ExtractToDirectory` with `await SafeZipExtractor.ExtractAsync` in `BedrockAddonInstaller.cs`.
2. **Patch Handle Leaks:** Audit and refactor all `Process.Start` calls to be wrapped in `using var proc = Process.Start(...)`.
3. **Ensure Atomic Writes:** Standardize file writing by replacing `File.WriteAllText` with `FileUtils.AtomicWriteAllText` across the repository.
4. **Improve Playit Config Reliability:** Add directory creation checks and exception handling around Playit TOML writes in `PlayitAgentService.cs`.
5. **Add UI Feedback for Addons:** Enhance error handling and user feedback in `BedrockAddonInstaller` to visibly alert users of invalid packs.

---

### Small patch plan for top 3 issues

1. **Process Handle Leaks:** Run a global find-and-replace for `Process.Start(` outside of using blocks. Update them to:
   ```csharp
   using var process = Process.Start(...);
   ```
2. **Zip Slip:** In `BedrockAddonInstaller.cs`:
   ```csharp
   // Replace
   await Task.Run(() => ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true), ct);
   // With
   await SafeZipExtractor.ExtractAsync(sourceFilePath, tempDir);
   ```
3. **Atomic File Writes:** In `BedrockAddonInstaller.cs`, `DiagnosticReportingService.cs`, and `SummaryStorageService.cs`, change `File.WriteAllText(path, contents)` to `FileUtils.AtomicWriteAllText(path, contents)`.

### Verdict
**NOT YET.** The critical Zip Slip vulnerability in add-on installation and high-severity handle leaks must be addressed prior to a stable public release.

### Manual QA Checklist for Windows
- [ ] Create a new Bedrock instance and install a dummy `.mcpack` add-on. Verify it extracts correctly.
- [ ] Monitor the application process in Task Manager; open the dashboard, launch external links, and ensure handle count does not steadily increase.
- [ ] Manually terminate the main PocketMC process via Task Manager while a server is running and verify the server child processes are correctly terminated by the Job Object.
- [ ] Use the "Fix Bedrock LAN" feature to trigger `CheckNetIsolation.exe` and confirm it elevates correctly and executes without silent failure.
