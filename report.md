## A. Executive summary
- **What the project does:** PocketMC is a Windows-focused desktop application built with .NET 8 and WPF. It is a local-first server manager that allows users to download, configure, run, and monitor Minecraft servers (Java, Bedrock, and PocketMine-MP), with additional features for backups, AI session summaries, and playit.gg tunnels.
- **Overall maturity:** The project has strong architectural foundations. It utilizes dependency injection, job objects for clean process tree termination, decoupled MVVM architecture, and safe file abstraction concepts like atomic writes and safe zip extraction. However, implementation of these safety mechanisms is inconsistent, leaving several critical code paths exposed to race conditions, resource leaks, and path traversal vulnerabilities.
- **Biggest risks:** The primary risks are resource leaks (un-disposed processes) and data corruption/loss due to non-atomic file writes. Furthermore, incomplete implementation of the custom `SafeZipExtractor` across all ZIP extraction points leaves the application open to potential Zip Slip path traversal vulnerabilities when processing maliciously crafted modpacks or plugins. Given the app's target audience of both non-technical users and power users, data loss and silent background process leaks are severe blockers.

## B. Bug and risk findings

### 1. Incomplete Zip Slip Protection
- **Severity:** High
- **Evidence:** While `SafeZipExtractor.ExtractAsync` exists and correctly enforces path validation using `PathSafety.ValidateContainedPath`, several areas in the codebase still open zips directly using `ZipFile.OpenRead` (e.g., `ModpackService.cs`, `MarketplaceArchiveInspector.cs`, `ModpackParser.cs`). While `ModpackService.cs` partially secures extractions using `ModpackOverridePolicy` and `PathSafety`, it still uses `entry.ExtractToFile(destinationPath, true)` manually without `SafeZipExtractor`, leaving edge cases like missing directory structures for deep paths potentially unhandled. Other locations read zip entries directly without verifying if `entry.FullName` attempts directory traversal.
- **Why it matters:** Handling malicious modpacks or plugins that contain traversal paths (`../`) in their filenames could lead to arbitrary file writes or reads outside the intended application directory, potentially overwriting system files or executing malicious code.
- **Suggested fix:** Ensure all ZIP file extractions strictly use the project's custom `SafeZipExtractor.ExtractAsync` instead of standard methods, and ensure any inspection of zip entries strictly validates `entry.FullName` with `PathSafety` before attempting to resolve or open the entry streams.

### 2. File System Race Conditions and Flakiness
- **Severity:** High
- **Evidence:** `File.WriteAllText` and `File.WriteAllTextAsync` are used directly in several critical paths, including `GeyserProvisioningService.cs`, `DiagnosticReportingService.cs`, `WhitelistService.cs`, and `App.xaml.cs`.
- **Why it matters:** Standard file write operations are not atomic. If the application crashes, the system loses power, or the background process is terminated by the Job Object during a write operation, the resulting file can be corrupted, left partially written, or completely empty. This can lead to unrecoverable data loss for settings or white-lists.
- **Suggested fix:** Replace all instances of `File.WriteAllText` and `File.WriteAllTextAsync` with the application's existing `FileUtils.AtomicWriteAllText` and `FileUtils.AtomicWriteAllTextAsync` utilities.

### 3. Process Handle Leaks
- **Severity:** Medium
- **Evidence:** Multiple files invoke processes without proper disposal. Specifically, naked `Process.Start(...)` calls without `using var proc = ...` are present in `AboutPage.xaml.cs`, `DashboardActionsVM.cs`, `TunnelPage.xaml.cs`, `DropboxBackupProvider.cs`, `GoogleDriveBackupProvider.cs`, `AppSettingsPage.xaml.cs`, and `MapBrowserPage.xaml.cs`.
- **Why it matters:** Failing to dispose of `Process` instances results in resource and handle leaks over time, which can degrade application performance or eventually cause system-wide handle exhaustion, particularly for users who leave the server manager running for extended periods.
- **Suggested fix:** Ensure all `Process.Start` calls are properly disposed by wrapping them in `using var proc = Process.Start(...)`.

### 4. Silent Null Reference Potential in DependencyResolverService
- **Severity:** Low
- **Evidence:** Compiler warnings during `dotnet test` indicate `warning CS8604: Possible null reference argument for parameter 'serverDir' in 'Task<List<ResolvedDependency>> DependencyResolverService.ResolveAsync...'`
- **Why it matters:** Passing a null `serverDir` could lead to a runtime `NullReferenceException` crashing the application or specific workflows if the parameter is not handled properly within `ResolveAsync`.
- **Suggested fix:** Implement explicit null checks or coalescing for `serverDir` before invoking `DependencyResolverService.ResolveAsync`.

## C. Testing audit
- **What is tested well:** Job Object process management and path safety utilities have some test coverage.
- **What is untested:** End-to-end extraction and parsing of maliciously crafted Zip files (Zip Slip scenarios), and atomic write crash-recovery scenarios.
- **Missing test categories:** E2E workflow tests for Modpack/Addon installation, and tests covering file-locking/race-conditions.
- **Highest priority tests to add:** Tests specifically covering ZIP extraction to guarantee that malicious archives cannot escape the target directory even when directly parsing entries, and tests to verify that `FileUtils.AtomicWriteAllText` correctly recovers from a simulated crash mid-write.

## D. Product and UX issues
- **Friction points for users:** The Forge/NeoForge installation process buffers thousands of lines of stdout and parses them. If the installer fails, it leaves a partial `libraries/` directory which the user must manually understand and clean up, otherwise subsequent runs fail.
- **Confusing behaviors:** Background downloads for specific Java versions (like Java 25) might trigger unexpected network load without user intent if not explicitly highlighted in the UI.
- **Missing flags, settings, or recovery flows:** No clear UI recovery flow for corrupted server properties or configuration files if an atomic write wasn't used previously.

## E. Release readiness
- **Blockers before production use:**
  - Fix non-atomic writes by migrating to `FileUtils.AtomicWriteAllText`.
  - Fix process handle leaks by adding `using var` to all `Process.Start` calls.
  - Consistently apply `SafeZipExtractor` for all Zip operations to eliminate Zip Slip risks.
- **Nice-to-have improvements:** Address compiler warnings related to unused mock events in the test suite (`FakeLifecycleService.OnRestartCountdownTick`, etc.).
- **Any packaging, install, or CI gaps:** The tests fail locally on non-Windows environments (like Linux sandboxes) due to missing `Microsoft.WindowsDesktop.App` framework. CI/CD pipelines need to consistently apply the MSBuild property `-p:EnableWindowsTargeting=true`.

## F. Action plan
1. Find and wrap all naked `Process.Start(...)` calls with `using var proc = ...` to prevent handle leaks.
2. Replace all instances of `File.WriteAllText` and `File.WriteAllTextAsync` with `FileUtils.AtomicWriteAllText` and `FileUtils.AtomicWriteAllTextAsync`.
3. Audit all usages of `ZipFile.OpenRead` and `ZipFile.ExtractToDirectory` and refactor them to use `SafeZipExtractor.ExtractAsync`.
4. Ensure `serverDir` null-safety when calling `DependencyResolverService.ResolveAsync`.
5. Update CI/CD build scripts to consistently include `-p:EnableWindowsTargeting=true` for test runs.