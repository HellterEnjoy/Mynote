# Mynote.Avalonia

Desktop note-taking app on **.NET 8 + Avalonia** with project-based storage, editor + Kanban workflow, folders, tags, and quick command palette.

## Roadmap

- See [ROADMAP.md](./ROADMAP.md) for planned future updates.

## Features

- Multi-project workflow with project picker.
- Rich note editing with search and quick open.
- Kanban board with drag-and-drop cards and notes.
- Folder organization and tag filtering.
- Wiki-style links `[[Note Title]]`, linked notes, and backlinks.
- Theme switching (light/dark).
- Autosave modes: on blur, on close, interval.
- Optional project password gate (hash-based, not encryption).

## Tech Stack

- .NET 8
- Avalonia 11.3.9
- ReactiveUI integration

## Getting Started

## Prerequisites

- .NET SDK 8.0+

## Run

```bash
dotnet restore
dotnet run --project Mynote.Avalonia.csproj
```

## Build

```bash
dotnet build -c Release
```

## Project Structure

- `Views/` UI windows and controls.
- `ViewModels/` presentation logic and commands.
- `Models/` domain entities.
- `NoteStore.cs` storage engine for notes/kanban/projects.
- `ProjectRegistry.cs` recently used projects registry.
- `ProjectConfigStore.cs` project-level config and password metadata.
- `AppSettingsStore.cs` app-level preferences.

## Data Storage

By default, app settings and project registry are stored in local application data under `Mynote`.

Project data is stored in the selected project folder:

- `.mynote/config.json` project metadata (projects, columns, folders)
- `.mynote/state.json` UI/workflow state (e.g., last opened note)
- `.mynote/kanban.json` kanban cards
- `notes/*.md` note files with front matter

## Keyboard Shortcuts

- `Ctrl+P` open command palette
- `Ctrl+Shift+F` focus note search
- `Ctrl+Shift+N` create folder
- `Ctrl+K` wrap selection in wiki link `[[...]]`

## Notes

- Current build shows Avalonia deprecation warnings around drag-and-drop API, but builds successfully.
- Password support is an access gate. It does not encrypt project content files.
