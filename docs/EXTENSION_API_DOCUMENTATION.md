# MnemoApp Extension API Documentation

## Overview

MnemoApp provides a comprehensive API for extensions to integrate with the application. Extensions can access UI components, data storage, AI services, task management, and more through the `MnemoAPI` interface.

## Accessing the API

Extensions receive API access through the `IExtensionContext`:

```csharp
public async Task OnLoadAsync(IExtensionContext context)
{
    // Access the full MnemoAPI
    var api = context.API;
    
    // Use specific API modules
    api.ui.toast.show("Hello from extension!");
    api.ai.GetAllModelsAsync();
    api.tasks.scheduleTask(myTask);
}
```

## API Structure

The MnemoAPI is organized into specialized modules:

- `api.navigate` - Navigation and routing
- `api.sidebar` - Sidebar management
- `api.ui` - UI components and overlays
- `api.system` - System operations
- `api.data` - Data storage
- `api.storage` - Package storage
- `api.ai` - AI services
- `api.tasks` - Task management
- `api.files` - File processing
- `api.latex` - LaTeX rendering
- `api.settings` - Settings management
- `api.extensions` - Extension management

---

## Navigation API (`api.navigate`)

### Methods

#### `Navigate<TViewModel>()`
Navigate to a view model by type.

```csharp
api.navigate.Navigate<MyViewModel>();
```

#### `Navigate(object viewModelInstance)`
Navigate to a view model instance.

```csharp
var vm = new MyViewModel();
api.navigate.Navigate(vm);
```

#### `Navigate(object viewModelInstance, string breadcrumbTitle, bool clearBreadcrumbs = false)`
Navigate with breadcrumb support.

```csharp
api.navigate.Navigate(vm, "My Page", clearBreadcrumbs: true);
```

#### `NavigateToBreadcrumb(BreadcrumbItem breadcrumb)`
Navigate to a specific breadcrumb.

#### `GoBack()`
Navigate back to previous view.

### Properties

- `CanGoBack` - Whether back navigation is possible
- `GoBackCommand` - Command for back navigation
- `CurrentViewModel` - Currently active view model
- `Breadcrumbs` - Collection of breadcrumb items

### Events

- `ViewModelChanged` - Fired when view model changes
- `CanGoBackChanged` - Fired when back navigation availability changes

---

## Sidebar API (`api.sidebar`)

### Methods

#### `Register(string title, Type viewModelType, string categoryName, string iconPath = "")`
Register a sidebar item.

```csharp
api.sidebar.Register(
    "My Extension", 
    typeof(MyViewModel), 
    "Extensions", 
    "avares://MyExtension/icon.svg"
);
```

#### `Unregister(string title, string categoryName)`
Remove a sidebar item.

```csharp
api.sidebar.Unregister("My Extension", "Extensions");
```

#### `GetCategory(string categoryName)`
Get a sidebar category.

#### `GetItem(string title, string categoryName)`
Get a specific sidebar item.

#### `ClearAll()`
Clear all sidebar items.

### Properties

- `Categories` - Collection of sidebar categories

### Events

- `CategoryAdded` - Fired when category is added
- `ItemAdded` - Fired when item is added
- `ItemRemoved` - Fired when item is removed

---

## UI API (`api.ui`)

The UI API provides access to various UI components and services.

### Theme API (`api.ui.themes`)

#### Methods

##### `getAllThemes()`
Get all available themes.

```csharp
var themes = await api.ui.themes.getAllThemes();
```

##### `getTheme(string name)`
Get specific theme by name.

```csharp
var theme = await api.ui.themes.getTheme("Dark");
```

##### `setTheme(string name)`
Set the current theme.

```csharp
await api.ui.themes.setTheme("Dark");
```

##### `getCurrentTheme()`
Get the currently active theme.

##### `applyTheme(string name)`
Apply a theme immediately.

##### `import(string sourceDirectory)`
Import a theme from directory.

##### `export(string themeName, string destinationDirectory)`
Export a theme to directory.

##### `startWatching()` / `stopWatching()`
Start/stop theme file watching.

### Language API (`api.ui.language`)

#### Methods

##### `getCurrentLanguage()`
Get current language code.

##### `setLanguage(string code)`
Set the application language.

```csharp
await api.ui.language.setLanguage("en-US");
```

##### `getAvailableLanguages()`
Get all available languages.

##### `get(string ns, string key)`
Get localized string.

```csharp
var text = api.ui.language.get("Common", "Save");
```

##### `getCurrentLanguageManifest()`
Get current language manifest.

##### `registerLanguageJson(string code, string json)`
Register language from JSON.

##### `registerNamespace(string code, string ns, Dictionary<string, string> entries)`
Register namespace entries.

### Topbar API (`api.ui.topbar`)

#### Methods

##### `addButton(string iconPath, object? stroke = null, bool notification = false, int order = 0, ICommand? command = null, string? toolTip = null)`
Add a button to the topbar.

```csharp
var buttonId = api.ui.topbar.addButton(
    "avares://MyExtension/icon.svg",
    notification: true,
    command: myCommand,
    toolTip: "My Button"
);
```

##### `addCustom(Control control, int order = 0)`
Add custom control to topbar.

##### `remove(Guid id)`
Remove topbar item.

##### `setNotification(Guid id, bool notification)`
Set notification state.

##### `addSeparator(int order = 0, double height = 24, double thickness = 1)`
Add separator to topbar.

#### Properties

- `items` - Collection of topbar items

### Overlay API (`api.ui.overlay`)

#### Methods

##### `Show<T>(Control control, OverlayOptions? options = null, string? name = null, Guid? parentId = null)`
Show overlay with control.

```csharp
var result = await api.ui.overlay.Show<MyResultType>(myControl);
```

##### `Show<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)`
Show overlay from XAML.

##### `CreateOverlay(Control control, OverlayOptions? options = null, string? name = null, Guid? parentId = null)`
Create overlay (legacy).

##### `CreateOverlayFromXaml(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)`
Create overlay from XAML (legacy).

##### `CloseOverlay(Guid id, object? result = null)`
Close overlay by ID.

##### `CloseOverlay(string name, object? result = null)`
Close overlay by name.

##### `CloseAllOverlays()`
Close all overlays.

##### `CreateDialog(string title, string description, string primaryText, string secondaryText, OverlayOptions? options = null, string? name = null)`
Create confirmation dialog.

```csharp
var result = await api.ui.overlay.CreateDialog(
    "Confirm Action",
    "Are you sure?",
    "Yes",
    "No"
);
```

#### Properties

- `overlays` - Collection of active overlays
- `hasOverlays` - Whether any overlays are open

### Toast API (`api.ui.toast`)

#### Methods

##### `show(string title, string? message = null, ToastType type = ToastType.Info, TimeSpan? duration = null, bool dismissable = true)`
Show toast notification.

```csharp
var toastId = api.ui.toast.show("Success", "Operation completed", ToastType.Success);
```

##### `showStatus(string title, string? message = null, ToastType type = ToastType.Process, bool dismissable = true, double? initialProgress = null, string? progressText = null)`
Show status toast with progress.

##### `updateStatus(Guid id, double? progress = null, string? progressText = null, string? title = null, string? message = null, ToastType? type = null)`
Update status toast.

##### `completeStatus(Guid id)`
Complete status toast.

##### `remove(Guid id)`
Remove toast.

##### `clear()`
Clear all toasts.

##### `showForTask(Guid taskId, bool showProgress = true)`
Show toast for task.

##### `updateForTask(Guid taskId)`
Update toast for task.

##### `removeForTask(Guid taskId)`
Remove toast for task.

#### Properties

- `passive` - Collection of passive toasts
- `status` - Collection of status toasts

### Dropdown API (`api.ui.dropdown`)

#### Methods

##### `RegisterItem(DropdownType dropdownType, DropdownItemBase item)`
Register dropdown item.

##### `RegisterItems(DropdownType dropdownType, IEnumerable<DropdownItemBase> items)`
Register multiple items.

##### `Show(Control anchorControl, DropdownType dropdownType, IEnumerable<DropdownItemBase>? additionalItems = null, string? category = null, OverlayOptions? options = null, string? name = null)`
Show dropdown.

##### `ShowCustom(Control anchorControl, IEnumerable<DropdownItemBase> items, OverlayOptions? options = null, string? name = null)`
Show custom dropdown.

##### `ShowWithBuilder(Control anchorControl, Action<IList<DropdownItemBase>> configureItems, DropdownType dropdownType = DropdownType.Options, string? category = null, OverlayOptions? options = null, string? name = null)`
Show dropdown with builder pattern.

```csharp
api.ui.dropdown.ShowWithBuilder(anchorControl, items =>
{
    items.Add(new DropdownItem("Option 1", () => DoAction1()));
    items.Add(new DropdownItem("Option 2", () => DoAction2()));
});
```

##### `GetRegisteredItems(DropdownType dropdownType, string? category = null)`
Get registered items.

##### `RemoveItem(DropdownType dropdownType, string itemId)`
Remove item.

##### `ClearItems(DropdownType dropdownType)`
Clear all items for type.

### Loading Overlay API (`api.ui.loading`)

#### Methods

##### `showForTask(Guid taskId, OverlayOptions? options = null)`
Show loading overlay for task.

##### `show(string title, string? description = null, double initialProgress = 0.0, string? progressText = null, OverlayOptions? options = null)`
Show custom loading overlay.

##### `update(Guid overlayId, double? progress = null, string? progressText = null, string? title = null, string? description = null)`
Update loading overlay.

##### `close(Guid overlayId)`
Close loading overlay.

##### `isOpenForTask(Guid taskId)`
Check if overlay is open for task.

##### `getOverlayIdForTask(Guid taskId)`
Get overlay ID for task.

---

## System API (`api.system`)

### Methods

#### `minimize()`
Minimize the main window.

#### `maximize()`
Toggle maximize/restore window.

#### `exit()`
Exit the application.

---

## Data API (`api.data`)

### Methods

#### `GetProperty<T>(string key, StorageScope scope = StorageScope.Runtime)`
Get property from storage.

```csharp
var value = api.data.GetProperty<string>("myKey");
```

#### `SetProperty<T>(string key, T value, StorageScope scope = StorageScope.Runtime)`
Set property in storage.

```csharp
api.data.SetProperty("myKey", "myValue");
```

#### `HasProperty(string key, StorageScope scope = StorageScope.Runtime)`
Check if property exists.

#### `RemoveProperty(string key, StorageScope scope = StorageScope.Runtime)`
Remove property.

#### `AddProperty<T>(string key, T value, StorageScope scope = StorageScope.Runtime)`
Add property (for collections).

### Storage Scopes

- `Runtime` - Application runtime data
- `Packaged` - Package-specific data

---

## Storage API (`api.storage`)

Access to packaged storage manager for advanced operations.

---

## AI API (`api.ai`)

### Methods

#### `GetAllNamesAsync()`
Get all available model names.

```csharp
var modelNames = await api.ai.GetAllNamesAsync();
```

#### `GetAllModelsAsync()`
Get detailed model information.

#### `GetModelAsync(string modelName)`
Get specific model.

#### `IsAvailableAsync(string modelName)`
Check if model is available.

#### `GetAdaptersAsync()`
Get all LoRA adapters.

#### `GetCompatibleAdaptersAsync(string modelName)`
Get adapters compatible with model.

#### `InferAsync(AIInferenceRequest request, CancellationToken cancellationToken = default)`
Perform AI inference.

```csharp
var request = api.ai.CreateRequest("gpt-3.5-turbo", "Hello world");
var response = await api.ai.InferAsync(request);
```

#### `InferStreamAsync(AIInferenceRequest request, CancellationToken cancellationToken = default)`
Streaming inference.

```csharp
await foreach (var token in api.ai.InferStreamAsync(request))
{
    Console.Write(token);
}
```

#### `CountTokensAsync(string text, string modelName)`
Count tokens in text.

#### `HasTokenizerAsync(string modelName)`
Check if model has tokenizer.

#### `RefreshAsync()`
Refresh model registry.

#### `CreateRequest(string modelName, string prompt, string? systemPrompt = null)`
Create simple request.

#### `CreateAdvancedRequest(string modelName, string prompt, string? systemPrompt = null, List<string>? conversationHistory = null, string? loraAdapter = null, float temperature = 0.7f, float topP = 0.9f, int topK = 40, float repeatPenalty = 1.1f, int maxTokens = 512, List<string>? stopTokens = null, bool stream = false)`
Create advanced request.

#### `CreateRequestWithSelectedModel(string prompt, string? systemPrompt = null)`
Create request with selected model.

#### `CreateAdvancedRequestWithSelectedModel(...)`
Create advanced request with selected model.

#### `GetSelectedModel()`
Get currently selected model.

#### `SetSelectedModel(string? modelName)`
Set selected model.

#### `GetAvailableModelsObservable()`
Get models as observable collection.

#### `HasModels()`
Check if any models available.

### Events

#### `SubscribeToModelUpdates(Action<IReadOnlyList<AIModel>> callback)`
Subscribe to model updates.

#### `UnsubscribeFromModelUpdates(Action<IReadOnlyList<AIModel>> callback)`
Unsubscribe from model updates.

#### `SubscribeToAdapterUpdates(Action<IReadOnlyList<LoraAdapter>> callback)`
Subscribe to adapter updates.

#### `UnsubscribeFromAdapterUpdates(Action<IReadOnlyList<LoraAdapter>> callback)`
Unsubscribe from adapter updates.

#### `SubscribeToSelectedModelChanges(Action<string?> callback)`
Subscribe to model selection changes.

#### `UnsubscribeFromSelectedModelChanges(Action<string?> callback)`
Unsubscribe from model selection changes.

---

## Task API (`api.tasks`)

### Properties

- `allTasks` - All tasks in system
- `runningTasks` - Currently running tasks
- `pendingTasks` - Pending tasks
- `completedTasks` - Completed tasks
- `isRunning` - Whether scheduler is running
- `maxParallelTasks` - Maximum parallel tasks

### Methods

#### `scheduleTask(IMnemoTask task)`
Schedule custom task.

```csharp
var taskId = api.tasks.scheduleTask(myTask);
```

#### `getTask(Guid taskId)`
Get task by ID.

#### `cancelTask(Guid taskId)`
Cancel task.

#### `pauseTask(Guid taskId)`
Pause task.

#### `resumeTask(Guid taskId)`
Resume task.

#### `removeTask(Guid taskId)`
Remove completed task.

#### `clearCompletedTasks()`
Clear all completed tasks.

#### `getSubTasks(Guid parentTaskId)`
Get sub-tasks.

#### `getRootTask(Guid taskId)`
Get root task.

#### `scheduleAIGeneration(string prompt, string name, string? description = null, string? modelName = null, int maxTokens = 1000)`
Schedule AI generation task.

```csharp
var taskId = api.tasks.scheduleAIGeneration("Write a story", "Story Generation");
```

#### `scheduleParseAttachments(string[] filePaths)`
Schedule file parsing task.

#### `scheduleGeneratePath(string pathTopic, int unitCount)`
Schedule learning path generation.

#### `scheduleCreatePath(string notes)`
Schedule path creation from notes.

#### `scheduleGenerateUnit(string pathId, int unitOrder)`
Schedule unit generation.

#### `start()` / `stop()`
Start/stop task scheduler.

### Event Handlers

#### `onTaskStarted(Action<IMnemoTask> handler)`
Task started event.

#### `onTaskCompleted(Action<IMnemoTask> handler)`
Task completed event.

#### `onTaskFailed(Action<IMnemoTask> handler)`
Task failed event.

#### `onTaskCancelled(Action<IMnemoTask> handler)`
Task cancelled event.

#### `onTaskProgressChanged(Action<IMnemoTask, TaskProgress?> handler)`
Task progress changed event.

---

## File API (`api.files`)

### Methods

#### `ProcessFile(string filePath, CancellationToken cancellationToken = default)`
Process single file.

```csharp
var result = await api.files.ProcessFile("document.pdf");
```

#### `ProcessFiles(string[] filePaths, CancellationToken cancellationToken = default)`
Process multiple files.

```csharp
var content = await api.files.ProcessFiles(new[] { "doc1.pdf", "doc2.docx" });
```

#### `IsFileSupported(string filePath)`
Check if file type is supported.

#### `GetSupportedExtensions()`
Get all supported file extensions.

#### `RegisterProcessor(IFileProcessor processor)`
Register custom file processor.

```csharp
api.files.RegisterProcessor(new MyFileProcessor());
```

#### `GetRegisteredProcessors()`
Get all registered processor names.

---

## LaTeX API (`api.latex`)

### Methods

#### `Render(string latex, double fontSize = 16.0)`
Render LaTeX to control (synchronous).

```csharp
var control = api.latex.Render("\\frac{a}{b}");
```

#### `RenderAsync(string latex, double fontSize = 16.0)`
Render LaTeX asynchronously.

```csharp
var control = await api.latex.RenderAsync("\\sum_{i=1}^{n} i");
```

#### `ClearCache()`
Clear rendering cache.

#### `IsValid(string latex)`
Validate LaTeX syntax.

#### `GetAvailableSymbols()`
Get all available symbols.

#### `HasSymbol(string command)`
Check if symbol exists.

#### `GetSymbolUnicode(string command)`
Get Unicode for symbol.

#### `RegisterCustomSymbol(string command, string unicode)`
Register custom symbol.

```csharp
api.latex.RegisterCustomSymbol("mySymbol", "★");
```

#### `GetAllSymbols()`
Get all symbols dictionary.

---

## Settings API (`api.settings`)

### Methods

#### `Get<T>(string key, T? defaultValue = default)`
Get setting value.

```csharp
var value = api.settings.Get<string>("mySetting", "default");
```

#### `Set<T>(string key, T value)`
Set setting value.

```csharp
api.settings.Set("mySetting", "myValue");
```

#### `Has(string key)`
Check if setting exists.

#### `Remove(string key)`
Remove setting.

#### `GetString(string key, string defaultValue = "")`
Get string setting.

#### `SetString(string key, string value)`
Set string setting.

#### `GetInt(string key, int defaultValue = 0)`
Get integer setting.

#### `SetInt(string key, int value)`
Set integer setting.

#### `GetBool(string key, bool defaultValue = false)`
Get boolean setting.

#### `SetBool(string key, bool value)`
Set boolean setting.

#### `GetDouble(string key, double defaultValue = 0.0)`
Get double setting.

#### `SetDouble(string key, double value)`
Set double setting.

---

## Extensions API (`api.extensions`)

### Methods

#### `listInstalled()`
Get all installed extensions.

```csharp
var extensions = api.extensions.listInstalled();
```

#### `getMetadata(string name)`
Get extension metadata.

#### `install(string path)`
Install extension from path.

```csharp
var result = await api.extensions.install("path/to/extension.mnemoext");
```

#### `uninstall(string name)`
Uninstall extension.

#### `enable(string name)`
Enable extension.

#### `disable(string name)`
Disable extension.

#### `reload(string name)`
Reload extension.

#### `refresh()`
Refresh extension discovery.

#### `load(string name)` / `unload(string name)`
Load/unload extension.

---

## Extension Context

Extensions receive an `IExtensionContext` providing:

### Properties

- `Metadata` - Extension metadata
- `API` - Full MnemoAPI access
- `Services` - Dependency injection container
- `Logger` - Extension logger
- `DataDirectory` - Extension data folder
- `StoragePrefix` - Storage key prefix

### Methods

#### `HasPermission(ExtensionPermission permission)`
Check if extension has permission.

```csharp
if (context.HasPermission(ExtensionPermission.FileAccess))
{
    // Do file operations
}
```

#### `RequestPermissionAsync(ExtensionPermission permission)`
Request permission from user.

```csharp
var granted = await context.RequestPermissionAsync(ExtensionPermission.NetworkAccess);
```

### Permissions

- `FileAccess` - Read/write files outside extension directory
- `NetworkAccess` - HTTP requests and network operations
- `UIAccess` - Register views, sidebar items, overlays
- `ApiRegistration` - Register custom API endpoints
- `FullTrust` - Complete access (development extensions)

---

## Example Extension

```csharp
using MnemoApp.Core.Extensions;

public class MyExtension : IMnemoExtension, IUIContributor
{
    public async Task OnLoadAsync(IExtensionContext context)
    {
        context.Logger.LogInfo("My extension loading...");
        
        // Register UI components
        await RegisterUIAsync(context);
        
        // Use AI service
        var models = await context.API.ai.GetAllModelsAsync();
        context.Logger.LogInfo($"Found {models.Count} AI models");
        
        // Schedule a task
        var taskId = context.API.tasks.scheduleAIGeneration(
            "Generate a summary", 
            "Summary Generation"
        );
        
        // Show toast
        context.API.ui.toast.show("Extension loaded successfully!");
    }

    public async Task RegisterUIAsync(IExtensionContext context)
    {
        // Register sidebar item
        context.API.sidebar.Register(
            "My Extension",
            typeof(MyViewModel),
            "Extensions",
            "avares://MyExtension/icon.svg"
        );
        
        // Add topbar button
        context.API.ui.topbar.addButton(
            "avares://MyExtension/button.svg",
            command: new RelayCommand(() => ShowMyOverlay(context))
        );
    }

    private void ShowMyOverlay(IExtensionContext context)
    {
        var overlay = new MyOverlayControl();
        context.API.ui.overlay.Show(overlay);
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
    public Task OnEnableAsync() => Task.CompletedTask;
    public Task OnDisableAsync() => Task.CompletedTask;
}
```

---

## Best Practices

1. **Request Minimal Permissions**: Only request what you need
2. **Handle Errors Gracefully**: Catch exceptions and log appropriately
3. **Clean Up Resources**: Dispose resources in `OnUnloadAsync`
4. **Use Async Methods**: Prefer async methods for better performance
5. **Namespace Storage Keys**: Prefix storage keys with extension name
6. **Validate Input**: Don't trust external data
7. **Use Dependency Injection**: Register services through `IServiceContributor`
8. **Test Thoroughly**: Test with different permission levels

---

## Troubleshooting

### Common Issues

1. **Permission Denied**: Check if extension has required permissions
2. **API Not Available**: Ensure service is registered in DI container
3. **UI Not Showing**: Check if `UIAccess` permission is granted
4. **Storage Errors**: Verify storage key format and scope
5. **Task Failures**: Check task implementation and dependencies

### Debug Tips

- Use `context.Logger` for debugging
- Check extension state in Library module
- Verify permissions in extension settings
- Monitor task progress in Task API
- Use toast notifications for user feedback

---

## API Version

Current API Version: **1.0.0**

The extension API follows semantic versioning:
- **Major**: Breaking changes (require extension updates)
- **Minor**: New features (backward compatible)
- **Patch**: Bug fixes (fully compatible)



