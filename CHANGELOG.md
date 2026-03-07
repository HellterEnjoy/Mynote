# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project follows Semantic Versioning.

## [Unreleased]

### Added

- Initial `.gitignore` for public repository hygiene.
- Ignored build artifacts (`bin/`, `obj/`, `out/`).
- Ignored IDE and user-local files (`.vs/`, `.vscode/`, `.idea/`, `*.user`, `*.suo`, `*.rsuser`).
- Ignored NuGet package artifacts and temporary/log/system files.
- Added project documentation file `README.MD` with setup, structure, storage model, and shortcuts.
- Added `CHANGELOG.md`.

### Changed

- Internal refactoring in `NoteStore` without behavior changes.
- Extracted shared persistence/data-reset helpers.
- Consolidated JSON read/write logic.
- Reduced unnecessary allocations in next-order calculations.
- Internal optimization in `CommandPaletteViewModel`.
- Reused compiled tag regex via static field.

### Fixed

- No functional bug fixes in this entry.
