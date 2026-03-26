# SwaggerToMarkdownTool

一个命令行工具，用于将 Swagger / OpenAPI 接口文档导出为 Markdown 文件。

## 功能特性

- 支持 **Swagger 2.0** 和 **OpenAPI 3.0** 规范
- 自动识别 Swagger UI 页面，自动发现 API 文档地址
- 支持 Spring Cloud Gateway 等网关场景（多服务聚合文档）
- 支持直接传入 API docs JSON 地址
- 支持跳过 SSL 证书校验（适用于测试环境）
- 生成结构清晰的 Markdown，包含目录、接口分组、参数表格、请求体、响应、数据模型等
- 基于 Native AOT 编译，启动快、无需安装运行时

## 环境要求

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

## 构建

```bash
dotnet build
```

发布为独立可执行文件（Native AOT）：

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## 打包发布

项目提供 `publish.ps1` 脚本，可一键构建并打包为压缩包（Windows 为 `.zip`，Linux/macOS 为 `.tar.gz`）。

打包当前平台：

```powershell
./publish.ps1 -Runtime win-x64
```

打包多个平台：

```powershell
./publish.ps1 -Runtime win-x64, linux-x64, osx-arm64
```

打包所有支持的平台：

```powershell
./publish.ps1 -Runtime all
```

产物输出到 `artifacts/` 目录。

支持的目标平台：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`

## 使用方法

```
SwaggerToMarkdownTool --url <swagger-url> [--output <file>] [--skip-ssl]
```

### 参数说明

| 参数 | 说明 | 必填 | 默认值 |
|------|------|------|--------|
| `--url` | Swagger UI 页面地址或 API docs JSON 地址 | 是 | — |
| `--output` / `-o` | 输出的 Markdown 文件路径 | 否 | `swagger.md` |
| `--skip-ssl` | 跳过 SSL 证书校验 | 否 | — |
| `--help` / `-h` | 显示帮助信息 | 否 | — |

### 示例

从 Swagger UI 页面导出：

```bash
SwaggerToMarkdownTool --url https://example.com/swagger-ui/index.html --output api.md
```

从 API docs JSON 地址导出：

```bash
SwaggerToMarkdownTool --url https://example.com/v3/api-docs -o api.md
```

测试环境跳过 SSL 校验：

```bash
SwaggerToMarkdownTool --url https://test-server:8400/gateway/swagger-ui/index.html --output swagger.md --skip-ssl
```

## URL 自动识别

工具会根据传入的 URL 自动判断类型并获取 API 文档：

1. **Swagger UI 页面**（URL 包含 `swagger-ui`）— 自动提取 Base URL，依次尝试：
   - `/v3/api-docs/swagger-config`（springdoc 多服务发现）
   - `/swagger-resources`（springfox 多服务发现）
   - `/v3/api-docs`、`/v2/api-docs` 等常见路径
2. **URL 带 query 参数**（如 `?url=/v3/api-docs`）— 直接使用指定地址
3. **直接 JSON 地址** — 直接获取并解析

## 输出格式

生成的 Markdown 文件包含以下内容：

- API 标题、描述、版本、Base URL
- 认证方式说明
- 按 Tag 分组的接口目录
- 每个接口的详细信息：
  - HTTP 方法 + 路径
  - 接口描述
  - 参数表格（名称、位置、类型、是否必填、说明）
  - 请求体结构
  - 响应状态码及响应体结构
- 数据模型（Models）定义

## 项目结构

```
├── publish.ps1                         # 打包发布脚本
├── SwaggerToMarkdownTool/
│   ├── Program.cs                      # 入口，命令行参数解析
│   ├── SwaggerClient.cs                # HTTP 客户端，获取 Swagger JSON
│   ├── MarkdownConverter.cs            # 将 Swagger JSON 转换为 Markdown
│   └── SwaggerToMarkdownTool.csproj
└── artifacts/                          # 打包产物输出目录（git 忽略）
```

## License

MIT
