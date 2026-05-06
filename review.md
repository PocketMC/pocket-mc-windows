# PocketMC Desktop Review Report

## A. Executive Summary

PocketMC is a Windows desktop application built with .NET 8 WPF designed to simplify the creation, management, and tunneling of Minecraft servers (Java, Bedrock, and PocketMine-MP) without requiring command-line interaction. It automates runtime provisioning (Java/PHP), manages process lifecycles via Windows Job Objects, and integrates with services like Playit.gg and Modrinth.

**Overall Maturity**:
The codebase demonstrates a solid, modern .NET architecture using Dependency Injection, asynchronous programming, and specific abstraction layers for features. However, while the structural design is mature, the implementation contains critical flaws in file/path safety, process safety guarantees, and configuration parsing. The project sits at an advanced alpha or early beta stage—feature-rich but brittle.

**Biggest Risks**:
1.  **Process Escape & Orphaning**: Failure to bind child processes to Windows Job Objects fails silently, completely defeating the automatic cleanup and leading to zombie processes locking files and ports if the UI crashes.
2.  **Weak Path Validation**: Zip extraction uses string `StartsWith` for validation rather than robust `DirectoryInfo` comparison. Even with trailing slash normalization, this is known to be vulnerable to complex ZipSlip variants on Windows depending on UNC prefix manipulation.
3.  **Hardcoded Bedrock Assumptions**: Core features like Bedrock Addon installation ignore runtime configuration (`server.properties`), opting for hardcoded paths. This will cause silent failures for users customizing their setups.

---

## B. Bug and Risk Findings

### 1. Silent Job Object Binding Failure Leading to Process Leaks
- **Severity**: Critical
- **Evidence**: In `ServerProcess.cs` (`StartAsync`):
  ```csharp
  try { _jobObject.AddProcess(_process.Handle); }
  catch (Exception ex) { _logger.LogWarning(ex, "Failed to assign process to job object."); }
  ```
- **Why it matters**: The core value proposition of a desktop server manager is that it manages the server cleanly. If binding to the Job Object fails (due to permissions, AV interference, or handle exhaustion), the child `java.exe` or `bedrock_server.exe` continues to run independently. If the PocketMC UI is closed or crashes, the server remains running in the background. The user won't know, ports will remain bound, and the next launch attempt will fail mysteriously.
- **Suggested Fix**: Catching this exception must be fatal to the server launch. If the job object cannot be bound, the process should be killed immediately (`_process.Kill()`) and an error surfaced to the user via the `OnServerCrashed` event.

### 2. Path Traversal Vulnerability in `SafeZipExtractor`
- **Severity**: High
- **Evidence**: In `SafeZipExtractor.cs`:
  ```csharp
  string destinationPath = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
  if (!destinationPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase)) { ... }
  ```
- **Why it matters**: While the code appends a trailing directory separator to `extractRoot`, using `StartsWith` for path containment checks on Windows is inherently unsafe. Depending on how `Path.GetFullPath` handles edge cases (like `\\?\` prefix injection, `..` evaluation anomalies, or alternate data streams), an attacker crafting a malicious modpack or backup zip can potentially write files outside the intended destination directory (ZipSlip).
- **Suggested Fix**: Replace the string prefix check with a robust path comparison, or ensure that iterating through `DirectoryInfo.Parent` never leaves the `extractRoot` `DirectoryInfo` object.

### 3. Bedrock Addon Installation Ignores `level-name`
- **Severity**: Medium
- **Evidence**: In `BedrockAddonInstaller.cs` (`ResolveWorldDirectory`):
  ```csharp
  string preferred = Path.Combine(serverDir, WorldsDir, DefaultWorldName); // "worlds/Bedrock level"
  if (Directory.Exists(preferred)) return preferred;
  var worldsParent = Path.Combine(serverDir, WorldsDir);
  // ... returns first dir found or creates preferred
  ```
- **Why it matters**: The user can easily change their Bedrock world name by editing `server.properties` (`level-name=MyCustomWorld`). If they do this, `ResolveWorldDirectory` will either pick up an old, unused "Bedrock level" folder, or pick the alphabetically first folder if multiple exist. The addons will be installed to the wrong world, and the user will perceive the feature as broken.
- **Suggested Fix**: The addon installer must read `server.properties` and extract the value of `level-name`. It should fall back to `DefaultWorldName` only if the file or key does not exist.

### 4. Lack of Unescaped Argument Testing in Process Configuration
- **Severity**: Medium
- **Evidence**: `ServerLaunchConfigurator.ConfigureAsync` builds `ProcessStartInfo` arguments for Java and Bedrock dynamically. There are no unit tests validating that user-supplied paths, JVM args, or server names containing spaces or quotes are properly escaped before execution.
- **Why it matters**: Command-line injection or simple failure-to-launch scenarios are common when dealing with Windows paths containing spaces (e.g., `C:\Users\John Doe\AppData\...`).
- **Suggested Fix**: Implement comprehensive unit tests passing paths with spaces, special characters, and quotes into `ServerLaunchConfigurator` and assert that the resulting `psi.Arguments` are correctly enclosed and escaped.

---

## C. Testing Audit

- **What is tested well**: Path traversal basics are tested (`SafeZipExtractorTests`), but they lack coverage for advanced Windows-specific bypasses. There are robust tests for Port handling (`PortDiagnosticsSnapshotBuilderTests`, `PortProbeServiceTests`) and Dependency Resolvers.
- **What is untested**: Integration testing for the core process lifecycle. The actual interaction between `ServerProcessManager`, `ServerProcess`, and `JobObject` is untested.
- **Missing test categories**:
  - Configuration parsing logic (e.g., reading/writing `server.properties` and updating `level-name`).
  - Handling of malformed JSON in manifest parsing within `BedrockAddonInstaller`.
  - Process exit and crash state transitions.
- **Highest priority tests to add**: Mock `Process` wrapper integration tests to ensure `ServerState` updates correctly when a process exits abruptly, and tests ensuring `JobObject` failure triggers a graceful abort.

---

## D. Product and UX Issues

- **Friction Points for Users**: The `UwpLoopbackHelper` relies on an external executable (`CheckNetIsolation.exe`) and launches with `CreateNoWindow = false` to trigger a UAC prompt. If the user denies the UAC prompt, the app may not handle the `Win32Exception` gracefully, leading to a poor UX.
- **Confusing Behaviors**: `BedrockAddonInstaller` swallows generic exceptions when parsing `manifest.json`. If a user downloads a corrupted mod, the installer logs a warning and silently skips it, leaving the user wondering why their mod didn't install.
- **Missing Recovery Flows**: If the Java automatic download fails mid-stream or the extracted runtime is corrupted, there does not appear to be an obvious UI flow to "verify integrity" or force a re-download of the runtime stack.

---

## E. Release Readiness

- **Ship it / Not Yet**: **Not Yet.**
- **Blockers before production use**:
  - The silent failure of Job Object assignment must be addressed. A server manager that leaks server processes is critically flawed.
  - The path traversal vulnerability must be fully mitigated.
- **Nice-to-have improvements**:
  - Improve error surfacing for addon installations.
  - Implement a dynamic `server.properties` parser for Bedrock world resolution.

---

## F. Action Plan

1. **Fix Job Object Leak**: Modify `ServerProcess.StartAsync` to terminate `_process` and throw an exception/update state if `_jobObject.AddProcess` fails.
2. **Harden Path Validation**: Refactor `SafeZipExtractor` to use `new DirectoryInfo(destPath).FullName.StartsWith(...)` or similar hardened validation methods against ZipSlip.
3. **Dynamic Bedrock World Pathing**: Update `BedrockAddonInstaller` to read `server.properties` and extract `level-name` for accurate target directory resolution.
4. **Improve UAC UX**: Wrap the `UwpLoopbackHelper` process start in a `try/catch` specifically for `Win32Exception` (error code `ERROR_CANCELLED`) to provide a helpful UI message when UAC is denied.
5. **Add Process Integration Tests**: Build tests specifically for the `ServerProcess` class focusing on argument escaping and state transitions upon unexpected exits.
