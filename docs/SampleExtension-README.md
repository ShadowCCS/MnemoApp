# Sample Extension

A comprehensive example extension for MnemoApp that demonstrates the full capabilities of the extension system.

## Features Demonstrated

### Core Extension System
- **Extension Lifecycle**: Proper implementation of `OnLoadAsync`, `OnEnableAsync`, `OnDisableAsync`, and `OnUnloadAsync`
- **Service Registration**: Uses `IServiceContributor` to register view models
- **UI Contributions**: Implements `IUIContributor` for sidebar and topbar integration
- **Permissions**: Requests appropriate permissions in manifest

### UI Integration
- **Sidebar Registration**: Appears in the "Extensions" category
- **Topbar Button**: Quick access button with custom icon
- **Custom View**: Full-featured view with modern UI design
- **Overlay System**: Demonstrates custom overlay creation

### API Capabilities
- **Toast Notifications**: Shows various types of toast messages
- **Task System**: Creates and schedules custom tasks with progress tracking
- **Data Storage**: Uses extension-scoped storage for persistence
- **AI Integration**: Tests AI model availability and inference
- **Navigation**: Integrates with the main navigation system

### Task Management
- **Custom Task**: `SampleTask` demonstrates task creation and execution
- **Progress Tracking**: Real-time progress updates with toast notifications
- **Task History**: Displays recent tasks in the UI
- **Error Handling**: Proper error handling and user feedback

## File Structure

```
SampleExtension/
├── manifest.json              # Extension metadata and permissions
├── SampleExtension.cs         # Main extension class
├── SampleExtensionViewModel.cs # View model with business logic
├── SampleExtensionView.axaml  # UI definition
├── SampleExtensionView.axaml.cs # Code-behind
├── Tasks/
│   └── SampleTask.cs          # Custom task implementation
├── storage/
│   └── user_data.json         # Extension data directory
└── README.md                  # This documentation
```

## Usage

1. The extension automatically loads when MnemoApp starts
2. Navigate to the "Extensions" section in the sidebar
3. Click on "Sample Extension" to open the main view
4. Use the various buttons to test different API capabilities
5. Check the console output for detailed logging information

## API Examples

### Scheduling a Task
```csharp
var task = new SampleTask(storage, "Input data");
var taskId = context.API.tasks.scheduleTask(task);
context.API.ui.toast.showForTask(taskId, showProgress: true);
```

### Showing a Toast
```csharp
context.API.ui.toast.show("Title", "Message", ToastType.Success);
```

### Using Storage
```csharp
context.API.data.SetProperty($"{context.StoragePrefix}key", value);
var retrieved = context.API.data.GetProperty<string>($"{context.StoragePrefix}key");
```

### AI Integration
```csharp
var models = await context.API.ai.GetAllModelsAsync();
var request = context.API.ai.CreateRequest(modelName, prompt);
var response = await context.API.ai.InferAsync(request);
```

## Development Notes

- The extension uses source-based loading (`loadMode: "SourceBased"`)
- All storage keys are properly namespaced with the extension prefix
- Error handling is implemented throughout for robustness
- The UI follows MnemoApp's design patterns and theming
- Logging is used extensively for debugging and monitoring

## Permissions Required

- `UIAccess`: For sidebar registration and UI components
- `FileAccess`: For accessing the extension's data directory
- `ApiRegistration`: For potential future API contributions

This extension serves as a comprehensive reference for developing MnemoApp extensions and demonstrates best practices for integration with the extension system.
