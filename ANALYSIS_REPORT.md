# PocketMC Repository Analysis

### A. Executive Summary
PocketMC is a well-architected .NET 8 WPF application that takes a modern, container-like approach to local Minecraft server hosting. By managing isolated JREs, PHP runtimes, and utilizing Windows Job Objects to prevent orphan processes, it avoids the systemic mess usually associated with local Windows server management.

**Overall maturity:** High. The usage of Job Objects, asynchronous RCON for graceful shutdowns, and solid MVVM practices indicate a strong engineering foundation.
**Biggest risks:** Despite the solid backend architecture, the project suffers from a catastrophic performance trap during startup (unbounded directory traversal), lacks crucial Windows desktop integrations (meaning it will look blurry and crash on long paths), and relies on unauthenticated GitHub API calls that will fail under standard rate limits.

---

### B. Bug and Risk Findings

#### 1. Catastrophic Startup Lag via Unbounded Directory Traversal
* **Severity:** Critical
* **Evidence:** `ServerLaunchConfigurator.cs` uses `Directory.GetFiles(workingDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();`
* **Why it matters:** `SearchOption.AllDirectories` traverses the entire directory tree synchronously. If a user imports an existing server with a large world (e.g., millions of region files) or deep backup folders, this scan will lock the disk, max out I/O, and cause the server startup to hang for several minutes. If it encounters a locked or protected folder, it will throw an `UnauthorizedAccessException` and abort the launch entirely.
* **Suggested fix:** Restrict the search to the root directory and the `libraries` folder, where Forge/NeoForge actually place this file.

#### 2. Missing Application Manifest (Blurry UI & Long Path Crashes)
* **Severity:** High
* **Evidence:** No `app.manifest` exists in the repository, and the `.csproj` lacks an `<ApplicationManifest>` tag.
* **Why it matters:** WPF applications without a manifest default to "System DPI Awareness". On modern laptops with 125% or 150% display scaling, Windows will aggressively upscale the app, making text and images look incredibly blurry. Furthermore, omitting the manifest disables Windows 10 `longPathAware` support, meaning any deep plugin or modpack paths exceeding 260 characters will crash the app via `PathTooLongException`.
* **Suggested fix:** Add an `app.manifest` file to the project, uncomment the `<dpiAwareness>PerMonitorV2</dpiAwareness>` and `<longPathAware>true</longPathAware>` sections, and link it in the `.csproj`.

#### 3. GitHub API Rate Limit Blackout for Pocketmine
* **Severity:** High
* **Evidence:** `PocketmineProvider.cs` fetches versions using an anonymous `HttpClient.GetFromJsonAsync` call to `api.github.com`.
* **Why it matters:** GitHub limits anonymous API requests to 60 per hour per IP address. Users on shared networks (CGNAT, universities) or users who simply navigate the UI a few times will hit this limit. The API will return HTTP 403, the provider will catch the exception, and the version list will silently appear empty, breaking Pocketmine installation.
* **Suggested fix:** Do not use `api.github.com` for client-side version manifests. Mirror the releases to a static JSON file (like you do for Bedrock via Kittizz), or cache the response heavily.

#### 4. Hardcoded World Lock Cleanup Fails for Bedrock
* **Severity:** Medium
* **Evidence:** `ServerProcess.cs -> CleanSessionLock()` hardcodes the cleanup path to `Path.Combine(workingDir, "world", "session.lock")`.
* **Why it matters:** The default world folder for Bedrock Dedicated Server is `worlds/Bedrock level`. Java servers can also change their folder via `level-name` in `server.properties`. If a server crashes, the stranded `session.lock` won't be deleted because PocketMC looks in the wrong folder. The server will then refuse to boot on the next launch.
* **Suggested fix:** Use `ServerPropertiesParser` to read the `level-name` property. For Bedrock, append it to the `worlds/` directory. Delete the lock file dynamically based on the configuration.

#### 5. "List" Command Console Spam
* **Severity:** Low / UX
* **Evidence:** `ResourceMonitorService.cs` calculates a tick rate and executes `Task.Run(() => sp.WriteInputAsync("list"));` every 30 seconds.
* **Why it matters:** While this ensures player counts stay synced, it injects `list` into standard input every 30 seconds. On Vanilla and Bedrock servers, this causes the console to echo `[Server] list` and the player list continuously. This pollutes the logs and annoys server administrators trying to read chat or debug errors.
* **Suggested fix:** Rely entirely on the regex parsing of "joined/left" events for player counts. Only fallback to RCON polling if strictly necessary, as RCON often suppresses console echo.

---

### C. Testing Audit
* **What is tested well:** Networking edge cases (port probing, dual-stack binding, UWP Udp recovery) and path safety algorithms (`SafeZipExtractor`, `PathSafety`) have excellent coverage.
* **What is untested:** Real process execution lifecycle. `ServerProcessManagerTests` likely mocks the process, meaning the Standard Output deadlocks and arguments edge cases aren't caught. UI interaction and ViewModels are entirely untested.
* **Missing test categories:** Live integration tests. There are no tests verifying that Mojang, GitHub, or Playit.gg API payloads haven't changed their JSON schemas.
* **Highest priority tests to add:** An integration test for `ServerLaunchConfigurator` that creates a dummy directory with 10,000 subfolders to prove it doesn't hang.

---

### D. Product and UX Issues
* **Friction points:** As mentioned, the blurry UI on scaled monitors will instantly degrade the perceived quality of the app.
* **Confusing behaviors:** Bedrock UWP loopback (`UwpLoopbackHelper`) requires a UAC elevation prompt. If this happens silently in the background when the user clicks "Start", it will look like malware. Ensure there is clear UI explaining *why* the UAC prompt is appearing.
* **Missing recovery flows:** If Java or PHP fails to download via the provisioning service, the user is left with a broken instance. There needs to be a clear "Verify/Repair Run-times" button in the settings.

---

### E. Release Readiness
* **Verdict:** **NOT YET.**
* **Blockers:** The Directory Traversal bug (Issue 1) and the Missing Manifest (Issue 2) are immediate blockers. Shipping a WPF desktop app without `PerMonitorV2` DPI awareness in 2024 is unacceptable, and the traversal bug will break the app for power users with large worlds.
* **Packaging:** Velopack integration is excellent, but ensure the `.NET 8 Desktop Runtime` is configured as a prerequisite in the setup bootstrapper so non-technical users aren't sent to a Microsoft download page.

---

### F. Action Plan & Patch Guide

**Top 5 fixes in priority order:**

1. **Fix Startup Hang (Directory Traversal):**
   *File:* `PocketMC.Desktop/Features/Instances/Services/ServerLaunchConfigurator.cs`
   ```csharp
   // Change from SearchOption.AllDirectories to a targeted search:
   var winArgs = Directory.GetFiles(workingDir, "win_args.txt", SearchOption.TopDirectoryOnly).FirstOrDefault();
   if (winArgs == null)
   {
       string libsDir = Path.Combine(workingDir, "libraries");
       if (Directory.Exists(libsDir)) {
           winArgs = Directory.GetFiles(libsDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
       }
   }
   ```

2. **Add Application Manifest:**
   * Create `app.manifest` in the Desktop project.
   * Add `<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2, PerMonitor</dpiAwareness>`.
   * Add `<longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>`.
   * Add `<ApplicationManifest>app.manifest</ApplicationManifest>` to the `.csproj`.

3. **Fix Lock Cleanup:**
   *File:* `PocketMC.Desktop/Features/Instances/Models/ServerProcess.cs`
   * Read `server.properties`, extract `level-name`. If it's a Bedrock server, clean `worlds/{level-name}/session.lock`.

4. **Mitigate Pocketmine Rate Limits:**
   * Stop hitting `api.github.com` from the client. Either scrape the tags from the HTML release page, route it through a cheap Cloudflare Worker, or cache a `pocketmine-versions.json` file on your own GitHub pages.

5. **Remove "List" Spam:**
   *File:* `PocketMC.Desktop/Features/Instances/Services/ResourceMonitorService.cs`
   * Remove lines 91-95 that blindly send `sp.WriteInputAsync("list")`. Trust your regex parser.
