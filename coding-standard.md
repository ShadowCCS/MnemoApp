# MnemoApp Coding Standards

## Naming Conventions

### DO
- Use PascalCase for classes, interfaces, methods, properties, and public members
- Prefix interfaces with `I` (e.g., `IAIService`, `INavigationService`)
- Suffix async methods with `Async` (e.g., `InitializeAsync`, `LoadDataAsync`)
- Use descriptive, self-documenting names
- Use singular nouns for classes (e.g., `TaskScheduler`, not `TaskSchedulers`)

### DON'T
- Use abbreviations unless widely understood (e.g., `ctx` is acceptable, `mgr` is not)
- Use Hungarian notation (e.g., `strName`, `intCount`)
- Use underscores in public identifiers (private fields may use `_` prefix)

## Architecture Principles

### DO
- Prefer lightweight, modular, and future-proof architecture
- Separate concerns: Core (interfaces), Infrastructure (implementations), UI (presentation)
- Use Dependency Injection for all service dependencies
- Design for extensibility: modules should auto-discover and register themselves
- Keep Core layer dependency-free (interfaces and models only)
- Use interfaces for all services to enable testing and swapping implementations

### DON'T
- Create "God Objects" that do everything (e.g., avoid monolithic `IMnemoAPI`)
- Hard-code module registration in startup code
- Mix business logic with UI code
- Create circular dependencies between layers

## Code Organization

### DO
- Group related functionality into namespaces:
  - `Mnemo.Core.Services` for service interfaces
  - `Mnemo.Core.Models` for data models
  - `Mnemo.Infrastructure` for implementations
  - `Mnemo.UI.Components` for reusable UI components
- Keep files focused: one class/interface per file
- Use `partial` classes only when necessary (e.g., code-behind files)

### DON'T
- Put implementations in the Core layer
- Mix UI components with business logic
- Create deep namespace hierarchies (max 3-4 levels)

## Async/Await

### DO
- Use `async`/`await` for all I/O operations
- Return `Task` or `Task<T>` from async methods
- Use `CancellationToken` parameters in long-running operations
- Use `ConfigureAwait(false)` in library code when appropriate

### DON'T
- Block async code with `.Result` or `.Wait()`
- Mix async and sync code without careful consideration
- Ignore cancellation tokens

## Error Handling

### DO
- Use exceptions for exceptional circumstances
- Return `Result<T>` or `bool` for expected failure cases
- Log errors with context (use structured logging when possible)
- Handle exceptions at appropriate boundaries (UI, service boundaries)

### DON'T
- Swallow exceptions silently
- Use exceptions for control flow
- Catch `Exception` without re-throwing or logging

## UI Guidelines

### DO
- Use MVVM pattern: ViewModels contain logic, Views are declarative
- Bind to ViewModels, not directly to services
- Use Avalonia's built-in controls when possible
- Create custom controls only when adding new functionality (not just styling)
- Use styles/themes for visual customization, not subclassing controls

### DON'T
- Put business logic in code-behind files
- Create custom controls just to change colors/borders (use styles instead)
- Hard-code strings in UI (use localization)
- Block the UI thread with long-running operations

## Storage & Data

### DO
- Use SQLite for runtime data
- Use `.mnemo` ZIP format for portable exports
- Separate runtime storage from packaged storage
- Version your data schemas

### DON'T
- Store large binary data in SQLite (use file system with references)
- Mix runtime and packaged data without clear boundaries
- Create storage without migration paths

## Extensibility

### DO
- Implement `IModule` interface for auto-discovery
- Register services, routes, and tools in module initialization
- Use `IFunctionRegistry` for AI tool registration
- Design APIs that extensions can consume

### DON'T
- Require manual registration of modules
- Create tight coupling between modules
- Expose internal implementation details to extensions

## Performance

### DO
- Profile before optimizing
- Use lazy loading for heavy resources
- Cache expensive computations
- Use appropriate data structures (e.g., `ConcurrentDictionary` for thread-safe lookups)

### DON'T
- Prematurely optimize
- Load all data at startup
- Create memory leaks (unsubscribe from events, dispose resources)

## Testing

### DO
- Write unit tests for business logic
- Mock dependencies using interfaces
- Test edge cases and error conditions

### DON'T
- Test implementation details
- Create tests that depend on external resources without mocking
- Skip testing error paths

## Documentation

### DO
- Add XML documentation comments for public APIs
- Document complex algorithms and business rules
- Keep README files updated

### DON'T
- Document obvious code (e.g., `// Sets the name`)
- Leave TODO comments without tracking issues
- Write documentation that duplicates the code