# LiteSnap — Agent Guide

## Overview

LiteSnap is a snapshot/backup manager for Minecraft saves (and any folder). It stores file-level snapshots using content-addressable storage (SHA-512) with a LiteDB index.

## Build

```bash
dotnet build
dotnet run --project src/LiteSnap.App
```

## Project Structure

```
src/
├── LiteSnap.Core/          # Class library — no UI dependency
│   ├── IO/HashStorage.cs   # Content-addressable file store
│   ├── Models/             # VersionData, FileVersionObjects, ObjectType, FileVersionObjectsComparer
│   └── SnapLiteManager.cs  # Full read/write snapshot management
└── LiteSnap.App/           # Avalonia desktop app
    ├── ViewModels/         # MainWindowViewModel (CommunityToolkit.Mvvm)
    ├── Views/              # MainWindow.axaml (WinUI3-style toolbar layout)
    ├── App.axaml / Program.cs
    └── LiteSnap.App.csproj
```

## Conventions

- **Nullable enabled** everywhere. Use nullable annotations (`?`, `!`) correctly.
- **File-scoped namespaces** (`namespace X.Y;`).
- **Primary constructors** preferred for simple classes.
- **No regions** (`#region`). Use comment headers (`// ── X ──`) for section grouping.
- **Async**: `async Task` for I/O, `ConfigureAwait(false)` on library code, not in UI layer.
- **MVVM**: `[ObservableProperty]` for bindable fields, `[RelayCommand]` for commands.
- Use `Collection<T>` over `List<T>` for public bindable APIs.
- SCREAMING_SNAKE for `private const` fields.

## Key Types

| Type | Location | Purpose |
|---|---|---|
| `SnapLiteManager` | Core | Entry point: read/write/export snapshots |
| `HashStorage` | Core | Get/Put/Delete files by SHA-512 hash |
| `VersionData` | Core.Models | Snapshot metadata (NodeId, Name, Created, Desc) |
| `FileVersionObjects` | Core.Models | Single file/dir entry (Path, Hash, Length, ObjectType) |
| `ObjectType` | Core.Models | `File` / `Directory` |
| `MainWindowViewModel` | App | UI state & commands |

## Data Storage Layout

```
.litesnap/
├── index.db          # LiteDB — VersionData table ("index"), per-version tables ("node_{guid}")
└── objects/
    └── {hash[..2]}/  # Prefix folder (first 2 hex chars of SHA-512)
        └── {hash}    # Raw file content
```

- `HashStorage` computes SHA-512 of file contents.
- `LiteDB` stores the version index and per-version file object lists.
- `SnapLiteManager.Initialize(rootPath)` creates a fresh `.litesnap` structure.

## Testing

No tests yet. When adding tests:
- Unit test `SnapLiteManager` methods with a temp folder + LiteDB in-memory.
- Use `HashStorage` with a temp directory.
- No Mock frameworks unless necessary.
