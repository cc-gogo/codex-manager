# Codex 对话管理器

<p align="center">
  <img src="assets/codex-manager-cover.png" alt="Codex 对话管理器" width="360">
</p>

[详细使用说明](README-%E4%BD%BF%E7%94%A8%E8%AF%B4%E6%98%8E.md)

一款用于 Codex 桌面应用的本地对话管理工具，帮助你查看、整理、备份、导入、导出和管理保存在本机的 Codex 对话。

项目名称：**Codex 对话管理器**（Codex Manager）。

## 主要功能

- 扫描并分类本地 Codex 对话
- 浏览普通、子代理、归档、残留、损坏和重复对话
- 查看对话详情及原始文件路径
- 将对话导出为 Markdown 文档
- 备份选中的对话
- 导入一个或多个 `.jsonl` 对话文件
- 导入时若对话 ID 已存在，可生成新 ID 并导入副本
- 在 API 登录与账号登录模式之间同步对话
- 完全退出 Codex 后，删除选中的本地对话
- 在管理器中退出或重启 Codex
- 构建 Windows 和 macOS 发布包

## Codex 兼容性

管理器直接读取本地对话记录（JSONL）、Codex 的 SQLite 数据库和本地侧栏状态。刷新列表和查看对话详情时，不会启动 Codex 应用服务器。管理器中的**“最近”**表示没有归属项目的对话，不跟随 Codex 桌面应用新版跨项目的最近活动列表。

导入、删除和模型提供商同步操作均使用 SQLite 一致性备份。执行这些操作前，请完全退出 Codex；完成后重启 Codex，使侧栏重新加载数据。

## 支持平台

- Windows：WPF 桌面应用及安装包
- macOS 苹果芯片：Avalonia 应用包
- macOS 英特尔芯片：Avalonia 应用包

## 构建方式

需要 .NET 8。可使用项目提供的脚本运行测试、发布 Windows 版本和打包 macOS 版本：

    .\tools\run-tests.ps1
    .\tools\publish-windows.ps1
    .\tools\package-macos.ps1

## 数据安全

本项目仅管理本机的 Codex 数据。导入、删除或同步对话前，请完全退出 Codex，避免它在操作期间重新写入本地索引。

执行永久删除等不可恢复的操作前，请先备份重要对话。

## 开源许可

本项目采用 [MIT 许可证](LICENSE)。
