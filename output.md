A. Executive summary
- PocketMC is a Windows desktop application that runs and manages Minecraft Bedrock and Java servers natively, avoiding terminal use and manually managed dependencies.
- Overall maturity: The codebase is well-structured using .NET 8, WPF, and DI. Features include background tunneling via Playit, server monitoring, modpacks handling, and UWP loopback exemptions. However, some security holes exist in specific zip extraction paths, and error handling for subprocesses is inconsistent.
- Biggest risks: Path traversal vulnerabilities (Zip Slip) exist when installing Bedrock Addons. Additionally, unhandled `Process.Start` exceptions or failing to `Dispose` external processes can cause silent resource leaks.

B. Bug and risk findings

1. Zip Slip / Path Traversal in BedrockAddonInstaller
- Severity: Critical
- Evidence: In `BedrockAddonInstaller.cs` line 71, `ZipFile.ExtractToDirectory` is used directly on arbitrary user-provided `.mcpack`/`.mcaddon` files.
- Why it matters: A malicious add-on could contain relative paths (e.g., `../../windows/system32/malware.exe`) and write arbitrary files to the user's system when the pack is added.
- Suggested fix: Replace `ZipFile.ExtractToDirectory` with `SafeZipExtractor.ExtractAsync` which is already implemented and used correctly in `BackupService.cs`.

2. Missing Process Disposal
- Severity: Medium
- Evidence: Across `AboutPage.xaml.cs`, `NewInstancePage.xaml.cs`, `InstanceManager.cs`, `AppSettingsPage.xaml.cs`, `TunnelPage.xaml.cs`, `PlayitSetupWizardPage.xaml.cs`, and `MapBrowserPage.xaml.cs`, `Process.Start()` is called but the returned `Process` object is not disposed (no `using` statement or manual `.Dispose()`).
- Why it matters: `Process.Start` allocates OS handles. Failing to dispose them results in handle leaks which can crash the app or slow down Windows over long uptimes.
- Suggested fix: Wrap fire-and-forget `Process.Start` calls in `using var proc = Process.Start(...)`.

3. Weak DPAPI Fallback in DataProtector
- Severity: Low/Medium
- Evidence: In `DataProtector.cs`, if DPAPI fails to unprotect data, it simply returns the raw ciphertext string, assuming it's legacy plaintext.
- Why it matters: If the DPAPI keys are rotated (e.g., password change), returning encrypted garbage as plaintext can break settings deserialization and crash the app or corrupt user config.
- Suggested fix: Instead of blindly returning `cipherText`, verify if it looks like Base64 or is valid JSON/plaintext before returning it, or wipe it if decryption genuinely fails.

4. Potential Deadlock in Installer Output Processing
- Severity: Medium
- Evidence: In `ServerLaunchConfigurator.cs`, `Task.Run` is used to consume StandardOutput and StandardError asynchronously to prevent deadlocks, but `proc.WaitForExitAsync()` runs in parallel with them and does not ensure the tasks actually finish before continuing, leading to race conditions.
- Why it matters: Moving forward before logging has finished could cause file locks or lost logs during forge installation.
- Suggested fix: Await `Task.WhenAll(outputTask, errorTask, proc.WaitForExitAsync())`.

C. Testing audit
- What is tested well: Path traversal detection (`PathSafetyTests`, `ModpackPathTraversalTests`) and log sanitization (`LogSanitizerTests`).
- What is untested: Real process lifecycle and UI views. The testhost fails to initialize because it relies on the `Microsoft.WindowsDesktop.App` framework, which requires MSBuild `EnableWindowsTargeting=true`.
- Missing test categories: UI tests, concurrency/race condition tests for file writes, actual addon installation tests.
- Highest priority tests to add: Tests around `BedrockAddonInstaller` and `FileUtils.AtomicWriteAllText`.

D. Product and UX issues
- Friction points: Error messages from raw `Process.Start` exceptions (e.g., when trying to open a URL and the user has no default browser) are ugly or crash the app. `AboutPage.xaml.cs` catches this but `MapBrowserPage.xaml.cs` and `TunnelPage.xaml.cs` do not.
- Missing recovery flows: If UAC is declined during UWP loopback exemption (`UwpLoopbackHelper.cs`), it just returns false instead of guiding the user on how to fix it manually.

E. Release readiness
- Blockers before production use: The Zip Slip vulnerability in Bedrock Addon Installer must be fixed. Resource leaks via `Process.Start` must be patched.
- CI/CD: The CI likely fails or cannot run Windows Desktop tests correctly on Linux runners without the correct MSBuild properties.

F. Action plan
1. Fix Zip Slip in `BedrockAddonInstaller.cs` using `SafeZipExtractor.ExtractAsync`.
2. Wrap all `Process.Start()` calls with `using var` or call `.Dispose()` across the UI pages.
3. Catch `Win32Exception` for `Process.Start` calls opening URLs (in case no default browser is set) across UI pages.
4. Update `ServerLaunchConfigurator.cs` to properly await installer output streams.
5. Improve `DataProtector.cs` to handle decryption failures more safely.
