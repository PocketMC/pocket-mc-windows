1. **Code quality and architecture**
   - Check dependency injection, view model bindings, interface separation.
   - Look at `Infrastructure/` components (JobObject, FileSystem, Process).
2. **Bug risks and failure modes**
   - Identify unhandled exceptions in async code (e.g. `ServerProcess.cs`, `PlayitAgentService.cs`).
   - Concurrency issues (e.g. `ServerProcessManager.cs` dictionary operations).
   - Race conditions in file operations (e.g. `BackupService.cs`, `FileUtils.cs`).
3. **Test coverage and test quality**
   - Audit `PocketMC.Desktop.Tests/` to see what's covered vs missing (e.g., UI, real process lifecycle).
4. **CLI / UI / workflow usability**
   - Look at `App.xaml.cs` or `NewInstancePage.xaml.cs` for error handling during interactions.
5. **Security and safety issues**
   - Path traversals (Zip slip) -> already reviewed `SafeZipExtractor`, but found standard `ZipFile.ExtractToDirectory` in `BedrockAddonInstaller.cs`!
   - Process security (JobObject, RCON passwords in logs?).
   - DPAPI usage in `DataProtector.cs` -> legacy fallback might be weak.
6. **Packaging, installation, and upgrade paths**
   - Check `UpdateService.cs` or Velopack usage.
7. **Windows-specific logic**
   - Path length issues (no `\\?\` prefix usage found).
   - Space in paths for `Process.Start` arguments.
   - `UwpLoopbackHelper.cs` (CheckNetIsolation usage).
8. **Error handling and recovery**
   - Server crash recovery (`ServerProcess.cs`).
   - Playit agent restart loop (`PlayitAgentService.cs`).
