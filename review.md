# Review of PocketMC.Desktop Repository

## A. Executive Summary
- **What the project does**: PocketMC is a Windows-focused desktop application built with WPF and .NET 8. It manages, configures, and runs Minecraft servers (Java, Bedrock, Geyser). It includes UI components for tunneling (via playit.gg), mod/addon management, server metrics, and diagnostics.
- **Overall maturity**: Moderate to Good. The project demonstrates strong architectural patterns like Dependency Injection, MVVM for UI state, and dedicated infrastructure classes. Tests exist for core business components like `ServerProcessManager` and networking elements.
- **Biggest risks**:
  1. A Zip Slip vulnerability exists in the Bedrock Addon Installer which allows untrusted third-party `.mcpack` files to traverse paths and write arbitrary files outside the destination directory.
  2. Extensive process handle leaking occurs across the entire UI layer; `Process.Start` is called repeatedly for opening URLs and folders without any resource disposal (`using` blocks). Over extended uptime, this will degrade performance.
  3. Non-atomic file writes (`File.WriteAllText`) are used for critical configuration files (e.g. `playit.toml`, Bedrock JSON files), creating opportunities for file corruption during unexpected crashes or power loss.

## B. Bug and risk findings

### 1. Zip Slip Vulnerability in Bedrock Addon Installer
- **Severity**: Critical
- **Evidence**: `BedrockAddonInstaller.cs` calls `await Task.Run(() => ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true), ct);`.
- **Why it matters**: Despite the existence of `SafeZipExtractor` built explicitly to defend against path traversal, the Bedrock addon installer ignores it. Malicious addon files could contain entries like `../../../../Windows/System32/config/system` and silently compromise the system.
- **Suggested fix**: Use `await SafeZipExtractor.ExtractAsync(sourceFilePath, tempDir);` instead.

### 2. Process Handle Leaks in UI Navigation
- **Severity**: High
- **Evidence**: Throughout the UI layer (e.g., `AboutPage.xaml.cs`, `DashboardActionsVM.cs`, `NewInstancePage.xaml.cs`), shell navigation relies on `Process.Start(psi)`.
- **Why it matters**: The `Process` class wraps native Windows OS handles. By not assigning the return value to a variable and calling `Dispose()` (or utilizing a `using var proc = ...` statement), the application leaks memory and handles every time a user clicks a link or opens a folder.
- **Suggested fix**: Wrap all fire-and-forget `Process.Start` calls with `using var proc = Process.Start(psi);`.

### 3. Non-atomic Writes for Configuration State
- **Severity**: Medium
- **Evidence**: `PlayitAgentService.cs` calls `File.WriteAllText(tomlPath, $"secret_key = ...");`. `BedrockAddonInstaller.cs` repeatedly uses `File.WriteAllText` for JSON manipulation.
- **Why it matters**: `File.WriteAllText` truncates the file and then writes. If the process is terminated in the middle of writing, the configuration is irrecoverably destroyed (empty file).
- **Suggested fix**: Replace direct calls with `FileUtils.AtomicWriteAllText` or `FileUtils.AtomicWriteAllTextAsync`.

## C. Testing audit
- **What is tested well**: Complex logic like port selection, Playit API response parsing, java runtime resolving, and exponential backoff configuration are robustly tested via xUnit suites.
- **What is untested**: UI behavior validation, resource cleanup on edge-cases, and integration tests ensuring components like `SafeZipExtractor` are actually utilized in production pipelines.
- **Missing test categories**: End-to-end integration flows that simulate actual third-party interactions (like fake `java.exe` stubs returning certain error codes for the launcher configurator).
- **Highest priority tests to add**: Tests confirming `BedrockAddonInstaller` rejects path traversal payloads (similar to `SafeZipExtractorTests.cs`).

## D. Product and UX issues
- **Friction points for users**: Applying the UWP Loopback Exemption triggers a UAC prompt without clear in-app warning. Users might decline the prompt out of confusion, causing Bedrock server connections to fail silently.
- **Confusing behaviors**: If Java is missing entirely, there is no automatic fallback or prompt redirecting the user to download it.

## E. Release readiness
- **Verdict**: Not ready for production.
- **Blockers**: The Zip Slip vulnerability and the UI process handle leaks must be addressed prior to any general release.
- **Nice-to-have improvements**: Streamline stream readers in `ServerProcess` to avoid unhandled async exceptions propagating during shutdown.

## F. Action plan
1. Replace `ZipFile.ExtractToDirectory` with `SafeZipExtractor` in `BedrockAddonInstaller`.
2. Review all `Process.Start` calls across the codebase and wrap with `using var`.
3. Standardize file writing on `FileUtils.AtomicWriteAllText`.