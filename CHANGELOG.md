# Changelog

## 0.6.0 (2026-11-27)

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
