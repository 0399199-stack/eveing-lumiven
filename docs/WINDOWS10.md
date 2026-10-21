# Windows 10 兼容记录

版本：V1.2.0。兼容目标为 Windows 10 22H2 x64（10.0.19045）与 Windows 11 x64，不包含 Windows 7/8、32 位、ARM64 或更早 Win10 版本。2026-10-19 维护者确认此前 Win10 兼容重构版可打开并正常使用；2026-10-21 要求 V1.2.0 继续兼容 Win10。当前程序、安装器最低版本和兼容降级保持一致，本轮新增解析、主题、语言与修复入口在 Windows 11 验收，尚无本次完整 Win10 实机复测；不能从旧版基础使用推导新功能全部通过。

## 已做的适配

- 应用 `TargetPlatformMinVersion`、服务测试最低系统版本和安装器 `MinVersion` 一致降至 19045。编译使用的 Windows SDK 26100 保持不变。
- 标题栏自定义先检查 `AppWindowTitleBar.IsCustomizationSupported()`；不支持时保留系统标题栏。
- 桌面透明背景先检查 `DesktopAcrylicController.IsSupported()`；不支持或关闭系统透明效果时使用实色主题。
- .NET 与 Windows App SDK 自包含发布；原生依赖检查仍以构建机为准，不能证明 Win10 可加载全部依赖。
- 新版沿用标题栏和桌面材质支持检查；新增的文件定位使用 Win10 已有的 Shell 接口，不依赖 Win11 专属命令。系统关闭动画或透明效果时遵循系统设置。

微软说明 Windows App SDK 可向下运行至 Windows 10 1809；本项目主动限定为 22H2，以控制验收范围。[官方版本说明](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)

标题栏颜色自定义在 Windows 10 上会被忽略，外观可能与 Win11 略有不同。[官方标题栏说明](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar)

操作系统技术可运行范围与厂商维护支持并不相同。.NET 的支持依赖 Windows 版本及其维护状态，不能因为允许安装就承诺所有 Win10 版本仍获微软支持。[.NET 10 系统支持表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

## 无实机时的验收方法

可在合法授权的 Windows 10 22H2 x64 虚拟机中完成第一轮功能验收。虚拟机能验证启动及大部分功能，但不能代替所有显卡、媒体解码和网络环境验证；本轮没有下载系统镜像或修改虚拟化设置。

| 项目 | Windows 10 状态 |
| --- | --- |
| 启动及基础使用 | 用户于 2026-10-19 确认可正常使用；未提供详细系统构建号 |
| 干净系统安装、升级、卸载 | 待验证 |
| 标题栏拖动、缩放、托盘隐藏与退出 | 待验证 |
| 浅色、深色、系统主题、关闭透明效果 | 待验证 |
| 导入、播放、打开目录、中文长路径 | 待验证 |
| 两种删除、取消、刷新、重启、文件被占用 | 待验证 |
| 用户主动启用微信下载、下载完成、退出恢复代理 | 待验证 |

V1.2.0 正式安装包允许 Win10 22H2 x64 安装，并保留上述兼容措施；发布说明公开 Win10 本次新增功能仍需实机补充验收的边界，不宣称这张清单已经全部通过。
