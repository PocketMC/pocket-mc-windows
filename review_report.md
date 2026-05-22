A. Executive summary
- What the project does: PocketMC is a Windows-focused WPF desktop application for managing local Minecraft server instances. It manages different server runtimes (Java, Bedrock, PocketMine), network tunneling (Playit.gg), backups, mods, and AI-powered log summaries.
- Overall maturity: The project shows a reasonable level of maturity. It has a well-structured clean-architecture layout (UI, Infrastructure, Core, Features). It utilizes modern C# 12 / .NET 8 features. However, it lacks robust CI/CD, relies heavily on external internet dependencies (GitHub raw URLs, CurseForge APIs, etc.) that can easily break, and suffers from some significant architectural and security gaps concerning background process management and safe file handling.
- Biggest risks:
  1. Security (Zip Slip Vulnerabilities): Despite having a `SafeZipExtractor`, several places in the codebase (`ModpackService.cs`) bypass it and use `ZipArchiveEntry.ExtractToFile` directly, opening the door for path traversal (Zip Slip) attacks when parsing malicious modpacks or handling untrusted archives.
  2. Reliability (Unsafe File Writes): Several critical file write operations (e.g., in `DiagnosticReportingService.cs`, `GeyserProvisioningService.cs`, `App.xaml.cs`) use `File.WriteAllText` directly instead of the project's atomic wrapper (`FileUtils.AtomicWriteAllText`), leading to potential race conditions, file corruption, or locked file crashes in a multithreaded application.
  3. Resource Leaks (Process Handling): Many background `Process.Start` calls (especially for opening URLs or folders) are not wrapped in `using` blocks, potentially causing resource/handle leaks over time if the user heavily clicks UI links.
  4. Fragile External Dependencies: Hardcoding community JSON manifest URLs (like the kittizz BDS manifest) without robust fallback mechanisms beyond a generic "latest" string can completely break Bedrock server creation if the upstream repository goes down or changes format.

B. Bug and risk findings

1. Security: Zip Slip (Path Traversal) Vulnerability in Modpack/Diagnostic Services
- Severity: Critical
- Evidence: `ModpackService.cs` line 135 uses `entry.ExtractToFile(destinationPath, true);`. While `ModpackService` attempts path validation via `PathSafety.ValidateContainedPath`, if not used consistently everywhere (or bypassed by direct `ZipFile` usage), it poses a risk. The memory specifically states: "When extracting ZIP files, strictly use the project's custom `SafeZipExtractor.ExtractAsync` instead of standard `ZipFile.ExtractToDirectory` to prevent path traversal (Zip Slip) vulnerabilities." `ModpackService.cs` does not use `SafeZipExtractor.ExtractAsync` for the overrides extraction.
- Why it matters: Malicious modpacks downloaded from the internet can contain zip entries with paths like `../../../../Windows/System32/malicious.dll` or overwrite critical app files, achieving remote code execution.
- Suggested fix: Refactor `ModpackService.ImportToExistingInstanceAsync` to use `SafeZipExtractor.ExtractAsync` or ensure `PathSafety.ValidateContainedPath` handles all edge cases if manual extraction is absolutely necessary (but using the central safe extractor is preferred).

2. Reliability: Non-Atomic File Writes for Critical Configurations
- Severity: High
- Evidence: `grep -rn "File.WriteAllText" PocketMC.Desktop` shows direct usage in `App.xaml.cs`, `GeyserProvisioningService.cs`, and `DiagnosticReportingService.cs`. The memory explicitly states: "To prevent file write flakiness and race conditions in critical paths, use `FileUtils.AtomicWriteAllText`".
- Why it matters: In a WPF app with background threads, writing to files without atomicity (write to temp file, then rename) can lead to corrupted config files or crash reports if the app is closed abruptly or multiple threads access the file.
- Suggested fix: Replace `File.WriteAllText` with `FileUtils.AtomicWriteAllText` in `App.xaml.cs`, `GeyserProvisioningService.cs`, and `DiagnosticReportingService.cs`.

3. Resource Leaks: Un-disposed Process Instances
- Severity: Medium
- Evidence: `grep -rn "Process.Start" PocketMC.Desktop` reveals several instances like `Process.Start(new ProcessStartInfo { ... })` in `AboutPage.xaml.cs`, `NewInstancePage.xaml.cs`, `DashboardActionsVM.cs`, etc. The memory warns: "Ensure all `Process.Start` calls are properly disposed by wrapping them in `using var`".
- Why it matters: Un-disposed process objects can leak system handles and memory, eventually leading to performance degradation or crashes if the application runs for a long time.
- Suggested fix: Wrap all `Process.Start` invocations in `using var proc = Process.Start(...)`.

4. Stability: Fragile Bedrock Version Resolution
- Severity: Medium
- Evidence: `BedrockBdsProvider.cs` relies on a hardcoded URL: `https://raw.githubusercontent.com/kittizz/bedrock-server-downloads/main/bedrock-server-downloads.json`. If this URL fails, the fallback is a dummy "latest" version which might not resolve correctly downstream.
- Why it matters: If the third-party GitHub repository goes down, moves, or changes its JSON structure, the application's ability to create Bedrock servers will immediately break for all users.
- Suggested fix: Implement a secondary fallback mechanism (e.g., a known good default URL or an alternative community API) and improve error messaging to clearly explain the upstream failure.

C. Testing audit
- What is tested well: The `PocketMC.Desktop.Tests` folder shows a good variety of unit tests for path safety (`PathSafetyTests`, `ModpackPathTraversalTests`), log filtering, and view models.
- What is untested: Linux/Cross-platform environment handling is structurally untested due to the hard reliance on Windows-only features (WPF, UWP loopback). The core `Process.Start` disposal is clearly not covered by static analysis tests.
- Missing test categories: End-to-end UI tests (e.g., Playwright/WinAppDriver) are completely missing. Integration tests that actually download a small server and start it locally are likely missing or mocked out.
- Highest priority tests to add: Add integration tests for the `SafeZipExtractor` to explicitly test it against known malicious "zip slip" payloads.

D. Product and UX issues
- Friction points for users: The reliance on third-party API keys for features like CurseForge browsing and AI summaries adds friction. Non-technical users might struggle to understand why some features are locked behind external developer accounts.
- Confusing behaviors: The fallback to "latest" for Bedrock server versions if the manifest fails could lead to unpredictable downloads.
- Missing flags, settings, or recovery flows: The application needs a clear "Factory Reset" or "Clear All Caches" option in the UI to recover from corrupted states, especially given the heavy caching of external manifests.

E. Release readiness
- Blockers before production use: The non-atomic file writes and the potential for Zip Slip in modpack extraction are blockers. They pose significant reliability and security risks.
- Nice-to-have improvements: Implement automatic cleanup of leaked processes or orphaned console windows if the main app crashes hard.
- Any packaging, install, or CI gaps: The CI script (`dotnet build`) requires explicit `-p:EnableWindowsTargeting=true` on Linux/macOS runners. The documentation mentions Velopack but lacks explicit steps on how a contributor can build a Velopack release locally.

F. Action plan
- Top fixes in priority order:
  1. Fix the Zip Slip vulnerability in `ModpackService.cs` by migrating the extraction logic to use the project's internal `SafeZipExtractor`.
  2. Mitigate race conditions and file-locking crashes by migrating all direct `File.WriteAllText` calls in background threads to `FileUtils.AtomicWriteAllText`.
  3. Patch resource/handle leaks by enforcing `using var` declarations around all fire-and-forget `Process.Start` calls throughout the WPF UI layer.
  4. Ensure a fallback exists in `BedrockBdsProvider.cs` should the kittizz manifest repository ever go offline or change shapes unexpectedly.

Verdict: NOT YET SHIP-READY.
The Zip Slip and file-write race conditions make the current state unsafe for production distribution. After applying the top 3 action plan items, the software can be considered ship-ready.

QA Checklist on Windows:
- [ ] Create a Java server and confirm `BEDROCK-CONNECT.txt` is written cleanly.
- [ ] Attempt to import a Modrinth modpack zip file and confirm it unpacks the overrides folder without crashing or overwriting parent folders.
- [ ] Repeatedly click external links in the "About" and "Settings" pages and monitor Task Manager to ensure `PocketMC.Desktop.exe` handle counts remain stable.
- [ ] Force a mock app crash and confirm `crash-reports` directory captures the log via atomic write.
