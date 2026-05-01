# PocketMC Codebase Review Report

## A. Executive summary
PocketMC is a Windows desktop application built with C# and .NET 8 (WPF) that serves as a server manager for Minecraft Java, Bedrock, and Cross-play servers. It aims to eliminate command-line usage and manual runtime provisioning for users by managing Java/PHP runtimes, providing integrated tunneling (Playit.gg), automating modpack/addon installations, and adding features like AI session summaries and automated backups.

**Overall maturity:**
The project shows a high degree of architectural maturity for a desktop application. It employs robust patterns like Windows Job Objects for resilient child process management, isolated runtimes, and strict path validation logic in most places. However, it still contains isolated security risks (e.g., a ZipSlip vulnerability) and UX issues related to error surfacing during long-running background tasks.

**Biggest risks:**
1. **Security (Zip Slip):** A critical path traversal vulnerability exists in `BedrockAddonInstaller.cs` where standard `ZipFile.ExtractToDirectory` is used instead of the project's hardened `SafeZipExtractor`, potentially allowing malicious addons to overwrite arbitrary system files.
2. **Process Management Edge Cases:** While `JobObject` guarantees process death on app crash, the manual shutdown logic (`TryStopViaRconAsync` vs standard input `stop`) in `ServerProcess.cs` could lead to hung processes if the RCON port is misconfigured or inaccessible.
3. **UI Thread Blocking:** Large server logs or rapid output from server instances (like Forge installers) risk overwhelming the WPF Dispatcher, though some throttling mitigations are in place.
4. **Third-Party Service Dependency:** Tight integration with external services (CurseForge, Playit.gg) without robust fallback mechanisms could render key features unusable if APIs change or services experience downtime.

## B. Bug and risk findings

### 1. Zip Slip / Path Traversal Vulnerability in Addon Installer
*   **Severity:** Critical
*   **Evidence from repo:** `PocketMC.Desktop/Features/Mods/BedrockAddonInstaller.cs` uses `await Task.Run(() => ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true), ct);` at line 71.
*   **Why it matters:** While the project has a `SafeZipExtractor` class specifically designed to prevent malicious zip archives from writing outside their intended extraction directory, `BedrockAddonInstaller` bypasses it. A user importing a maliciously crafted `.mcpack` or `.mcaddon` could overwrite critical system files or inject malware.
*   **Suggested fix:** Replace `ZipFile.ExtractToDirectory` with `await SafeZipExtractor.ExtractAsync(sourceFilePath, tempDir);`

### 2. Potential RCON Shutdown Deadlock
*   **Severity:** Medium
*   **Evidence from repo:** `ServerProcess.cs` attempts RCON shutdown first, and if `false`, falls back to writing "stop" to standard input. However, if RCON connection hangs instead of failing immediately, the standard input fallback might not trigger before the timeout.
*   **Why it matters:** Servers might be forcefully killed (losing unsaved data) if the graceful shutdown sequence hangs due to a network timeout on the local RCON connection.
*   **Suggested fix:** Implement a strict `CancellationTokenSource` specifically around the `TryStopViaRconAsync` connection phase.

### 3. Missing Exception Handling in Process Invocation
*   **Severity:** Low
*   **Evidence from repo:** Numerous calls to `Process.Start(new ProcessStartInfo { ... })` across UI view models (e.g., `AboutPage.xaml.cs`, `DashboardActionsVM.cs`).
*   **Why it matters:** If the OS cannot find the default application to open a URL or file path (e.g., corrupted file associations), `Process.Start` throws an exception, potentially crashing the app if unhandled in asynchronous void event handlers.
*   **Suggested fix:** Wrap `Process.Start` calls in generic try-catch blocks and display a user-friendly error dialog.

## C. Testing audit
*   **What is tested well:** Unit tests cover core utilities comprehensively. `PathSafetyTests` effectively verifies path traversal mitigations, `SafeZipExtractorTests` proves the core zip-slip protection works, and specific logic like `ServerProcessManagerTests` handles restart delays.
*   **What is untested:** The UI layer and integration between the desktop shell and the background services lack test coverage.
*   **Missing test categories:** UI integration tests, end-to-end server provisioning tests (mocking HTTP calls to Modrinth/CurseForge).
*   **Highest priority tests to add:** Integration tests that simulate the full lifecycle of a server instance (Create -> Start -> Send Command -> Stop -> Backup -> Delete) using a mock executable instead of a real Minecraft server.

## D. Product and UX issues
*   **Friction points for users:** The "Fix Bedrock LAN" feature requires a UAC elevation prompt every time it is run if the exemption is missing.
*   **Confusing behaviors:** AI Summarization automatically runs on server stop if configured. For extremely long sessions, this might incur unexpected costs or delays without explicit user confirmation (though a warning threshold exists for >1.5MB logs).
*   **Missing recovery flows:** If the initial JRE download fails during setup due to network issues, the application might enter a state where it expects Java to be present but it isn't. An explicit "Repair Runtimes" button is needed.

## E. Release readiness
*   **Blockers before production use:** The ZipSlip vulnerability in `BedrockAddonInstaller.cs` must be resolved.
*   **Nice-to-have improvements:** Add a telemetry opt-out toggle and ensure all temporary extraction directories are proactively cleaned up on application exit, not just during the specific operation.
*   **Packaging/Install Gaps:** Velopack is configured for updates, but there is no evidence of code signing configuration in the visible `.csproj` snippets, which will trigger Windows SmartScreen warnings for non-technical users.

## F. Action plan
1.  **Fix ZipSlip:** Update `BedrockAddonInstaller.cs` to use `SafeZipExtractor`.
2.  **Harden Process Launches:** Create a helper for `Process.Start(UrlOrPath)` that catches `Win32Exception` and surfaces a generic dialog instead of crashing.
3.  **RCON Timeout:** Add a strict timeout to `RconClient.ConnectAsync`.
4.  **Runtime Repair:** Expose a manual "Re-download Java/PHP" option in Settings.
5.  **SmartScreen Mitigation:** Ensure CI/CD pipelines incorporate EV Code Signing for the final Setup.exe.

---
**Verdict:** **Not Yet.** Fix the critical ZipSlip vulnerability before shipping.
