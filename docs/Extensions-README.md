# MnemoApp Extension System

## Overview

The MnemoApp extension system provides a powerful, flexible platform for extending the application with custom functionality. Extensions can add UI components, services, tasks, file processors, and more.

## Features

- **Dual Loading Modes**: Support for both compiled DLL extensions and source-based development
- **Permission System**: Fine-grained control over extension capabilities
- **Full API Access**: Extensions can use the complete MnemoAPI
- **Dependency Injection**: First-class support for DI services
- **Hot Reload**: Source extensions can be reloaded without app restart (future)
- **Package Format**: Installable .mnemoext packages for easy distribution
- **Trust Management**: User-controlled trust decisions with persistent storage

## Architecture

### Core Components

```
Core/Extensions/
├── Models/                        # Data models
│   ├── ExtensionManifest.cs      # manifest.json schema
│   ├── ExtensionMetadata.cs      # Runtime metadata
│   ├── ExtensionPermission.cs    # Permission flags
│   └── ExtensionLoadMode.cs      # Loading strategy
├── Services/                      # Management services
│   ├── IExtensionService.cs      # Extension manager interface
│   └── ExtensionService.cs       # Core lifecycle management
├── Security/                      # Permission system
│   ├── PermissionValidator.cs    # Permission checking
│   ├── PermissionPromptService.cs # User prompts
│   └── TrustStore.cs             # Trust persistence
├── Packaging/                     # Package support
│   ├── ExtensionPackager.cs      # Create .mnemoext files
│   └── ExtensionUnpacker.cs      # Extract and validate
├── IMnemoExtension.cs            # Base extension interface
├── IExtensionContext.cs          # Extension runtime context
├── IUIContributor.cs             # UI capability interface
├── IServiceContributor.cs        # DI capability interface
├── IApiContributor.cs            # API capability interface
├── ExtensionContext.cs           # Context implementation
├── ExtensionLoader.cs            # Assembly loading
├── EXTENSION_DEV.md              # Developer documentation
└── README.md                     # This file
```

### Integration Points

- **ApplicationHost**: Initializes extension system, registers services
- **MnemoAPI**: Exposes ExtensionApi for extension management
- **Library Module**: UI for browsing, installing, managing extensions

## Extension Lifecycle

```
┌──────────────┐
│  Discovered  │ (Extension found on disk)
└──────┬───────┘
       │
       v
┌──────────────┐
│   Loading    │ (Assembly loaded, instance created)
└──────┬───────┘
       │
       v
┌──────────────┐
│    Loaded    │ (OnLoadAsync called)
└──────┬───────┘
       │
       ├─────────────┐
       v             v
┌──────────┐   ┌──────────┐
│ Enabled  │   │ Disabled │
└────┬─────┘   └────┬─────┘
     │              │
     │              │
     v              v
┌──────────────────────┐
│     Unloading        │ (OnUnloadAsync called)
└──────────────────────┘
```

## Permission System

### Permission Types

- **FileAccess**: Read/write files outside extension directory
- **NetworkAccess**: HTTP requests and network operations
- **UIAccess**: Register views, sidebar items, overlays
- **ApiRegistration**: Register custom API endpoints
- **FullTrust**: Complete access (auto-granted to development extensions)

### Trust Levels

- **Untrusted**: Default for new extensions, prompts user
- **Trusted**: User explicitly approved
- **Development**: Extensions in `Modules/Extensions/` (auto full trust)
- **Verified**: Cryptographically signed (future)

### Permission Flow

```
Extension Loads
      │
      v
Has Trust Decision? ──No──> Prompt User
      │                          │
     Yes                         │
      │                          v
      v                    User Approves?
Grant Stored                     │
Permissions              Yes     │    No
      │                   │      │
      └───────────────────┘      v
      │                    Load Fails
      v
Extension Runs
```

## Extension Discovery

Extensions are discovered from:

1. **User Extensions**: `%AppData%/MnemoApp/Extensions/`
   - Installed by users
   - Require permission prompts
   - Can be uninstalled

2. **Bundled Extensions**: `{AppDir}/Modules/Extensions/`
   - Development extensions
   - Full trust by default
   - Cannot be uninstalled (only disabled)

## Package Format

`.mnemoext` files are ZIP archives containing:

```
MyExtension.mnemoext (ZIP)
├── manifest.json           # Required
├── MyExtension.dll         # For compiled extensions
├── Dependencies/           # Optional: Dependencies
│   └── ThirdParty.dll
├── Assets/                 # Optional: Resources
│   ├── icon.png
│   └── styles.xaml
└── README.md              # Optional: Documentation
```

## API Integration

### MnemoAPI Extension Methods

```csharp
// Through MnemoAPI
api.extensions.listInstalled()
api.extensions.install(path)
api.extensions.uninstall(name)
api.extensions.enable(name)
api.extensions.disable(name)
api.extensions.reload(name)
api.extensions.getMetadata(name)
```

### Extension Context API

Extensions receive `IExtensionContext`:

```csharp
public async Task OnLoadAsync(IExtensionContext context)
{
    // Access MnemoAPI
    context.API.ui.toast.show("Hello!");
    
    // Use logger
    context.Logger.LogInfo("Extension loaded");
    
    // Check permissions
    if (context.HasPermission(ExtensionPermission.FileAccess))
    {
        // Do file operations
    }
    
    // Access data directory
    var file = Path.Combine(context.DataDirectory, "config.json");
    
    // Use DI services
    var service = context.Services.GetService<IMyService>();
}
```

## Development Workflow

### Quick Start

1. Create folder in `Modules/Extensions/YourExtension/`
2. Add `manifest.json`
3. Create extension class implementing `IMnemoExtension`
4. Run MnemoApp - extension loads automatically
5. Debug with breakpoints

### Compilation

For compiled extensions:

```bash
# Build your extension
dotnet build YourExtension.csproj

# Create package
dotnet run --project PackageTool -- pack YourExtension/ -o YourExtension.mnemoext
```

### Testing

```csharp
[Fact]
public async Task Extension_Initializes_Correctly()
{
    // Arrange
    var mockContext = new MockExtensionContext();
    var extension = new MyExtension();
    
    // Act
    await extension.OnLoadAsync(mockContext);
    
    // Assert
    Assert.True(extension.IsInitialized);
}
```

## Security Considerations

### Isolation

- Extensions run in-process (not sandboxed)
- Permission system provides soft isolation
- Errors are caught and logged, not propagated

### Best Practices

1. **Request Minimal Permissions**: Only request what you need
2. **Validate Input**: Don't trust external data
3. **Handle Errors**: Catch exceptions, log appropriately
4. **Clean Up**: Dispose resources in OnUnloadAsync
5. **Namespace Keys**: Prefix storage keys with extension name

## Future Enhancements

### Planned Features

- **Source-Based Hot Reload**: Automatic recompilation and reload
- **Code Signing**: Cryptographic verification of extensions
- **Marketplace Integration**: Browse and install from online registry
- **Update System**: Automatic extension updates
- **Sandboxing**: Process-level isolation for untrusted extensions
- **API Versioning**: Better compatibility management
- **Extension Dependencies**: Automatic dependency resolution

### API Evolution

The extension API follows semantic versioning:
- **Major**: Breaking changes (require extension updates)
- **Minor**: New features (backward compatible)
- **Patch**: Bug fixes (fully compatible)

Current API Version: **1.0.0**

## Examples

### Minimal Extension

```csharp
using MnemoApp.Core.Extensions;

public class MinimalExtension : IMnemoExtension
{
    public async Task OnLoadAsync(IExtensionContext context)
    {
        context.Logger.LogInfo("Hello from minimal extension!");
        await Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
    public Task OnEnableAsync() => Task.CompletedTask;
    public Task OnDisableAsync() => Task.CompletedTask;
}
```

### Full-Featured Extension

See `Modules/Extensions/ExampleExtension/` for comprehensive example.

## Troubleshooting

### Extension Not Loading

Check Debug output for errors:
```
[EXT_SERVICE] Failed to load extension: <error message>
```

Common issues:
- Invalid manifest.json
- Missing mainClass or entryPoint
- API version mismatch
- Missing dependencies

### Permission Issues

Extensions requiring permissions will prompt on first load. Check:
- Permission requested in manifest
- User approved the prompt
- Trust level in Library module

### Performance

If extensions cause slowdowns:
- Profile with dotTrace/PerfView
- Avoid heavy operations in lifecycle hooks
- Use background tasks for long operations
- Dispose resources properly

## Support

- **Documentation**: `EXTENSION_DEV.md` (comprehensive developer guide)
- **Examples**: `Modules/Extensions/` (sample implementations)
- **API Reference**: Code documentation (XML comments)
- **Community**: GitHub Discussions

## Contributing

To contribute to the extension system:

1. Fork the repository
2. Create feature branch
3. Add tests for new features
4. Update documentation
5. Submit pull request

Extension system PRs should update:
- Code implementation
- Unit tests
- EXTENSION_DEV.md (if API changes)
- This README (if architecture changes)

