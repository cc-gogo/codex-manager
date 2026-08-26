# Provider 标签同步

## 目标

在 Codex 对话管理器中增加“同步到当前登录模式”，将本地历史对话在各持久化层中的 `model_provider` 标签统一到当前 `config.toml` 选择的 provider，使账号登录与 API/custom 登录尽可能看到同一批本地对话。

## 明确边界

- 这是本地元数据迁移，不上传对话、不调用云端导入接口、不保证跨设备同步。
- 不读取、打印或修改 API Key、`auth.json`、对话正文、标题、时间戳或项目文件。
- 只处理值严格匹配源 provider 的字段；其他 provider 原样保留。

## 数据范围

同步检查并可修改：

1. `sessions` 和 `archived_sessions` 下 rollout JSONL 的 `session_meta.payload.model_provider`。
2. `.codex/state_5.sqlite` 的 `threads.model_provider`。
3. `.codex/sqlite/state_5.sqlite` 的 `threads.model_provider`（文件存在且表存在时）。
4. `.codex/sqlite/codex-dev.db` 的 `local_thread_catalog.model_provider`。

## 交互流程

- 主窗口增加“同步到当前登录模式”按钮。
- 点击后只读扫描并显示当前 provider、目标记录数量和涉及文件。
- 用户必须完全退出 Codex、ChatGPT、Codex++ 后才能点击执行。
- 执行前要求输入确认词“同步 provider”，执行过程中显示每层计数。
- 执行前创建位于用户指定备份目录的时间戳备份；失败自动回滚并报告。
- 完成后重新扫描对话列表，并显示迁移结果。

## 验收

- Dry-run 不改变任何文件。
- Apply 只更新严格匹配源 provider 的字段，并在四类数据源中验证目标值数量。
- 非目标 provider、正文和配置认证文件保持不变。
- Codex/ChatGPT 运行时拒绝执行。
- 二次执行幂等，目标数量为 0。
- 模拟失败能恢复备份并保留原始哈希。
