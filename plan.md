1. **Fix Circular Dependency**: The CI failed with `error MSB4006: There is a circular dependency... [PocketMC.Application.csproj]`. When I added `<ProjectReference Include="..\PocketMC.Infrastructure\PocketMC.Infrastructure.csproj" />` to `PocketMC.Application.csproj`, I created a circular dependency because `PocketMC.Infrastructure` already depends on `PocketMC.Application`. I will remove the `PocketMC.Infrastructure` reference from `PocketMC.Application.csproj`.
2. **Move Concrete Implementations to Infrastructure**: If code moved to `PocketMC.Application` relies on `PocketMC.Infrastructure` classes like `FileUtils` or `RconClient`, it should either depend on interfaces, or that specific code belongs in `PocketMC.Infrastructure`. The Great Migration meant extracting UI logic vs Business logic vs Infrastructure logic. If I can't easily decouple them yet, I'll move any `Features/Instances` classes that strictly depend on `Infrastructure` into `PocketMC.Infrastructure/Instances` or similar, or just remove the circular reference and see what fails to build.
Wait, let's look at `PocketMC.Infrastructure.csproj`. Does it reference `PocketMC.Application.csproj`? Yes.
I will remove the `<ProjectReference Include="..\PocketMC.Infrastructure\PocketMC.Infrastructure.csproj" />` from `PocketMC.Application.csproj` and see if `Application` builds without it. If it fails due to missing classes, I will move those classes to `PocketMC.Infrastructure`.
3. Run `dotnet build` to ensure the project compiles.
4. Run tests with `dotnet test`.
5. Complete pre-commit steps.
6. Submit.
