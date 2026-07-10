# Superpowers Review - feature/multi-host

This review covers the changes introduced in the recent commits on the `feature/multi-host` branch, focusing on the root directory setup and local server folder import features.

## Blockers

### 1. High Risk of Permanent Data Loss during Local Folder Import Move Failures
- **File**: [InstanceImportService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Instances/ImportExport/InstanceImportService.cs#L1424-L1425) & [L1555-L1579](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Instances/ImportExport/InstanceImportService.cs#L1555-L1579)
- **Description**:
  When a user imports a local Minecraft server folder with `CopyFiles = false` (a Move operation), `ImportLocalFolderAsync` first pre-creates the destination folder on line 1414 (`Directory.CreateDirectory(destinationPath);`). Inside `MoveDirectoryAsync`, the initial `Directory.Move` call *always* fails because the destination directory already exists. 
  
  This triggers a fallback to recursive copy-then-delete (`CopyDirectoryAsync` followed by `Directory.Delete(sourceDir, true)`). If the `Directory.Delete` call throws an `UnauthorizedAccessException` mid-way (e.g. because of read-only/locked files in the source directory):
  1. The move operation is aborted, and since the exception occurred during line 1424, the `didMove` flag remains `false`.
  2. The catch block in `ImportLocalFolderAsync` executes. Because `didMove` is `false`, it runs the cleanup block:
     ```csharp
     if (Directory.Exists(destinationPath))
     {
         try { Directory.Delete(destinationPath, true); } catch { }
     }
     ```
     This deletes the destination directory containing the successfully copied files.
  3. However, before throwing the exception, `Directory.Delete(sourceDir, true)` had already deleted several files from the original source folder.
- **Impact**: Any files deleted from the source before the exception occurred are permanently lost, as both the source and the copied destination files are deleted.

---

## Majors

### 1. Folder Move Operation Always Falls Back to Slower Copy-then-Delete
- **File**: [InstanceImportService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Instances/ImportExport/InstanceImportService.cs#L1414)
- **Description**:
  Because `Directory.CreateDirectory(destinationPath)` is executed before `MoveDirectoryAsync` attempts `Directory.Move(sourceDir, targetDir)`, `Directory.Move` will always throw an `IOException` due to the target directory already existing.
- **Impact**: Instantaneous directory renames/moves are never utilized. The application always falls back to copying the server files byte-by-byte (very slow for large servers) and then deleting the source.

### 2. Minecraft Version Detection Hijacked by Arbitrary Versioned Files
- **File**: [ServerDetectionService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Instances/ImportExport/ServerDetectionService.cs#L98-L113)
- **Description**:
  In `DetectServerTypeAndVersionAsync`, Step C uses a broad regex fallback that matches the first file containing a version pattern `\d+(?:\.\d+)+` (not starting with `0.`) and immediately breaks the outer file loop.
  
  If the root directory contains dependency jars like `log4j-2.14.1.jar`, `commons-lang3-3.12.0.jar`, or any timestamped backup file, and it is processed before specific game jars (based on file listing order), it will be detected as the Minecraft version.
- **Impact**: The user is frequently shown incorrect Minecraft versions (like `2.14.1` or `3.12.0`) in the import UI.

---

## Minors

### 1. Incomplete Clean-up of Destination Folder on Failure
- **File**: [InstanceImportService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Instances/ImportExport/InstanceImportService.cs#L1577)
- **Description**:
  In the `ImportLocalFolderAsync` catch block, `Directory.Delete(destinationPath, true)` is used to clean up. If any copied files are read-only, this call will throw `UnauthorizedAccessException` and leave orphaned files inside `.staging`.
- **Recommendation**: Use the project's custom `FileUtils.CleanDirectoryAsync` which strips read-only flags first.

### 2. No Check for Target Directory Emptiness during Transfer
- **File**: [AppSettingsPage.xaml.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Setup/AppSettingsPage.xaml.cs#L1318)
- **Description**:
  When changing the root directory and transferring files, the application copies files directly using `FileUtils.CopyDirectoryAsync(currentRoot, targetPath)` without verifying if the target directory is empty.
- **Impact**: Existing files in the target directory could be silently overwritten or mixed.

---

## Nits

### 1. Leftover Empty Directory on Selection Cancel
- **File**: [RootDirectorySetupPage.xaml.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Setup/RootDirectorySetupPage.xaml.cs#L52-L62)
- **Description**:
  When selecting a directory, `RootDirectorySetupPage` pre-creates the suggested path folder. If the user cancels the dialog, the empty folder is left on the disk.

### 2. Inconsistent Back Navigation Parameter
- **File**: [InstanceImportPage.xaml.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Instances/ImportExport/InstanceImportPage.xaml.cs#L241)
- **Description**:
  In `HandleBackNavigation`, `BtnBack_Click(BtnCancel, new RoutedEventArgs())` is called using `BtnCancel` instead of `BtnBack`.

---

## Summary & Next Actions

Overall, the code is well-structured and aligns with Clean Architecture principles. However, the data loss risk during folder move and the version detection hijacking are significant issues that should be addressed before merging this branch.

### Next Actions:
1. **Fix data loss & fallback move**: Delay the call to `Directory.CreateDirectory(destinationPath)` during a Move operation, and strip read-only attributes properly when deleting files on failure.
2. **Refine version detection**: Scan the directory for candidate server jars first, and evaluate files sequentially without breaking the loop on generic fallbacks.
