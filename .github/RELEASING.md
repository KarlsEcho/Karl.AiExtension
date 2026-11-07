# 发布 NuGet 包

框架的 4 个包发布到 [nuget.org](https://www.nuget.org/)：

| 包 | 内容 | 说明 |
| --- | --- | --- |
| `AI.Extension.Abstractions` | 契约特性、桥接接口、UI 抽象 + **内置 Roslyn 源生成器**（`analyzers/dotnet/cs`） | 引用它即自动获得生成器，不用单独引 Analyzer |
| `AI.Extension.Core` | 会话、工具循环、MCP 进程内宿主、供应商/模型管理、MVVM 基类 | 依赖 Abstractions |
| `AI.Extension.WPF` | WPF 外壳：聊天/供应商/诊断面板、视图工厂、`WpfUiDispatcher` | 依赖 Core；`net10.0-windows` |
| `AI.Extension.Blazor` | Blazor 外壳组件与静态资源 | 依赖 Core；`net10.0-windows` |

`demo/` 下的项目、`tests/` 下的测试项目都设置了 `IsPackable=false`，不会进包。

---

## 发布方式：Trusted Publishing（OIDC，无长期 API Key）

nuget.org 官方推荐方式：GitHub Actions 用 OIDC 短期 token 换取 **1 小时有效**的临时 API Key，仓库里不存任何发布密钥。

工作流：[`.github/workflows/publish-nuget.yml`](workflows/publish-nuget.yml)。

### 一次性准备

#### 1. 在 nuget.org 创建 Trusted Publishing 策略

登录 nuget.org → 头像 → **Trusted Publishing** → **Create**：

| 字段 | 填什么 |
| --- | --- |
| Policy Name | 任意，例如 `Karl.AiExtension` |
| Package Owner | 你的 nuget.org 用户名（例如 `karlsecho`） |
| CI/CD Provider | `GitHub Actions` |
| Repository Owner | **GitHub 账号名**（例如 `KarlsEcho`，注意不是昵称） |
| Repository | 仓库名，例如 `Karl.AiExtension` |
| Workflow File | **`publish-nuget.yml`**（只填文件名，不要带 `.github/workflows/`） |
| Environment | 留空（workflow 没有配 environment） |
| Select Scopes | 勾 `Push` → `Push new packages and package versions`；Glob pattern 填 `AI.Extension.*` |

> 字段大小写不敏感，但 Repository Owner / Repository 必须和 GitHub 上一致，Workflow File 必须和工作流文件名一致。
> 公开仓库创建的策略会立即生效；私有仓库可能先有 7 天临时激活期（官方文档有说明）。

#### 2. 在 GitHub 添加 `NUGET_USER` Secret

仓库 → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**：

- Name：`NUGET_USER`
- Value：nuget.org 的用户名（**不是邮箱**，例如 `karlsecho`）

> 不需要 `NUGET_API_KEY`。临时 Key 由 `NuGet/login@v1` 在发布时动态获取。

---

## 发布流程（推荐：打 tag 自动发）

版本号从 tag 解析：`v0.1.0-beta.1` → `0.1.0-beta.1`。

```powershell
# 1) 确保 main 干净且已推送
git status
git push origin main

# 2) 打 tag 并推送（触发 Publish NuGet）
git tag v0.1.0-beta.1
git push origin v0.1.0-beta.1
```

然后到仓库 **Actions → Publish NuGet** 看运行结果；成功后到 nuget.org 的包列表确认（索引有几分钟延迟）。

> NuGet 版本**不可覆盖**：同一个版本号只能发布一次。发错了只能换新版本号（旧版本可在 nuget.org 上 Delist）。

### 手动触发（不必须打 tag）

Actions → **Publish NuGet** → **Run workflow**：

- `version` 留空：使用 `Directory.Build.props` 里的 `<Version>`；
- 填 `0.1.0-beta.2`：覆盖版本号。

### 日常 CI

`.github/workflows/ci.yml`：push/PR 到 `main` 时自动 restore → build → test → pack，并把 `.nupkg/.snupkg` 作为 artifact 上传（不发布）。

---

## 本地验证（发布前建议跑一遍）

```powershell
dotnet pack -c Release -o artifacts
Get-ChildItem artifacts   # 应看到 4 个 .nupkg + 4 个 .snupkg
```

检查包内容：

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead('artifacts\AI.Extension.Abstractions.0.1.0-beta.1.nupkg')
$zip.Entries.FullName | Where-Object { $_ -notmatch '^_rels|^\[Content_Types\]|^package/' }
$zip.Dispose()
```

应包含：

- `lib/net10.0/AI.Extension.Abstractions.dll`
- `analyzers/dotnet/cs/AI.Extension.Abstractions.Generator.dll`
- `README.md`
- `.nuspec`（`license=MIT`、`repository` 带 commit）

用本地包做一次消费验证：

```powershell
# 新建一个 WPF 工程，NuGet.config 里加本地源 artifacts
dotnet add package AI.Extension.WPF --version 0.1.0-beta.1
dotnet build
```

---

## 版本号约定

- 预览：`0.1.0-beta.1`、`0.1.0-beta.2`…
- 正式：`0.1.0`、`0.2.0`…
- 破坏性变更升 minor（0.x 阶段），并更新 README/`docs`。

版本号默认在根目录 `Directory.Build.props` 的 `<Version>`；打 tag 时以 tag 为准（workflow 会传 `-p:Version=`）。

---

## 常见问题

| 现象 | 处理 |
| --- | --- |
| `401/403` 或 OIDC 换取 Key 失败 | 检查 Trusted Publishing 策略：Repository Owner / Repository / Workflow File 是否与实际完全一致；`NUGET_USER` 是否是 nuget.org 用户名；workflow 是否声明了 `permissions: id-token: write` |
| `409 Conflict` / 版本已存在 | NuGet 不允许覆盖；换新版本号重发 |
| 包体积明显偏小、没生成器 | 检查 `AI.Extension.Abstractions` 包内是否有 `analyzers/dotnet/cs/*.dll`；确保 `dotnet pack` 前 Generator 已构建（项目已用 ProjectReference 保证） |
| WPF 包引用后编译报缺少 ASP.NET Core | 包内已带 `frameworkReference Microsoft.AspNetCore.App`；若仍然报错，在宿主项目显式加 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` |
| Actions 不触发 | 确认 tag 形如 `v0.1.0-beta.1`，且 workflow 文件在 `main` 上；fork 仓库的 Actions 可能被禁用 |

> 如果你更想用传统长期 API Key：nuget.org → API Keys 创建 Key，在 GitHub 加 `NUGET_API_KEY` Secret，并把 workflow 的 `NuGet/login` 步骤换回 `--api-key $env:NUGET_API_KEY`。当前仓库默认使用更安全的 OIDC 方式。
