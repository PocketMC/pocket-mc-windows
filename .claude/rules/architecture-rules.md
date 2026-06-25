# Architecture & Layering

PocketMC strictly follows Clean Architecture principles to separate concerns.

## Pattern

- **PocketMC.Domain**: Core business logic, pure models, enums. Has NO dependencies on other layers or WPF.
- **PocketMC.Application**: Interfaces, application logic, and use cases. Depends ONLY on Domain.
- **PocketMC.Infrastructure**: Concrete implementations of external concerns (Networking, Cloud Backups, AI, HTTP). Depends on Application and Domain.
- **PocketMC.Desktop**: The WPF Presentation layer. Contains Views, ViewModels, UI-specific logic, and DI container config.

## When to Apply

Apply these rules any time you are adding new features, services, or models. Never bleed WPF/UI logic (`System.Windows.*`) into Domain, Application, or Infrastructure.
