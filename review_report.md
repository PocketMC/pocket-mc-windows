# PocketMC.Desktop - Codebase Review & Audit Report

**A. Executive Summary**
*   **What it does:** PocketMC is a Windows WPF desktop application for managing local Minecraft server instances (Java, Bedrock, PocketMine-MP, Cross-play/Geyser). It handles downloading server software, managing app-local Java/PHP runtimes, providing a dashboard/console UI, managing plugins/mods via marketplaces (Modrinth, CurseForge), enabling public access via Playit.gg, and offering local/cloud backup capabilities.
*   **Overall maturity:** The project is a relatively mature, full-featured desktop application with robust functionality for managing the full lifecycle of Minecraft servers locally. It shows good architectural separation (using Dependency Injection, MVVM for the UI, and separate feature modules). However, it exhibits a heavy reliance on "God Services" and concurrent dictionaries for state, and it lacks comprehensive test coverage for some of the more complex interactions, especially given its platform-specific (Windows) nature.
*   **Biggest Risks:**
    1.  **Process Management & Handle Leaks:** Pervasive use of `Process.Start` without proper `Dispose()` calls throughout the UI and utility code (except in specific robust areas like `ServerLaunchConfigurator`), leading to handle leaks over time.
    2.  **Concurrency & State Management:** The `ServerLifecycleService` and `ServerProcessManager` are "God objects" that heavily rely on global `ConcurrentDictionary` instances, making race conditions during instance lifecycle events (start/stop/restart) a significant risk, especially during rapid state changes or rapid user inputs.
    3.  **Missing Test Coverage for Critical Paths:** While tests exist, complex flows like the full server lifecycle, Bedrock/Forge process launching, UWP loopback (`CheckNetIsolation.exe`), and backup restorations are hard to verify outside of Windows environments, leaving large gaps in automated confidence for core features.

**B. Bug and Risk Findings**

1.  **Resource / Handle Leaks on `Process.Start`**
    *   **Severity:** Medium
    *   **Evidence:** `PocketMC.Desktop/Features/Shell/AboutPage.xaml.cs`, `DashboardActionsVM.cs`, `SettingsAddonsVM.cs`, `CloudBackups/Providers/*` all use `Process.Start` to open links or explorer windows but do not dispose of the returned `Process` object (e.g., `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });`). Memory indicates "Ensure all `Process.Start` calls are properly disposed by wrapping them in `using var`".
    *   **Why it matters:** In a long-running desktop app, constantly leaking process handles when users click links or open folders will eventually degrade performance or lead to resource exhaustion.
    *   **Suggested fix:** Create a central `ProcessHelper.OpenUrl(string)` and `ProcessHelper.OpenFolder(string)` utility that wraps the `Process.Start` call in a `using` block or explicitly disposes it immediately for shell execute scenarios, or apply `using var` locally.

2.  **Flaky File Writes / Race Conditions**
    *   **Severity:** Medium
    *   **Evidence:** `GeyserProvisioningService.cs` (line 243) uses `File.WriteAllText(guidePath, ...)`, and `DiagnosticReportingService.cs` uses `File.WriteAllText` in several places. Memory states: "To prevent file write flakiness and race conditions in critical paths, use `FileUtils.AtomicWriteAllText`... instead of the standard `File.WriteAllText`."
    *   **Why it matters:** Direct file writes can fail if an antivirus scans the file or if two operations happen simultaneously. `DiagnosticReportingService` might be acceptable since it writes to temporary folders, but `GeyserProvisioningService` writes to the instance folder while the server is likely launching.
    *   **Suggested fix:** Replace `File.WriteAllText` with `FileUtils.AtomicWriteAllText` in `GeyserProvisioningService.cs` and other non-temporary write locations.

3.  **Missing `ZipFile.ExtractToDirectory` Vulnerability Check**
    *   **Severity:** Low / Mitigated (but needs auditing)
    *   **Evidence:** The codebase has a custom `SafeZipExtractor` built to prevent Zip Slip, which is correctly used in `BackupService.cs`.
    *   **Why it matters:** Zip Slip allows arbitrary file overwrite.
    *   **Suggested fix:** Keep enforcing the use of `SafeZipExtractor` via linting rules or code reviews to ensure new code doesn't regress to using `ZipFile.ExtractToDirectory`.

4.  **"God Object" State Management (`ServerLifecycleService`)**
    *   **Severity:** Low (Architectural Debt)
    *   **Evidence:** `ServerLifecycleService` manages `_consecutiveRestarts`, `_lastStartTime`, `_restartCancellations`, `_sessionStartTimes`, and `_startLocks` as concurrent dictionaries keyed by Instance ID.
    *   **Why it matters:** This centralizes state that should belong to an `InstanceContext` or the `ServerProcess` itself. It makes the service extremely complex and prone to synchronization bugs if logic paths diverge.
    *   **Suggested fix:** Refactor instance state into an `InstanceContext` object tied to the lifecycle of the instance, rather than tracking everything in decoupled dictionaries inside a singleton service.

5.  **UWP Loopback Exemption Failure Handling**
    *   **Severity:** Low
    *   **Evidence:** `AppSettingsPage.xaml.cs` handles UWP loopback via `UwpLoopbackHelper`, which invokes `CheckNetIsolation.exe`. The error handling is generic ("Could not apply the exemption...").
    *   **Why it matters:** `CheckNetIsolation.exe` requires elevation. If a user denies UAC, it fails gracefully, but if the tool is completely missing or the OS is broken, the user gets little diagnostic info.
    *   **Suggested fix:** Improve logging around the exact exit code or standard error output of `CheckNetIsolation.exe`.

**C. Testing Audit**
*   **What is tested well:** The project has tests for utility classes, parsing (e.g., `LogLineClassifierTests`, `PlayerListParserTests`), serialization/manifests, and security (e.g., `PathSafetyTests`, `BackupManifestSecurityTests`).
*   **What is untested (or untestable in CI):** Real Windows process execution (`JobObject`, `ServerProcessManager`), WPF UI behaviors, and actual Modrinth/Playit.gg API integrations (likely mocked, which is good, but lacks e2e confidence). Tests cannot run in standard Linux environments without MSBuild properties (`-p:EnableWindowsTargeting=true`) and even then, `Microsoft.WindowsDesktop.App` tests fail.
*   **Missing test categories:** E2E UI testing (e.g., with Appium or WinAppDriver) and integration tests for the `ServerLifecycleService` orchestrating a full dummy process.
*   **Highest priority tests to add:** Comprehensive mocked integration tests covering the state machine in `ServerLifecycleService` (Start -> Crash -> Auto-Restart -> Stop).

**D. Product and UX Issues**
*   **Friction points:** The Forge/NeoForge installer flow blocks the UI or produces thousands of lines of output, which the app attempts to sample/throttle. If it fails, debugging the installer is tough for non-technical users.
*   **Confusing behaviors:** "External Backup Directory" silently copies ZIPs. If the drive is full, it logs an error but the local backup still succeeds, potentially giving false confidence that the off-site backup worked.
*   **Missing recovery flows:** If `playit.exe` gets stuck or leaves a stale process, the app tries to kill it, but a "Force Reset Networking" button might be needed if things get corrupted.

**E. Release Readiness**
*   **Blockers:** The `Process.Start` handle leaks should be patched before a major release, as they affect the stability of the long-running UI shell.
*   **Nice-to-haves:** Refactoring `ServerLifecycleService` dictionaries into instance contexts. Using `AtomicWriteAllText` everywhere instead of standard file writes for instance configs.
*   **Packaging:** Uses Velopack, which is excellent for WPF. The CI pipeline seems to handle builds well.

**F. Action Plan (Top 5 fixes)**
1.  **Wrap all `Process.Start` calls for URLs/Folders in `using var` blocks.**
2.  **Replace `File.WriteAllText` with `FileUtils.AtomicWriteAllText` in `GeyserProvisioningService` and `DiagnosticReportingService`.**
3.  **Refactor Backup external replication** to surface errors to the UI or manifest if the external copy fails, rather than just logging it.
4.  **Add stronger error reporting to the UWP Loopback helper** to capture `CheckNetIsolation.exe` stderr.
5.  **Refactor `ServerLifecycleService`** to group instance state dictionaries into a cohesive `RunningInstanceContext` class to reduce race condition surface area.

**Verdict:** Ship it (with minor patches). The architecture is surprisingly solid for a desktop app, with good security practices (PathSafety, LogSanitizer) already in place. The identified issues are mostly technical debt and minor resource leaks, not catastrophic failures.