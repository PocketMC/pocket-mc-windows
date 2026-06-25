# Configuration Source of Truth

The embedded `pocketmc.yml` file is the master configuration for the application.

## Pattern

- **`pocketmc.yml`**: This YAML file holds the application's version, release channel, backend proxies (auth/telemetry), and social links. 
- **NO Hardcoded Versions**: Do not hardcode the version in `.csproj` files, `Directory.Build.props`, or C# classes. 
- **Dynamic Retrieval**: Always retrieve values dynamically from `AppConfig` (which parses `pocketmc.yml` at runtime).

## When to Apply

Apply this whenever updating the app version or referencing proxy URLs.
