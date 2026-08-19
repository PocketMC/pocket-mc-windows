# Contributing to PocketMC

Guidelines and workflows for developing, testing, and contributing to the PocketMC codebase.

---

## Development Setup

### Prerequisites

- **.NET 8 SDK** (pinned via `global.json`)
- **Visual Studio 2022** (with *.NET desktop development* workload) or **JetBrains Rider**
- **Windows 10 Build 17763+** or **Windows 11** (x64)

### Building from Source

```bash
git clone https://github.com/PocketMC/pocket-mc-windows.git
cd pocket-mc-windows

dotnet restore
dotnet build PocketMC.Desktop.sln
dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
```

The application uses `pocketmc.yml` as the single source of truth for versioning, release channels, and backend proxy URLs. Never hardcode version strings in `.csproj` files or C# source.

### Packaging (Velopack)

PocketMC uses Velopack for packaging and auto-updates.

```bash
# 1. Install Velopack CLI tool
dotnet tool install -g vpk

# 2. Build and publish Release artifacts
dotnet build -c Release
dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj -c Release -r win-x64 --self-contained false -o publish

# 3. Pack the release (replace <version> with version from pocketmc.yml)
vpk pack --packId PocketMC --packVersion <version> --packDir publish --mainExe PocketMC.Desktop.exe --framework net8.0 --runtime win-x64 --outputDir Releases
```

---

## Solution Structure & Architecture

PocketMC follows strict Clean Architecture across 5 production projects and 5 dedicated test projects:

```
├── PocketMC.Domain                 # Core domain models, enums, path safety (Zero external dependencies)
├── PocketMC.Application            # Use cases, interfaces, and policy validations
├── PocketMC.Infrastructure         # Concrete integrations: Adoptium, Cloud, AI, Playit, Process, DPAPI
├── PocketMC.RemoteControl          # Embedded ASP.NET Core web host, REST API, WebSockets, tunnels
├── PocketMC.Desktop                # WPF presentation (Wpf.Ui), ViewModels, XAML pages, DI Composition
│
├── PocketMC.Domain.Tests           # Unit tests for domain models, serialization, and path safety
├── PocketMC.Application.Tests      # Use case workflows and policy tests with mocked infrastructure
├── PocketMC.Infrastructure.Tests   # Concrete provider, cloud, process, and network integration tests
├── PocketMC.RemoteControl.Tests    # Web server host, remote auth, WebSocket, and tunnel tests
└── PocketMC.Desktop.Tests          # ViewModel lifecycle, navigation, dialog, and XAML binding tests
```

---

## Development & Architecture Rules

### 1. Clean Architecture & Layer Boundaries

Dependencies flow strictly inward: `Desktop` / `RemoteControl` → `Infrastructure` → `Application` → `Domain`.

- **`PocketMC.Domain`**: Pure enterprise models, value objects, domain exceptions, and domain contracts (`PathSafety`, `SafeZipExtractor`). Contains zero external dependencies, no file I/O, and no UI.
- **`PocketMC.Application`**: Use cases, interfaces, orchestrators, and policy validators (`ModpackOverridePolicy`, `MarketplaceDownloadPolicy`). Depends only on `Domain`.
- **`PocketMC.Infrastructure`**: Concrete external integrations (Adoptium Java provisioning, Cloud backups, AI strategies, Playit agent, server process management, port probes, DPAPI security). Depends on `Application` and `Domain`.
- **`PocketMC.RemoteControl`**: Embedded ASP.NET Core web server, REST API, WebSocket console streaming, auth session security, and tunnel providers.
- **`PocketMC.Desktop`**: WPF presentation (`Wpf.Ui`), MVVM ViewModels, Pages, and DI Composition Root.

**Enforced Constraints**:
- Lower layers must never reference higher layers.
- Presentation namespaces (`System.Windows.*`, WPF controls) must never leak into `Domain`, `Application`, `Infrastructure`, or `RemoteControl`.
- Business logic must never reside in WPF code-behind or generic utility dumping grounds.

### 2. Central Package Management (CPM)

All NuGet dependency versions are centrally managed in `Directory.Packages.props` at the repository root.

- Never add `<Version>` attributes to `<PackageReference>` elements in `.csproj` files.
- When introducing a new package, define its version in `Directory.Packages.props` and reference it without a version tag in the target project.

### 3. Dependency Injection & Service Registration

- **Constructor Injection**: Use constructor injection exclusively for all service dependencies.
- **No Service Locator**: Never inject `IServiceProvider` to resolve dependencies dynamically within services, and never instantiate services directly with `new`.
- **Central Registration**: Register all dependencies in `PocketMC.Desktop/Composition/ServiceCollectionExtensions.cs` or modular feature extensions (e.g., `InstanceServiceCollectionExtensions.cs`).

### 4. MVVM & Presentation Standards

- **ViewModels**: Inherit from CommunityToolkit's `ObservableObject` and implement `INavigationAware` when tied to page navigation lifecycle.
- **Code-Behind**: Code-behind files (`.xaml.cs`) are strictly for view initialization and `INavigableView<T>` binding. No business, validation, or orchestration logic is permitted in code-behind.
- **Feature Cohesion**: Group ViewModels, Pages, Dialogs, and UI models by feature namespace (e.g., `Features.Instances`, `Features.Mods`, `Features.Settings`).

### 5. Configuration as Single Source of Truth

- `pocketmc.yml` is the master configuration file for application versioning, release channels, and backend proxy endpoints.
- Read dynamic configuration via `AppConfig`. Never duplicate configuration values, hardcode URLs, or hardcode version numbers in C# source or `.csproj` files.

### 6. HTTP Resilience & Fallback Loops

- When implementing multi-host fallback loops across backend proxy endpoints, **do not** attach global Polly Circuit Breaker policies (`.AddStandardResilience()`) to the `HttpClient`. This prevents `BrokenCircuitException` from blocking fallback attempts to alternative healthy hosts.

### 7. Scoped Assembly Visibility & Test Layer Mirroring

- Production assemblies use `<InternalsVisibleTo Include="..." />` to expose internal members strictly to their matching test project (e.g., `PocketMC.Infrastructure` exposes internals to `PocketMC.Infrastructure.Tests`).
- Test projects must mirror their production layers. Tests for lower layers (`Domain.Tests`, `Application.Tests`) must never reference upper production layers.

---

## Testing Guidelines

The test suite is organized into 5 projects directly mirroring production layers. All unit and integration tests use **xUnit** and **Moq**.

```bash
# Run all solution tests (680 tests, 100% passing)
dotnet test PocketMC.Desktop.sln

# Target specific layers
dotnet test PocketMC.Domain.Tests/PocketMC.Domain.Tests.csproj
dotnet test PocketMC.Application.Tests/PocketMC.Application.Tests.csproj
dotnet test PocketMC.Infrastructure.Tests/PocketMC.Infrastructure.Tests.csproj
dotnet test PocketMC.RemoteControl.Tests/PocketMC.RemoteControl.Tests.csproj
dotnet test PocketMC.Desktop.Tests/PocketMC.Desktop.Tests.csproj
```

### Test Placement Rules

- **Domain Tests**: Pure unit tests verifying business rules, entity invariants, `PathSafety`, `SafeZipExtractor`, and `VersionStringComparer`.
- **Application Tests**: Use case workflows and policy enforcement (`ModpackOverridePolicy`, `MarketplaceDownloadPolicy`). Infrastructure dependencies must be mocked.
- **Infrastructure Tests**: External integrations, Adoptium provisioning, cloud backup providers, process management, and port diagnostics using isolated fixtures (`PortReliabilityTestWorkspace`, `TestSourceFileResolver`).
- **RemoteControl Tests**: Web host lifecycle, JWT/cookie authentication, WebSocket streaming, rate limiting, and tunnel providers.
- **Desktop Tests**: ViewModels, commands, dialog coordinators, and UI state management.

---

## Security Practices

- **Path Traversal Prevention**: Always validate untrusted paths (archive entries, modpacks, user inputs) with `PathSafety.ValidateContainedPath`.
- **Credential Storage**: Sensitive tokens (OAuth tokens, API keys) must be encrypted at rest using DPAPI via `DataProtector`.
- **Input Sanitization**: Validate server ports, command inputs, and player names before execution.
- **Atomic Operations**: File writes and updates should use staged promotion (write to `.partial` / staging directory, validate checksums/signatures, then promote).

---

## Pull Request Checklist

Before submitting a pull request, verify:

- [ ] Solution builds with 0 warnings and 0 errors: `dotnet build PocketMC.Desktop.sln -c Release`
- [ ] All tests pass: `dotnet test PocketMC.Desktop.sln`
- [ ] New tests are placed in the appropriate test project mirroring the modified production layer.
- [ ] No WPF or UI namespaces leak into lower layers (`Domain`, `Application`, `Infrastructure`, `RemoteControl`).
- [ ] No hardcoded versions or credentials exist in source code or `.csproj` files.
- [ ] New dependencies are registered in `Directory.Packages.props` (CPM) and DI composition extensions.
- [ ] Commits are atomic and descriptive.

---

## Code of Conduct

All participants in the PocketMC community are expected to follow the standards outlined in our [Code of Conduct](CODE_OF_CONDUCT.md). Please report any unacceptable behavior to [sahajitaliya33@gmail.com](mailto:sahajitaliya33@gmail.com).