# 构建与发布

## 本地构建

使用 PowerShell 7 和 x64 Windows 11。执行 `scripts/Prepare-Tools.ps1 -IncludeInstaller` 下载固定 SDK、Zig 0.13.0、Node 24.19.0 及 Inno Setup；每份下载在解压/运行前验 SHA256 或 SHA512。工具仅保存在被忽略的 `.tools`。Go 使用 CGO，默认由固定 Zig C 编译器处理；已有 `CC` 时尊重调用者指定值。

`scripts/Build.ps1 -Installer` 先检查许可证残留，运行真实 .NET 媒体库/缓存服务测试、微信注入脚本测试和 Go app/desktop/storage/handlers 测试，再构建隐藏控制台的内核；Node 黑盒测试通过真实 HTTP 处理器、Gopeed 和 SQLite 检查 5/2 并发、页面上传、字节、鉴权与重启，不修改系统代理或证书。最后发布自包含 WinUI、收集实际依赖许可，扫描发布目录及安装器内所有 PE 的普通与延迟导入，编译每用户安装包。扫描结果保存在 `artifacts/qa/*-imports.json`；它检查本机可解析的原生依赖，不代替 Windows 11 实机启动验证。Go build/test 使用 `-tags nosqlite`，仅禁用 Gopeed torrent 的 crawshaw SQLite；应用下载记录仍使用 SQLite。输出应用目录 `artifacts/publish` 与安装包 `artifacts/release`。不可将上游 wx_channel 预编译程序复制进发行目录。

`scripts/Installer-Smoke.ps1` 将当前版本（默认 1.1.0）安装器定向安装到 `artifacts/qa-install`，用隔离目录 `artifacts/install-qa` 的下一补丁版本测试安装器验证版本升级，再卸载。它不运行应用；对安装前后的 WinInet 配置、两种根证书库、真实 AppData 与媒体内容进行指纹比对。已存在正式安装时拒绝测试，避免修改用户实际安装。

源归档必须在准确待发布提交和干净工作区上执行。归档包含固定提交及 Go vendor 依赖源码，不包含 `.tools`、日志、数据库、密钥或下载视频。NuGet 包版本由项目固定，完整运行时许可随安装包附带。

## 发布顺序

1. 完成 `docs/VALIDATION.md`，记录本机微信版本与 Windows 版本，不留“待验证”。
2. 提交本次实现和验收记录，运行完整构建与源码归档。
3. 检查输出目录只有当前版本安装包、源码包与校验文件。
4. 将对应标签（例如 `v1.1.0`）指向该提交；维护者手动运行发布工作流，由工作流再次测试、构建和核对标签。
5. 工作流只创建包含这三项的 Release。不得先发安装包后补对应源码。

如需 GitHub 环境人工审批，仓库维护者须另行设置 required reviewer；工作流的 `environment` 字段本身不会开启审批。CI 本身不应将编译成功误当作真实微信验收。
