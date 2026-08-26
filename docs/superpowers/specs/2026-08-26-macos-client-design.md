# macOS Client Design

## Goal

Provide a native macOS build of Codex Conversation Manager while keeping the existing Windows WPF application unchanged and sharing the established conversation management logic.

## Architecture

Add a new `CodexConversationManager.Mac` Avalonia desktop project targeting .NET 8. It references `CodexConversationManager.Core` and builds its inventory from the same scanner, SQLite readers, index reader, and classifier used by the Windows client. The WPF project remains Windows-only.

The macOS screen supports the same local workflow: choose the Codex home folder, refresh the inventory, browse categories, select and copy metadata/source paths, export Markdown, import multiple JSONL files (regenerating duplicate IDs), make manual backups, delete selected local conversations, and synchronize providers. Delete and provider synchronization require Codex to be fully closed. The window can stop and reopen Codex using macOS process handling and `open -a Codex`.

## Data And Safety

The macOS client operates exclusively on the user-selected local Codex root. Destructive operations are scoped to selected known session records and reuse the Core deletion plan, which blocks parent records with descendants. Import, deletion, and provider synchronization must be performed only after Codex has completely exited.

## Packaging

Windows builds publish to the stable `publish` directory and Inno Setup overwrites `installer-output/CodexConversationManager-Setup.exe`.

macOS publishing targets `osx-arm64` and `osx-x64` into stable `publish-macos-arm64` and `publish-macos-x64` directories. A `tools/package-macos.ps1` script validates the published app host and assembles an unsigned `.app` bundle for each architecture. DMG creation, code signing, notarization, and real launch verification require a macOS host and Apple developer credentials, so they are explicitly outside Windows-only verification.

## UI

Use Avalonia controls with a dense three-column manager layout and commands for refresh, provider sync, Markdown export, import, exit/restart Codex, backup, and delete:

- left: category buttons and folder/project tree;
- center: selectable conversation list;
- right: metadata, original session file path, and safe read-only status.

The initial screen uses English identifiers in code and Chinese UI copy. The language selector is present; full localized Avalonia resources remain a follow-up because the WPF localization markup cannot be shared directly.

## Testing

Unit tests cover the macOS inventory composition using temp Codex roots and verify that refresh does not write to the supplied root. Full test verification runs the existing suite. macOS compile and bundle verification are performed using the .NET SDK with both runtime identifiers; final launch, signing, notarization, and DMG verification require macOS.
