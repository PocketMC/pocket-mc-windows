# Contributing to PocketMC

Thank you for your interest in contributing to PocketMC! This guide will help you get started with the development environment, contribution process, and best practices.

## 🛠️ Development Setup

### Prerequisites

- **.NET 8 SDK** (the project uses `global.json` to pin the SDK version)
- **Visual Studio 2022** (with "Desktop development with .NET" workload) or **JetBrains Rider**
- **Windows 10 1809+** or **Windows 11** (the app targets Windows x64)

### Building from Source

1. Clone the repository:

   ```bash
   git clone https://github.com/PocketMC/pocket-mc-windows.git
   cd pocket-mc-windows
   ```

2. Restore dependencies and build:

   ```bash
   dotnet restore
   dotnet build
   ```

3. Run the desktop application:

   ```bash
   dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
   ```

   > 💡 The app uses the embedded `pocketmc.yml` as the single source of truth for version, channel, and backend proxy URLs. Never hardcode versions in `.csproj` files or C# code.

### Packaging (Velopack)

PocketMC uses **Velopack** for updates and installation. To create a release package locally:

1. Install the Velopack CLI:

   ```bash
   dotnet tool install -g vpk
   ```

2. Build the project in Release mode and publish:

   ```bash
   dotnet build -c Release
   dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj -c Release -r win-x64 --self-contained false -o publish
   ```

3. Pack the release:

   ```bash
   vpk pack --packId PocketMC --packVersion <version> --packDir publish --mainExe PocketMC.Desktop.exe --framework net8.0 --runtime win-x64 --outputDir Releases
   ```

   Replace `<version>` with the version from `pocketmc.yml`.

## 🧪 Testing

We use **xUnit** for unit testing and **Moq** for mocking. Ensure all tests pass before submitting a Pull Request.

Run tests via CLI:

```bash
dotnet test
```

### Key Areas Requiring Comprehensive Tests

- **Process Lifecycle**: Crash recovery, orphan process cleanup, graceful shutdowns, and port lease management.
- **Path Safety**: Path traversal prevention in mod/plugin imports, backup restores, and ZIP extraction.
- **Provisioning**: JRE and PHP runtime download, isolation, on-demand prompt confirmation/denial flows.
- **Cloud Backups**: OAuth flows, token refresh, upload/download resilience, and retention pruning.
- **Remote Control**: Authentication, rate limiting, WebSocket console, and tunnel providers.
- **Mod/Addon Management**: Scanning, toggling, update checks, and dependency resolution.

> ⚠️ **Testing Cloud Backups**: To test OAuth flows locally, you need to register your own application with the provider (Google/Dropbox/OneDrive) and configure the client secrets in the proxy backend or use the test credentials provided in the documentation.

## 📜 Contribution Guidelines

1. **Fork and Branch**: Always create a new branch from `master` for your changes.
2. **Atomic Commits**: Keep your commits focused on a single logical change.
3. **Draft PRs**: Feel free to open a Draft PR if you want early feedback on an implementation.
4. **Issue First**: For significant architectural changes, please open an issue to discuss the approach first.

### Pull Request Checklist

- [ ] Code follows the project's coding conventions (see `.editorconfig`).
- [ ] No hardcoded versions – use `AppConfig` to read from `pocketmc.yml`.
- [ ] All new services are registered via Dependency Injection in `Composition/ServiceCollectionExtensions.cs`.
- [ ] No WPF/UI logic (`System.Windows.*`) leaks into Domain, Application, or Infrastructure layers.
- [ ] Tests cover the new functionality (or update existing tests).
- [ ] Documentation updated (README, CHANGELOG, or feature docs) if applicable.
- [ ] Security implications considered (path safety, credential handling, input validation).
- [ ] Build and tests pass on your local machine.

### Code Review Expectations

- **Constructive Feedback**: Reviews should be kind, specific, and actionable.
- **Approval Required**: At least one maintainer approval is required before merging.
- **CI Must Pass**: All checks (build, tests, linting) must be green.

## 🏗️ Project Structure & Architecture

```
├── PocketMC.Domain           # Core models, enums, and pure logic (no external deps)
├── PocketMC.Application      # Interfaces, use cases, and application services
├── PocketMC.Infrastructure   # Concrete implementations (network, cloud, AI, HTTP, OS)
├── PocketMC.Desktop          # WPF UI, ViewModels, DI container, main entry
├── PocketMC.RemoteControl    # ASP.NET Core web host, API, WebSockets, tunnel providers
└── Tests                     # Corresponding test projects for each layer
```

### Architectural Rules

1. **Layering**: Domain → Application → Infrastructure → Desktop/RemoteControl. No upward dependencies.
2. **Dependency Injection**: Use constructor injection exclusively. Register all services in the composition layer.
3. **MVVM**: View logic belongs in the ViewModel; avoid code-behind for business logic. ViewModels should inherit from `ObservableObject` and implement `INavigationAware` when needed.
4. **Single Source of Truth**: The `pocketmc.yml` file is the master configuration. Do not hardcode versions, proxy URLs, or social links in code.
5. **HTTP Resilience**: When implementing manual fallback loops (e.g., trying multiple proxy backends), **do not** attach global Polly Circuit Breaker policies (`.AddStandardResilience()`) to the `HttpClient`. This prevents `BrokenCircuitException` from blocking fallback attempts. Use explicit retry logic in the client code.

## 🔒 Security Best Practices

- **Path Safety**: Always use `PathSafety.ValidateContainedPath` when constructing file paths from untrusted input (ZIP entries, modpack overrides, user-supplied paths).
- **Credential Storage**: Use `DataProtector.Protect`/`Unprotect` for any sensitive strings (API keys, OAuth tokens, passwords). These are encrypted with DPAPI per Windows user account.
- **Input Validation**: Validate player names, server ports, and file names before using them in commands or filesystem operations.
- **Modpack Overrides**: When extracting overrides, the policy currently allows executables and scripts to support modpacks. Please ensure any new override extraction logic respects the existing policy and warns the user about potential risks.
- **Telemetry**: Telemetry is anonymous and opt-out. Do not include PII, IP addresses, or server logs in telemetry payloads.

## 🧪 Special Development Notes

### Cloud Backups

To test cloud backup integrations locally, you need to configure OAuth credentials:
- **Google Drive**: Register a project in Google Cloud Console, enable Drive API, and set the redirect URI to `http://127.0.0.1:49384/callback`. The client secret is managed by the proxy backend (`PocketMC.PlayitPartnerProxy`). For testing, you can use the test client ID embedded in the code (but note the proxy must be running).
- **Dropbox**: Register an app, set redirect URI to `http://localhost/`, and use the client ID `fie4wk21xomfr30` (provided for testing).
- **OneDrive**: Register an app in Azure AD with redirect URI `http://localhost` and use the client ID `b6d4713b-afdf-4e6e-bf14-08aa6633d6c9`.

### Playit.gg Integration

- The Playit agent is downloaded and verified via SHA-256 and Authenticode signature. When updating the agent version, update both the download URL and the expected hash in `DownloaderService.cs`.
- For partner provisioning, the app uses a proxy backend. The URLs are defined in `pocketmc.yml` under `auth_proxies`. For local testing, you can set the environment variable `POCKETMC_PLAYIT_BACKEND_URL` to point to your own proxy instance.

### Remote Control Dashboard

- The web dashboard uses static files from `PocketMC.RemoteControl/Web/`. Ensure any changes to HTML/CSS/JS are reflected in the build output.
- Authentication uses cookie-based sessions with `HttpOnly` and `SameSite=Strict`. Rate limiting is in-memory; if scaling to multiple instances, consider using a distributed cache.

## 📦 Release Process

Releases are automated via GitHub Actions workflows:
- **Beta**: Triggered on pushes to `master` when `channel: beta` in `pocketmc.yml`.
- **Release**: Triggered on pushes to `master` when `channel: release`.

### Manual Release Steps (Maintainers)

1. Update the version in `pocketmc.yml` and ensure `WhatsNew.txt` is updated with the changelog.
2. Commit and push to `master`.
3. The CI will build, pack, and create a GitHub Release with the artifacts.
4. Discord notifications are sent automatically.

### CI/CD PowerShell Escaping

When modifying GitHub Actions workflows that use `shell: pwsh`, be careful with multiline strings (e.g., commit messages, release notes). Use environment variables or `$env:GITHUB_OUTPUT` to avoid backtick escaping issues.

---

Thank you for contributing to PocketMC! Your efforts help make Minecraft server management simpler and more enjoyable for everyone. If you have any questions, feel free to reach out on [Discord](https://discord.gg/mWdMr8Mc2m) or open a discussion on GitHub.