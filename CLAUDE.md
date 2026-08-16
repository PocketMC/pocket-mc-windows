# PocketMC Windows

## Project Description

PocketMC is a native WPF/.NET 8 desktop app for local Minecraft server hosting. It handles software downloads, isolated instances, managed Java and PHP runtimes, live metrics, logs, backups, cloud replication, Playit.gg tunnels, add-ons, and a remote web dashboard all within a native Windows UI.

## Tech Stack

- **Platform**: C# .NET 8 Desktop Runtime (WPF)
- **UI Framework**: Wpf.Ui (MVVM Pattern)
- **Architecture**: Clean Architecture (Domain -> Application -> Infrastructure -> Desktop)
- **Testing**: xUnit, Moq
- **Configuration**: YAML (`pocketmc.yml`), embedded resources
- **CI/CD**: GitHub Actions (Windows runners), MSBuild

## Project Structure

```
├── PocketMC.Domain                 # Enterprise models, enums, path safety (Zero external dependencies)
├── PocketMC.Application            # Use cases, interfaces, policies (Depends on Domain)
├── PocketMC.Infrastructure         # External integrations: Adoptium, Cloud, AI, Playit, Process (Depends on App, Domain)
├── PocketMC.RemoteControl          # Embedded ASP.NET Core web server, REST API & WebSocket console
├── PocketMC.Desktop                # WPF UI (Wpf.Ui), MVVM ViewModels, Pages, DI Composition
│
├── PocketMC.Domain.Tests           # Isolated domain model and security unit tests (79 tests)
├── PocketMC.Application.Tests      # Isolated use case & policy tests with mocked infrastructure (50 tests)
├── PocketMC.Infrastructure.Tests   # Concrete provider, network, process & cloud integration tests (298 tests)
├── PocketMC.RemoteControl.Tests    # Web dashboard host, auth & tunnel provider tests (46 tests)
├── PocketMC.Desktop.Tests          # WPF ViewModel, navigation, dialog & XAML binding tests (207 tests)
│
└── pocketmc.yml                    # Single Source of Truth for configs
```

## Key Conventions & Development Guidelines

- **Architecture Rules**: Never bleed WPF/UI logic (`System.Windows.*`) into the Domain, Application, or Infrastructure layers. Keep projects strictly separated.
- **Layer Isolation & Testing**:
  - `PocketMC.Domain.Tests`: Pure isolation, zero external dependencies.
  - `PocketMC.Application.Tests`: Mocks infrastructure interfaces; never references concrete infrastructure or desktop projects.
  - `PocketMC.Infrastructure.Tests`: Uses scoped test fixtures (`PortReliabilityTestWorkspace`, `TestSourceFileResolver`).
  - `PocketMC.Desktop.Tests`: Focuses exclusively on ViewModels, navigation, and presentation layer behavior.
- **Scoped Visibility**: Scoped `<InternalsVisibleTo Include="..." />` is configured across layers for corresponding test projects.
- **Single Source of Truth**: The `pocketmc.yml` file is the master config. Do not hardcode versions in `.csproj` files or C# source. Use `AppConfig` to parse versions and proxies.
- **Dependency Injection**: Use Constructor Injection exclusively. All new dependencies must be registered in the `Composition` folder using `IServiceCollection`.
- **MVVM Pattern**: View logic belongs in the ViewModel. Avoid code-behind for business logic. ViewModels should inherit from `ObservableObject` and `INavigationAware`.
- **HTTP Fallbacks & Resilience**: When writing a manual retry loop for multiple backend proxies (e.g., in auth providers), do NOT attach global Polly Circuit Breaker policies (`.AddStandardResilience()`) to the injected `HttpClient`. This prevents `BrokenCircuitException` deadlocks.
- **CI/CD PowerShell Escaping**: When passing string variables in GitHub Actions using `shell: pwsh`, PowerShell treats the backtick as an escape character. Markdown code blocks with triple backticks require explicit string concatenation (e.g., `+ '```text' +`).
- **Beta Tags**: Beta builds calculate their number relative to the version string (e.g. `1.9.5-beta.3`) based on existing tags on the repository, automatically resetting on version bumps.

## Commands

### Building and Running
```bash
dotnet restore
dotnet build PocketMC.Desktop.sln
dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
```

### Testing
```bash
# Run all 680 tests across the full solution (100% pass rate)
dotnet test PocketMC.Desktop.sln

# Target specific architectural layers
dotnet test PocketMC.Domain.Tests/PocketMC.Domain.Tests.csproj
dotnet test PocketMC.Application.Tests/PocketMC.Application.Tests.csproj
dotnet test PocketMC.Infrastructure.Tests/PocketMC.Infrastructure.Tests.csproj
dotnet test PocketMC.RemoteControl.Tests/PocketMC.RemoteControl.Tests.csproj
dotnet test PocketMC.Desktop.Tests/PocketMC.Desktop.Tests.csproj
```

## Available Skills

This project includes custom `.claude/skills` to assist with development:
- `generate-mvvm`: Scaffolds a new View and ViewModel and registers it in the DI container.
- `local-publish`: Compiles a standalone, self-contained executable for testing release builds locally.

