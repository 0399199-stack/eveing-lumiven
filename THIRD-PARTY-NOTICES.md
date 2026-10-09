# 开源组件与许可

EVEING 新增代码使用 GPL-3.0-only。免费提供是本项目的分发方式，不改变 GPL 赋予接收者的权利。

| 组件 | 来源 / 锁定版本 | 许可与文本 |
| --- | --- | --- |
| wx_channel | nobiyou/wx_channel，`889abf890cc14772beccfc8933f7c6e699269c55` | MIT，`core/LICENSE` |
| SunnyNet | 内核中的本地修改版；基线声明 v1.0.3，未发现对应上游 tag | MIT，`docs/licenses/SunnyNet-MIT.txt`；移除 NetFilter 后仅使用本地代理功能 |
| Gopeed | v1.8.3，版本由 `core/go.mod` 固定 | GPL-3.0，`docs/licenses/Gopeed-GPL-3.0.txt` |
| Go 派生 TLS/CPU 代码 | SunnyNet 中保留 Go Authors 版权的文件 | BSD-3-Clause，`docs/licenses/Go-BSD.txt` |
| Gorilla WebSocket 派生代码 | SunnyNet 内嵌 websocket 代码 | BSD-2-Clause，`docs/licenses/Gorilla-BSD.txt` |
| SSLMate / Opsmate PKCS12 | SunnyNet 内嵌 pkcs 代码 | BSD-3-Clause，`docs/licenses/PKCS12-BSD.txt` |
| Damian Gryski RC2 | pkcs 内嵌 RC2 实现 | MIT，`docs/licenses/RC2-MIT.txt` |
| JSZip / Pako / FileSaver | wx_channel 原有内嵌网页保存组件 | 选择兼容的 MIT 许可，`docs/licenses/JSZip-MIT-GPL.txt`、`Pako-MIT.txt`、`FileSaver-MIT.txt` |
| 其他 Go 模块 | `core/go.mod`、`core/go.sum` | 构建时从实际使用的模块缓存收集到 `licenses/go-modules/` |
| .NET / Windows App SDK | `scripts/toolchain.json`、`app/Lumiven/Lumiven.csproj` | 保留发布输出与 NuGet 包中的许可文本，附于 `licenses/dotnet-packages/` |

安装包附完整许可文本。对应源码归档包含本项目提交、Go 依赖 vendor 源码、固定依赖清单与构建脚本。不得将上游 MIT 标记理解为覆盖 NetFilter SDK 的商业驱动/API 许可。
