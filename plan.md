1. **Move Interfaces**: Move non-UI interfaces (`IFileSystem.cs`, `IAppNavigationService.cs`, `IRconClient.cs` - I will create `IRconClient.cs` if missing, or use existing) from `PocketMC.Desktop/Core/Interfaces` into `PocketMC.Application/Interfaces`. Verify using `ls`.
2. **Move Infrastructure**: Move `PocketMC.Desktop/Infrastructure` into `PocketMC.Infrastructure`. Make sure `PocketMC.Infrastructure` references `PocketMC.Application`. Modify classes to implement the newly moved interfaces. Verify using `dotnet build`.
3. **Move Application Features**: Move `PocketMC.Desktop/Features/Instances` into `PocketMC.Application/Features/Instances`. Update the constructors in `PocketMC.Application/Features/Instances` to use interfaces (e.g. `IRconClient`, `IFileSystem`) instead of concrete classes to avoid referencing `Infrastructure`. Verify using `dotnet build`.
4. **Wire up Desktop DI**: Ensure `PocketMC.Desktop` references `PocketMC.Application` and `PocketMC.Infrastructure`. Update `ServiceCollectionExtensions.cs` to map `IFileSystem` -> `PhysicalFileSystem` and `IRconClient` -> `RconClient`. Verify using `dotnet build`.
5. Run tests with `dotnet test -p:EnableWindowsTargeting=true`.
6. Complete pre-commit steps.
7. Submit the final changes.
