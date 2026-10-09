# 来源与再分发审计

审计日期：2026-09-28。主内核基线为 [nobiyou/wx_channel](https://github.com/nobiyou/wx_channel/tree/889abf890cc14772beccfc8933f7c6e699269c55)。根 LICENSE 是 nobiyou 的 MIT，不覆盖独立组件的许可。

## SunnyNet 与 NetFilter

基线将 `github.com/qtgolang/SunnyNet v1.0.3` replace 到 `pkg/sunnynet`，但未附 LICENSE，也未找到对应上游 v1.0.3 tag。上游 [固定提交 64c9dad6cbef5df4e6caa05681e4e22e03bc77cd 的 LICENSE](https://github.com/qtgolang/SunnyNet/blob/64c9dad6cbef5df4e6caa05681e4e22e03bc77cd/LICENSE) 为秦天的中文 MIT。Lumiven 保留版权与文件级 Go/Gorilla/Opsmate 通知，并附完整文本；不将该提交冒充整个 vendored fork 的逐文件一致来源。

原内核的 WFP 驱动及 Driver 桥与该 SunnyNet 提交存在字节一致证据：

| 原文件 | Git blob |
| --- | --- |
| WFP amd64 netfilter2.sys | `71581bb5972e344a778127ed46dc63bf66cd0e25` |
| WFP i386 netfilter2.sys | `847cfe442ae65e3f427ae77cd66cb905821ab9df` |
| Driver.go | `96a4e6cc09a228af306cb3491960f345be03adb1` |
| Driver.c | `bc2e1fe41eb4b2122f49310c6ffb375159c9160e` |
| Driver.h | `9b63c07a39ab5a503495472aed15a0e1228a684e` |

上游缺失的两份 nfapi.dll 可从相同提交定位，版本信息为 FileVersion 1.5.2.3、ProductVersion 1.6.6.0。但 **Lumiven 不分发它们**：NetFilter SDK 官方 [License Statement](https://netfiltersdk.com/license.html) 限制 SDK 衍生源码再分发；仓库中的 MIT 文本不能替代组件权利人的许可。

因此本项目删除 `Resource/nfapi`、商业 API 绑定及上游三个预编译 exe，使用 SunnyNet HTTP/HTTPS 代理与显式的当前用户 WinInet 代理启用/恢复。不打包 NetFilter 驱动、nfapi.dll 或 SDK 源码。构建前 `scripts/Verify-Licenses.ps1` 强制检查这些残留。

## 许可文本的固定来源

- SunnyNet MIT：`qtgolang/SunnyNet@64c9dad6cbef5df4e6caa05681e4e22e03bc77cd/LICENSE`。
- Go BSD：`golang/go@go1.24.3/LICENSE`。
- Gorilla BSD：`gorilla/websocket@ce903f6d1d961af3a8602f2842c8b1c3fca58c4d/LICENSE`。
- PKCS12 BSD：`SSLMate/go-pkcs12@c0472edb16891765fbc86573ea468365b7fd2197/LICENSE`。
- RC2 MIT：`dgryski/go-rc2@8a9021637152186df738b1ec376caf2100fef194/LICENSE`。
- Gopeed GPL：`GopeedLab/gopeed@v1.8.3/LICENSE`。

运行时的 Go 模块版本由 go.sum 校验。发布构建保存实际模块版本与许可证，源码归档另带 Go vendor 源码。Windows App SDK / .NET 的固定版本与 NuGet 许可随发布包保存。

