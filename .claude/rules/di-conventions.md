# Dependency Injection Conventions

PocketMC uses standard Microsoft.Extensions.DependencyInjection for all services.

## Pattern

1. **Constructor Injection**: Use Constructor Injection exclusively for passing dependencies.
2. **Registration**: All new services must be registered via the DI container extensions in `PocketMC.Desktop/Composition/ServiceCollectionExtensions.cs` or nested feature extensions like `InstanceServiceCollectionExtensions.cs`.
3. **No Service Locators**: Avoid passing `IServiceProvider` unless building a factory class.

## When to Apply

Apply this rule whenever you add a new service, ViewModel, or provider class.
