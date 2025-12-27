# MnemoApp Rewrite Plan

This documentation covers the migration and upgrade plan from MNEMOAPP -> mnemo-new

The coding-standard.md is important to understand what to do and not do

## 1. Core Architecture (Clean Architecture)

**Goal:** strict separation of concerns, lightweight core, and modular extensibility.

### 1.1 Project Breakdown

-   **`MnemoApp.Core`**:
    -   **Role:** The "Contract". Interfaces, Models, Enums only. No dependencies.
    -   **Key Interfaces:** `IModule`, `ITaskScheduler`, `IFunctionRegistry`, `IStorageProvider`, `ILoggerService`.
-   **`MnemoApp.Infrastructure`**:
    -   **Role:** The "Implementation".
    -   **Contents:**
        -   `SqliteStorageProvider` (Data persistence).
        -   `MnemoPackageHandler` (ZIP/.mnemo export/import).
        -   `TaskSchedulerService` (Background job management).
        -   `AIService` (LLM integration).
        -   `FileProcessingService`.
        -   `LoggerService` (Centralized logging with file/console sinks).
-   **`MnemoApp.UI`**:
    -   **Role:** The "Shell". Avalonia Project.
    -   **Components:** `TaskDashboard` (Overlay), `ShellWindow`, `NavigationHost`.

## 2. The Task System

**Refactored for Performance & Visibility:**

-   **Execution Modes:**
    -   `Parallel`: For light I/O tasks.
    -   `Exclusive`: For Local AI generation (pauses others).
-   **UI Representation:**
    -   **Status Bar:** Shows summary (e.g., "Processing files... (2/5)").
    -   **Task Overlay:** A collapsible panel showing active tasks, progress bars, and cancel buttons.

## 3. Data & Storage (`.mnemo`)

**Standardized Portable Format:**

-   **Structure:**
    ```text
    / (Root of .mnemo zip)
    ├── manifest.json   (Metadata: Version, Type, CreatedAt)
    ├── data.db         (SQLite fragment or JSON - portable data)
    └── assets/         (Images, PDF attachments, etc.)
    ```

-   **Service:** `IPackageManager`.

## 4. Modularity & Auto-Discovery

**No More Manual Registration:**

-   **The `IModule` Interface:**
    ```csharp
    public interface IModule {
        void ConfigureServices(IServiceCollection services);
        void RegisterRoutes(INavigationRegistry registry);
        void RegisterTools(IFunctionRegistry registry);
    }
    ```

-   **Startup Logic:**
    -   Scan `AppDomain` and `Modules/` folder for `IModule` implementations.

## 5. Logging Service (New)

**Centralized diagnostics for Core and Modules:**

-   **Interface:** `ILoggerService` in Core.
-   **Features:**
    -   Categorized logging (e.g., "AI", "Storage", "UI").
    -   Log levels: Debug, Info, Warning, Error, Critical.
    -   Sinks: File (for bug reports) and Console/Debug (for development).
-   **Integration:** Injected into all services and modules to replace `Debug.WriteLine`.

## 6. Functional Tool Registry (AI Capabilities)

**Extending AI without touching Core:**

-   **Concept:** AI needs "Tools" (functions) to act.
-   **Implementation:** Modules register `AIToolDefinition`s in `IFunctionRegistry`.

## 7. UI & Navigation

-   **Navigation:** Route-based (`Navigate("settings")`) to decouple ViewModels.
-   **Components:** Strict separation of styles and functional controls.

## 8. Migration Roadmap

1.  **Setup Solution:** Create the 3-project structure (`Core`, `Infrastructure`, `UI`).
2.  **Core Contracts:** Define `IModule`, `ITaskScheduler`, `IStorageProvider`, `ILoggerService`.
3.  **Infrastructure Port:** Move `TaskScheduler`, `FileProcessing`, and implement `LoggerService`.
4.  **Storage Engine:** Implement `SqliteStorageProvider` and `MnemoPackageHandler`.
5.  **The Shell:** Build `MnemoApp.UI` with the new Auto-Discovery Bootstrapper.
6.  **Module Migration:** Port `Dashboard` and `Notes` as standard `IModule`s.