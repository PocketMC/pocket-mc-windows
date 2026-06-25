# PocketMC Windows Agent Rules

## Project Overview

PocketMC is a native WPF/.NET 8 desktop app for local Minecraft server hosting. It handles software downloads, isolated instances, managed Java and PHP runtimes, live metrics, logs, backups, cloud replication, Playit.gg tunnels, add-ons, and a remote web dashboard all within a native Windows UI.

The architecture strictly follows Clean Architecture: Domain (models), Application (use cases), Infrastructure (networking/external), Desktop (WPF UI), and RemoteControl (web API). 

## Building and Running

Use the following commands to build, run, and test the project:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project PocketMC.Desktop/PocketMC.Desktop.csproj
```

## Development Conventions

These rules dictate the architectural constraints and development practices for this repository. Adhere to them strictly when making modifications or adding new features.

## 1. Architecture & Layering
This project strictly follows Clean Architecture principles, separated into specific projects:
- **PocketMC.Domain**: Core business logic, pure models, enums. Has NO dependencies on other layers or WPF.
- **PocketMC.Application**: Interfaces, application logic, and use cases (e.g., ILlmProvider, ILlmProviderFactory). Depends ONLY on Domain.
- **PocketMC.Infrastructure**: Concrete implementations of external concerns (Networking, Cloud Backups, AI API Clients like GeminiProvider, HTTP infrastructure). Depends on Application and Domain.
- **PocketMC.Desktop**: The WPF Presentation layer. Contains Views, ViewModels, and UI-specific logic. Configures the Dependency Injection container.
- **PocketMC.RemoteControl**: Cross-cutting API project.

*Rule*: Never bleed WPF/UI logic (System.Windows.*) into Domain, Application, or Infrastructure.

## 2. Dependency Management
- **Central Package Management (CPM)**: We use Directory.Packages.props. **NEVER** specify <Version> attributes inside individual <PackageReference> nodes in .csproj files. If you need a new NuGet package, add its version to Directory.Packages.props first, then add the versionless reference to the target project.
- **Shared Properties**: We use Directory.Build.props to enforce .NET 8/10, Nullable enablement, and TreatWarningsAsErrors. Do not override these locally without a very good reason.

## 3. Testing
- **Framework**: Use xUnit and Moq for all automated tests.
- **Structure**: Tests are separated to mirror the core projects (PocketMC.Domain.Tests, PocketMC.Application.Tests, PocketMC.Infrastructure.Tests).
- **Isolation**: When writing a test for a specific layer, do not accidentally include dependencies or context from a higher layer (especially PocketMC.Desktop).

## 4. Coding Standards & Conventions
- **No Junk Drawers**: Avoid Helpers or Utils folders. Group classes by feature cohesion (e.g., Features/Networking, Features/Intelligence).
- **Naming Conventions**: Use ViewModel suffix (not VM). Use clear, descriptive names.
- **Dependency Injection**: Use Constructor Injection exclusively. All new services must be registered via the DI container extensions in Composition/ServiceCollectionExtensions.cs.
- **Global Usings**: Do not use massive GlobalUsings.cs files to mask namespace dependencies. Keep usings explicit per file.

## 5. AI Integration Strategy
- We use a Strategy pattern for AI. Do NOT build monolithic API clients.
- If adding a new AI model or provider, implement ILlmProvider in PocketMC.Infrastructure.AI.Providers and register it with the ILlmProviderFactory.

## 6. UI Guidelines
- The desktop app uses the **WPF UI (Wpf.Ui)** library. 
- Stick to the MVVM pattern. View logic belongs in the ViewModel, avoiding code-behind where possible unless interacting directly with WPF visual elements.

## 7. Configuration & Single Source of Truth
- **`pocketmc.yml`**: This embedded YAML file is the single source of truth for the application's version, release channel, backend proxies (auth/telemetry), and social links. 
- **NO Hardcoded Versions**: Do not hardcode the version in `.csproj` files, `Directory.Build.props`, or C# classes. Always retrieve it dynamically from `AppConfig` (which parses `pocketmc.yml`).

## 8. CI/CD & GitHub Actions
- **PowerShell Escaping**: Be extremely careful when passing strings from GitHub Actions YAML into inline PowerShell scripts (`shell: pwsh`). PowerShell treats the backtick (`` ` ``) as an escape character, so literal backticks (like markdown code blocks) must be constructed using string concatenation with single quotes (e.g., `+ '```text' +`) to prevent parser crashes.
- **Beta Releases**: Beta releases dynamically calculate their count relative to the version string (e.g., `v1.9.5-beta.3`) by checking existing Git tags, ensuring clean, predictable, release-wise numbering.

## 9. HTTP Resilience & Proxies
- **Proxy Fallback Loops**: When writing custom fallback loops across multiple backend proxy URLs, DO NOT attach global Polly Circuit Breaker policies (like `.AddStandardResilience()`) to the `HttpClient`. If the first proxy times out or returns a 5xx, the circuit breaker will open and immediately block the fallback attempt to the second proxy with a `BrokenCircuitException`. Instead, rely on your explicit application logic to discard the error and move to the next URL.
