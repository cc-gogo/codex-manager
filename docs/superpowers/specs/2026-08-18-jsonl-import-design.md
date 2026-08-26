# Codex JSONL Conversation Import Design

## Goal

Import one or more Codex rollout JSONL files supplied by another person. After the user restarts Codex, each accepted conversation must appear in the Codex sidebar under the chosen existing project, a newly created local project, or the normal recent conversation list.

## Safety Boundary

- Import is disabled unless Codex and ChatGPT are completely exited.
- The importer accepts only UTF-8 JSONL files that contain a valid `session_meta` record with a UUID thread id.
- It rejects duplicate thread ids by default. An explicit "generate new id" choice creates a copy with a new UUID and replaces every matching thread-id value in the imported JSONL.
- The preview shows each source path, title, original id, target id, target project, destination rollout path, provider change, and conflicting local ids before any local file is changed.
- Before applying, the importer snapshots every destination file that exists, `state_5.sqlite`, `sqlite/codex-dev.db` when present, and `.codex-global-state.json` into a timestamped importer backup directory.
- On any failed write or validation, it restores all snapshots and removes only files created by that import attempt.
- It never reads, displays, or changes API keys, `auth.json`, attachments, project source files, or imported source files.

## Data Validation And Transformation

1. Parse every line as JSON. Reject malformed JSONL, empty files, missing `session_meta`, or a missing/invalid UUID thread id.
2. Derive the title, cwd, timestamps, source, and provider from `session_meta`; use conservative fallback values only where Codex permits them.
3. Default the imported session's `model_provider` to the provider configured in the current Codex profile. The preview offers an explicit option to preserve the original provider instead.
4. Preserve all conversation event content. Only `session_meta` provider and, when explicitly requested, thread-id references are transformed.
5. Write the destination as a new `rollout-<target-id>.jsonl` in Codex's active sessions directory.

## Sidebar Registration

The importer registers each imported conversation in all local stores that Codex uses after restart:

- Insert a complete row in `state_5.sqlite` `threads`, including rollout path, timestamps, title, cwd, provider, non-archived state, preview, and recency.
- Insert or update `sqlite/codex-dev.db` `local_thread_catalog` when its schema is present.
- For an existing project, add `thread-project-assignments[target-id]`, append it to that project's `sidebar-project-thread-orders`, and remove it from `projectless-thread-ids`.
- For a normal recent import, add it to `projectless-thread-ids` and leave it without a project assignment.
- For a new project, create the requested physical folder under the selected parent directory, create the local-project entry, preserve project ordering, and then perform the existing-project assignment flow.

The global state file is written through a temporary file and atomic replace. The state database changes use a single SQLite transaction. The catalog database changes use a separate transaction and are included in the same backup/rollback scope.

## User Flow

1. The user opens "导入对话" and receives an exit-Codex warning.
2. The user chooses JSONL files.
3. The preview lists accepted and rejected files, conflicts, selected provider behavior, and current target selection.
4. The user chooses one destination for the batch: an existing project, normal recent conversations, or a new project (parent directory and project name).
5. The user confirms with a fixed confirmation phrase. Apply remains disabled while Codex is running or while unresolved duplicate ids remain.
6. The app imports, validates the copied files and index rows, refreshes its own inventory, and tells the user to restart Codex.

## Verification

Automated tests cover valid import, invalid JSONL rejection, duplicate-id rejection, generated-id rewrite, existing-project registration, new-project creation, current-provider rewrite, catalog/state/global-state updates, and rollback after a forced failure.

The final manual check uses an isolated temporary Codex root: import a fixture, close/reopen Codex against that root when available, and verify the sidebar assignment via the local reader. Production imports require the same exit-Codex guard and backups.
