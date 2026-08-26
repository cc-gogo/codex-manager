# Sidebar Category Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split normal conversations into Codex-sidebar `普通` and local-only `残留对话`, and merge ghost/damaged records into `异常对话`.

**Architecture:** The classifier will derive the new residual category from existing state-database evidence. The WPF view will default to `普通` and present the revised category list.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit.

## Global Constraints

- Do not modify Codex data while classifying records.
- `普通` requires a non-archived state-database row.
- Combine missing-content and damaged records as `异常对话`.

---

### Task 1: Classifier Categories

**Files:**
- Modify: `src/CodexConversationManager.Core/Domain/ConversationCategory.cs`
- Modify: `src/CodexConversationManager.Core/Inventory/ConversationClassifier.cs`
- Test: `tests/CodexConversationManager.Tests/Inventory/ConversationClassifierTests.cs`

- [ ] Write failing tests for a readable local-only residual record and a catalog-only abnormal record.
- [ ] Run the focused classifier test and confirm failure.
- [ ] Add `Residual`, remove the ghost category, and route both missing-content and damaged evidence to `Damaged`.
- [ ] Run focused classifier tests.

### Task 2: Default Browser Category

**Files:**
- Modify: `src/CodexConversationManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml.cs`
- Test: `tests/CodexConversationManager.Tests/ViewModels/MainViewModelTests.cs`

- [ ] Write a failing test that refresh defaults to normal records and excludes residual records.
- [ ] Run the focused view-model test and confirm failure.
- [ ] Set the default category to `Normal`; remove the all-category command; render `残留对话` and `异常对话` in the category list.
- [ ] Run focused view-model tests.

### Task 3: Verification

**Files:**
- Modify only files required by Tasks 1-2.

- [ ] Run `build-tools\\dotnet\\dotnet.exe test CodexConversationManager.sln`.
- [ ] Publish the WPF application to a new, non-overwriting directory.
