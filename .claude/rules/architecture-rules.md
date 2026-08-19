# Architecture & Layering

PocketMC strictly follows Clean Architecture principles across 5 production projects and 5 dedicated test projects.

## Pattern

### Production Layers
- **PocketMC.Domain**: Core business logic, pure models, enums, path safety (`PathSafety`), and archive contracts (`SafeZipExtractor`). Has NO dependencies on other layers or WPF.
- **PocketMC.Application**: Interfaces, application use cases, and policies (`ModpackOverridePolicy`, `MarketplaceDownloadPolicy`). Depends ONLY on Domain.
- **PocketMC.Infrastructure**: Concrete implementations of external concerns (Networking, Adoptium Java provisioning, Cloud Backups, AI API clients, server process management, port probes, DPAPI security). Depends on Application and Domain.
- **PocketMC.RemoteControl**: Embedded ASP.NET Core web server, REST API, WebSocket console streaming, pairing authorization, rate limiting, and tunnel providers.
- **PocketMC.Desktop**: The WPF Presentation layer (`Wpf.Ui`). Contains Views, ViewModels, UI-specific logic, and DI composition.

### Test Suite Mirroring
- **PocketMC.Domain.Tests**: Isolated unit tests for models and domain security contracts.
- **PocketMC.Application.Tests**: Isolated use case & policy tests with mocked infrastructure dependencies.
- **PocketMC.Infrastructure.Tests**: Concrete external provider, cloud, process, and network integration tests.
- **PocketMC.RemoteControl.Tests**: Web host, remote authentication, WebSocket streaming, and tunnel provider tests.
- **PocketMC.Desktop.Tests**: ViewModels, navigation, dialog workflows, and XAML bindings.

## When to Apply

Apply these rules any time you are adding new features, services, models, or tests. Never bleed WPF/UI logic (`System.Windows.*`) into Domain, Application, Infrastructure, or RemoteControl. Always add new tests into the exact matching test project layer.

