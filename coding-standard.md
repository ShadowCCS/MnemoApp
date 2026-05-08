# Mnemo Coding Standard

This file is the source of truth for project conventions.  

## Priority Rules

1. Architecture boundary is strict: `Core` (interfaces/models only, zero implementation deps), `Infrastructure` (implementations), `UI` (presentation).
2. UI uses MVVM: logic in ViewModels/services, not in views/code-behind.
3. Service dependencies use DI and interfaces.
4. Async I/O must be truly async (`Task`/`Task<T>`, cancellation support, no `.Result`/`.Wait()`).
5. Do not swallow exceptions.
6. For Avalonia layout controls: `StackPanel` and `Grid` do not get `Padding` or `CornerRadius`; use `Margin` or wrap in `Border`.

## Naming

### Required
- PascalCase for classes, interfaces, methods, properties, and public members.
- Prefix interfaces with `I` (example: `IAIService`).
- Suffix async methods with `Async` (example: `LoadDataAsync`).
- Use singular class names (example: `TaskScheduler`).
- Use descriptive names.

### Avoid
- Non-standard abbreviations (acceptable: common ones like `ctx`; avoid `mgr`).
- Hungarian notation (`strName`, `intCount`).
- Underscores in public identifiers (private fields may use `_fieldName`).

## Architecture and Layering

### Required
- Keep `Mnemo.Core` dependency-light and implementation-free.
- Put implementation code in `Mnemo.Infrastructure`.
- Keep presentation concerns in `Mnemo.UI`.
- Keep modules extensible via `IModule` auto-discovery.
- Register extension tools through `IFunctionRegistry`.

### Avoid
- God objects.
- Circular dependencies between layers.
- Hard-coded module registration.
- Leaking implementation details across boundaries.

## Code Organization

### Required
- One class/interface per file unless there is a strong reason.
- Use focused namespaces (typical patterns):
  - `Mnemo.Core.Services`
  - `Mnemo.Core.Models`
  - `Mnemo.Infrastructure.*`
  - `Mnemo.UI.Components.*`
- Keep namespace depth reasonable (typically <= 4 levels).
- Use `partial` only where it naturally belongs (for example, code-behind patterns).

## Async, Concurrency, and Task Execution

### Required
- Use `async`/`await` for all I/O and long-running async workflows.
- Accept `CancellationToken` for cancellable/long operations.
- Use `ConfigureAwait(false)` in non-UI/library code when appropriate.
- Use `TaskExecutionMode.Exclusive` for resource-heavy tasks (for example local AI inference).
- Use `TaskExecutionMode.Parallel` for lightweight independent I/O tasks.
- Support progress reporting when useful (`IProgress<T>`).

### Avoid
- Blocking waits on async (`.Result`, `.Wait()`).
- Ignoring cancellation once token is passed in.

## Error Handling

### Required
- Throw exceptions for exceptional failures.
- Use `Result<T>` or `bool` for expected non-exception flow.
- Log with context (prefer structured logging).
- Handle exceptions at clear boundaries (UI, service/API boundary).

### Avoid
- Silent catches.
- Exceptions as control flow.
- Catching broad `Exception` without handling and logging context.

## UI and Avalonia

### Required
- Prefer built-in Avalonia controls.
- Bind to dynamic theme values (brushes).
- Keep views declarative and bind to ViewModels.
- Localize UI strings (no hard-coded user-facing text).
- Keep UI thread responsive.

### Avoid
- Business logic in code-behind.
- Creating custom controls when styles/templates are enough.
- Applying unsupported layout styling:
  - Do not set `Padding` on `StackPanel` or `Grid`.
  - Do not set `CornerRadius` on `StackPanel` or `Grid`.
  - Instead: set `Margin` and/or wrap with `Border`.

## Storage and Data

### Required
- SQLite for runtime data.
- `.mnemo` ZIP for portable exports.
- Separate runtime storage from packaged/export storage.
- Version schemas and keep migration paths.

### Avoid
- Large binary payloads in SQLite (store files and reference paths/ids).
- Mixing runtime and packaged concerns.

## Performance and Lifecycle

### Required
- Profile before optimizing.
- Lazy-load heavy resources.
- Cache expensive computations where justified.
- Use correct concurrent data structures (`ConcurrentDictionary`, etc.).
- Prevent leaks (unsubscribe events, dispose resources).

### Avoid
- Premature optimization.
- Loading all data at startup by default.

## Testing

### Required
- Unit test business logic.
- Mock dependencies through interfaces.
- Cover edge cases and failure paths.

### Avoid
- Tests coupled to implementation details.
- Tests requiring external services without test doubles.

## Documentation

### Required
- XML docs for public APIs where useful.
- Explain complex business rules and non-obvious algorithms.
- Keep module-level docs current.

### Avoid
- Comments that restate obvious code.
- Untracked TODOs.
- Docs that duplicate code without adding intent.