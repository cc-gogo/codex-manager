# Sidebar Category Design

## Goal

Make the default category mirror the normal conversations available in Codex, while keeping locally discovered records outside that sidebar distinct.

## Categories

- `普通`: a readable, non-archived conversation with a Codex state-database row.
- `残留对话`: a readable, non-archived local conversation without a Codex state-database row.
- `归档`, `子代理`, and `重复`: retain their existing meanings and precedence.
- `异常对话`: combine the prior missing-content and damaged-record cases.

## UI

Remove `全部对话`. The category list starts with `普通`, and the view model selects it on startup. Rename the UI's former ghost/damaged presentation to one `异常对话` item.

## Safety

This only changes browsing categories. It does not modify Codex files or deletion behavior.
