<div align="center">

![EVEING Lumiven — Save. Organize. Revisit.](docs/design/github-banner.svg)

**简体中文** · [English](README.en.md)

### 留下喜欢的视频，整理自己的媒体库。

免费开源的 Windows 视频下载与管理工具。

视频号 · 抖音 · 小红书，从下载到回看，在一个清透简洁的桌面界面中完成。

[![Latest release](https://img.shields.io/github/v/release/0399199-stack/eveing-lumiven?style=flat-square&color=4185d7&label=Release)](https://github.com/0399199-stack/eveing-lumiven/releases/latest) [![Windows 10 22H2 / 11 x64](https://img.shields.io/badge/Windows-10%2022H2%20%2F%2011%20x64-64748b?style=flat-square)](docs/WINDOWS10.md) [![GPL-3.0-only](https://img.shields.io/badge/License-GPL--3.0--only-64748b?style=flat-square)](LICENSE)

**[下载 Windows 安装包](https://github.com/0399199-stack/eveing-lumiven/releases/latest)** &nbsp; · &nbsp; **[V1.2.0 更新说明](https://github.com/0399199-stack/eveing-lumiven/releases/tag/v1.2.0)** &nbsp; · &nbsp; **[使用帮助](#快速开始)**

</div>

---

## 一个界面，完成下载与回看

| 下载 | 管理 | 使用体验 |
| :--- | :--- | :--- |
| **微信视频号** — 在微信视频号页面选择画质并下载，在 Lumiven 查看任务与结果。 | **本地媒体库** — 下载完成自动收录，按标题或作者搜索，重启后保留记录。 | **清透玻璃界面** — 浅色、深色、跟随系统，支持中文与英文。 |
| **抖音与小红书** — 粘贴视频分享链接或整段分享文案，解析后直接保存。 | **播放与定位** — 内置播放、全屏、文件定位；不支持的编码可交给系统播放器。 | **GitHub 更新** — 自动检查、后台下载、SHA-256 校验，确认后安装。 |
| **统一任务管理** — 查看进度与错误，已结束的解析记录可单独清理。 | **旧目录与批量整理** — 导入已有视频而不移动文件；删除记录默认保留视频。 | **视频号帮助** — 内置教程与“视频号异常修复”，处理本软件遗留的连接状态。 |

### 支持哪些链接？

| 来源 | V1.2.0 支持范围 | 入口 |
| :--- | :--- | :--- |
| 微信视频号 | 电脑微信视频号页面中的视频；需先启用本地接入 | 视频号下载 |
| 抖音 | `v.douyin.com` 视频分享短链接，可直接粘贴整段分享文案 | 视频解析 → 抖音 |
| 小红书 | 视频笔记链接与分享短链接 | 视频解析 → 小红书 |

优先保存平台可提供的无水印视频源，不抹除画面中已有的水印。本版不包含图集、直播录制或受限作品下载；平台页面、网络或访问限制发生变化时，解析可能失败。

<details>
<summary><strong>界面预览与设计参考</strong></summary>

![Lumiven 媒体库设计参考](docs/design/approved-library.png)

这是项目采用的界面设计参考，**不是软件运行截图**。

![Lumiven 早期版本实际媒体库测试窗口](docs/verification/runtime-performance-library.jpg)

这是早期版本的真实程序窗口，使用 1,000 条合成占位记录进行性能测试；不展示真实用户视频，也不代表 V1.2.0 的全部页面。实际验收范围见 [验收记录](docs/VALIDATION.md)。

</details>

## 下载与系统要求

**[前往最新正式版 →](https://github.com/0399199-stack/eveing-lumiven/releases/latest)** 选择名称包含 `Setup` 的 `.exe` 安装包，例如 `EVEING-Lumiven-Setup-1.2.0-x64.exe`。

| 项目 | 说明 |
| :--- | :--- |
| 系统 | Windows 10 22H2 x64（19045）或 Windows 11 x64；不支持更早 Win10、32 位或 ARM64 |
| 运行环境 | 安装包自带 .NET 与 Windows App SDK，无需另装 Go 或 .NET |
| 发布附件 | Windows 安装包、对应源码 ZIP、`SHA256SUMS` 校验文件 |
| 升级与卸载 | 升级保留设置、下载记录与视频；卸载保留用户数据和下载视频 |

**Windows 10 验证说明：** 此前兼容版本的基础使用已由维护者在实机确认；V1.2.0 新增功能尚未在 Win10 实机完整复测。标题栏和透明效果按系统能力降级，详见 [兼容记录](docs/WINDOWS10.md)。安装包目前未进行开发者代码签名。

## 快速开始

### 抖音 / 小红书

1. 打开 **视频解析**，选择对应平台。
2. 粘贴视频分享链接或整段分享文案，点击 **解析并下载**。
3. 完成后进入 **已下载**，播放视频或打开所在文件夹。

这两个平台的解析不需要启用微信接入或安装微信接入证书。

### 微信视频号

1. 在 **视频号下载** 中点击 **启用微信下载**，阅读本地证书、HTTPS 流量处理及当前用户代理说明；出现 Windows 证书提示时，请核对并自行确认。
2. 重新打开电脑微信视频号，在视频页面出现的下载入口选择画质并下载。
3. 在 Lumiven 查看任务，完成后进入 **已下载** 播放或定位文件。
4. 使用结束后 **停止接入**。软件会恢复之前的代理状态并移除本次接入安装的证书；如有 Windows 确认提示，请完成确认。

若视频号无法加载，可在设置中使用 **视频号异常修复**。它只处理 Lumiven 自己保存的连接状态，不重置其他软件的代理或删除其他证书。微信页面及代理使用方式可能随版本变化，不能保证所有微信版本都兼容。

<details>
<summary><strong>文件管理、删除与安装行为</strong></summary>

- 在设置中选择保存文件夹；“导入旧目录”收录已有视频，不移动原文件。外部移走或删除的文件会标记为缺失。
- 卡片菜单和批量选择支持删除记录，默认保留视频。只有明确选择同时删除本地文件并确认，才会永久删除视频，**不经过回收站**。文件被占用时保留失败记录。
- 已删除记录不会因刷新、自动扫描或旧任务通知而重新出现；重新下载的新任务可重新收录。媒体库索引升级后旧版本不能读取，请勿用旧版覆盖运行。
- 安装器仅安装应用文件及所选快捷方式，不安装驱动、不静默修改代理、不自动设置开机启动。卸载前请停止微信接入。

</details>

## 后续更新

默认启动后从官方 GitHub Release 检查更高版本的正式版，并在后台下载；校验通过后，**由你确认安装**。设置和关于页提供手动检查入口，设置中可关闭自动更新。V1.2.0 起会展示新版功能说明。

**V1.1.0 / V1.1.1 用户可直接在软件内升级。** V1.0.x 用户请先手动安装新版。安装前需完成下载并停止微信接入，无需 GitHub 账号。详见 [更新机制](docs/UPDATES.md)。

## 开发与文档

WinUI 3 桌面界面 · C# / .NET · 独立 Go 视频号内核

| 文档 | 内容 |
| :--- | :--- |
| [构建说明](docs/BUILD.md) | 固定工具链、源码构建与打包 |
| [更新记录](docs/RELEASE-NOTES.md) | 当前版本与历史变更 |
| [验收记录](docs/VALIDATION.md) | 已验证结果与测试边界 |
| [Windows 10 兼容说明](docs/WINDOWS10.md) | 系统要求及兼容验证范围 |
| [组件与许可](THIRD-PARTY-NOTICES.md) | 上游来源、版本及许可证 |

<details>
<summary><strong>从源码构建</strong></summary>

在 x64 Windows 11 上使用 PowerShell 7。固定工具链包括 .NET SDK 10.0.401、Go 1.27.1、Zig 0.13.0、Node 24.19.0（测试）及 Inno Setup 6.5.4；下载地址与校验值见 `scripts/toolchain.json`。

```powershell
pwsh ./scripts/Prepare-Tools.ps1 -IncludeInstaller
pwsh ./scripts/Verify-Licenses.ps1
pwsh ./scripts/Build.ps1 -Installer
# 对准确的已提交源码，在干净工作区生成对应源码包与校验文件：
pwsh ./scripts/Build.ps1 -Installer -SourceArchive
```

产物位于 `artifacts/release`。GitHub Actions 对 main 执行构建与必要测试；正式发布还需完成相应验收。完整步骤见 [构建说明](docs/BUILD.md)。

</details>

## 开源致谢

视频号内核基于 [nobiyou/wx_channel](https://github.com/nobiyou/wx_channel)。抖音与小红书解析参考 [douyin-watermark-free-downloader](https://github.com/qgeng1465/douyin-watermark-free-downloader)、[parse-video](https://github.com/wujunwei928/parse-video) 和 [xhs-downloader](https://github.com/Andy-SoulShell/xhs-downloader)，在本项目中以 C# 适配实现。感谢上游作者与开源贡献者。

EVEING 新增代码采用 **GPL-3.0-only**，继承的 wx_channel 文件保留 MIT 版权；Gopeed、SunnyNet 等组件遵循各自许可证。详见 [LICENSE](LICENSE)、[NOTICE](NOTICE)、[组件清单](THIRD-PARTY-NOTICES.md) 与 [来源审计](docs/PROVENANCE.md)。本项目不包含 NetFilter SDK 商业驱动/API，与相关平台及上游作者没有官方隶属关系。

## 开发与免费声明

本软件由 **EVEING** 开发与维护，官方版本免费提供，源代码公开。

如您通过任何渠道付费购买本软件，请立即向销售方或交易平台申请退款。

请通过官方 GitHub 仓库免费获取安装包及后续更新。

请仅下载您拥有版权或已获授权的视频，遵守法律法规及平台规则，尊重著作权与个人隐私。

**问题反馈：** [GitHub Issues](https://github.com/0399199-stack/eveing-lumiven/issues) · [0399199@gmail.com](mailto:0399199@gmail.com)

请附上软件版本、系统版本、问题现象与复现步骤；不要提交密码、验证码、Cookie 或其他敏感信息。
