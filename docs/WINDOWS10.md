# Windows 10 兼容候选

版本：1.0.1-rc.1。最低目标为 Windows 10 22H2 x64（10.0.19045），不包含 Windows 7/8、32 位或更早 Win10 版本。当前没有 Win10 实机或虚拟机，不能将编译、降低版本门槛或 Win11 测试当作 Win10 验收。

## 已做的适配

- 应用 `TargetPlatformMinVersion`、服务测试最低系统版本和安装器 `MinVersion` 一致降至 19045。编译使用的 Windows SDK 26100 保持不变。
- 标题栏自定义先检查 `AppWindowTitleBar.IsCustomizationSupported()`；不支持时保留系统标题栏。
- 桌面透明背景先检查 `DesktopAcrylicController.IsSupported()`；不支持或关闭系统透明效果时使用实色主题。
- .NET 与 Windows App SDK 自包含发布；原生依赖检查仍以构建机为准，不能证明 Win10 可加载全部依赖。

微软说明 Windows App SDK 可向下运行至 Windows 10 1809；本项目主动限定为 22H2，以控制验收范围。[官方版本说明](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)

标题栏颜色自定义在 Windows 10 上会被忽略，外观可能与 Win11 略有不同。[官方标题栏说明](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar)

操作系统技术可运行范围与厂商维护支持并不相同。.NET 的支持依赖 Windows 版本及其维护状态，不能因为允许安装就承诺所有 Win10 版本仍获微软支持。[.NET 10 系统支持表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

## 无实机时的验收方法

可在合法授权的 Windows 10 22H2 x64 虚拟机中完成第一轮功能验收。虚拟机能验证启动及大部分功能，但不能代替所有显卡、媒体解码和网络环境验证；本轮没有下载系统镜像或修改虚拟化设置。

| 项目 | Windows 10 状态 |
| --- | --- |
| 干净系统安装、启动、升级、卸载 | 待验证 |
| 标题栏拖动、缩放、托盘隐藏与退出 | 待验证 |
| 浅色、深色、系统主题、关闭透明效果 | 待验证 |
| 导入、播放、打开目录、中文长路径 | 待验证 |
| 两种删除、取消、刷新、重启、文件被占用 | 待验证 |
| 用户主动启用微信下载、下载完成、退出恢复代理 | 待验证 |

在这些检查完成前，以“Win10 兼容测试版”分发，不宣称已经完整支持 Win10。
