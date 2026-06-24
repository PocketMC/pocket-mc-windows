\# PocketMC Windows — Architecture Transformation \& Refactoring Audit



> \*\*Reviewer role:\*\* Principal Software Architect, Staff Engineer, Open Source Maintainer, Security Reviewer, DX Expert

> \*\*Scope:\*\* Complete repository transformation for production-grade, long-term sustainability

> \*\*Tone:\*\* Brutally honest, specific, actionable



\---



\# Phase 1: Repository Understanding



\## Architecture Overview



\*\*PocketMC Windows\*\* is a native WPF/.NET 8 desktop application that provides local-first Minecraft server management for Windows 10 1809+/11. It is not a cloud host, not a launcher, and not a web panel — it is a single-user desktop orchestrator for Minecraft server software, runtimes, networking, backups, and remote access.



\### Mental Model



```

┌─────────────────────────────────────────────────────────────────────┐

│                        PocketMC.Desktop (single assembly)            │

│                                                                      │

│  ┌─────────┐  ┌──────────┐  ┌────────────┐  ┌──────────────┐        │

│  │  Shell   │  │ Features │  │ Infrastructure│ │  Core        │       │

│  │  (WPF)   │→ │ (mixed   │→ │ (mixed OS/  │→ │ (interfaces, │       │

│  │  Nav,    │  │  VM+Svc+ │  │  HTTP+UI+   │  │  MVVM, conv) │       │

│  │  Tray)   │  │  View)   │  │  Security)  │  │              │       │

│  └─────────┘  └──────────┘  └────────────┘  └──────────────┘        │

│       │              │              │               │                │

│       └──────────────┴──────────────┴───────────────┘                │

│                          │                                           │

│                    ┌─────┴──────┐                                    │

│                    │ Composition │                                    │

│                    │ (DI wiring) │                                    │

│                    └────────────┘                                    │

└─────────────────────────────────────────────────────────────────────┘

&#x20;        │                    │                    │

&#x20;   ┌────┴────┐         ┌────┴────┐          ┌────┴────┐

&#x20;   │ Filesystem│       │ HTTP    │          │ OS APIs │

&#x20;   │ (instances,│      │ (Modrinth,│        │ (Process,│

&#x20;   │  runtimes) │      │  Playit, │          │  Registry,│

&#x20;   │            │      │  Adoptium)│         │  UWP)    │

&#x20;   └──────────┘       └─────────┘          └─────────┘

```



\### Key Characteristics



| Aspect | Current State |

|--------|--------------|

| \*\*Project count\*\* | 2 (`PocketMC.Desktop`, `PocketMC.Desktop.Tests`) |

| \*\*Assembly separation\*\* | None — all domain, infrastructure, UI, and OS code in one assembly |

| \*\*Architecture style\*\* | Feature-folder based, but feature folders mix VMs, services, views, models, providers |

| \*\*DI\*\* | Microsoft.Extensions.DependencyInjection via `Composition/ServiceCollectionExtensions.cs` |

| \*\*MVVM\*\* | Custom `ViewModelBase`, `RelayCommand`, `AsyncRelayCommand` (CommunityToolkit.Mvvm not used) |

| \*\*HTTP\*\* | Per-feature ad-hoc `HttpClient` usage — no shared client factory pattern visible |

| \*\*Persistence\*\* | JSON files on disk via `IFileSystem` abstraction (good), no database |

| \*\*Testing\*\* | xUnit + Moq, \~80 test files, flat structure |

| \*\*CI/CD\*\* | GitHub Actions (`production-build.yml`), Velopack for releases |

| \*\*Remote Control\*\* | Embedded ASP.NET Core Kestrel host serving static HTML/CSS/JS + REST API + WebSocket |

| \*\*AI\*\* | Multi-provider LLM integration (Gemini, OpenAI, Claude, Mistral, Groq, Ollama) for session summaries |



\### External Dependencies (APIs)



| Provider | Purpose | Integration Point |

|----------|---------|-------------------|

| Adoptium | Java runtime downloads | `Features/Java/JavaAdoptiumClient.cs` |

| Modrinth | Mod/plugin marketplace | `Features/Marketplace/ModrinthService.cs` |

| CurseForge | Mod marketplace | `Features/Marketplace/CurseForgeService.cs` |

| Poggit | PocketMine plugins | (inferred from Marketplace) |

| Playit.gg | Tunnel agent + API | `Features/Tunnel/PlayitApiClient.cs` |

| Cloudflare | Quick tunnels for Remote Control | `Features/RemoteControl/Tunnels/CloudflaredQuickTunnelProvider.cs` |

| Google Drive | Cloud backups | `Features/CloudBackups/Providers/GoogleDriveBackupProvider.cs` |

| Dropbox | Cloud backups | `Features/CloudBackups/Providers/DropboxBackupProvider.cs` |

| OneDrive | Cloud backups | `Features/CloudBackups/Providers/OneDriveBackupProvider.cs` |

| Multiple LLMs | AI session summaries | `Features/Intelligence/AiApiClient.cs` |

| Discord | RPC + bot integration | `Features/Instances/Services/DiscordRpcService.cs` |



\### Data Flow (typical: start server)



```

User clicks "Start" on Dashboard

&#x20; → DashboardViewModel (command)

&#x20;   → IServerLifecycleService.StartAsync(instanceId)

&#x20;     → InstanceRegistry.Get(id)

&#x20;     → PortPreflightService.Check(instanceId)

&#x20;     → ServerLaunchConfigurator.Configure(instance)

&#x20;     → JavaRuntimeResolver / PhpProvisioningService (if needed)

&#x20;     → ServerProcessManager.Launch(process, config)

&#x20;     → ResourceMonitorService.BeginTracking(process)

&#x20;     → ConsoleLogHistoryService.BeginCapture(process.Stdout)

&#x20;     → DiscordRpcService.UpdatePresence(...)

&#x20;     → SleepPreventionService.Activate()

&#x20;     → (if tunnel) InstanceTunnelOrchestrator.EnsureTunnel(...)

```



This is a deep call chain with implicit ordering dependencies — no explicit state machine or pipeline.



\---



\# Phase 2: Architecture Review



\## Issue Catalog



\### 2.1 — Single-Assembly Monolith



\*\*Severity:\*\* Critical

\*\*Location:\*\* `PocketMC.Desktop.csproj`



\*\*Problem:\*\* The entire application — domain logic, HTTP clients, OS integrations (registry, UWP, process management, power management), WPF views, ViewModels, ASP.NET Core remote host, and static web assets — lives in one assembly. There is no compile-time enforcement of dependency direction. UI code can (and does) call infrastructure directly. Domain logic references WPF types.



\*\*Long-term impact:\*\*

\- Cannot reuse domain logic in a CLI tool, headless service, or different UI framework

\- Every test project pulls in WPF assemblies, slowing test execution

\- Build times grow linearly with codebase growth

\- No way to enforce layering rules via `InternalsVisibleTo` or project references

\- Contributors cannot understand the dependency graph without reading every file



\*\*Suggested solution:\*\* Split into 4+ projects:

\- `PocketMC.Domain` — entities, value objects, domain services (no external deps)

\- `PocketMC.Application` — use cases, orchestration, abstractions

\- `PocketMC.Infrastructure` — HTTP clients, filesystem, OS integrations

\- `PocketMC.Desktop` — WPF views, ViewModels, DI composition

\- `PocketMC.RemoteControl` — ASP.NET Core host (separate deployable)



\---



\### 2.2 — Feature Folders Mix Responsibilities



\*\*Severity:\*\* Critical

\*\*Location:\*\* Every `Features/\*/` directory



\*\*Problem:\*\* Each feature folder contains a mix of:

\- WPF Views (`.xaml` + `.xaml.cs`)

\- ViewModels

\- Domain services

\- Infrastructure services (HTTP clients, file I/O)

\- Data models

\- Provider implementations



Example — `Features/Instances/` contains:

```

Backups/           → domain + infrastructure (BackupService, SafeZipExtractor)

ImportExport/      → UI + service (InstanceExportPage.xaml + InstanceExportService)

Models/            → ServerProcess.cs (one file — why a folder?)

Providers/         → strategy pattern (good, but should be in Domain)

Services/          → 14 files including InstanceManager, DiscordRpcService, SlugHelper

Updates/           → 11 files — an entire sub-system

RuntimeDownloadDialog.xaml → UI

ServerPropertiesParser.cs → pure logic

```



`DiscordRpcService` does not belong in `Instances/Services`. `SlugHelper` is a utility, not an instance service. `ServerPropertiesParser` is a parser, not a service.



\*\*Long-term impact:\*\* Contributors cannot find code by responsibility. "Where does HTTP live?" has no answer. "Where is the domain model?" has no answer. Feature folders become junk drawers.



\*\*Suggested solution:\*\* Separate by layer within each feature, or separate by layer at the project level. See Phase 3.



\---



\### 2.3 — ViewModel Naming Inconsistency



\*\*Severity:\*\* Medium

\*\*Location:\*\* Throughout `Features/`



\*\*Problem:\*\* Two naming conventions coexist:

\- `ViewModel` suffix: `DashboardViewModel`, `PlayerManagementViewModel`, `InstanceExportViewModel`, `ShellViewModel`, `TrayIconViewModel`

\- `VM` suffix: `DashboardActionsVM`, `DashboardInstanceListVM`, `DashboardMetricsVM`, `SettingsGeneralVM`, `SettingsBackupsVM`, `SettingsBedrockVM`, `SettingsAdvancedVM`, `SettingsPerformanceVM`, `SettingsSummariesVM`, `SettingsVersionUpdatesVM`, `SettingsWorldVM`



\*\*Long-term impact:\*\* Signals lack of code review discipline. Contributors won't know which to use. Search becomes harder (`\*ViewModel` vs `\*VM`).



\*\*Suggested solution:\*\* Standardize on `ViewModel` suffix. The `VM` suffix is a shorthand that saves 6 characters at the cost of consistency.



\---



\### 2.4 — Duplicate/Overlapping Tunnel Logic



\*\*Severity:\*\* High

\*\*Location:\*\* `Features/Tunnel/` vs `Features/RemoteControl/Tunnels/`



\*\*Problem:\*\* Two separate tunnel systems exist:

\- `Features/Tunnel/` — Playit agent management, `PlayitAgentService`, `PlayitAgentStateMachine`, `TunnelService`, `InstanceTunnelOrchestrator`

\- `Features/RemoteControl/Tunnels/` — `RemoteTunnelManager`, `CloudflaredQuickTunnelProvider`, `PlayitHttpsTunnelProvider`, `IRemoteTunnelProvider`



These are solving related problems (exposing local services via tunnels) with different abstractions. `Features/Networking/` adds a third layer with `PortProbeService`, `PortPreflightService`, `PortConflictInfo`, `PortLeaseRegistry` — 22 files for port management.



\*\*Long-term impact:\*\* Three tunnel/port subsystems will diverge. Bug fixes in one won't propagate. Contributors will implement the same feature three times.



\*\*Suggested solution:\*\* Unify under a single `Networking` or `Connectivity` domain with clear sub-boundaries: `Ports` (local management), `Tunnels` (public exposure — Playit + Cloudflare), `RemoteAccess` (the dashboard host that uses tunnels).



\---



\### 2.5 — Settings Folder is a Junk Drawer



\*\*Severity:\*\* High

\*\*Location:\*\* `Features/Settings/`



\*\*Problem:\*\* 17 files with mixed responsibilities:

\- 9 ViewModels (inconsistent naming)

\- `SettingsManager` — persistence

\- `TelemetryService` — analytics (cross-cutting, not settings)

\- `ImageCropPage.xaml` — UI utility (not settings-specific)

\- `MinecraftMotdConverter` — value converter (belongs in presentation)

\- `ServerRuntimeSettingApplier` — runtime config application (belongs in Instances)

\- `ServerSettingsProfile` — data model (belongs in Models)

\- `PropertyItem` — UI model (belongs in presentation)

\- `AddonAutoUpdateService` — add-on logic (belongs in Mods)

\- `ServerCloudBackupViewModel` — cloud backup VM (belongs in CloudBackups)



\*\*Long-term impact:\*\* "Settings" becomes the dumping ground for anything vaguely configurable. No one knows what's there.



\*\*Suggested solution:\*\* Move each file to its proper feature/domain. Keep `Settings` for app-level configuration UI and persistence only.



\---



\### 2.6 — `Helpers` Folder Anti-Pattern



\*\*Severity:\*\* Medium

\*\*Location:\*\* `PocketMC.Desktop/Helpers/`



\*\*Problem:\*\* Four unrelated files:

\- `AnimatedNavIndicatorBehavior.cs` — WPF behavior (presentation)

\- `CommandFormatter.cs` — console formatting (Console feature)

\- `GeyserDetector.cs` + `IGeyserDetector.cs` — cross-play detection (Instances feature)



\*\*Long-term impact:\*\* "Helpers" is universally recognized as a code smell. It signals "I didn't know where to put this." Contributors will add more junk here.



\*\*Suggested solution:\*\* Move each to its proper feature. Delete the `Helpers` folder.



\---



\### 2.7 — No Shared HTTP Client Architecture



\*\*Severity:\*\* High

\*\*Location:\*\* `AiApiClient.cs`, `PlayitApiClient.cs`, `JavaAdoptiumClient.cs`, `ModrinthService.cs`, `CurseForgeService.cs`, cloud backup providers



\*\*Problem:\*\* Each external API integration creates and manages its own HTTP client. No shared:

\- Retry policy (Polly)

\- Timeout configuration

\- Circuit breaker

\- Request/response logging

\- HttpClient factory registration

\- Shared header injection (User-Agent, correlation IDs)



`CloudBackupService` has a `ResilientUploadPolicy` — but only for cloud backups. Why isn't this pattern shared with all HTTP clients?



\*\*Long-term impact:\*\* Inconsistent resilience. One API call might retry 3 times, another might fail immediately. HttpClient socket exhaustion under load. No centralized observability.



\*\*Suggested solution:\*\* Introduce `IHttpClientFactory` with named/typed clients. Apply Polly policies centrally in `Composition`. Create a `DelegatingHandler` for logging, User-Agent, and correlation.



\---



\### 2.8 — `GlobalUsings.cs` Hides Dependencies



\*\*Severity:\*\* Medium

\*\*Location:\*\* `PocketMC.Desktop/GlobalUsings.cs`



\*\*Problem:\*\* Global usings make every file implicitly depend on namespaces without showing it. For a project expecting external contributors, this is hostile. A new developer reading `InstanceManager.cs` cannot see what namespaces it uses without checking `GlobalUsings.cs`.



\*\*Long-term impact:\*\* Reduces readability. Makes static analysis harder. Encourages "just add it to GlobalUsings" instead of thinking about dependencies.



\*\*Suggested solution:\*\* Remove `GlobalUsings.cs`. Use explicit `using` statements in each file. This is the standard for professional .NET projects.



\---



\### 2.9 — No Centralized Error Handling Strategy



\*\*Severity:\*\* High

\*\*Location:\*\* Throughout



\*\*Problem:\*\* No visible:

\- Domain exception hierarchy (no `PocketMCException` base, no `DomainException`, `InfrastructureException`)

\- Result pattern or OneOf for expected failures

\- Global exception handler in WPF (no `DispatcherUnhandledException` visible in structure)

\- Consistent error propagation from services to ViewModels



`PortReliabilityException`, `AddonUnavailableException` exist — but ad-hoc. No pattern.



\*\*Long-term impact:\*\* Every service handles errors differently. ViewModels can't trust service contracts. Users see raw exception messages or generic "something went wrong."



\*\*Suggested solution:\*\*

1\. Define exception hierarchy: `PocketMCException` → `DomainException`, `InfrastructureException`, `ValidationException`

2\. Use `Result<T>` or `OneOf<T, Error>` for expected failures (port conflicts, file locks, missing runtimes)

3\. Add `DispatcherUnhandledException` + `TaskScheduler.UnobservedTaskException` handlers in `App.xaml.cs`

4\. Map exceptions to user-facing messages in a central `IExceptionMapper`



\---



\### 2.10 — Test Structure is Flat and Unnavigable



\*\*Severity:\*\* High

\*\*Location:\*\* `PocketMC.Desktop.Tests/`



\*\*Problem:\*\* 80+ test files in the root directory. Only 3 subdirectories exist: `Models/`, `Providers/`, `RemoteControl/`. Finding tests for a specific feature requires scrolling through 80 files.



Worse — test naming is inconsistent:

\- `\*Tests.cs` (majority)

\- `\*SourceTests.cs` (source-based tests — different pattern)

\- `\*SecurityTests.cs` (security-focused)

\- `\*Integration/` (one integration folder under RemoteControl)



\*\*Long-term impact:\*\* Test coverage becomes unmaintainable. Contributors won't know where to add tests. Parallel test execution conflicts. No clear separation between fast unit tests and slow integration tests.



\*\*Suggested solution:\*\*

1\. Mirror source structure: `Tests/Features/Instances/`, `Tests/Features/Tunnel/`, etc.

2\. Separate unit from integration: `Tests/Unit/` and `Tests/Integration/`

3\. Use `xunit` traits for categorization: `\[Trait("Category", "Integration")]`

4\. Standardize naming: `\*Tests.cs` for all



\---



\### 2.11 — Backup Logic Scattered Across Three Features



\*\*Severity:\*\* High

\*\*Location:\*\* `Features/Instances/Backups/`, `Features/CloudBackups/`, `Features/Settings/`



\*\*Problem:\*\*

\- `Features/Instances/Backups/` — `BackupService`, `BackupSchedulerService`, `BackupMetadata`, `SafeZipExtractor` (local backups)

\- `Features/CloudBackups/` — `CloudBackupService`, providers, OAuth, upload history (cloud replication)

\- `Features/Settings/` — `SettingsBackupsVM`, `ServerCloudBackupViewModel`, `CloudBackupSettingsViewModel` (backup settings UI)



Backup is one domain split across three feature folders. A contributor working on "backup" must navigate three locations.



\*\*Long-term impact:\*\* Backup bugs require changes in 3 folders. Backup-related settings are missed. Cloud backup and local backup logic can diverge.



\*\*Suggested solution:\*\* Consolidate into a single `Backups` domain with sub-areas: `Local/`, `Cloud/`, `Scheduling/`, `Settings/`.



\---



\### 2.12 — `Composition/ServiceCollectionExtensions.cs` is Likely a God File



\*\*Severity:\*\* High

\*\*Location:\*\* `Composition/ServiceCollectionExtensions.cs`



\*\*Problem:\*\* A single file registering all services for the entire application. With the feature count shown, this file is likely 500+ lines. Every new feature adds to it.



\*\*Long-term impact:\*\* Merge conflicts on every PR. No feature ownership of DI registration. New contributors must read the entire file to find where to register a service.



\*\*Suggested solution:\*\* Each feature module exposes its own `Add{Feature}Services(this IServiceCollection)` extension method. The composition root calls each one. This is the standard modular DI pattern.



\---



\### 2.13 — No Domain Model Layer



\*\*Severity:\*\* High

\*\*Location:\*\* `PocketMC.Desktop/Models/` (9 files) + scattered models in features



\*\*Problem:\*\* Domain models (`InstanceMetadata`, `ServerConfiguration`, `ServerState`, `MinecraftVersion`, `ModLoaderVersion`, `EngineCompatibility`) live in a flat `Models/` folder. But feature-specific models are scattered:

\- `Features/CloudBackups/CloudBackupModels.cs` (multiple models in one file)

\- `Features/Instances/Updates/InstanceUpdateModels.cs` (same)

\- `Features/Marketplace/Models/MarketplaceModels.cs` (same)

\- `Features/Mods/AddonInventoryItem.cs`, `JavaModMetadata.cs`, `AddonUpdateModels.cs`

\- `Features/Networking/PortLease.cs`, `PortConflictInfo.cs`, `PortCheckResult.cs` (17 model-like files)

\- `Features/RemoteControl/Models/` (9 DTO files)



There is no single source of truth for domain entities. `CloudBackupModels.cs` containing "multiple models" is a code smell.



\*\*Long-term impact:\*\* Models are anemic (just data bags). No domain behavior. Duplicated concepts (e.g., port info appears in `Networking/` and `Models/`). No aggregate boundaries.



\*\*Suggested solution:\*\* Define domain entities in `PocketMC.Domain` project with proper aggregate roots (`Instance` aggregate, `Backup` aggregate, `Tunnel` aggregate). Move DTOs to `Application/DTOs/` or keep in feature folders but clearly labeled as DTOs.



\---



\### 2.14 — Remote Control Web Assets Embedded in Desktop Project



\*\*Severity:\*\* Medium

\*\*Location:\*\* `Features/RemoteControl/Web/app.js`, `index.html`, `styles.css`



\*\*Problem:\*\* Static web assets (HTML/CSS/JS) live inside the WPF desktop project. These are:

\- Not version-controlled separately

\- Not testable

\- Not lintable

\- Not buildable through any frontend pipeline

\- Mixed with C# code



\*\*Long-term impact:\*\* The web dashboard grows but has no build system. No minification. No TypeScript. No CSS preprocessing. No linting. Contributors who know frontend won't find or improve these files.



\*\*Suggested solution:\*\* Either:

1\. Move to a separate `PocketMC.RemoteControl.Web` project with a proper frontend setup (even if minimal — at least ESLint + a bundler)

2\. Or if keeping minimal: move to `wwwroot/` folder with clear separation, add a `README.md` explaining the stack



\---



\### 2.15 — `WhatsNew` and `ChangelogParser` Instead of Standard CHANGELOG



\*\*Severity:\*\* Low

\*\*Location:\*\* `Features/WhatsNew/`



\*\*Problem:\*\* Custom changelog parsing infrastructure (`ChangelogParser.cs`, `WhatsNewService.cs`, `WhatsNewWindow.xaml`) exists instead of using a standard `CHANGELOG.md` following Keep a Changelog format. The `Assets/WhatsNew.txt` is the source of truth.



\*\*Long-term impact:\*\* Non-standard format that tools can't consume. GitHub Releases won't auto-generate from it. No semantic versioning integration.



\*\*Suggested solution:\*\* Adopt `CHANGELOG.md` in Keep a Changelog format. The WhatsNew UI can still parse it. Use GitHub's release notes generation.



\---



\# Phase 3: Target Architecture Design



\## Design Principles



1\. \*\*Project-per-layer\*\* — compiler-enforced dependency direction

2\. \*\*Feature modules\*\* — each feature owns its services, but lives in the correct layer

3\. \*\*Domain purity\*\* — no infrastructure concerns in domain code

4\. \*\*Testability\*\* — every layer testable in isolation

5\. \*\*Contributor clarity\*\* — a new developer can find code by responsibility, not by guessing



\## Proposed Solution Structure



```

pocket-mc-windows/

│

├── src/

│   ├── PocketMC.Domain/                          # Pure domain — zero external deps

│   │   ├── PocketMC.Domain.csproj                # NetStandard 2.0 (max compatibility)

│   │   ├── Entities/

│   │   │   ├── Instance.cs                       # Aggregate root

│   │   │   ├── InstanceId.cs                     # Value object

│   │   │   ├── ServerProcess.cs                  # Aggregate root

│   │   │   ├── Backup.cs                         # Aggregate root

│   │   │   ├── TunnelAllocation.cs               # Aggregate root

│   │   │   ├── Addon.cs                          # Aggregate root

│   │   │   └── Player.cs                         # Entity

│   │   ├── ValueObjects/

│   │   │   ├── MinecraftVersion.cs

│   │   │   ├── ModLoaderVersion.cs

│   │   │   ├── PortNumber.cs

│   │   │   ├── ServerConfiguration.cs

│   │   │   ├── EngineCompatibility.cs

│   │   │   └── InstanceMetrics.cs

│   │   ├── Enums/

│   │   │   ├── ServerSoftwareType.cs

│   │   │   ├── AddonKind.cs

│   │   │   ├── AddonState.cs

│   │   │   ├── PortProtocol.cs

│   │   │   ├── PortBindingRole.cs

│   │   │   └── CloudBackupProviderType.cs

│   │   ├── Events/

│   │   │   ├── InstanceStarted.cs

│   │   │   ├── InstanceStopped.cs

│   │   │   ├── InstanceCrashed.cs

│   │   │   ├── BackupCompleted.cs

│   │   │   ├── TunnelEstablished.cs

│   │   │   └── PlayerJoined.cs

│   │   ├── Exceptions/

│   │   │   ├── PocketMCException.cs              # Base

│   │   │   ├── DomainException.cs

│   │   │   ├── InfrastructureException.cs

│   │   │   ├── ValidationException.cs

│   │   │   └── ConcurrencyException.cs

│   │   └── Services/                             # Domain services (pure logic)

│   │       ├── IServerSoftwareProvider.cs        # Strategy interface

│   │       ├── PortAllocationPolicy.cs           # Pure port logic

│   │       ├── BackupRetentionPolicy.cs

│   │       └── AddonCompatibilityPolicy.cs

│   │

│   ├── PocketMC.Application/                     # Use cases, orchestration, abstractions

│   │   ├── PocketMC.Application.csproj           # References Domain

│   │   ├── Abstractions/

│   │   │   ├── IInstanceRepository.cs

│   │   │   ├── IBackupRepository.cs

│   │   │   ├── IAddonRepository.cs

│   │   │   ├── IFileStorage.cs                   # File system abstraction

│   │   │   ├── IHttpClient.cs                    # HTTP abstraction

│   │   │   ├── IProcessRunner.cs                 # Process execution abstraction

│   │   │   ├── IClock.cs                         # Time abstraction

│   │   │   └── IRandomProvider.cs

│   │   ├── Instances/

│   │   │   ├── Commands/

│   │   │   │   ├── CreateInstanceCommand.cs

│   │   │   │   ├── CreateInstanceHandler.cs

│   │   │   │   ├── StartInstanceCommand.cs

│   │   │   │   ├── StartInstanceHandler.cs

│   │   │   │   ├── StopInstanceCommand.cs

│   │   │   │   ├── StopInstanceHandler.cs

│   │   │   │   ├── UpdateInstanceCommand.cs

│   │   │   │   └── UpdateInstanceHandler.cs

│   │   │   ├── Queries/

│   │   │   │   ├── GetInstanceQuery.cs

│   │   │   │   ├── GetInstanceHandler.cs

│   │   │   │   ├── ListInstancesQuery.cs

│   │   │   │   └── ListInstancesHandler.cs

│   │   │   └── Pipelines/

│   │   │       └── ServerStartupPipeline.cs      # Explicit startup steps

│   │   ├── Backups/

│   │   │   ├── Commands/

│   │   │   │   ├── CreateBackupCommand.cs

│   │   │   │   ├── CreateBackupHandler.cs

│   │   │   │   ├── RestoreBackupCommand.cs

│   │   │   │   ├── RestoreBackupHandler.cs

│   │   │   │   ├── UploadToCloudCommand.cs

│   │   │   │   └── UploadToCloudHandler.cs

│   │   │   └── Scheduling/

│   │   │       └── BackupSchedulerService.cs

│   │   ├── Networking/

│   │   │   ├── Commands/

│   │   │   │   ├── CheckPortCommand.cs

│   │   │   │   └── CreateTunnelCommand.cs

│   │   │   └── Services/

│   │   │       ├── PortPreflightService.cs

│   │   │       ├── PortProbeService.cs

│   │   │       └── PortLeaseRegistry.cs

│   │   ├── Addons/

│   │   │   ├── Commands/

│   │   │   │   ├── InstallAddonCommand.cs

│   │   │   │   ├── ToggleAddonCommand.cs

│   │   │   │   └── CheckAddonUpdatesCommand.cs

│   │   │   └── Services/

│   │   │       ├── AddonInventoryService.cs

│   │   │       ├── AddonToggleService.cs

│   │   │       └── DependencyResolverService.cs

│   │   ├── Runtimes/

│   │   │   ├── Commands/

│   │   │   │   ├── ProvisionJavaCommand.cs

│   │   │   │   └── ProvisionPhpCommand.cs

│   │   │   └── Services/

│   │   │       ├── JavaRuntimeResolver.cs

│   │   │       ├── JavaRuntimeValidator.cs

│   │   │       └── PhpProvisioningService.cs

│   │   ├── Intelligence/                        # AI session summaries

│   │   │   ├── Abstractions/

│   │   │   │   ├── ILlmProvider.cs              # Provider strategy

│   │   │   │   ├── ILlmClient.cs                # HTTP abstraction

│   │   │   │   └── ISummaryStorage.cs

│   │   │   ├── Commands/

│   │   │   │   └── GenerateSessionSummaryCommand.cs

│   │   │   ├── Services/

│   │   │   │   ├── SessionSummarizationService.cs

│   │   │   │   └── SessionLogPreprocessor.cs

│   │   │   ├── Prompts/

│   │   │   │   ├── SessionSummaryPrompt.cs      # Prompt template

│   │   │   │   └── PromptVariables.cs

│   │   │   └── Providers/

│   │   │       ├── GeminiProvider.cs

│   │   │       ├── OpenAiProvider.cs

│   │   │       ├── ClaudeProvider.cs

│   │   │       ├── MistralProvider.cs

│   │   │       ├── GroqProvider.cs

│   │   │       └── OllamaProvider.cs

│   │   └── DTOs/                                 # Cross-layer data transfer

│   │       ├── InstanceDto.cs

│   │       ├── BackupDto.cs

│   │       ├── PortConflictDto.cs

│   │       └── RemoteInstanceDto.cs

│   │

│   ├── PocketMC.Infrastructure/                  # Implementations of abstractions

│   │   ├── PocketMC.Infrastructure.csproj        # References Application

│   │   ├── FileSystem/

│   │   │   ├── PhysicalFileStorage.cs            # Implements IFileStorage

│   │   │   ├── FileUtils.cs

│   │   │   ├── PathSafety.cs                     # Path traversal prevention

│   │   │   └── SafeZipExtractor.cs

│   │   ├── Http/

│   │   │   ├── HttpClientFactory.cs

│   │   │   ├── RetryDelegatingHandler.cs

│   │   │   ├── UserAgentDelegatingHandler.cs

│   │   │   └── ResilientHttpPolicy.cs            # Polly policies (shared)

│   │   ├── Processes/

│   │   │   ├── ProcessRunner.cs                  # Implements IProcessRunner

│   │   │   ├── ServerProcessManager.cs

│   │   │   ├── MemoryHelper.cs

│   │   │   ├── RconClient.cs

│   │   │   └── JobObject.cs                      # Windows Job Object

│   │   ├── Persistence/

│   │   │   ├── InstanceRepository.cs             # JSON-file backed

│   │   │   ├── BackupRepository.cs

│   │   │   ├── AddonRepository.cs

│   │   │   ├── SettingsStore.cs

│   │   │   └── SummaryStorageService.cs

│   │   ├── Security/

│   │   │   ├── DataProtector.cs                  # DPAPI wrapper

│   │   │   └── SecretStore.cs                    # API key storage

│   │   ├── Windows/

│   │   │   ├── Power/

│   │   │   │   ├── SleepPreventionService.cs

│   │   │   │   └── ServerSleepPreventionCoordinator.cs

│   │   │   ├── Registry/

│   │   │   │   └── ProtocolRegistrationService.cs

│   │   │   ├── Loopback/

│   │   │   │   └── UwpLoopbackHelper.cs

│   │   │   ├── Startup/

│   │   │   │   └── WindowsStartupService.cs

│   │   │   └── Notifications/

│   │   │       └── WindowsToastNotificationService.cs

│   │   ├── Updates/

│   │   │   ├── UpdateService.cs                  # Velopack

│   │   │   ├── InstanceUpdateService.cs

│   │   │   ├── InstanceUpdatePlanner.cs

│   │   │   ├── InstanceUpdateApplier.cs

│   │   │   ├── InstanceRollbackService.cs

│   │   │   └── InstanceUpdateJournalStore.cs

│   │   └── ApiClients/                           # External API implementations

│   │       ├── Adoptium/

│   │       │   └── JavaAdoptiumClient.cs

│   │       ├── Modrinth/

│   │       │   └── ModrinthClient.cs

│   │       ├── CurseForge/

│   │       │   └── CurseForgeClient.cs

│   │       ├── Playit/

│   │       │   ├── PlayitApiClient.cs

│   │       │   ├── PlayitAgentService.cs

│   │       │   └── PlayitAgentStateMachine.cs

│   │       ├── Cloudflare/

│   │       │   └── CloudflaredQuickTunnelProvider.cs

│   │       ├── CloudBackup/

│   │       │   ├── GoogleDriveBackupProvider.cs

│   │       │   ├── DropboxBackupProvider.cs

│   │       │   ├── OneDriveBackupProvider.cs

│   │       │   ├── LoopbackOAuthReceiver.cs

│   │       │   └── PkceHelper.cs

│   │       └── Llm/

│   │           ├── LlmHttpClient.cs              # Implements ILlmClient

│   │           └── LlmResponseParser.cs

│   │

│   ├── PocketMC.Desktop/                         # WPF presentation only

│   │   ├── PocketMC.Desktop.csproj               # References Application + Infrastructure

│   │   ├── App.xaml

│   │   ├── App.xaml.cs                           # Composition root + exception handlers

│   │   ├── Program.cs                            # Entry point

│   │   ├── Composition/

│   │   │   ├── ServiceCollectionExtensions.cs    # Thin — calls feature modules

│   │   │   ├── ServiceRegistrar.cs               # Orchestrates all AddXxx() calls

│   │   │   └── ViewModelLocator.cs               # If needed

│   │   ├── Views/

│   │   │   ├── Shell/

│   │   │   │   ├── MainWindow.xaml

│   │   │   │   ├── MainWindow.xaml.cs

│   │   │   │   └── AboutPage.xaml

│   │   │   ├── Dashboard/

│   │   │   │   └── DashboardPage.xaml

│   │   │   ├── Console/

│   │   │   │   └── ServerConsolePage.xaml

│   │   │   ├── Instances/

│   │   │   │   ├── NewInstancePage.xaml

│   │   │   │   ├── InstanceExportPage.xaml

│   │   │   │   └── InstanceImportPage.xaml

│   │   │   ├── Marketplace/

│   │   │   │   ├── MapBrowserPage.xaml

│   │   │   │   └── PluginBrowserPage.xaml

│   │   │   ├── Players/

│   │   │   │   └── PlayerManagementPage.xaml

│   │   │   ├── Settings/

│   │   │   │   ├── ServerSettingsPage.xaml

│   │   │   │   └── ...

│   │   │   ├── Tunnel/

│   │   │   │   ├── TunnelPage.xaml

│   │   │   │   ├── PortsMapPage.xaml

│   │   │   │   └── CreateTunnelDialog.xaml

│   │   │   ├── RemoteControl/

│   │   │   │   └── RemoteControlPage.xaml

│   │   │   └── Setup/

│   │   │       ├── JavaSetupPage.xaml

│   │   │       └── RootDirectorySetupPage.xaml

│   │   ├── ViewModels/

│   │   │   ├── ShellViewModel.cs

│   │   │   ├── DashboardViewModel.cs

│   │   │   ├── DashboardActionsViewModel.cs

│   │   │   ├── DashboardInstanceListViewModel.cs

│   │   │   ├── DashboardMetricsViewModel.cs

│   │   │   ├── InstanceCardViewModel.cs

│   │   │   ├── PlayerManagementViewModel.cs

│   │   │   ├── ServerConsoleViewModel.cs

│   │   │   ├── TunnelViewModel.cs

│   │   │   ├── RemoteControlViewModel.cs

│   │   │   └── ...

│   │   ├── Controls/                             # Custom WPF controls

│   │   │   ├── NativeMarkdownViewer.xaml

│   │   │   └── AnimatedNavIndicatorBehavior.cs

│   │   ├── Converters/                           # Value converters

│   │   │   ├── MinecraftMotdConverter.cs

│   │   │   └── Converters.cs

│   │   ├── Services/                             # WPF-specific services

│   │   │   ├── WpfAppDispatcher.cs

│   │   │   ├── WpfDialogService.cs

│   │   │   ├── WpfAssetProvider.cs

│   │   │   ├── AppNavigationService.cs

│   │   │   ├── ClipboardHelper.cs

│   │   │   ├── ImageProcessingService.cs

│   │   │   ├── AccentColorService.cs

│   │   │   ├── WallpaperMicaService.cs

│   │   │   └── WindowsCornerService.cs

│   │   ├── Behaviors/                            # WPF attached behaviors

│   │   │   └── ScrollViewerHelper.cs

│   │   └── Themes/                               # Styles, templates

│   │       ├── Colors.xaml

│   │       ├── Controls.xaml

│   │       └── Fonts.xaml

│   │

│   └── PocketMC.RemoteControl/                   # ASP.NET Core host (separate concern)

│       ├── PocketMC.RemoteControl.csproj

│       ├── RemoteControlHostedService.cs

│       ├── RemoteDashboardHost.cs

│       ├── Endpoints/

│       │   ├── InstanceEndpoints.cs              # Minimal API

│       │   ├── PlayerEndpoints.cs

│       │   ├── ConsoleEndpoints.cs               # WebSocket

│       │   └── AuthEndpoints.cs

│       ├── Middleware/

│       │   ├── RemoteAuthenticationMiddleware.cs

│       │   └── RemoteRequestLimiter.cs

│       ├── Services/

│       │   ├── RemoteAuthenticationService.cs

│       │   ├── RemoteControlCoordinator.cs

│       │   ├── RemoteInstanceControlService.cs

│       │   ├── RemotePlayerActionService.cs

│       │   ├── RemoteStatusService.cs

│       │   ├── RemoteAuditLogService.cs

│       │   └── LocalNetworkAddressService.cs

│       ├── Tunnels/

│       │   ├── RemoteTunnelManager.cs

│       │   ├── IRemoteTunnelProvider.cs

│       │   ├── CloudflaredInstaller.cs

│       │   └── PlayitHttpsTunnelProvider.cs

│       └── wwwroot/                              # Static web assets

│           ├── index.html

│           ├── app.js

│           ├── styles.css

│           └── README.md

│

├── tests/

│   ├── PocketMC.Domain.Tests/

│   │   ├── Entities/

│   │   │   └── InstanceTests.cs

│   │   ├── ValueObjects/

│   │   │   ├── MinecraftVersionTests.cs

│   │   │   └── PortNumberTests.cs

│   │   └── Services/

│   │       ├── PortAllocationPolicyTests.cs

│   │       └── BackupRetentionPolicyTests.cs

│   │

│   ├── PocketMC.Application.Tests/

│   │   ├── Instances/

│   │   │   ├── CreateInstanceHandlerTests.cs

│   │   │   ├── StartInstanceHandlerTests.cs

│   │   │   └── UpdateInstanceHandlerTests.cs

│   │   ├── Backups/

│   │   │   ├── CreateBackupHandlerTests.cs

│   │   │   └── RestoreBackupHandlerTests.cs

│   │   ├── Networking/

│   │   │   └── PortPreflightServiceTests.cs

│   │   └── Intelligence/

│   │       └── SessionSummarizationServiceTests.cs

│   │

│   ├── PocketMC.Infrastructure.Tests/

│   │   ├── FileSystem/

│   │   │   ├── PathSafetyTests.cs

│   │   │   └── SafeZipExtractorTests.cs

│   │   ├── ApiClients/

│   │   │   ├── AdoptiumClientTests.cs

│   │   │   ├── ModrinthClientTests.cs

│   │   │   └── PlayitApiClientTests.cs

│   │   ├── Security/

│   │   │   └── DataProtectorTests.cs

│   │   └── Persistence/

│   │       └── InstanceRepositoryTests.cs

│   │

│   ├── PocketMC.Desktop.Tests/

│   │   └── ViewModels/

│   │       ├── DashboardViewModelTests.cs

│   │       ├── InstanceCardViewModelTests.cs

│   │       └── PlayerManagementViewModelTests.cs

│   │

│   └── PocketMC.RemoteControl.Tests/

│       ├── Integration/

│       │   └── RemoteControlApiIntegrationTests.cs

│       └── Endpoints/

│           └── InstanceEndpointsTests.cs

│

├── docs/

│   ├── architecture/

│   │   ├── overview.md

│   │   ├── layering.md

│   │   └── decisions/                            # ADRs

│   │       ├── 0001-split-into-multiple-projects.md

│   │       ├── 0002-command-query-pattern.md

│   │       └── 0003-llm-provider-strategy.md

│   ├── features/

│   │   ├── cloud-backups.md

│   │   ├── playit-tunnels.md

│   │   ├── remote-control.md

│   │   └── ai-summaries.md

│   ├── contributing/

│   │   ├── getting-started.md

│   │   ├── coding-standards.md

│   │   └── testing-guide.md

│   └── assets/

│       ├── branding/

│       ├── icons/

│       └── screenshots/

│

├── .github/

│   ├── CODE\_OF\_CONDUCT.md

│   ├── SECURITY.md

│   ├── PULL\_REQUEST\_TEMPLATE.md

│   ├── ISSUE\_TEMPLATE/

│   │   ├── bug\_report.yml                        # Use YAML forms, not markdown

│   │   ├── feature\_request.yml

│   │   └── config.yml

│   └── workflows/

│       ├── ci.yml                                # Build + test on PR

│       ├── codeql.yml                            # Security analysis

│       ├── production-build.yml

│       └── discord-commits.yml

│

├── Directory.Build.props                         # Centralized build config

├── Directory.Packages.props                      # Central package versions

├── .editorconfig                                 # Code style enforcement

├── global.json                                   # Pin .NET SDK version

├── CHANGELOG.md

├── CONTRIBUTING.md

├── README.md

├── LICENSE

└── PocketMC.sln

```



\## Why Each Top-Level Decision



| Decision | Rationale |

|----------|-----------|

| `src/` + `tests/` separation | Industry standard. Prevents test assemblies from shipping. Clear mental model. |

| `PocketMC.Domain` as separate project | Pure domain logic with zero dependencies. Compile-enforced purity. Reusable in any context (CLI, web, desktop). |

| `PocketMC.Application` as separate project | Use cases are the API of the system. Testing them without infrastructure is critical. CQRS pattern (commands/queries) makes intent explicit. |

| `PocketMC.Infrastructure` as separate project | All external concerns (HTTP, filesystem, OS, processes) isolated. Swappable. Testable via abstractions. |

| `PocketMC.Desktop` references Application + Infrastructure only | The desktop project is a thin shell: views, view models, DI wiring. No business logic. |

| `PocketMC.RemoteControl` as separate project | The web dashboard is a different deployment model (Kestrel host). It should not pull in WPF assemblies. |

| Per-feature DI registration (`Add{Feature}Services`) | Each feature owns its registration. No god file. Merge-conflict-free. |

| `docs/architecture/decisions/` (ADRs) | Architecture Decision Records capture \*why\* decisions were made. Critical for long-term projects. |

| `Directory.Build.props` + `Directory.Packages.props` | Centralized build settings and package versioning. No version drift across projects. |

| `.editorconfig` | Enforces code style at the editor level. No formatting PRs. |

| `global.json` | Pins SDK version. Prevents "works on my machine" build failures. |



\---



\# Phase 4: Refactoring Roadmap



\## Guiding Principles



1\. \*\*Never break the build\*\* — every step produces a compilable, runnable app

2\. \*\*Move, don't rewrite\*\* — extract existing code, don't reimplement

3\. \*\*Test before move\*\* — ensure tests pass before and after each step

4\. \*\*One concern per PR\*\* — each step is a reviewable PR



\## Step-by-Step Plan



\### Step 1: Establish Build Infrastructure (Safest)



\*\*Goal:\*\* Centralize build configuration before any code moves.



\*\*Files affected:\*\*

\- New: `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `global.json`

\- Modified: All `.csproj` files (remove duplicated properties)



\*\*Risk:\*\* Low — configuration only, no logic changes.



\*\*Benefits:\*\* Consistent builds, enforced code style, centralized package versions, SDK pinning.



\---



\### Step 2: Standardize Naming Conventions



\*\*Goal:\*\* Eliminate the `VM` vs `ViewModel` inconsistency.



\*\*Files affected:\*\*

\- `DashboardActionsVM.cs` → `DashboardActionsViewModel.cs` (and 10 similar renames)



\*\*Risk:\*\* Low — mechanical rename. Find-and-replace with compiler verification.



\*\*Benefits:\*\* Consistency. Searchability. Professional appearance.



\---



\### Step 3: Eliminate `GlobalUsings.cs`



\*\*Goal:\*\* Make dependencies explicit in every file.



\*\*Files affected:\*\*

\- Delete: `GlobalUsings.cs`

\- Modified: Every `.cs` file (add explicit `using` statements)



\*\*Risk:\*\* Low — compiler will flag missing usings.



\*\*Benefits:\*\* Readability. Dependency clarity for contributors. No hidden coupling.



\---



\### Step 4: Clean Up Junk Drawers



\*\*Goal:\*\* Move misplaced files to their proper homes.



\*\*Files affected:\*\*

\- `Helpers/AnimatedNavIndicatorBehavior.cs` → `Views/Controls/` or `Behaviors/`

\- `Helpers/CommandFormatter.cs` → `Features/Console/`

\- `Helpers/GeyserDetector.cs` + `IGeyserDetector.cs` → `Features/Instances/` or `Application/Instances/Services/`

\- `Features/Settings/ImageCropPage.xaml` → `Views/Shared/` or a new `ImageProcessing` feature

\- `Features/Settings/TelemetryService.cs` → `Infrastructure/Telemetry/`

\- `Features/Settings/AddonAutoUpdateService.cs` → `Features/Mods/`

\- `Features/Settings/ServerRuntimeSettingApplier.cs` → `Features/Instances/`

\- Delete: `Helpers/` folder



\*\*Risk:\*\* Low — file moves only. Namespace updates via IDE refactoring.



\*\*Benefits:\*\* Each file in its logical home. No more "where does this go?" decisions.



\---



\### Step 5: Consolidate Backup Logic



\*\*Goal:\*\* Unify backup code scattered across three feature folders.



\*\*Files affected:\*\*

\- Move `Features/Instances/Backups/\*` → `Features/Backups/Local/`

\- Move `Features/CloudBackups/\*` → `Features/Backups/Cloud/`

\- Move `Features/Settings/SettingsBackupsVM.cs` → `Features/Backups/ViewModels/`

\- Move `Features/Settings/ServerCloudBackupViewModel.cs` → `Features/Backups/ViewModels/`

\- Move `Features/Settings/CloudBackupSettingsViewModel.cs` → `Features/Backups/ViewModels/`



\*\*Risk:\*\* Medium — namespace changes affect many references. Run full test suite.



\*\*Benefits:\*\* Single backup domain. One place to look. Coherent mental model.



\---



\### Step 6: Split DI Registration by Feature



\*\*Goal:\*\* Break up the god file `Composition/ServiceCollectionExtensions.cs`.



\*\*Files affected:\*\*

\- New: `Features/Instances/Composition/InstanceServiceExtensions.cs`

\- New: `Features/Tunnel/Composition/TunnelServiceExtensions.cs`

\- New: `Features/Backups/Composition/BackupServiceExtensions.cs`

\- New: `Features/RemoteControl/Composition/RemoteControlServiceExtensions.cs`

\- (one per feature)

\- Modified: `Composition/ServiceCollectionExtensions.cs` → thin orchestrator calling each `Add{Feature}Services()`



\*\*Risk:\*\* Low — pure refactoring of DI registration. If a service is missed, the app crashes immediately on startup (fast failure).



\*\*Benefits:\*\* Feature ownership. No merge conflicts. Each feature is self-contained.



\---



\### Step 7: Extract Domain Models to `Models/` Properly



\*\*Goal:\*\* Consolidate scattered models into a coherent domain model area.



\*\*Files affected:\*\*

\- Move all model files from feature folders to `Models/` (or `Domain/Models/`)

\- Split multi-model files (`CloudBackupModels.cs`, `InstanceUpdateModels.cs`, `MarketplaceModels.cs`) into individual files

\- Separate DTOs from domain entities



\*\*Risk:\*\* Medium — many file moves. Some models may need namespace updates.



\*\*Benefits:\*\* Single source of truth for domain entities. Clear aggregate boundaries.



\---



\### Step 8: Introduce Shared HTTP Infrastructure



\*\*Goal:\*\* Centralize HTTP client creation, retry policies, and logging.



\*\*Files affected:\*\*

\- New: `Infrastructure/Http/HttpClientFactory.cs`

\- New: `Infrastructure/Http/RetryDelegatingHandler.cs`

\- New: `Infrastructure/Http/ResilientHttpPolicy.cs` (Polly)

\- Modified: `AiApiClient.cs`, `PlayitApiClient.cs`, `JavaAdoptiumClient.cs`, `ModrinthService.cs`, `CurseForgeService.cs`, all cloud backup providers



\*\*Risk:\*\* Medium — changing HTTP client creation in all API clients. Test each provider after change.



\*\*Benefits:\*\* Consistent resilience. Socket reuse. Centralized logging. Single point for User-Agent injection.



\---



\### Step 9: Extract `PocketMC.Domain` Project



\*\*Goal:\*\* First real project split. Extract pure domain code.



\*\*Files affected:\*\*

\- New: `src/PocketMC.Domain/PocketMC.Domain.csproj`

\- Move: All entities, value objects, enums, domain exceptions, domain service interfaces

\- Modified: `PocketMC.Desktop.csproj` (add project reference)



\*\*Risk:\*\* Medium — compilation errors will reveal hidden coupling (e.g., domain code referencing WPF types). Fix each violation.



\*\*Benefits:\*\* Compile-enforced domain purity. Faster builds (domain rarely changes). Reusable.



\---



\### Step 10: Extract `PocketMC.Application` Project



\*\*Goal:\*\* Extract use cases and abstractions.



\*\*Files affected:\*\*

\- New: `src/PocketMC.Application/PocketMC.Application.csproj`

\- Move: All service interfaces, command/query handlers, abstractions

\- Modified: Both other projects (add references)



\*\*Risk:\*\* Medium-High — this is where hidden dependencies surface. Services that directly call infrastructure will need abstraction injection.



\*\*Benefits:\*\* Testable use cases without infrastructure. Clear API surface. CQRS structure.



\---



\### Step 11: Extract `PocketMC.Infrastructure` Project



\*\*Goal:\*\* Isolate all external concerns.



\*\*Files affected:\*\*

\- New: `src/PocketMC.Infrastructure/PocketMC.Infrastructure.csproj`

\- Move: All HTTP clients, filesystem implementations, OS integrations, process management, persistence



\*\*Risk:\*\* High — this is the largest move. Many files. Potential for circular dependencies if abstractions aren't clean.



\*\*Benefits:\*\* Swappable implementations. Infrastructure testing in isolation. Clean dependency graph.



\---



\### Step 12: Extract `PocketMC.RemoteControl` Project



\*\*Goal:\*\* Separate the ASP.NET Core host from the WPF desktop.



\*\*Files affected:\*\*

\- New: `src/PocketMC.RemoteControl/PocketMC.RemoteControl.csproj`

\- Move: All RemoteControl hosting, endpoints, middleware, tunnels, services

\- Move: `Web/` static assets to `wwwroot/`



\*\*Risk:\*\* High — the Remote Control currently likely references desktop services directly. Need to abstract through Application layer interfaces.



\*\*Benefits:\*\* No WPF dependency for the web host. Potentially deployable separately. Cleaner separation.



\---



\### Step 13: Reorganize Test Structure



\*\*Goal:\*\* Mirror source structure in tests. Separate unit from integration.



\*\*Files affected:\*\*

\- Reorganize: All 80+ test files into `Tests/{Project}/{Feature}/` structure

\- New: `Tests/Integration/` for tests that hit real APIs or filesystem

\- Add: `\[Trait("Category", "Unit")]` and `\[Trait("Category", "Integration")]` to all tests



\*\*Risk:\*\* Low — file moves only. Tests don't change logic.



\*\*Benefits:\*\* Navigable test suite. Fast unit test runs (filter by trait). Clear coverage gaps.



\---



\### Step 14: Introduce Exception Hierarchy and Global Handlers



\*\*Goal:\*\* Consistent error handling strategy.



\*\*Files affected:\*\*

\- New: `Domain/Exceptions/PocketMCException.cs` and subclasses

\- Modified: `App.xaml.cs` (add `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`)

\- Modified: All services (replace ad-hoc exceptions with domain exceptions)

\- New: `Infrastructure/ExceptionMapper.cs`



\*\*Risk:\*\* Medium — touching many catch blocks. Test error paths carefully.



\*\*Benefits:\*\* Consistent error propagation. User-friendly error messages. No leaked stack traces.



\---



\# Phase 5: Code Quality Audit



\## 5.1 — Dead Code and Unused Elements



\*\*Without file contents\*\*, I can identify structural dead code:



| Location | Issue |

|----------|-------|

| `PocketMC.Desktop/AssemblyInfo.cs` | In .NET 8 SDK-style projects, assembly attributes belong in `.csproj`. This file is likely redundant. |

| `Features/Instances/Models/ServerProcess.cs` | A single file in a `Models/` folder under `Instances` — either it should be in the main `Models/` folder, or the folder exists only for this one file (over-organization). |

| `Features/Settings/PropertyItem.cs` | UI model living in Settings — likely unused outside one view, or used across views but misplaced. |

| `Core/Presentation/Converters.cs` | Generic name. Likely contains multiple converters in one file. Should be split. |



\*\*Action:\*\* Run `dotnet build` with warnings-as-errors for `CS0168` (unused variable), `CS0219` (unused variable assigned), `IDE0051` (unused private member). Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to `Directory.Build.props`.



\---



\## 5.2 — Large Files (Structural Indicators)



Based on naming patterns, these files are likely too large:



| File | Concern |

|------|---------|

| `Composition/ServiceCollectionExtensions.cs` | DI registration for the entire app — likely 300-600 lines |

| `Features/Instances/Services/InstanceManager.cs` | "Manager" suffix typically indicates a god class |

| `Features/Instances/Services/ServerProcessManager.cs` | Process lifecycle + likely metrics + likely RCON |

| `Features/Instances/Services/ServerLifecycleService.cs` | Overlaps with `ServerProcessManager` — likely duplicated responsibilities |

| `Features/RemoteControl/Services/RemoteControlCoordinator.cs` | "Coordinator" typically means it calls everything |

| `Features/CloudBackups/CloudBackupService.cs` | Orchestrates 3 providers + upload history + retention + path safety |

| `Features/Settings/SettingsManager.cs` | All settings in one manager |

| `Features/Intelligence/AiApiClient.cs` | 6 providers in one client — likely a massive switch/if-else |

| `App.xaml.cs` | WPF entry point — likely has startup orchestration that belongs elsewhere |



\*\*Action:\*\* Each of these files should be split. Use the single-responsibility principle. A class named `XxxManager` is almost always doing too much.



\---



\## 5.3 — Duplicate Logic (Structural Evidence)



| Pattern | Locations | Problem |

|---------|-----------|---------|

| HTTP retry logic | `CloudBackups/ResilientUploadPolicy.cs` + likely inline in each API client | No shared retry policy. Each client reinvents resilience. |

| Path safety checks | `Infrastructure/Security/PathSafety.cs` + `Features/CloudBackups/CloudPathSanitizer.cs` + `Features/Marketplace/MarketplaceFileNameSanitizer.cs` + `Features/Mods/AddonFileNamePolicy.cs` | Four path-sanitizing implementations. Should be one with feature-specific policies layered on top. |

| OAuth/PKCE | `Features/CloudBackups/OAuth/` | Self-implemented OAuth flow. Should use a library (e.g., `IdentityModel.OAuthClient`) or at least share with Remote Control auth. |

| Port checking | `Features/Networking/PortProbeService.cs` + `PortPreflightService.cs` + `PortConflictInfo.cs` + `PortDiagnosticsSnapshotBuilder.cs` | Multiple port-checking entry points. Unclear which is canonical. |

| Tunnel management | `Features/Tunnel/TunnelService.cs` + `Features/RemoteControl/Tunnels/RemoteTunnelManager.cs` | Two tunnel managers. |

| Process management | `ServerProcessManager.cs` + `ServerLifecycleService.cs` | Two services managing server processes. Unclear boundary. |

| Log sanitization | `Features/Console/LogSanitizer.cs` + `Features/Intelligence/SessionLogPreprocessor.cs` | Both sanitize logs. AI preprocessor should use the Console sanitizer. |



\---



\## 5.4 — Naming Issues



| File/Pattern | Issue | Fix |

|---------------|-------|-----|

| `\*VM.cs` files | Abbreviated suffix | Rename to `\*ViewModel.cs` |

| `InstanceManager.cs` | "Manager" is meaningless | Rename to `InstanceOrchestrator` or split into `InstanceCreator`, `InstanceStarter`, `InstanceStopper` |

| `ServerProcessManager.cs` vs `ServerLifecycleService.cs` | Two services, unclear difference | Clarify or merge |

| `AppDialog.cs` | Too generic | `DialogRequest` or `DialogModel` |

| `JobObject.cs` | Unclear without Windows API knowledge | `WindowsJobObject` with XML doc |

| `PortEngine.cs` | "Engine" is vague | `PortAllocationEngine` or `PortAllocator` |

| `ShellVisualService.cs` + `ShellUIStateService.cs` | Overlapping concerns | Merge or clearly separate visual vs. state |

| `WhatsNewService.cs` | Ambiguous | `ChangelogService.cs` |

| `DashboardActionsVM.cs` | "Actions" is vague | `DashboardToolbarViewModel.cs` or `DashboardActionsViewModel.cs` |



\---



\## 5.5 — Inconsistent Conventions



| Area | Inconsistency |

|------|---------------|

| ViewModel suffix | `ViewModel` vs `VM` |

| Service suffix | `Service` vs none (`InstanceManager`, `InstanceRegistry`) |

| Interface prefix | `I` (correct) but some abstractions in `Core/Interfaces/` vs some in feature folders |

| Test naming | `\*Tests.cs` vs `\*SourceTests.cs` vs `\*SecurityTests.cs` |

| Folder for interfaces | `Core/Interfaces/` (centralized) vs `Features/\*/Services/I\*.cs` (distributed) vs `Features/Shell/Interfaces/` |

| XAML code-behind naming | `\*.xaml.cs` (standard) but some files like `ServerSettingsPage.xaml.cs` exist without a matching `.xaml` listed — verify |

| Model files | Some are one-class-per-file (good), some are multi-class (`\*Models.cs`) |



\---



\## 5.6 — Missing Abstractions



| Concern | Current State | Needed Abstraction |

|---------|---------------|-------------------|

| HTTP resilience | Per-client ad-hoc | `IHttpResiliencePolicy` with Polly |

| Secret storage | `DataProtector.cs` only | `ISecretStore` with providers (DPAPI, environment, user prompt) |

| Background jobs | Ad-hoc `Task.Run` or hosted services | `IBackgroundJobQueue` (like Hangfire-lite) |

| Event publication | Direct method calls between services | `IEventBus` or `IMediator` (MediatR) for domain events |

| Configuration | `SettingsManager` + `AppSettings` | `IOptions<T>` pattern with validated settings |

| Logging | Likely `ILogger<T>` (verify) but no structured logging configuration | Serilog with structured sinks (file, seq, debug) |

| Cancellation | Inconsistent — some async methods have `CancellationToken`, some don't | Every async method must accept `CancellationToken` |



\---



\# Phase 6: AI/LLM Architecture Review



\## Current State



The `Features/Intelligence/` folder contains:

```

AiApiClient.cs                    — HTTP client for all 6 providers

NativeMarkdownViewer.xaml         — UI for rendering summaries

SessionLogPreprocessor.cs         — Log sanitization before LLM

SessionSummarizationService.cs    — Orchestration

SummaryEmojiFormatter.cs          — Post-processing

SummaryStorageService.cs          — Persistence

```



\## Issues



\### 6.1 — Single `AiApiClient` for 6 Providers



\*\*Severity:\*\* High



\*\*Problem:\*\* One file handles Gemini, OpenAI, Claude, Mistral, Groq, and Ollama. This means:

\- Provider-specific request/response formats are all in one class

\- Adding a new provider means modifying this file

\- Testing requires mocking the entire client

\- No provider-specific error handling, rate limiting, or retry logic



\*\*Solution:\*\* Strategy pattern:

```

ILlmProvider

├── GeminiProvider

├── OpenAiProvider

├── ClaudeProvider

├── MistralProvider

├── GroqProvider

└── OllamaProvider

```



Each provider encapsulates its request format, response parsing, error handling, and model selection. `SessionSummarizationService` depends on `ILlmProvider` — it doesn't know which provider it's using.



\### 6.2 — No Prompt Organization



\*\*Severity:\*\* High



\*\*Problem:\*\* Prompts are likely embedded in `SessionSummarizationService.cs` or `AiApiClient.cs` as string literals. This means:

\- Prompts can't be versioned

\- Prompt changes require code changes and recompilation

\- No A/B testing of prompts

\- No prompt template with variable injection



\*\*Solution:\*\*

```

Application/Intelligence/Prompts/

├── SessionSummaryPrompt.cs       # Template with placeholders

├── PromptVariables.cs            # Typed variables

└── PromptVersion.cs              # Version tracking

```



Use a template engine (even simple `string.Format` or Scriban) for prompt composition.



\### 6.3 — No Token Management



\*\*Severity:\*\* High



\*\*Problem:\*\* No visible:

\- Token counting before sending (to avoid exceeding context window)

\- Truncation strategy for large logs

\- Chunking strategy for very long sessions

\- Cost estimation before API call



\*\*Solution:\*\*

\- Add `ITokenCounter` (provider-specific — each LLM tokenizes differently)

\- Implement log chunking: if session exceeds context window, summarize in chunks, then meta-summarize

\- Add `TokenUsage` tracking: record input/output tokens per call for cost analysis



\### 6.4 — No Rate Limiting or Retry for LLM Calls



\*\*Severity:\*\* Medium



\*\*Problem:\*\* LLM APIs have strict rate limits (especially Groq, Mistral free tiers). No visible:

\- Rate limiter before API call

\- Retry with exponential backoff on 429

\- Fallback to a different provider on repeated failure

\- Queue for summary generation requests



\*\*Solution:\*\*

\- Apply Polly rate-limiting policy to LLM HTTP calls

\- Add `ILlmRateLimiter` with per-provider limits

\- Optional: provider fallback chain (try Gemini → fall back to Ollama)



\### 6.5 — No Streaming Support



\*\*Severity:\*\* Medium



\*\*Problem:\*\* All supported providers support streaming responses. The current architecture likely waits for the full response before displaying. For session summaries (which can take 10-30 seconds), this means a poor UX — no progress indication.



\*\*Solution:\*\* Add `StreamSummaryAsync` that yields partial results. Update the UI progressively.



\### 6.6 — No Observability



\*\*Severity:\*\* Medium



\*\*Problem:\*\* No visible:

\- Logging of LLM calls (provider, model, tokens, latency, success/failure)

\- Cost tracking dashboard

\- Quality feedback (thumbs up/down on summaries)

\- Metrics for summary generation rate



\*\*Solution:\*\*

\- Add `ILlmTelemetry` that records every call

\- Log: timestamp, provider, model, input tokens, output tokens, latency, success, error

\- Surface in Diagnostics page



\### 6.7 — Log Sanitization Duplication



\*\*Severity:\*\* Medium



\*\*Problem:\*\* `SessionLogPreprocessor.cs` sanitizes logs before sending to LLM (IPs, emails). `LogSanitizer.cs` in Console feature also sanitizes. These should share logic.



\*\*Solution:\*\* `SessionLogPreprocessor` should depend on `LogSanitizer` for the core sanitization, then add LLM-specific preprocessing (token budgeting, irrelevant line removal).



\## Proposed AI Architecture



```

Application/Intelligence/

├── Abstractions/

│   ├── ILlmProvider.cs           # Strategy interface

│   ├── ILlmRateLimiter.cs

│   ├── ITokenCounter.cs

│   └── ISummaryStorage.cs

├── Commands/

│   └── GenerateSessionSummaryCommand.cs

├── Handlers/

│   └── GenerateSessionSummaryHandler.cs

├── Services/

│   ├── SessionSummarizationService.cs   # Orchestrator

│   ├── SessionLogPreprocessor.cs        # Uses LogSanitizer + token budgeting

│   └── SummaryEmojiFormatter.cs

├── Prompts/

│   ├── SessionSummaryPromptTemplate.cs

│   └── PromptBuilder.cs

├── Models/

│   ├── LlmRequest.cs

│   ├── LlmResponse.cs

│   ├── TokenUsage.cs

│   └── SummaryResult.cs

└── Telemetry/

&#x20;   └── LlmTelemetryService.cs



Infrastructure/Llm/

├── Providers/

│   ├── GeminiProvider.cs         # Implements ILlmProvider

│   ├── OpenAiProvider.cs

│   ├── ClaudeProvider.cs

│   ├── MistralProvider.cs

│   ├── GroqProvider.cs

│   └── OllamaProvider.cs

├── LlmHttpClient.cs              # Shared HTTP with Polly

├── TokenCounters/

│   ├── TiktokenCounter.cs        # OpenAI-compatible

│   └── ApproximateTokenCounter.cs # Fallback

└── RateLimiters/

&#x20;   └── PollyRateLimiter.cs

```



\---



\# Phase 7: Testing Review



\## Current State



\- \*\*Framework:\*\* xUnit + Moq

\- \*\*Test count:\*\* \~80 files

\- \*\*Structure:\*\* Flat (only 3 subdirectories: `Models/`, `Providers/`, `RemoteControl/`)

\- \*\*Integration tests:\*\* Minimal (one `RemoteControl/Integration/` folder)

\- \*\*Test support:\*\* `MarketplaceHttpTestSupport.cs`, `PortReliabilityTestSupport.cs`, `TestSourceFileResolver.cs` — test helpers in the main test directory



\## Issues



\### 7.1 — No Test Categorization



\*\*Problem:\*\* No `\[Trait]` attributes visible. Unit tests, integration tests, and security tests are mixed. Running "fast tests only" is impossible.



\*\*Solution:\*\*

```csharp

\[Trait("Category", "Unit")]          // Fast, no I/O

\[Trait("Category", "Integration")]   // Slow, real I/O or HTTP

\[Trait("Category", "Security")]      // Security-focused

\[Trait("Layer", "Domain")]

\[Trait("Layer", "Application")]

\[Trait("Layer", "Infrastructure")]

```



Filter in CI: `dotnet test --filter "Category=Unit"` for fast feedback.



\### 7.2 — Test Helpers in Wrong Location



\*\*Problem:\*\* `MarketplaceHttpTestSupport.cs`, `PortReliabilityTestSupport.cs`, and `TestSourceFileResolver.cs` are test infrastructure but live alongside tests. They should be in a shared `TestSupport/` or `Testing/` project.



\*\*Solution:\*\* Create `PocketMC.Testing` project with shared fixtures, builders, and helpers.



\### 7.3 — Coverage Gaps (Structural Analysis)



| Feature | Test Files | Coverage Assessment |

|---------|-----------|-------------------|

| Instances | 15+ test files | Likely well-covered (lifecycle, process, updates, import/export) |

| Marketplace | 10+ test files | Good coverage (security, risk analysis, metadata) |

| Networking/Ports | 8+ test files | Well-covered (preflight, probe, recovery, lease) |

| CloudBackups | 3 test files | \*\*Gap\*\* — OAuth flow, provider-specific upload, retention, restore not tested |

| Intelligence/AI | 2 test files (`SummaryEmojiFormatterTests`, `SummaryStorageServiceTests`) | \*\*Critical gap\*\* — LLM client, prompt building, token management, log preprocessing not tested |

| RemoteControl | 12 test files | Moderate — integration tests exist but API endpoints may not be fully covered |

| Shell | 8 test files | Moderate — startup, backdrop, navigation tested |

| Java | 3 test files | \*\*Gap\*\* — provisioning download flow, version selection not tested |

| Tunnel/Playit | 4 test files | \*\*Gap\*\* — agent lifecycle, tunnel creation, port mapping not tested |

| Mods | 8 test files | Moderate — metadata scanning, toggles, modpacks tested |



\### 7.4 — Missing Test Types



| Type | Status | Action |

|------|--------|--------|

| Unit tests | Present but flat | Reorganize |

| Integration tests | Minimal | Add for: cloud backup providers (mock HTTP), AI providers (mock HTTP), filesystem operations |

| E2E tests | None | Add WPF UI automation tests (FlaUI or Appium) for critical paths: create instance, start server, backup |

| Snapshot tests | None | Add for XAML rendering (verify UI doesn't break) |

| Performance tests | None | Add for: log tailing performance, large backup extraction, many-instance dashboard rendering |

| Mutation tests | None | Consider Stryker.NET for critical domain logic |



\### 7.5 — Sample Test Structure



```csharp

// tests/PocketMC.Application.Tests/Instances/StartInstanceHandlerTests.cs



public class StartInstanceHandlerTests

{

&#x20;   private readonly Mock<IInstanceRepository> \_repo = new();

&#x20;   private readonly Mock<IPortPreflightService> \_portPreflight = new();

&#x20;   private readonly Mock<IServerProcessManager> \_processManager = new();

&#x20;   private readonly StartInstanceHandler \_handler;



&#x20;   public StartInstanceHandlerTests()

&#x20;   {

&#x20;       \_handler = new StartInstanceHandler(

&#x20;           \_repo.Object,

&#x20;           \_portPreflight.Object,

&#x20;           \_processManager.Object);

&#x20;   }



&#x20;   \[Fact]

&#x20;   \[Trait("Category", "Unit")]

&#x20;   \[Trait("Layer", "Application")]

&#x20;   public async Task Handle\_WhenPortIsAvailable\_StartsServer()

&#x20;   {

&#x20;       // Arrange

&#x20;       var instanceId = InstanceId.New();

&#x20;       var instance = InstanceFactory.Create(id: instanceId);

&#x20;       \_repo.Setup(r => r.GetAsync(instanceId, It.IsAny<CancellationToken>()))

&#x20;            .ReturnsAsync(instance);

&#x20;       \_portPreflight.Setup(p => p.CheckAsync(instance.Port, It.IsAny<CancellationToken>()))

&#x20;                     .ReturnsAsync(PortCheckResult.Available);



&#x20;       // Act

&#x20;       var result = await \_handler.Handle(

&#x20;           new StartInstanceCommand(instanceId),

&#x20;           CancellationToken.None);



&#x20;       // Assert

&#x20;       result.IsSuccess.Should().BeTrue();

&#x20;       \_processManager.Verify(

&#x20;           p => p.LaunchAsync(instance, It.IsAny<CancellationToken>()),

&#x20;           Times.Once);

&#x20;   }



&#x20;   \[Fact]

&#x20;   \[Trait("Category", "Unit")]

&#x20;   \[Trait("Layer", "Application")]

&#x20;   public async Task Handle\_WhenPortIsConflicted\_ReturnsFailure()

&#x20;   {

&#x20;       // Arrange

&#x20;       var instance = InstanceFactory.Create();

&#x20;       \_repo.Setup(r => r.GetAsync(instance.Id, It.IsAny<CancellationToken>()))

&#x20;            .ReturnsAsync(instance);

&#x20;       \_portPreflight.Setup(p => p.CheckAsync(instance.Port, It.IsAny<CancellationToken>()))

&#x20;                     .ReturnsAsync(PortCheckResult.Conflict(pid: 12345));



&#x20;       // Act

&#x20;       var result = await \_handler.Handle(

&#x20;           new StartInstanceCommand(instance.Id),

&#x20;           CancellationToken.None);



&#x20;       // Assert

&#x20;       result.IsFailure.Should().BeTrue();

&#x20;       result.Error.Should().Be("Port {Port} is in use by process {Pid}");

&#x20;       \_processManager.Verify(

&#x20;           p => p.LaunchAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()),

&#x20;           Times.Never);

&#x20;   }

}

```



\---



\# Phase 8: Security Review



\## Security Concerns Catalog



\### 8.1 — API Key Storage (Severity: Critical)



\*\*Location:\*\* `Infrastructure/Security/DataProtector.cs` + `SettingsManager.cs`



\*\*Problem:\*\* API keys (CurseForge, LLM providers) and OAuth tokens (Google Drive, Dropbox, OneDrive) are stored on disk. `DataProtector.cs` likely uses Windows DPAPI — good. But:

\- No visible key rotation strategy

\- No visible expiration checking for OAuth tokens

\- No visible revocation flow

\- If the DPAPI key is corrupted, all secrets are lost



\*\*Action:\*\*

\- Audit `DataProtector.cs` for proper DPAPI usage (scope: `CurrentUser`)

\- Add token expiration checking and automatic refresh

\- Add a "revoke all" feature for cloud backup authorizations

\- Document secret storage in `SECURITY.md`



\### 8.2 — Remote Control Authentication (Severity: Critical)



\*\*Location:\*\* `Features/RemoteControl/Services/RemoteAuthenticationService.cs`, `RemoteRequestLimiter.cs`



\*\*Problem:\*\* The Remote Control dashboard exposes server management over HTTP. Concerns:

\- Password-based authentication — how are passwords stored? (hashed? plaintext?)

\- Token lifetime — configurable but what's the default?

\- No visible CSRF protection for the web dashboard

\- No visible CORS configuration

\- `RemoteRequestLimiter.cs` exists — good — but what are the limits?

\- WebSocket authentication — is the console WebSocket authenticated?



\*\*Action:\*\*

\- Use ASP.NET Core Identity or at minimum `BCrypt.Net` for password hashing

\- Add CSRF tokens for state-changing operations

\- Configure CORS explicitly (deny by default, allow localhost)

\- Ensure WebSocket connection sends auth token

\- Document security model in `docs/features/remote-control-security.md`

\- Rate limits: document and make configurable



\### 8.3 — Path Traversal Protection (Severity: High)



\*\*Location:\*\* `Infrastructure/Security/PathSafety.cs`, `Features/CloudBackups/CloudPathSanitizer.cs`, `Features/Marketplace/MarketplaceFileNameSanitizer.cs`, `Features/Marketplace/MarketplaceArchiveInspector.cs`, `Features/Instances/Backups/SafeZipExtractor.cs`



\*\*Problem:\*\* Four path-sanitizing implementations. If one has a vulnerability, the others might not cover the same edge case. Test coverage exists (`PathSafetyTests.cs`, `MarketplaceDownloadPathSafetyTests.cs`, `ModpackPathTraversalTests.cs`, `DiskWriteSafetyTests.cs`) — good — but the fragmented implementation is a risk.



\*\*Action:\*\*

\- Consolidate to a single `IPathSanitizer` with configurable policies

\- Fuzz-test with malformed zip entries (`../../etc/passwd`, `C:\\Windows\\System32\\...`, symlink attacks)

\- Add a security test suite that runs all path operations against a corpus of malicious inputs



\### 8.4 — Process Execution Security (Severity: High)



\*\*Location:\*\* `Features/Instances/Services/ServerProcessManager.cs`, `Features/Tunnel/PlayitAgentProcessManager.cs`, `Features/RemoteControl/Tunnels/CloudflaredInstaller.cs`



\*\*Problem:\*\* The app launches external processes (Minecraft servers, Playit agent, Cloudflared). Concerns:

\- Are process arguments sanitized? (command injection via server name, world name?)

\- Are process working directories locked to instance directories?

\- Is the PATH searched for executables, or are full paths used?

\- Are downloaded executables (Playit agent, Cloudflared, Java) hash-verified?

\- Are executables signed/verified after download?



\*\*Action:\*\*

\- Always use full paths to executables — never rely on PATH

\- Hash-verify all downloaded executables before execution

\- Sanitize all user-provided strings before passing as process arguments

\- Lock process working directory to the instance directory

\- Consider `ProcessStartInfo` with `UseShellExecute=false` and explicit environment variables



\### 8.5 — RCON Security (Severity: Medium)



\*\*Location:\*\* `Infrastructure/Process/RconClient.cs`



\*\*Problem:\*\* RCON is used for graceful server shutdown and runtime commands. Concerns:

\- RCON password storage (where? how?)

\- RCON password generation (is it strong?)

\- RCON traffic is unencrypted — is it ever sent over network (not just localhost)?

\- RCON password in `server.properties` — is it sanitized from exports?



\*\*Action:\*\*

\- Ensure RCON binds to `127.0.0.1` only

\- Generate strong random RCON passwords

\- Ensure RCON password is excluded from instance exports

\- Document RCON security model



\### 8.6 — Supply Chain Risks (Severity: Medium)



\*\*Location:\*\* All NuGet dependencies



\*\*Problem:\*\* No visible:

\- `dotnet list package --vulnerable` in CI

\- Dependabot or similar dependency scanning

\- Pinned package versions (centralized via `Directory.Packages.props`)

\- License compliance check



\*\*Action:\*\*

\- Add `Directory.Packages.props` for centralized versioning

\- Add Dependabot configuration

\- Add `dotnet list package --vulnerable` to CI

\- Run `dotnet list package --deprecated` periodically



\### 8.7 — Custom URI Protocol (Severity: Medium)



\*\*Location:\*\* `Infrastructure/ProtocolRegistrationService.cs`, `pocketmc://` protocol



\*\*Problem:\*\* Custom URI schemes are a known attack vector. Any website can attempt `pocketmc://...`. Concerns:

\- What actions are exposed via URI?

\- Is user confirmation required before acting on URI commands?

\- Can a malicious website trigger server start/stop via URI?



\*\*Action:\*\*

\- Whitelist allowed URI actions

\- Always show a confirmation dialog before executing URI-triggered actions

\- Document the URI protocol schema in `SECURITY.md`



\### 8.8 — Regex DoS (Severity: Low)



\*\*Location:\*\* Various parsers (`ServerPropertiesParser`, `PlayerListParser`, `ChangelogParser`, `LogLineClassifier`)



\*\*Problem:\*\* User-controlled input (server logs, properties files) is processed with regex. A malicious log line could cause catastrophic backtracking.



\*\*Evidence:\*\* `RegexTimeoutSecurityTests.cs` exists — good. This means the team is aware.



\*\*Action:\*\* Ensure all `Regex` calls use `RegexOptions.Compiled` with a timeout. The existing test coverage should be expanded.



\---



\# Phase 9: Performance Review



\## Performance Analysis



\### 9.1 — WPF UI Thread Blockers (Priority: High)



\*\*Problem:\*\* Any synchronous I/O or heavy computation on the UI thread will freeze the app. Likely culprits:

\- `SettingsManager` — if JSON serialization is synchronous on UI thread

\- `InstanceRegistry` — if instance listing is synchronous

\- `ConsoleLogHistoryService` — if log reading blocks UI thread

\- `ServerPropertiesParser` — if parsing large files synchronously

\- `MarketplaceArchiveInspector` — ZIP inspection on UI thread



\*\*Action:\*\*

\- Profile with `dotnet-trace` or PerfView

\- Move all I/O to `Task.Run` or async methods

\- Use `IAppDispatcher` (already exists) for UI marshalling

\- Add `ConfigureAwait(false)` in all library code (non-UI)



\### 9.2 — Log Tailing Performance (Priority: High)



\*\*Problem:\*\* The README mentions "Large logs are tailed — no loading a 500 MB log file into the UI." Good — but the implementation matters:

\- Is there a ring buffer? Or does the log history grow unbounded?

\- Is there virtualization in the WPF console view? (VirtualizingStackPanel)

\- Are log lines stored as strings (high GC pressure) or as a custom struct?



\*\*Action:\*\*

\- Implement a bounded ring buffer (e.g., last 10,000 lines)

\- Use `VirtualizingStackPanel` with `IsVirtualizing="True"` and `ScrollUnit="Pixel"`

\- Consider `ReadOnlyMemory<char>` instead of `string` for log lines

\- Profile memory with large log output



\### 9.3 — HTTP Client Socket Exhaustion (Priority: Medium)



\*\*Problem:\*\* Without `IHttpClientFactory`, each API client may create and dispose `HttpClient` instances. This leads to socket exhaustion under load (TIME\_WAIT state).



\*\*Action:\*\* Use `IHttpClientFactory` with named clients. Register in DI:

```csharp

services.AddHttpClient("modrinth", c => {

&#x20;   c.BaseAddress = new Uri("https://api.modrinth.com");

&#x20;   c.DefaultRequestHeaders.Add("User-Agent", "PocketMC/1.4.3");

});

```



\### 9.4 — Instance Dashboard Rendering (Priority: Medium)



\*\*Problem:\*\* `DashboardInstanceListVM` with many instances (50+) may cause:

\- Slow rendering if each card has complex bindings

\- High memory usage if each card holds its own copy of instance data

\- UI lag if metrics polling updates all cards simultaneously



\*\*Action:\*\*

\- Use `UIVirtualization` in the dashboard list

\- Implement `INotifyPropertyChanged` efficiently (avoid unnecessary notifications)

\- Throttle metric updates (e.g., update UI at most once per second, not on every metric sample)

\- Consider `Collection<T>` batch updates with `INotifyCollectionChanged` reset action



\### 9.5 — Backup Compression (Priority: Medium)



\*\*Problem:\*\* `BackupService` + `SafeZipExtractor` likely use `System.IO.Compression.ZipArchive`. For large worlds (multiple GB), this can be slow:

\- Single-threaded compression

\- No progress reporting during compression

\- Full ZIP rebuild for incremental changes



\*\*Action:\*\*

\- Use `System.IO.Compression.ZipFile.CreateFromDirectory` with `CompressionLevel.Optimal` (or `Fastest` for speed)

\- Report progress via `IProgress<T>`

\- Consider streaming compression for large worlds

\- Document expected backup time for large worlds



\### 9.6 — File Watcher Overhead (Priority: Low)



\*\*Problem:\*\* If the app uses `FileSystemWatcher` to monitor instance directories for changes (e.g., addon detection), it can cause:

\- High CPU on directories with many file changes

\- Buffer overflow on rapid changes

\- Duplicate events



\*\*Action:\*\*

\- Use debouncing (e.g., 500ms delay before processing)

\- Increase `InternalBufferSize` appropriately

\- Handle `Error` event for buffer overflow

\- Consider polling as a fallback



\### 9.7 — Concurrent Operations (Priority: Low)



\*\*Problem:\*\* Multiple instances can start/stop simultaneously. If `InstanceManager` uses locks, this can serialize operations unnecessarily.



\*\*Action:\*\*

\- Use `ConcurrentDictionary` for instance registry

\- Use `SemaphoreSlim` (async) instead of `lock` for instance-level operations

\- Ensure per-instance locks, not global locks



\---



\# Phase 10: Open Source Readiness



\## Current State



| Asset | Status | Quality |

|-------|--------|---------|

| README.md | ✅ Present | Excellent — comprehensive, visual, well-structured |

| CONTRIBUTING.md | ✅ Present | Thin — missing coding standards, testing guide, architecture overview |

| LICENSE | ✅ Present | MIT — correct |

| SECURITY.md | ✅ Present | (content not shown — verify it has vulnerability reporting process) |

| CHANGELOG.md | ❌ Missing | Custom `WhatsNew.txt` instead — non-standard |

| CODE\_OF\_CONDUCT.md | ❌ Missing | Required for community projects |

| Issue templates | ✅ Present | `.md` format — should use `.yml` forms for structured input |

| PR template | ❌ Missing | No PR template |

| GitHub Actions | ✅ Present | `production-build.yml` only — no PR CI, no code quality |

| FUNDING.yml | ✅ Present | Good |

| .editorconfig | ❌ Missing | No code style enforcement |

| Directory.Build.props | ❌ Missing | No centralized build config |

| Directory.Packages.props | ❌ Missing | No centralized package versions |

| global.json | ❌ Missing | No SDK pinning |

| Dependabot | ❌ Missing | No dependency automation |

| CodeQL | ❌ Missing | No security analysis |



\## Required Additions



\### `CHANGELOG.md`



```markdown

\# Changelog



All notable changes to PocketMC are documented in this file.



The format is based on \[Keep a Changelog](https://keepachangelog.com/en/1.1.0/),

and this project adheres to \[Semantic Versioning](https://semver.org/spec/v2.0.0.html).



\## \[Unreleased]



\### Added

\- Provider abstraction for AI session summaries



\### Changed

\- Refactored tunnel management into unified Connectivity domain



\### Fixed

\- Path traversal prevention in marketplace archive extraction



\## \[1.4.3] - 2025-01-15



\### Added

\- Remote Control web dashboard with Cloudflare Quick Tunnels

\- AI session summaries for server logs

\- Discord bot integration for tunnel URL delivery



\### Changed

\- Java 25 auto-provisioning for Minecraft 1.21.2+

\- Backup manifests now include checksums and failure state



\### Fixed

\- RCON graceful shutdown reliability on Bedrock servers

\- Port conflict detection for Simple Voice Chat

```



\### `CODE\_OF\_CONDUCT.md`



Use the \[Contributor Covenant 2.1](https://www.contributor-covenant.org/version/2/1/code\_of\_conduct/). Standard, recognized, enforceable.



\### `PULL\_REQUEST\_TEMPLATE.md`



```markdown

\## Description



<!-- What does this PR change? Why? -->



\## Type of Change



\- \[ ] Bug fix (non-breaking change which fixes an issue)

\- \[ ] New feature (non-breaking change which adds functionality)

\- \[ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)

\- \[ ] Refactoring (no functional changes)

\- \[ ] Documentation update

\- \[ ] Security improvement



\## Affected Areas



\- \[ ] Instance lifecycle

\- \[ ] Runtime provisioning (Java/PHP)

\- \[ ] Backups / Cloud backups

\- \[ ] Tunnel / Networking

\- \[ ] Marketplace / Add-ons

\- \[ ] Remote Control

\- \[ ] AI / Intelligence

\- \[ ] Settings / UI

\- \[ ] Security

\- \[ ] CI/CD



\## Testing



\- \[ ] `dotnet build` passes

\- \[ ] `dotnet test` passes

\- \[ ] Added new tests for changes

\- \[ ] Manually tested affected feature



\## Security Considerations



<!-- If this PR touches filesystem, process execution, network, or auth — describe security implications -->



\## Checklist



\- \[ ] Code follows project style conventions

\- \[ ] Self-reviewed my code

\- \[ ] Comments added for complex logic

\- \[ ] Documentation updated (if applicable)

\- \[ ] No new warnings introduced

```



\### `ci.yml` (PR Validation Workflow)



```yaml

name: CI



on:

&#x20; pull\_request:

&#x20;   branches: \[master, main]

&#x20; push:

&#x20;   branches: \[master, main]



jobs:

&#x20; build-and-test:

&#x20;   runs-on: windows-latest

&#x20;   steps:

&#x20;     - uses: actions/checkout@v4



&#x20;     - name: Setup .NET

&#x20;       uses: actions/setup-dotnet@v4

&#x20;       with:

&#x20;         dotnet-version: '8.0.x'



&#x20;     - name: Restore

&#x20;       run: dotnet restore



&#x20;     - name: Build

&#x20;       run: dotnet build --no-restore --configuration Release



&#x20;     - name: Test (Unit)

&#x20;       run: dotnet test --no-build --configuration Release --filter "Category=Unit"



&#x20;     - name: Test (Integration)

&#x20;       run: dotnet test --no-build --configuration Release --filter "Category=Integration"



&#x20;     - name: Check for vulnerable packages

&#x20;       run: dotnet list package --vulnerable



&#x20;     - name: Upload coverage

&#x20;       uses: codecov/codecov-action@v4

&#x20;       if: always()

```



\### `codeql.yml` (Security Analysis)



```yaml

name: CodeQL



on:

&#x20; push:

&#x20;   branches: \[master, main]

&#x20; pull\_request:

&#x20;   branches: \[master, main]

&#x20; schedule:

&#x20;   - cron: '0 0 \* \* 1'  # Weekly



jobs:

&#x20; analyze:

&#x20;   runs-on: windows-latest

&#x20;   permissions:

&#x20;     security-events: write

&#x20;   steps:

&#x20;     - uses: actions/checkout@v4

&#x20;     - uses: github/codeql-action/init@v3

&#x20;       with:

&#x20;         languages: csharp

&#x20;     - uses: github/codeql-action/analyze@v3

```



\### Issue Template Upgrade (`.github/ISSUE\_TEMPLATE/bug\_report.yml`)



```yaml

name: Bug Report

description: Report a bug in PocketMC

labels: \["bug", "triage"]

body:

&#x20; - type: markdown

&#x20;   attributes:

&#x20;     value: |

&#x20;       Thanks for taking the time to report a bug! Please fill out all fields.



&#x20; - type: textarea

&#x20;   id: description

&#x20;   attributes:

&#x20;     label: Bug Description

&#x20;     description: What happened? What did you expect?

&#x20;   validations:

&#x20;     required: true



&#x20; - type: dropdown

&#x20;   id: server-type

&#x20;   attributes:

&#x20;     label: Server Type

&#x20;     options:

&#x20;       - Vanilla Java

&#x20;       - Paper

&#x20;       - Fabric

&#x20;       - Forge

&#x20;       - NeoForge

&#x20;       - Bedrock (BDS)

&#x20;       - PocketMine-MP

&#x20;   validations:

&#x20;     required: true



&#x20; - type: input

&#x20;   id: minecraft-version

&#x20;   attributes:

&#x20;     label: Minecraft Version

&#x20;     placeholder: "1.21.4"

&#x20;   validations:

&#x20;     required: true



&#x20; - type: input

&#x20;   id: pocketmc-version

&#x20;   attributes:

&#x20;     label: PocketMC Version

&#x20;     placeholder: "1.4.3"

&#x20;   validations:

&#x20;     required: true



&#x20; - type: textarea

&#x20;   id: logs

&#x20;   attributes:

&#x20;     label: Relevant Logs

&#x20;     description: Paste any error messages or console output

&#x20;     render: shell



&#x20; - type: textarea

&#x20;   id: repro

&#x20;   attributes:

&#x20;     label: Reproduction Steps

&#x20;     description: How can we reproduce this?

```



\---



\# Phase 11: Developer Experience (DX)



\## Current DX Assessment



| Area | Score | Notes |

|------|-------|-------|

| Setup complexity | 6/10 | `dotnet restore \&\& dotnet build` is standard, but no `Directory.Build.props` or `global.json` means environment drift |

| Build process | 5/10 | Single project = slow incremental builds. No solution filters for faster iteration. |

| Dependency management | 3/10 | No centralized package versions. No Dependabot. No vulnerability scanning. |

| Linting | 2/10 | No `.editorconfig`. No analyzers. No warnings-as-errors. |

| Formatting | 2/10 | No `dotnet format` enforcement. Style depends on developer. |

| Type checking | 7/10 | C# is statically typed. But no nullable reference types enforcement visible. |

| Debugging | 5/10 | WPF debugging is standard, but no logging infrastructure for production debugging |



\## Recommendations



\### `.editorconfig`



```ini

\# .editorconfig

root = true



\[\*]

charset = utf-8

end\_of\_line = crlf

insert\_final\_newline = true

trim\_trailing\_whitespace = true

indent\_style = space

indent\_size = 4



\[\*.{xaml,xml,csproj,props,targets,yml,yaml,json}]

indent\_size = 2



\[\*.cs]

\# Style rules

dotnet\_sort\_system\_directives\_first = true

dotnet\_separate\_import\_directive\_groups = false



\# Naming conventions

dotnet\_naming\_symbols.private\_fields.applicable\_kinds = field

dotnet\_naming\_symbols.private\_fields.applicable\_accessibilities = private

dotnet\_naming\_rule.private\_fields\_must\_be\_camel\_case.symbols = private\_fields

dotnet\_naming\_rule.private\_fields\_must\_be\_camel\_case.style = camel\_case\_underscore

dotnet\_naming\_style.camel\_case\_underscore.required\_prefix = \_

dotnet\_naming\_style.camel\_case\_underscore.capitalization = camel\_case



\# Async methods must end with Async

dotnet\_naming\_rule.async\_methods\_end\_with\_async.symbols = async\_methods

dotnet\_naming\_rule.async\_methods\_end\_with\_async.style = end\_with\_async

dotnet\_naming\_symbols.async\_methods.applicable\_kinds = method

dotnet\_naming\_symbols.async\_methods.required\_modifiers = async

dotnet\_naming\_style.end\_with\_async.required\_suffix = Async

dotnet\_naming\_style.end\_with\_async.capitalization = pascal\_case



\# Enforce var

csharp\_style\_var\_for\_built\_in\_types = true:suggestion

csharp\_style\_var\_when\_type\_is\_apparent = true:suggestion

csharp\_style\_var\_elsewhere = true:suggestion



\# Pattern matching

csharp\_style\_pattern\_matching\_over\_is\_with\_cast\_check = true:suggestion

csharp\_style\_pattern\_matching\_over\_as\_with\_null\_check = true:suggestion



\# Null checks

csharp\_style\_throw\_expression = true:suggestion

csharp\_style\_conditional\_delegate\_call = true:suggestion



\# Code quality

dotnet\_diagnostic.CA1062.severity = warning  # Validate arguments of public methods

dotnet\_diagnostic.CA2007.severity = warning  # Do not directly await a Task

dotnet\_diagnostic.IDE0063.severity = suggestion  # Use simple using statement

```



\### `Directory.Build.props`



```xml

<Project>

&#x20; <PropertyGroup>

&#x20;   <TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>

&#x20;   <LangVersion>latest</LangVersion>

&#x20;   <Nullable>enable</Nullable>

&#x20;   <ImplicitUsings>disable</ImplicitUsings>

&#x20;   <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

&#x20;   <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>

&#x20;   <GenerateDocumentationFile>true</GenerateDocumentationFile>

&#x20;   <NoWarn>CS1591</NoWarn>  <!-- Missing XML doc comments -->



&#x20;   <Authors>PocketMC Contributors</Authors>

&#x20;   <Copyright>Copyright (c) 2026 PocketMC</Copyright>

&#x20;   <Version>1.4.3</Version>

&#x20;   <FileVersion>1.4.3.0</FileVersion>

&#x20;   <AssemblyVersion>1.4.3.0</AssemblyVersion>



&#x20;   <EnableNETAnalyzers>true</EnableNETAnalyzers>

&#x20;   <AnalysisLevel>latest-recommended</AnalysisLevel>

&#x20;   <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

&#x20; </PropertyGroup>

</Project>

```



\### `Directory.Packages.props`



```xml

<Project>

&#x20; <PropertyGroup>

&#x20;   <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>

&#x20;   <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>

&#x20; </PropertyGroup>

&#x20; <ItemGroup>

&#x20;   <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />

&#x20;   <PackageVersion Include="Microsoft.Extensions.Http" Version="8.0.1" />

&#x20;   <PackageVersion Include="Microsoft.Extensions.Http.Polly" Version="8.0.1" />

&#x20;   <PackageVersion Include="Polly" Version="8.4.1" />

&#x20;   <PackageVersion Include="Serilog" Version="4.0.1" />

&#x20;   <PackageVersion Include="Serilog.Sinks.File" Version="6.0.0" />

&#x20;   <PackageVersion Include="Serilog.Sinks.Debug" Version="3.0.0" />

&#x20;   <PackageVersion Include="xunit" Version="2.9.0" />

&#x20;   <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />

&#x20;   <PackageVersion Include="Moq" Version="4.20.70" />

&#x20;   <PackageVersion Include="FluentAssertions" Version="6.12.0" />

&#x20;   <!-- ... all packages centralized ... -->

&#x20; </ItemGroup>

</Project>

```



\### `global.json`



```json

{

&#x20; "sdk": {

&#x20;   "version": "8.0.400",

&#x20;   "rollForward": "latestFeature"

&#x20; }

}

```



\### Pre-commit Hook (`.git/hooks/pre-commit` or via Husky)



```bash

\#!/bin/sh

echo "Running dotnet format..."

dotnet format --verify-no-changes --severity warn

if \[ $? -ne 0 ]; then

&#x20; echo "Code format check failed. Run 'dotnet format' to fix."

&#x20; exit 1

fi



echo "Running build..."

dotnet build --no-incremental -warnaserror

if \[ $? -ne 0 ]; then

&#x20; echo "Build failed."

&#x20; exit 1

fi

```



\### Recommended Tooling



| Tool | Purpose | Priority |

|------|---------|----------|

| `.editorconfig` | Code style | Critical |

| `Directory.Build.props` | Build config | Critical |

| `Directory.Packages.props` | Package versioning | Critical |

| `global.json` | SDK pinning | Critical |

| `dotnet format` | Formatting | High |

| .NET analyzers | Code quality | High |

| Roslynator | Additional analyzers | Medium |

| Husky.NET | Git hooks | Medium |

| Stryker.NET | Mutation testing | Low (experimental) |

| SonarCloud | Code quality cloud | Low |



\---



\# Phase 12: Contributor Experience



\## Onboarding Assessment



\*\*Estimated onboarding time for a new contributor today:\*\* 2-4 hours to understand the structure, 1-2 days to make a first meaningful contribution.



\*\*Estimated onboarding time after refactoring:\*\* 30 minutes to understand structure, 2-4 hours to first contribution.



\## What Would Confuse a New Developer



\### Confusion 1: "Where does business logic live?"



\*\*Current state:\*\* Business logic is in `Features/\*/Services/`, `Features/\*/Providers/`, `Infrastructure/`, `Core/Interfaces/`, `Models/`, and `Helpers/`. There is no single answer to "where is the domain logic?"



\*\*After refactoring:\*\* "Business logic lives in `PocketMC.Application`. Domain entities are in `PocketMC.Domain`. External integrations are in `PocketMC.Infrastructure`."



\### Confusion 2: "How do services find each other?"



\*\*Current state:\*\* All services are registered in one `ServiceCollectionExtensions.cs`. A contributor adding a new service must find this file, find the right section, and add a registration. They must also know which lifetime to use (singleton? scoped? transient?).



\*\*After refactoring:\*\* Each feature module has its own `Add{Feature}Services()` extension. The contributor adds their service to their feature's registration file. A `CONTRIBUTING.md` section explains lifetime guidelines.



\### Confusion 3: "What's the difference between ServerProcessManager and ServerLifecycleService?"



\*\*Current state:\*\* Two services with overlapping names. Unclear boundary.



\*\*After refactoring:\*\* Clear separation — `ServerProcessManager` (Infrastructure) manages OS process creation/destruction. `StartInstanceHandler` (Application) orchestrates the startup pipeline (port check → runtime → launch → monitoring). No overlap.



\### Confusion 4: "Where do I add a test?"



\*\*Current state:\*\* 80 test files in one directory. No guidance on naming, structure, or what to test.



\*\*After refactoring:\*\* Tests mirror source structure. `CONTRIBUTING.md` has a testing guide. Test traits categorize tests.



\### Confusion 5: "How do I add a new server software provider?"



\*\*Current state:\*\* Look at `Features/Instances/Providers/`. See `IServerSoftwareProvider`. Implement it. But where to register? In the god DI file. Where do downloads go? In `DownloaderService`. Where does version selection go? Probably in the provider. Unclear.



\*\*After refactoring:\*\*

1\. Implement `IServerSoftwareProvider` in `Infrastructure/ApiClients/{ProviderName}/`

2\. Register in `Infrastructure/Composition/ServerSoftwareExtensions.cs`

3\. Add tests in `Tests/Infrastructure/{ProviderName}Tests.cs`

4\. Document in `docs/features/server-providers.md`



\## Missing Documentation



| Document | Purpose | Priority |

|----------|---------|----------|

| `docs/architecture/overview.md` | Architecture diagram, layering rules, dependency direction | Critical |

| `docs/architecture/layering.md` | What goes in Domain vs Application vs Infrastructure vs Desktop | Critical |

| `docs/contributing/getting-started.md` | Step-by-step first contribution guide | High |

| `docs/contributing/coding-standards.md` | Naming, async, error handling, DI lifetime guidelines | High |

| `docs/contributing/testing-guide.md` | What to test, how to structure tests, test traits | High |

| `docs/architecture/decisions/` | ADRs for significant decisions | Medium |

| `docs/features/ai-summaries.md` | AI architecture, provider model, prompt organization | Medium |

| `docs/features/remote-control.md` | Remote control architecture, auth model, tunnel integration | Medium |

| `docs/features/remote-control-security.md` | Security model for remote control | Critical |



\---



\# Phase 13: Professional Standards Scorecard



| Category | Score | Justification |

|----------|-------|---------------|

| \*\*Architecture\*\* | 4/10 | Feature folders are a good start, but single-assembly monolith, mixed responsibilities, no layering enforcement, duplicate tunnel/backup/port subsystems, and god DI file prevent scalability. The structure suggests organic growth without architectural governance. |

| \*\*Maintainability\*\* | 4/10 | Code is organized by feature (good) but within features, responsibilities are mixed (bad). Naming inconsistency (VM vs ViewModel) signals review gaps. Scattered backup logic and duplicate path sanitizers make maintenance error-prone. Tests exist but are unnavigable. |

| \*\*Scalability\*\* | 5/10 | The app handles multiple instances and features, but the architecture won't scale to more developers. Single project means slow builds. No modular boundaries mean every change touches the same assembly. Remote Control and AI features will outgrow their current homes. |

| \*\*Security\*\* | 6/10 | Good fundamentals: DPAPI for secrets, PKCE for OAuth, path traversal tests, regex timeout tests, SafeZipExtractor. But: fragmented path sanitizers, unclear Remote Control auth details, no supply chain scanning, custom URI protocol without documented security model. Above average for open source, below enterprise-grade. |

| \*\*Documentation\*\* | 5/10 | README is excellent (8/10). CONTRIBUTING.md is thin (3/10). No architecture docs, no ADRs, no coding standards, no testing guide. Feature docs exist but are minimal (2 files). No CHANGELOG.md. The README sets expectations the docs can't support. |

| \*\*Testing\*\* | 5/10 | \~80 test files with good coverage of critical paths (path safety, backups, marketplace, ports). But: flat structure, no categorization, no integration/unit separation, critical gaps in AI/LLM, cloud backup, tunnel, and Java provisioning. No E2E tests. No coverage reporting. |

| \*\*Performance\*\* | 6/10 | Good awareness (log tailing mentioned, bounded operations). But: no IHttpClientFactory (socket exhaustion risk), no profiling infrastructure, potential UI thread blockers, no performance tests. Adequate for current scale, risky for growth. |

| \*\*Open Source Readiness\*\* | 5/10 | README and LICENSE are excellent. Issue templates exist. But: no CODE\_OF\_CONDUCT, no CHANGELOG, no PR template, no CI on PRs, no CodeQL, no Dependabot, no centralized build config. The project looks maintained but not engineered for community contribution. |

| \*\*Contributor Friendliness\*\* | 3/10 | This is the weakest area. No architecture documentation. No coding standards. Flat test structure. Mixed responsibilities in feature folders. Inconsistent naming. No ADRs. A new contributor would need to ask many questions that should be answered by docs. The barrier to first contribution is unnecessarily high. |

| \*\*Developer Experience\*\* | 4/10 | No `.editorconfig`, no `Directory.Build.props`, no `Directory.Packages.props`, no `global.json`. No `dotnet format` enforcement. No pre-commit hooks. No analyzer configuration. No centralized package management. Build is standard `dotnet build` but without guardrails. |



\*\*Overall: 4.7/10\*\* — The project has strong product thinking, good security instincts, and reasonable test coverage, but the engineering infrastructure and architecture governance are not yet at a level that would support sustained multi-contributor development.



\---



\# Phase 14: Final Deliverables



\## 1. Executive Summary



PocketMC Windows is a feature-rich, well-conceived Minecraft server management application with strong product vision and good security instincts. However, it currently suffers from \*\*single-assembly monolith architecture\*\*, \*\*mixed responsibilities within feature folders\*\*, \*\*fragmented domain logic\*\*, \*\*inconsistent conventions\*\*, and \*\*insufficient engineering infrastructure for open-source collaboration\*\*.



The project's greatest strength — comprehensive feature coverage (7 server types, cloud backups, tunnels, remote control, AI summaries, marketplace) — is also its greatest architectural risk: the codebase has grown organically without enforced layering, and the cost of change will increase exponentially as features are added.



The highest-leverage improvement is \*\*splitting into multiple projects\*\* (Domain, Application, Infrastructure, Desktop, RemoteControl) with compile-enforced dependency direction. This single change would unlock: faster builds, isolated testing, contributor clarity, and future platform expansion (CLI, headless, alternative UIs).



The second highest-leverage improvement is \*\*establishing engineering infrastructure\*\* (`.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, CI on PRs, CodeQL, Dependabot). This costs 1-2 days of work and pays dividends forever.



The third is \*\*documentation\*\*: architecture overview, coding standards, testing guide, and ADRs. Without these, contributor onboarding will remain the bottleneck.



\## 2. Top 10 Highest-Impact Improvements (Ranked by ROI)



| Rank | Improvement | Effort | Impact |

|------|------------|--------|--------|

| 1 | Add `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` | 2 hours | Enforces consistency, prevents environment drift, centralizes package versions |

| 2 | Add CI workflow for PRs (`ci.yml`) with build + test + vulnerability check | 2 hours | Catches regressions before merge, enables confident contribution |

| 3 | Standardize ViewModel naming (eliminate `VM` suffix) | 1 hour | Consistency, searchability |

| 4 | Split `Composition/ServiceCollectionExtensions.cs` into per-feature extensions | 4 hours | Eliminates god file, enables feature ownership, reduces merge conflicts |

| 5 | Reorganize test suite into mirrored structure with traits | 4 hours | Navigable tests, fast unit test runs, clear coverage gaps |

| 6 | Add missing open-source files (CODE\_OF\_CONDUCT, CHANGELOG, PR template, YAML issue forms) | 2 hours | Professional appearance, structured issue reporting |

| 7 | Consolidate backup logic from 3 feature folders into 1 | 4 hours | Single backup domain, coherent mental model |

| 8 | Extract `PocketMC.Domain` project | 1-2 days | Compile-enforced domain purity, reusable logic, faster builds |

| 9 | Introduce shared HTTP infrastructure with Polly | 1 day | Consistent resilience, socket reuse, centralized logging |

| 10 | Write architecture documentation + ADRs | 1 day | Contributor onboarding, decision history, future reference |



\## 3. Complete Refactoring Roadmap (Ordered)



| Phase | Step | Risk | Duration | Dependency |

|-------|------|------|----------|------------|

| 1 | Build infrastructure (`.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`) | Low | 2h | None |

| 1 | CI workflow for PRs | Low | 2h | Phase 1.1 |

| 1 | CodeQL workflow | Low | 1h | None |

| 1 | Dependabot configuration | Low | 30min | None |

| 2 | Standardize naming (`VM` → `ViewModel`) | Low | 1h | None |

| 2 | Remove `GlobalUsings.cs`, add explicit usings | Low | 2h | None |

| 2 | Clean up `Helpers/` junk drawer | Low | 1h | None |

| 2 | Clean up `Settings/` junk drawer | Low | 2h | None |

| 3 | Split DI registration by feature | Low | 4h | None |

| 3 | Consolidate backup logic | Medium | 4h | None |

| 3 | Unify path sanitizers | Medium | 4h | None |

| 3 | Add exception hierarchy + global handlers | Medium | 4h | None |

| 4 | Reorganize test suite | Low | 4h | None |

| 4 | Add test traits and categories | Low | 2h | Phase 4.1 |

| 4 | Add test coverage gaps (AI, cloud, tunnel, Java) | Medium | 2-3 days | Phase 4.1 |

| 5 | Extract `PocketMC.Domain` project | Medium | 1-2 days | Phase 1-3 |

| 5 | Extract `PocketMC.Application` project | Medium-High | 2-3 days | Phase 5.1 |

| 5 | Extract `PocketMC.Infrastructure` project | High | 2-3 days | Phase 5.2 |

| 5 | Extract `PocketMC.RemoteControl` project | High | 1-2 days | Phase 5.3 |

| 6 | Introduce shared HTTP infrastructure | Medium | 1 day | Phase 5.3 |

| 6 | Refactor AI/LLM to strategy pattern | Medium | 1 day | Phase 5.2 |

| 6 | Add prompt organization | Medium | 4h | Phase 6.2 |

| 7 | Write architecture docs + ADRs | Low | 1 day | Phase 5 |

| 7 | Write coding standards | Low | 4h | Phase 1.1 |

| 7 | Write testing guide | Low | 4h | Phase 4.1 |

| 7 | Write contributor onboarding guide | Low | 4h | Phase 7.1-7.3 |



\*\*Total estimated effort:\*\* 3-4 weeks of focused work for a single developer. Can be parallelized across 2-3 developers in 2 weeks.



\## 4. Target Folder Structure



(See Phase 3 — full tree provided above)



\## 5. Risks of Current Architecture (Most Important Technical Debt)



| Risk | Impact | Likelihood | Mitigation |

|------|--------|------------|------------|

| \*\*Single assembly\*\* — all code in one project, no compile-time layering | Every change risks cross-layer coupling. Build times grow. Cannot reuse logic. | Certain | Extract projects (Phase 5) |

| \*\*God DI file\*\* — all registration in one file | Merge conflicts on every PR. No feature ownership. | High | Split per feature (Phase 3, Step 6) |

| \*\*Fragmented backup/port/tunnel logic\*\* — same domain in multiple folders | Bug fixes in one place don't propagate. Divergent behavior. | High | Consolidate (Phase 3, Steps 5 \& 11) |

| \*\*No shared HTTP resilience\*\* — each API client reinvents retry | Inconsistent failure behavior. Socket exhaustion. No centralized logging. | High | Shared HTTP infra (Phase 6, Step 1) |

| \*\*AI client as monolith\*\* — 6 providers in one class | Adding providers is risky. Testing is hard. No provider-specific behavior. | Medium | Strategy pattern (Phase 6, Step 2) |

| \*\*No exception hierarchy\*\* — ad-hoc error handling | Unpredictable failure modes. Leaked stack traces. No user-friendly errors. | High | Exception hierarchy (Phase 3, Step 14) |

| \*\*Flat test structure\*\* — 80 files, no organization | Can't find tests. Can't run fast subset. Can't identify coverage gaps. | Certain | Reorganize (Phase 4, Step 13) |

| \*\*No engineering infrastructure\*\* — no editorconfig, no centralized packages | Style drift. Version drift. Environment drift. | Certain | Phase 1 |

| \*\*Settings folder as junk drawer\*\* | Settings logic scattered. Telemetry, image cropping, add-on updates all in Settings. | High | Phase 2, Step 4 |

| \*\*Remote Control embedded in Desktop project\*\* | Web host pulls in WPF. Can't deploy separately. Frontend has no build system. | Medium | Extract project (Phase 5, Step 12) |



\## 6. Future-Proofing Recommendations (10x Growth)



\### 6.1 — Plugin Architecture



When PocketMC gains hundreds of users, they will want custom server software providers, custom backup destinations, custom tunnel providers. Design for this now:



\- Define `IServerSoftwareProvider`, `IBackupProvider`, `ITunnelProvider` as public plugin interfaces in `PocketMC.Application.Abstractions`

\- Create a plugin loading mechanism (MEF or custom assembly scanning)

\- Document the plugin API



\### 6.2 — Configuration System Migration



JSON files on disk work for a single-user desktop app. But if PocketMC ever adds:

\- Multi-user support (multiple Windows users sharing instances)

\- Cloud sync of settings

\- Profile-based configurations



Then migrate to a proper configuration system:

\- `IOptions<T>` pattern with `IOptionsMonitor<T>` for hot reload

\- Configuration providers: JSON file, environment variables, command-line args

\- Validated settings with `IValidateOptions<T>`



\### 6.3 — Observability Pipeline



For enterprise users and power users running many instances:

\- Structured logging with Serilog (file, rolling, JSON format)

\- OpenTelemetry traces for cross-service operations (instance start → runtime provision → tunnel creation)

\- Metrics export (Prometheus-compatible) for monitoring running instances

\- A diagnostics dashboard within the app showing all operations



\### 6.4 — Headless Mode



A headless mode (CLI or Windows Service) would allow:

\- Running PocketMC as a background service

\- Managing instances without a GUI

\- Integration with automation tools



This is only possible if domain and application logic are in separate projects from the WPF UI. \*\*This is the strongest argument for the project split.\*\*



\### 6.5 — Cross-Platform Considerations



Even though PocketMC is Windows-only today, designing the domain and application layers as platform-agnostic means:

\- A future Avalonia UI could replace WPF

\- A future web-based UI could replace the desktop app

\- A future CLI tool could reuse all business logic



Keep all `Microsoft.Win32`, `System.Windows`, `Windows.Storage` references in `PocketMC.Infrastructure` and `PocketMC.Desktop` only. Never in Domain or Application.



\### 6.6 — Telemetry and Analytics



`TelemetryService.cs` exists but is in the Settings junk drawer. For sustainable open-source development:

\- Make telemetry opt-in (not opt-out)

\- Use a privacy-respecting analytics provider (PostHog, Plausible)

\- Track: feature usage, crash reports, performance metrics

\- Never track: server IPs, player names, file paths, API keys

\- Document exactly what is collected in `SECURITY.md`



\### 6.7 — Release Pipeline



The current Velopack-based release is good. For scale:

\- Add semantic versioning enforcement (MinVer or versionize)

\- Add release notes auto-generation from conventional commits

\- Add staged rollouts (release to 10% of users, then 50%, then 100%)

\- Add crash reporting (Sentry or similar) for the released app

\- Add a `RELEASE.md` documenting the release process step-by-step



\### 6.8 — Governance Model



For a project expecting hundreds of stars and external contributors:

\- Document the governance model (who has commit access? how do you become a maintainer?)

\- Define the PR review process (how many reviewers? who can merge?)

\- Create a `MAINTAINERS.md` file listing maintainers and their areas of expertise

\- Consider adopting a governance framework (e.g., Contributor Covenant's governance model)



\---



\## Closing Statement



PocketMC is a project with \*\*exceptional product vision\*\* and \*\*adequate engineering execution\*\* that needs \*\*architectural maturity\*\* to reach its full potential. The feature set rivals commercial Minecraft server managers, the security instincts are above average for open source, and the test coverage shows genuine care.



But the codebase is at an inflection point. The current structure — single assembly, mixed responsibilities, fragmented domains, no engineering infrastructure — will not scale to the next phase of growth. Every new feature will be harder to add, every bug harder to fix, every contributor harder to onboard.



The recommendations in this audit are not optional polish. They are the difference between a project that grows gracefully and one that collapses under its own weight. The investment is 3-4 weeks of focused refactoring. The payoff is years of sustainable development.



\*\*Start with Phase 1. Do it today.\*\*

