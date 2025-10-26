# Extension Developer Guide

## Overview

MnemoApp's extension system allows you to extend the application with custom functionality, UI components, services, and more. Extensions can be developed as either compiled DLLs or source-based projects.

## Quick Start

### 1. Create Extension Structure

```
MyExtension/
├── manifest.json          # Required: Extension metadata
├── MyExtension.cs        # Your main extension class
├── icon.png              # Optional: Extension icon
└── README.md             # Optional: Documentation
```

### 2. Create Manifest

Create `manifest.json` with required fields:

```json
{
  "name": "MyExtension",
  "version": "1.0.0",
  "apiVersion": "1.0.0",
  "displayName": "My Amazing Extension",
  "description": "Does amazing things",
  "author": "Your Name",
  "mainClass": "MyNamespace.MyExtension",
  "permissions": [
    "UIAccess",
    "FileAccess"
  ]
}
```

### 3. Implement Extension Class

```csharp
using System.Threading.Tasks;
using MnemoApp.Core.Extensions;

namespace MyNamespace
{
    public class MyExtension : IMnemoExtension
    {
        private IExtensionContext? _context;

        public async Task OnLoadAsync(IExtensionContext context)
        {
            _context = context;
            context.Logger.LogInfo("Extension loading...");
            
            // Initialize your extension here
            
            await Task.CompletedTask;
        }

        public async Task OnUnloadAsync()
        {
            // Clean up resources
            await Task.CompletedTask;
        }

        public async Task OnEnableAsync()
        {
            // Extension is enabled
            await Task.CompletedTask;
        }

        public async Task OnDisableAsync()
        {
            // Extension is disabled
            await Task.CompletedTask;
        }
    }
}
```

### 4. Install Extension

- Copy your extension folder to `%AppData%/MnemoApp/Extensions/`
- Or for development: place in `Modules/Extensions/`
- Restart MnemoApp or use the Library module to reload

## Manifest Schema

### Required Fields

- **name** (string): Unique identifier (alphanumeric, hyphens, underscores)
- **version** (string): Semantic version (e.g., "1.0.0")
- **apiVersion** (string): Required MnemoApp API version

### Optional Fields

- **displayName** (string): Human-readable name
- **description** (string): Short description
- **author** (string): Author name or organization
- **website** (string): Project website or repository URL
- **license** (string): License identifier (e.g., "MIT", "Apache-2.0")
- **icon** (string): Path to icon file (relative to extension folder)
- **entryPoint** (string): Main DLL file name (for compiled extensions)
- **mainClass** (string): Fully qualified name of main extension class
- **permissions** (array): Requested permissions (see Permissions section)
- **dependencies** (object): Other extension dependencies (name: version)
- **tags** (array): Keywords for categorization
- **changelog** (string): Release notes
- **loadMode** (string): "CompiledAssembly" or "SourceBased" (auto-detected if omitted)

## Extension Lifecycle

### Lifecycle Hooks

1. **OnLoadAsync**: Called once when extension is first loaded
   - Initialize resources, load configuration
   - Access to extension context and MnemoAPI

2. **OnEnableAsync**: Called when extension is enabled
   - Register UI components, start background tasks
   - Called after OnLoadAsync for enabled extensions

3. **OnDisableAsync**: Called when extension is disabled
   - Stop background tasks, hide UI
   - Extension remains loaded but inactive

4. **OnUnloadAsync**: Called when extension is unloaded
   - Clean up all resources, save state
   - Release memory, close connections

5. **OnUpdateAsync**: Optional, called periodically
   - Perform background updates
   - Not called by default (future feature)

### Extension Context

The `IExtensionContext` provides access to:

```csharp
public interface IExtensionContext
{
    ExtensionMetadata Metadata { get; }     // Extension info
    IMnemoAPI API { get; }                   // MnemoAPI access
    IServiceProvider Services { get; }       // DI services
    IExtensionLogger Logger { get; }         // Logging
    string DataDirectory { get; }            // Extension data folder
    string StoragePrefix { get; }            // Storage key prefix
    
    bool HasPermission(ExtensionPermission permission);
    Task<bool> RequestPermissionAsync(ExtensionPermission permission);
}
```

## Capabilities

### UI Contributions

Implement `IUIContributor` to add UI elements:

```csharp
public class MyExtension : IMnemoExtension, IUIContributor
{
    public async Task RegisterUIAsync(IExtensionContext context)
    {
        // Register sidebar item
        context.API.sidebar.Register(
            "My View",
            typeof(MyViewModel),
            "My Category",
            "avares://MyExtension/icon.svg"
        );
        
        // Show overlay
        context.API.ui.overlay.show(myControl);
        
        // Show toast notification
        context.API.ui.toast.show("Hello from extension!");
    }
}
```

### Service Contributions

Implement `IServiceContributor` to register services:

```csharp
public class MyExtension : IMnemoExtension, IServiceContributor
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IMyService, MyService>();
        services.AddTransient<MyViewModel>();
    }
}
```

**Note**: Service registration happens during application startup, before the service provider is built.

### API Contributions

Implement `IApiContributor` to expose custom APIs:

```csharp
public class MyExtension : IMnemoExtension, IApiContributor
{
    public async Task RegisterApiAsync(IExtensionContext context)
    {
        // Register API endpoint
        // Future: Register with API router
    }
    
    public async Task<object?> HandleApiCallAsync(string endpoint, object? parameters)
    {
        return endpoint switch
        {
            "getData" => GetData(),
            "processInput" => ProcessInput(parameters),
            _ => null
        };
    }
}
```

### Tasks

Extensions can schedule tasks using the MnemoAPI:

```csharp
public class MyCustomTask : MnemoTaskBase
{
    public MyCustomTask() 
        : base("My Task", "Does something", TaskPriority.Normal, TaskExecutionMode.Parallel)
    {
    }

    protected override async Task<TaskResult> ExecuteTaskAsync(
        IProgress<TaskProgress> progress, 
        CancellationToken cancellationToken)
    {
        progress.Report(new TaskProgress(0.5, "Working..."));
        
        // Do work here
        
        return new TaskResult(true, "Success!");
    }
}

// Schedule task
var task = new MyCustomTask();
var taskId = context.API.tasks.scheduleTask(task);
context.API.ui.toast.showForTask(taskId, showProgress: true);
```

### File Processors

Register custom file processors:

```csharp
public class MyFileProcessor : IFileProcessor
{
    public string ProcessorName => "My Processor";
    public string[] SupportedExtensions => new[] { ".myext" };
    
    public bool CanHandle(string fileExtension)
    {
        return fileExtension.Equals(".myext", StringComparison.OrdinalIgnoreCase);
    }
    
    public async Task<FileProcessingResult> ProcessAsync(
        string filePath, 
        CancellationToken cancellationToken)
    {
        // Process file and return result
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return FileProcessingResult.CreateSuccess(content, Path.GetFileName(filePath), ".myext", new FileInfo(filePath).Length);
    }
}

// Register in OnLoadAsync
context.API.files.registerProcessor(new MyFileProcessor());
```

### Storage

Extensions have access to:

1. **Runtime Storage** (key-value, persisted):
```csharp
// Scoped to extension
context.API.data.SetProperty($"{context.StoragePrefix}mykey", myValue);
var value = context.API.data.GetProperty<MyType>($"{context.StoragePrefix}mykey");
```

2. **Data Directory** (files):
```csharp
var dataFile = Path.Combine(context.DataDirectory, "mydata.json");
await File.WriteAllTextAsync(dataFile, json);
```

## Permissions

### Available Permissions

- **FileAccess**: Read/write files outside extension directory
- **NetworkAccess**: Make network requests
- **UIAccess**: Register UI views, sidebar items, overlays
- **ApiRegistration**: Register custom API endpoints
- **FullTrust**: Unrestricted access (granted to development extensions)

### Requesting Permissions

In manifest.json:
```json
{
  "permissions": [
    "FileAccess",
    "UIAccess"
  ]
}
```

Runtime permission request:
```csharp
if (!context.HasPermission(ExtensionPermission.NetworkAccess))
{
    var granted = await context.RequestPermissionAsync(ExtensionPermission.NetworkAccess);
    if (!granted)
    {
        // Handle denial
    }
}
```

### Permission Levels

- **Untrusted**: User has not made trust decision, will be prompted
- **Trusted**: User approved the extension
- **Development**: Extensions in `Modules/Extensions/` (full trust)
- **Verified**: Signed extensions (future)

## Packaging and Distribution

### Creating a Package

Extensions can be distributed as `.mnemoext` files (ZIP archives):

```csharp
var packager = new ExtensionPackager();
await packager.PackageExtensionAsync(
    "path/to/extension",
    "MyExtension.mnemoext"
);
```

### Package Contents

A `.mnemoext` package contains:
- manifest.json (required)
- DLL files (for compiled extensions)
- Assets (icons, resources)
- README.md (optional)

### Installation

Users can install extensions via:
1. Library module → Install Extension button
2. Drag-and-drop .mnemoext file
3. Via API: `api.extensions.install("path/to/package")`

## Best Practices

### 1. Namespace Your Storage Keys
```csharp
// Good
context.API.data.SetProperty($"{context.StoragePrefix}settings", settings);

// Bad (could conflict with other extensions)
context.API.data.SetProperty("settings", settings);
```

### 2. Handle Errors Gracefully
```csharp
public async Task OnLoadAsync(IExtensionContext context)
{
    try
    {
        // Your code
    }
    catch (Exception ex)
    {
        context.Logger.LogError("Failed to initialize", ex);
        // Don't throw - let extension continue in degraded state
    }
}
```

### 3. Clean Up Resources
```csharp
public async Task OnUnloadAsync()
{
    // Dispose resources
    _httpClient?.Dispose();
    
    // Save state
    await SaveSettingsAsync();
    
    // Unsubscribe from events
    _service.SomeEvent -= OnSomeEvent;
}
```

### 4. Check Permissions Before Use
```csharp
if (context.HasPermission(ExtensionPermission.FileAccess))
{
    // Access files
}
else
{
    context.Logger.LogWarning("File access not granted");
}
```

### 5. Use Dependency Injection
```csharp
public class MyExtension : IMnemoExtension
{
    private readonly IAIService _aiService;
    
    // Constructor injection works if service is registered
    public MyExtension(IAIService aiService)
    {
        _aiService = aiService;
    }
}
```

### 6. Log Appropriately
```csharp
context.Logger.LogDebug("Verbose information for debugging");
context.Logger.LogInfo("General information");
context.Logger.LogWarning("Something unexpected but not critical");
context.Logger.LogError("Error occurred", exception);
```

## Development Workflow

### Local Development

1. Create extension folder in `Modules/Extensions/`
2. Add manifest.json
3. Create extension class
4. Build and run MnemoApp
5. Extension loads automatically with full trust

### Hot Reload (Future)

Source-based extensions in development mode will support hot reload:
- Modify source files
- Changes detected automatically
- Extension reloaded without restart

### Debugging

1. Set breakpoints in your extension code
2. Launch MnemoApp in debug mode
3. Breakpoints hit when extension loads/executes

### Testing

```csharp
// Unit tests
[Fact]
public async Task Extension_Loads_Successfully()
{
    var context = new MockExtensionContext();
    var extension = new MyExtension();
    
    await extension.OnLoadAsync(context);
    
    Assert.True(context.IsInitialized);
}
```

## API Reference

### MnemoAPI Modules

- **navigate**: Navigation service
- **sidebar**: Sidebar management
- **ui**: UI operations (toasts, overlays, themes, language)
- **system**: System information
- **data**: Runtime storage
- **storage**: Packaged storage
- **ai**: AI model management and inference
- **tasks**: Task scheduling
- **files**: File processing
- **latex**: LaTeX rendering
- **settings**: App settings
- **extensions**: Extension management

See individual API documentation for detailed methods.

## Security Considerations

### Sandboxing

Extensions run in the same process as MnemoApp but:
- Permissions control access to sensitive APIs
- Trust store tracks user decisions
- Errors are caught and isolated

### User Trust

- First-time extensions prompt for permissions
- Users can revoke permissions anytime
- Development extensions bypass prompts

### Code Signing (Future)

Verified extensions will be:
- Signed with developer certificate
- Verified by MnemoApp
- Granted higher trust level

## Examples

See `Modules/Extensions/ExampleExtension/` for a comprehensive example demonstrating:
- Extension lifecycle
- UI contributions
- Permission handling
- Storage usage
- Logging

## Troubleshooting

### Extension Won't Load

- Check manifest.json is valid JSON
- Verify mainClass matches your class name
- Check logs in Debug output
- Ensure API version is compatible

### Permission Denied

- Check manifest requests the permission
- Verify user granted the permission
- Use `context.HasPermission()` to check

### Service Not Found

- Ensure service is registered in DI
- Check service is available at extension load time
- Use `IServiceContributor` to register custom services

## Support

- GitHub: https://github.com/mnemoapp/mnemoapp
- Documentation: https://mnemoapp.dev/docs/extensions
- Community: https://discord.gg/mnemoapp

