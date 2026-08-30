# Codex 0.151 Compatibility Design

## Goal

Upgrade Codex Manager to `v0.2.0` so that it reads, classifies, imports, backs up, and safely deletes conversations created by Codex Desktop `0.151.x`, while retaining compatibility with legacy rollout files and databases.

## Compatibility Baseline

- Supported legacy rollout history: `history_mode = legacy` with `event_msg/user_message` and `event_msg/agent_message`.
- Supported modern rollout history: `history_mode = paginated` with `event_msg/item_completed` items such as `UserMessage` and `AgentMessage`.
- Supported state sources: `.codex/state_5.sqlite`, `.codex/thread_history_1.sqlite`, `.codex/sqlite/codex-dev.db`, `.codex/.codex-global-state.json`, `session_index.jsonl`, active rollout files, and archived rollout files.
- All readers must tolerate optional tables and columns so older Codex installations continue to work.
- Real `.codex` data is never used as writable test data.

## Architecture

The compatibility layer remains local-first. Read-only adapters collect evidence from each available Codex store and merge it by thread ID. Write operations use a centralized schema-aware mutation layer that knows which stores contain data for a thread, creates a recoverable backup before mutation, and verifies every known store afterward.

Version detection is capability-based rather than tied only to a Codex version number. SQLite readers inspect table and column availability, rollout readers inspect envelope and item types, and executable discovery selects the newest usable Codex App Server binary.

## Codex Executable Selection

`CodexExecutableLocator` will discover candidates from:

1. The Codex Desktop installation under `%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe` on Windows.
2. Native Codex binaries contained in npm installations on `PATH`.
3. Direct `codex.exe` candidates on `PATH`.

Candidates are probed with `--version`. The highest valid semantic/pre-release version is selected, with the Desktop binary winning ties. A failure to probe one candidate does not prevent trying the others. Tests use fake candidate probes and do not launch the user's real Codex installation.

## Modern Conversation Details

The local detail reader will recognize all of the following without duplicating displayed text:

- legacy `event_msg` records with `payload.message`;
- `response_item` records with textual content;
- modern `event_msg` records whose payload is `item_completed` and whose item type is `UserMessage` or `AgentMessage`.

The role mapping is `UserMessage -> user` and `AgentMessage -> assistant`. Text is assembled from textual `content` entries. If a file contains both generated legacy compatibility events and the corresponding modern records, stable message identity and adjacent content comparison prevent duplicate display blocks.

The import preview reader uses the same modern message extraction rule to derive a title when `session_meta.payload.title` is absent.

## Thread Relationships and Categories

A new read-only state relationship adapter reads `thread_spawn_edges` when present. Each parent record receives its direct and transitive descendant IDs. Legacy sub-agent detection through `thread_source` and source metadata remains supported.

Deletion planning blocks a parent whenever any descendant exists, regardless of whether that descendant is currently visible, selected, archived, or missing a rollout body. This makes the existing deletion warning effective against Codex's real hierarchy.

Project and sidebar evidence is read from both generations:

- legacy project definitions and assignments in `.codex-global-state.json`;
- modern `projects`, `project_roots`, `threads.project_id`, and catalog `project_id` when populated;
- `thread_sections`, `threads.thread_section_id`, and `is_pinned` when present.

Modern SQLite values take precedence when populated; legacy JSON remains the fallback. Existing UI categories remain unchanged, but the tree can represent project, recent, pinned, section, sub-agent, archived, residual, damaged, and duplicate evidence without moving a conversation into the wrong category.

The “recent” set is derived from current visible, unarchived state rows (`preview <> ''`) ordered by `recency_at_ms`. It must not use an arbitrary archived limit or exclude a sub-agent merely because its `thread_source` is `subagent`; membership should mirror Codex-visible state rather than infer a category from age alone.

## Import Strategy

For `v0.2.0`, imported modern JSONL remains converted into a locally readable legacy-compatible rollout copy. This is intentionally retained because writing Codex's private paginated projection tables directly would couple the importer to unstable internal ordinal and byte-offset rules.

Import will additionally:

- extract modern user messages for preview titles;
- retain original modern records and generate legacy display records only in the imported copy;
- insert all required `threads` fields that are present in the installed schema, including `history_mode = legacy`;
- write project assignment to modern columns only when those columns and corresponding project IDs are available;
- continue updating legacy global-state project assignments for backward compatibility;
- validate that the imported rollout, state row, title, provider, history mode, and project assignment agree.

The original source JSONL is never modified.

## Safe Deletion and Residual Verification

Before permanent deletion, the manager backs up every existing affected artifact:

- active and archived rollout JSONL files;
- matching rows or complete recoverable database copies for `state_5.sqlite`, `thread_history_1.sqlite`, and `codex-dev.db`;
- global state and session index files.

Local cleanup removes the selected thread from:

- `threads` and state tables that reference it but do not have reliable foreign-key cascade behavior;
- `thread_turns`, `thread_items`, `thread_realtime_items`, and `thread_history_projection_state`;
- `local_thread_catalog`;
- global state references and `session_index.jsonl`.

The cleanup checks table existence before issuing statements. It never removes another thread's row. Residual verification checks the same stores and reports exactly which store still contains the ID. If Codex is running or any required database cannot be locked, mutation is refused before deleting rollout files.

## Provider Synchronization and Backups

Provider synchronization remains limited to stores that actually contain `model_provider`. It dynamically checks columns and includes all current authoritative databases. It does not rewrite paginated item history because provider identity is session/thread metadata, not message content.

Conversation backups continue to save portable JSONL plus a manifest. Safety backups made for import, synchronization, or deletion additionally include every database or state file that the operation will mutate.

## macOS Distribution

The source compatibility changes apply to Windows and macOS builds. GitHub Actions will build macOS artifacts on a macOS runner so executable mode bits are preserved. Without Apple Developer secrets, the workflow may create an ad-hoc signed preview artifact and clearly label it as such. A generally trusted release requires Developer ID signing and Apple notarization; the workflow will support those secrets when provided.

## Error Handling

- Missing optional databases, tables, and columns are treated as unsupported capabilities, not damaged data.
- Malformed JSONL records are skipped for detail display but remain visible as damage evidence in inventory.
- A write operation fails closed if its backup, lock acquisition, schema inspection, or post-write verification fails.
- Failure messages identify the exact file, database, or compatibility capability involved without exposing conversation text in logs.

## Testing

Fixtures cover legacy Codex data and a sanitized `0.151` layout containing modern messages, paginated history projection rows, projects, sections, pinned state, and spawn edges.

Required regression tests verify:

- newest Desktop App Server selection over an older PATH CLI;
- modern user and assistant detail extraction without duplicates;
- modern import title extraction and legacy-compatible output;
- transitive descendant discovery and parent deletion blocking;
- history database cleanup and residual detection;
- project/sidebar precedence and current recent membership;
- schema fallback when new databases or columns do not exist;
- rollback after a simulated failure leaves all original stores intact.

The complete existing test suite must pass before packaging. Release verification also inspects Windows version metadata, macOS bundle architecture, executable permissions, and GitHub assets.

## Release Scope

The release version is `v0.2.0`. It includes the compatibility work described above and no unrelated UI redesign. Windows remains a self-contained installer. macOS remains available for Apple Silicon and Intel, with signing status stated accurately in the release notes.
