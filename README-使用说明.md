[项目介绍](README.md)

# Codex 对话管理器

用于读取和管理本机 Codex 对话，包括普通、归档、子代理、幽灵、损坏与重复记录。刷新与详情直接读取本地 JSONL、SQLite 数据库和侧栏状态，不启动 Codex App Server。管理器的“最近”表示无项目对话，不跟随官方新版跨项目的最近活动列表。


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
- “最近对话”归类无项目对话，不按最新活动时间截取固定条数。
- 检测到所选对话包含子对话时会阻止删除，避免误删未勾选的子对话。
- 删除直接清理本地 JSONL、SQLite 关联记录和侧栏索引。执行期间会创建临时数据库恢复副本；成功完成后移除临时副本。永久删除后不能依赖它恢复，请先手动备份重要对话。

## 导入对话

- “导入对话”只接受包含 `session_meta` 和有效 UUID 的 Codex rollout `.jsonl` 文件。
- 导入前必须完全退出 `Codex`、`ChatGPT` 和 `codex-code-mode-host`；导入成功后重启 Codex，左侧列表才会重新读取索引。
- 可导入到普通最近对话、已有项目，或选择父文件夹并创建新的真实项目目录。
- 外部文件的 `model_provider` 默认转换为当前登录模式，也可以在预览中选择保留来源 provider。
- 重复对话 ID 默认拒绝；选择“生成新 ID 导入副本”才会创建副本，不覆盖本机原对话。
- 导入前会备份状态数据库和项目状态；失败会自动恢复。数据库使用 SQLite 一致性备份，备份位于程序目录的 `backups\conversation-import`。
- 导入不会修改来源 `.jsonl`、API Key、`auth.json`、附件或项目源代码。
- 导入窗口提供“退出 Codex”“重新检查”和“重启 Codex”按钮；“重启 Codex”只在本次导入成功后启用，并会关闭相关进程后重新打开 Codex。

## 开源准备

项目采用 [MIT 许可证](LICENSE)。`.gitignore` 排除用户对话、数据库、日志、构建缓存和发布产物；开源前仍应人工确认未包含任何真实 `.codex` 数据或凭据。

