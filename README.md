# Codex Manager

<p align="center">
  <img src="assets/codex-manager-cover.png" alt="Codex Manager" width="360">
</p>

[中文说明](README-%E4%BD%BF%E7%94%A8%E8%AF%B4%E6%98%8E.md)

A local conversation manager for Codex Desktop. It helps you inspect, organize, back up, import, export, and manage locally stored Codex conversations.

> Chinese product name: **Codex 对话管理器**.

## Features

- Scan and classify local Codex conversations
- Browse normal, sub-agent, archived, residual, damaged, and duplicate conversations
- View conversation details and original file paths
- Export conversations to Markdown
- Back up selected conversations
- Import one or more `.jsonl` conversation files
- Generate a new ID when an imported conversation ID already exists
- Synchronize conversations between API-login and account-login modes
- Delete selected local conversations after Codex is fully closed
- Exit or restart Codex from the manager
- Build Windows and macOS releases

## Codex compatibility

The manager reads local rollout JSONL files, Codex SQLite databases, and the local sidebar state directly. Refresh and conversation details do not start a Codex App Server. In the manager, **Recent** means conversations without a project; it does not follow Codex Desktop's cross-project recent-activity view.

Import, deletion, and provider synchronization use consistent SQLite backups. Fully exit Codex before these operations, then restart it to reload the sidebar.

## Platforms

- Windows: WPF application and installer
- macOS Apple Silicon: Avalonia app bundle
- macOS Intel: Avalonia app bundle

## Build

Requires .NET 8. Use the included scripts:

    .\tools\run-tests.ps1
    .\tools\publish-windows.ps1
    .\tools\package-macos.ps1

## Data Safety

This project manages local Codex data only. Before importing, deleting, or synchronizing conversations, fully exit Codex so it cannot rewrite local indexes during the operation.

Back up important conversations before destructive operations.

## License

See [LICENSE](LICENSE).
