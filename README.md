<img src="https://media2.giphy.com/media/v1.Y2lkPTc5MGI3NjExNmFuYWZwOXFzNHlmOWQzZjJwYWM3czJka2F3dGQweWxkdHk3M3B1MyZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/96bvdlba25M2hrewuc/giphy.gif" width="100%">

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Status](https://img.shields.io/badge/status-In_Development-blue.svg)](https://github.com/ShadowCCS/mnemoapp)
![Framework](https://img.shields.io/badge/framework-.NET%209.0-purple.svg)
![UI](https://img.shields.io/badge/UI-Avalonia%2011-orange.svg)

> <div align="center">Free. Open-source. Cross-platform. A next-generation study application built for learners, by learners.</div>

</div>

## What is Mnemo?

Mnemo is a **fully modular**, cross-platform learning platform powered by **Avalonia 11** and **.NET 9**.  
It combines flashcards, notes, mindmaps, learning paths, games, and more, all in one place.  

> No subscriptions. No ads. No tracking. Ever.

We’ve built Mnemo to be:

- **Extensible** – Every feature is built with modularity in mind.
- **Customizable** – Theme & language system with full localization support. Adjust everything from fonts, colors, font size, etc.
- **Cross-platform** – Windows, macOS, and Linux  
- **Accessible** – Support features designed for inclusivity

## Current Status

Mnemo is under active development and not yet feature-complete.

**What works today:**
- Core application shell and navigation
- Learning paths and units
- Notes and content ingestion
- Local AI chat (offline)
- Theming, settings, and localization infrastructure


## Features (WIP)

- **AI-Powered Text Generation**: Local LLM support via LLamaSharp with CUDA acceleration
- **Knowledge Management**: Vector-based RAG (Retrieval-Augmented Generation) with semantic search and embeddings
- **Learning Paths**: AI-generated personalized learning paths from your knowledge base
- **Rich Text**: Markdown support with custom LaTeX rendering for mathematical expressions
- **Modular Architecture**: Extensible module system with auto-discovery
- **Theming**: Customizable themes
- **Localization**: Multi-language support infrastructure
- **Modules**:
  - **Overview**: Dashboard and welcome screen
  - **Notes**: Rich text note-taking with block-based editor
  - **Chat**: AI-powered conversational interface
  - **Mindmap**: Visual knowledge mapping
  - **Path**: Learning path creation and management
  - **Settings**: Comprehensive application configuration

> Note: Many features are under active development and may be incomplete or disabled in current builds.

### Roadmap
- **Flashcards** with spaced repetition algorithms (Anki, Quizlet...)
- **Text notes** and organization tools
- **Mindmaps** to visualize concepts
- **Learning paths** structured ways to learn
- **Games** powered by the same engine as *Stardew Valley* known as [Monogame](https://monogame.net/)
- **Explore** a place to download games, extensions, themes, languages etc.
- **Progress analytics** track your study progress
- **Extension development** create fully integrated extensions
- **Read Aloud, Audio Review, AI-generated video lessons** (done locally)

## Architecture

Mnemo follows a clean 3-layer architecture:

- **Mnemo.Core**: Interfaces, models, and contracts (zero dependencies)
- **Mnemo.Infrastructure**: Service implementations (AI, storage, knowledge management)
- **Mnemo.UI**: Avalonia-based user interface and presentation layer

## Project Structure

```
MnemoApp/
├── Mnemo.Core/              # Core interfaces and models
│   ├── Models/             # Data models
│   └── Services/           # Service interfaces
├── Mnemo.Infrastructure/   # Service implementations
│   └── Services/          # AI, storage, knowledge services
└── Mnemo.UI/              # User interface
    ├── Components/        # Reusable UI components
    ├── Modules/          # Feature modules
    ├── Services/         # UI-specific services
    └── Themes/           # UI themes and styles
```

## Tech Stack

- **.NET 9.0**: Modern C# runtime
- **Avalonia UI 11.3.6**: Cross-platform UI framework
- **LLamaSharp**: Local LLM inference
- **ONNX Runtime**: Embedding model inference
- **SQLite**: Local data and vector storage
- **Markdig**: Markdown processing
- **CommunityToolkit.Mvvm**: MVVM framework
- **Custom Tools**: Using tons of custom implementations and systems

## Screenshots

<table>
<tr>
<td width="50%">

**Learning Path**

![Path](https://i.ibb.co/prwTRhjq/image-17.png)

</td>
<td width="50%">

**Unit View**

![Unit](https://i.ibb.co/2YMQJd3c/image-19.png)

</td>
</tr>
<tr>
<td width="50%">

**Settings**

![Settings](https://i.ibb.co/cX8cGcMg/image-20.png)

</td>
<td width="50%">

**Account Settings**

![Account Settings](https://i.ibb.co/FLyr03VM/image-21.png)

</td>
</tr>
</table>

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Windows, Linux, or macOS
- For CUDA acceleration (optional): NVIDIA GPU with CUDA 12.x support

### Building

```bash
# Build the solution
dotnet build MnemoApp.sln

# Run the application
cd Mnemo.UI
dotnet run
```

### Installation

Pre-built releases will be available soon. For now, build from source using the instructions above.

## Development

See [coding-standard.md](coding-standard.md) for detailed coding standards and architecture guidelines.

### Key Principles

- **MVVM Pattern**: ViewModels contain logic, Views are declarative
- **Dependency Injection**: All services registered via DI container
- **Async/Await**: All I/O operations are asynchronous
- **Modular Design**: Features implemented as auto-discovering modules
- **Interface-Based**: Services defined by interfaces for testability

> Mnemo prioritizes local-first AI for privacy, offline use, and long-term sustainability.

## Contributing

Contributions are welcome! Please read the [coding standards](coding-standard.md) before submitting pull requests.

If you're new to the project, look for issues labeled `good first issue`.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

> "Education is not the learning of facts, but the training of the mind to think."  
> — Albert Einstein
