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
| mitt | wx_channel 内嵌的 [developit/mitt](https://github.com/developit/mitt) 事件组件 | MIT，`docs/licenses/Mitt-MIT.txt` |
| 其他 Go 模块 | `core/go.mod`、`core/go.sum` | 构建时从实际使用的模块缓存收集到 `licenses/go-modules/` |
| .NET / Windows App SDK | `scripts/toolchain.json`、`app/Lumiven/Lumiven.csproj` | 保留发布输出与 NuGet 包中的许可文本，附于 `licenses/dotnet-packages/` |
| 抖音分享页解析参考 | [qgeng1465/douyin-watermark-free-downloader](https://github.com/qgeng1465/douyin-watermark-free-downloader)，`c7ed2594b8d40416504e45e7d5ec22b342371504` | MIT，`docs/licenses/Douyin-Short-Link-MIT.txt`；使用 C# 实现短链接与分享页解析，不打包上游 GUI / Python |
| 抖音手机分享页请求参考 | [wujunwei928/parse-video](https://github.com/wujunwei928/parse-video)，`57590df94b49aafc827eb98e85c845eaa1cb3daf` | MIT，`docs/licenses/Parse-Video-MIT.txt`；参考移动端 User-Agent 与无尾斜杠页面请求，未引入 Cookie 详情接口 |
| 小红书页面解析参考 | [Andy-SoulShell/xhs-downloader](https://github.com/Andy-SoulShell/xhs-downloader)，`cc2bb34036acb12f5a722c95af7bad53ec696d03` | MIT，`docs/licenses/Xhs-Downloader-MIT.txt`；C# 适配初始状态与原视频地址解析，不打包浏览器或 Python |

安装包附完整许可文本。对应源码归档包含本项目提交、Go 依赖 vendor 源码、固定依赖清单与构建脚本。不得将上游 MIT 标记理解为覆盖 NetFilter SDK 的商业驱动/API 许可。
