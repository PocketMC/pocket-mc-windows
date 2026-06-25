---
name: local-publish
description: Compile and publish a standalone PocketMC executable locally
disable-model-invocation: true
allowed-tools: Bash(dotnet *)
---

# Local Publish

Compiles a standalone, self-contained single-file executable for PocketMC to test the production build locally.

## Instructions

1. **Get the Version**
   - Read the current version string from `pocketmc.yml`.
   - If missing, default to `1.0.0`.

2. **Execute Publish Command**
   - Run the following `dotnet` command in the root repository folder, replacing `<VERSION>` with the parsed version:
     ```bash
     dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj \
       -c Release \
       -r win-x64 \
       --self-contained true \
       -p:PublishSingleFile=true \
       -p:IncludeNativeLibrariesForSelfExtract=true \
       -p:DebugType=None \
       -p:DebugSymbols=false \
       -p:Version=<VERSION> \
       -o "LocalRelease"
     ```

3. **Report Status**
   - Wait for the build to finish.
   - If successful, notify the user that the executable is available in the `LocalRelease` folder.

## Examples

- "Publish a local build"
- "Create a standalone executable"
