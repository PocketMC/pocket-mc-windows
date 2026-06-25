---
name: generate-mvvm
description: Generate a new MVVM Feature (View and ViewModel) in PocketMC
argument-hint: [FeatureName]
disable-model-invocation: true
---

# Generate MVVM Feature

Generates a standard Wpf.Ui View and ViewModel pair for PocketMC and registers them in the Dependency Injection container.

## Instructions

1. **Verify the argument**
   - Ensure the user provided a FeatureName (e.g., `ModManagement`).
   - Determine if the feature should go into an existing folder (e.g., `Features/Marketplace`) or a new one based on the context.

2. **Create the ViewModel**
   - Location: `PocketMC.Desktop/Features/<Area>/<FeatureName>ViewModel.cs`
   - It must inherit from `ObservableObject` and implement `INavigationAware` (if it's a page).
   - Use Constructor Injection for any services.

3. **Create the View (Code-behind)**
   - Location: `PocketMC.Desktop/Features/<Area>/<FeatureName>Page.xaml.cs`
   - Inherit from `INavigableView<T>` where T is the ViewModel.
   - Assign `ViewModel` property in the constructor.

4. **Create the View (XAML)**
   - Location: `PocketMC.Desktop/Features/<Area>/<FeatureName>Page.xaml`
   - Use `ui:Page` from `http://schemas.lepo.co/wpfui/2022/xaml`.
   - Set `d:DataContext` for design-time bindings.

5. **Register in DI Container**
   - Open `PocketMC.Desktop/Composition/ServiceCollectionExtensions.cs` or the specific `<Area>ServiceCollectionExtensions.cs`.
   - Add `.AddTransient<<FeatureName>Page>()`
   - Add `.AddTransient<<FeatureName>ViewModel>()`

## Examples

- "Generate MVVM for PlayerSettings"
- "Create a new AddonBrowser view and viewmodel"
