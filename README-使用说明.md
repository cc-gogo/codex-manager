[English](README.md)

# Codex 对话管理器

用于读取和管理本机 Codex 对话，包括普通、归档、子代理、幽灵、损坏与重复记录。


## 运行方式

使用本地 .NET 8 SDK 构建：

```powershell
& .\build-tools\dotnet\dotnet.exe build .\CodexConversationManager.sln
& .\src\CodexConversationManager.App\bin\Debug\net8.0-windows\CodexConversationManager.App.exe
```

默认读取 `%USERPROFILE%\.codex`。可用启动参数读取另一个绝对路径，例如合成测试目录：

```powershell
CodexConversationManager.App.exe --codex-home "D:\fixture\.codex"
```

程序的 `data`、`logs` 与设置都位于程序自身目录下，可随整个文件夹移动到 C、D、E 等任意位置。

## 删除安全

- 浏览和搜索只读，不改写 Codex 数据。
- 永久删除前必须完全退出外部 `Codex`、`ChatGPT` 和 `codex-code-mode-host` 进程。
- “最近对话”和项目树是同一批对话的不同浏览入口，不会复制对话数据。
- 检测到所选对话包含子对话时会阻止删除，避免 Codex 的 `thread/delete` 连带删除未勾选的子对话。
- 删除不创建备份、副本或恢复点，误删无法恢复。
- 有效会话使用 Codex App Server 官方 `thread/delete`；只有确认没有正文的幽灵记录才会清除精确残留引用。

## 导入对话

- “导入对话”只接受包含 `session_meta` 和有效 UUID 的 Codex rollout `.jsonl` 文件。
- 导入前必须完全退出 `Codex`、`ChatGPT` 和 `codex-code-mode-host`；导入成功后重启 Codex，左侧列表才会重新读取索引。
- 可导入到普通最近对话、已有项目，或选择父文件夹并创建新的真实项目目录。
- 外部文件的 `model_provider` 默认转换为当前登录模式，也可以在预览中选择保留来源 provider。
- 重复对话 ID 默认拒绝；选择“生成新 ID 导入副本”才会创建副本，不覆盖本机原对话。
- 导入前会备份 rollout、状态数据库和项目状态；失败会自动恢复。备份位于程序目录的 `backups\conversation-import`。
- 导入不会修改来源 `.jsonl`、API Key、`auth.json`、附件或项目源代码。
- 导入窗口提供“退出 Codex”“重新检查”和“重启 Codex”按钮；“重启 Codex”只在本次导入成功后启用，并会关闭相关进程后重新打开 Codex。

## 开源准备

项目采用 [MIT License](LICENSE)。`.gitignore` 排除用户对话、数据库、日志、构建缓存和发布产物；开源前仍应人工确认未包含任何真实 `.codex` 数据或凭据。

