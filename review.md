## Executive Summary
PocketMC is a Windows WPF desktop application for managing local Minecraft server instances. It supports multiple server families (Java, Bedrock, Paper, Fabric, Forge, PocketMine-MP) and integrates features such as Modrinth, Playit.gg for tunneling, and Geyser for cross-play. The project aims to be a user-friendly abstraction over server management. The codebase demonstrates a reasonably high level of maturity, employing modern .NET 8 practices, dependency injection, and MVVM principles.
However, there are critical risks related to unsafe archive extraction, insecure temporary file operations, and missing test coverage in key lifecycle areas. Addressing these will prevent potential security vulnerabilities and improve reliability for both power users and casual users alike.

## Bug and Risk Findings

### 1. Critical: Path Traversal (Zip Slip) Vulnerability in BedrockAddonInstaller
- **Severity**: Critical
- **Evidence**: `PocketMC.Desktop/Features/Mods/BedrockAddonInstaller.cs` line 71 uses `ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true)`. Memory states: "When extracting ZIP files, strictly use the project's custom `SafeZipExtractor.ExtractAsync` instead of standard `ZipFile.ExtractToDirectory` to prevent path traversal (Zip Slip) vulnerabilities."
- **Why it matters**: A malicious Bedrock add-on (`.mcpack` or `.zip`) could contain relative paths (e.g., `../../../Windows/System32/evil.dll`), allowing an attacker to write arbitrary files to the user's filesystem when they attempt to install the add-on, leading to remote code execution or system compromise.
- **Suggested Fix**: Replace `ZipFile.ExtractToDirectory(sourceFilePath, tempDir, overwriteFiles: true)` with `await SafeZipExtractor.ExtractAsync(sourceFilePath, tempDir)`.

### 2. High: Potential Race Conditions and Data Loss using File.WriteAllText
- **Severity**: High
- **Evidence**: Numerous instances of `File.WriteAllText` and `File.WriteAllTextAsync` are used across the application, notably in `BedrockAddonInstaller.cs` (lines 254, 269), `AddonManifestService.cs`, and `SummaryStorageService.cs`. Memory states: "To prevent file write flakiness and race conditions in critical paths, use `FileUtils.AtomicWriteAllText` (from `PocketMC.Desktop.Infrastructure.FileSystem`) instead of the standard `File.WriteAllText`."
- **Why it matters**: Concurrent writes or crashes during file saving can lead to corrupted or empty configuration files (like world JSONs or manifests), causing the application to fail to load server configurations or user data upon restart.
- **Suggested Fix**: Update critical file writes to use `FileUtils.AtomicWriteAllText` or `FileUtils.AtomicWriteAllTextAsync`.

### 3. Medium: Resource Leaks from Un-disposed Process.Start Calls
- **Severity**: Medium
- **Evidence**: Several occurrences of `Process.Start` without `using var proc = Process.Start(...)`. For example, `PocketMC.Desktop/Features/InstanceCreation/NewInstancePage.xaml.cs` (line 563), `PocketMC.Desktop/Features/Instances/Services/InstanceManager.cs` (line 207), and various places handling shell execution or Explorer navigation. Memory specifically warns: "Ensure all `Process.Start` calls are properly disposed by wrapping them in `using var` (e.g., `using var proc = Process.Start(...)`) to prevent resource and handle leaks."
- **Why it matters**: In a long-running desktop application, repeatedly spawning processes without disposing of the `Process` object can lead to handle leaks, eventually exhausting system resources and causing the app or system to become unstable.
- **Suggested Fix**: Refactor all naked `Process.Start` calls to `using var proc = Process.Start(...)` where applicable.

### 4. Medium: Missing Test Coverage for Bedrock Addon Installation
- **Severity**: Medium
- **Evidence**: A check of `PocketMC.Desktop.Tests` reveals tests for Modpack path traversal and SafeZipExtractor, but no tests for `BedrockAddonInstaller.cs`.
- **Why it matters**: The `BedrockAddonInstaller` handles parsing external JSON manifests and modifying server configurations. Without tests, regressions in pack registration or directory management could easily slip in, breaking Bedrock instances.
- **Suggested Fix**: Add `BedrockAddonInstallerTests.cs` to cover manifest parsing, extraction (verifying it uses the safe extractor), and world JSON registration logic.

## Testing Audit

**What is tested well:**
- `SafeZipExtractor` correctly tests for Zip Slip prevention.
- `ModpackPathTraversalTests` ensures modrinth packs don't escape their intended bounds.
- Port networking services (`PortRecoveryService`, `PortProbeService`) have extensive test coverage for conflict scenarios.

**What is untested:**
- UI components and XAML interactions (minimal tests exist).
- `BedrockAddonInstaller` (crucial for modifying Bedrock server configuration).
- The behavior of `AtomicWriteAllText` itself or verifying its usage across services.

**Highest priority tests to add:**
- `BedrockAddonInstallerTests`: To verify that add-ons are installed securely and configurations are updated atomically.

## Product and UX Issues
- **Friction Points**: If file writes corrupt world JSONs, users will experience a broken server state with no clear recovery path short of manual file editing.
- **Confusing Behaviors**: The app might quietly leak handles over time, causing sluggish performance that is difficult for a non-technical user to diagnose.

## Release Readiness
- **Blockers before production use**: The path traversal vulnerability in `BedrockAddonInstaller` MUST be fixed. This is a critical security issue for an application managing untrusted internet downloads.
- **Nice-to-have improvements**: Replace `File.WriteAllText` with `FileUtils.AtomicWriteAllText` across the board to ensure configuration stability. Ensure all process launches are disposed.

## Action Plan (Top 5 fixes)
1. **Fix Zip Slip Vulnerability**: Update `BedrockAddonInstaller.cs` to use `SafeZipExtractor.ExtractAsync` instead of `ZipFile.ExtractToDirectory`.
2. **Fix Atomic Writes in BedrockAddonInstaller**: Update `RegisterInWorldJson` and `RemoveFromWorldJson` to use `FileUtils.AtomicWriteAllText`.
3. **Fix Atomic Writes in other critical areas**: Replace `File.WriteAllText` with atomic equivalents in `GeyserProvisioningService`, `PlayitAgentService`, `DiagnosticReportingService`, and `AddonManifestService`.
4. **Fix Resource Leaks**: Review and wrap all `Process.Start` calls with `using var` to prevent handle leaks.
5. **Add BedrockAddonInstaller Tests**: Ensure the fixes to the installer are verified automatically.
