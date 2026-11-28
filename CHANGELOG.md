## 0.7.0 (2026-11-28)

- **多程序集工具注册**：`UseMcpToolsFrom(params Assembly[])` / `AddMcpToolsFrom(...)`，保留单程序集旧 API；
- **多程序集契约聚合**：生成器改为每个桥接注册 `IAiContractBridge`，`AiContractCatalog` 从容器聚合全部桥接；
  `AddAiExtensionContracts(params Assembly[])` 支持多个程序集；
- **`ConfigureMcpServer` 回调**：宿主可在同一个 `IMcpServerBuilder` 上追加工具/配置，不需要绕过 `AddAiExtension`；
- HTTP/stdio 对外暴露也支持多工具程序集；
- 新增 `Demo.SecondTools` 与 `MultiAssembly.Tests`（多工具程序集 + 双契约目录 + 配置回调）。
- 已知边界：插件动态启停后的 `ListTools` 热刷新仍需重启 AI runtime（0.8+ 设计）；宿主不要静态引用多个插件的
  `AI.Extension.Generated` 类型，统一用 `AddAiExtensionContracts` / `UseMcpToolsFrom` 注册。
# Changelog

## 0.6.2 (2026-11-27)

- **net48 强名称解析兜底**：新增 `AiNet48Compatibility.EnsureAssemblyResolver()` 与模块初始化自动安装，
  解决老式 .NET Framework 宿主没有 bindingRedirect 时的 `FileNotFoundException`（`Microsoft.Extensions.*` 10.0.0.0 → 10.0.0.14）。

## 0.6.1 (2026-11-27)

首个正式版（去掉 `-beta`）。主要能力：

- **契约驱动 + 源生成器**：`[AiContractBinding]` / `[AiMethod]` 自动生成桥接与 DI 注册，Abstractions 包内置 Roslyn 分析器。
- **MCP 工具闭环**：进程内 MCP Server/Client、工具循环、失败回灌、工具轨迹；支持把同一套工具暴露给外部 harness。
- **WPF 外壳**：多标签会话、流式输出、会话持久化（`using/` + `stored/`）、供应商/模型管理（DPAPI）、UI 可替换（`IAiView<T>` + 视图工厂）。
- **Markdown 渲染**：AI 消息支持标题/列表/代码块/表格/引用，保留选中复制；远程图片与不安全链接有防护。
- **ask_user 内置工具**：AI 可在对话中向用户提问/提供选项，WPF 渲染选择卡片并继续对话。
- **MCP 对外暴露**：
  - HTTP（net10，Streamable HTTP，仅本机）；
  - stdio（WPF 命名管道 + `AiMcpStdioBridge.exe`，Bridge 随 NuGet 包自动复制）。
- **net48 多目标**：Abstractions/Core `net10.0;net48`，WPF `net10.0-windows;net48`；net48 仅支持 stdio（HTTP 依赖 ASP.NET Core，不可用），Blazor 保持 net10。
- **CI / 发布**：GitHub Actions 构建+测试+打包；Trusted Publishing（OIDC）发布到 nuget.org。


## 0.5.0-beta.1 (2026-11-27)

- 内置 `ask_user` 工具与 WPF 选择卡片。


## 0.4.0-beta.1 (2026-11-27)

- AI 消息 Markdown 渲染（MdXaml + 安全清洗）。


## 0.3.0-beta.1 (2026-11-27)

- 内置 MCP stdio 暴露（WPF 命名管道 + stdio Bridge）。


## 0.2.0-beta.1 (2026-11-27)

- NuGet 包 ID 改为 `Karl.AIExtension.*`。


## 0.1.0-beta.1 (2026-11-27)

- 首次发布：契约生成器、Core、WPF、Blazor。





