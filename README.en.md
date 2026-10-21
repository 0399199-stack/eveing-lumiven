<div align="center">

![EVEING Lumiven — Save. Organize. Revisit.](docs/design/github-banner.svg)

[简体中文](README.md) · **English**

### Keep the videos you love. Make them easy to find.

A free, open-source video downloader and local media library for Windows.

WeChat Channels · Douyin · Xiaohongshu — download, organize and watch in one clean desktop app.

[![Latest release](https://img.shields.io/github/v/release/0399199-stack/eveing-lumiven?style=flat-square&color=4185d7&label=Release)](https://github.com/0399199-stack/eveing-lumiven/releases/latest) [![Windows 10 22H2 / 11 x64](https://img.shields.io/badge/Windows-10%2022H2%20%2F%2011%20x64-64748b?style=flat-square)](docs/WINDOWS10.md) [![GPL-3.0-only](https://img.shields.io/badge/License-GPL--3.0--only-64748b?style=flat-square)](LICENSE)

**[Download for Windows](https://github.com/0399199-stack/eveing-lumiven/releases/latest)** &nbsp; · &nbsp; **[What's new in V1.2.0](docs/RELEASE-NOTES.en.md)** &nbsp; · &nbsp; **[Getting started](#getting-started)**

</div>

---

## From download to playback, in one place

| Download | Organize | Make it yours |
| :--- | :--- | :--- |
| **WeChat Channels** — Choose a quality and download inside WeChat; track progress and results in Lumiven. | **Local library** — Completed downloads appear automatically. Search by title or creator and keep your history after restarting. | **Glass-inspired design** — Light, dark or system theme, with Chinese and English interfaces. |
| **Douyin & Xiaohongshu** — Paste a video share link or the complete share text to parse and download. | **Play & locate** — Built-in playback, fullscreen and file location, with a system-player option for unsupported codecs. | **GitHub updates** — Automatic checks, background downloads and SHA-256 verification. Installation requires your confirmation. |
| **Task tracking** — View progress and errors; clear finished parsing tasks separately. | **Import & tidy up** — Import existing folders without moving files. Removing library records keeps videos by default. | **Channels help** — In-app instructions and a repair option for connection state left by Lumiven. |

### Supported links

| Source | V1.2.0 scope | Where to start |
| :--- | :--- | :--- |
| WeChat Channels | Videos in desktop WeChat Channels; local integration must be enabled first | Channels downloads |
| Douyin | Video share short links on `v.douyin.com`, including links within copied share text | Video parsing → Douyin |
| Xiaohongshu | Video-note URLs and share short links | Video parsing → Xiaohongshu |

Lumiven prefers watermark-free sources when the platform makes them available; it does not erase watermarks embedded in the picture. Image galleries, live recording and restricted content are outside this release's scope. Platform changes, network conditions or access restrictions may prevent parsing.

<details>
<summary><strong>Interface preview and design reference</strong></summary>

![Lumiven library design reference](docs/design/approved-library.png)

This is the project's approved design reference, **not a screenshot of the running app**.

![Earlier Lumiven build running a library performance test](docs/verification/runtime-performance-library.jpg)

This is a real window from an earlier build, with 1,000 synthetic placeholder records for performance testing. It contains no real user videos and does not show all V1.2.0 pages. See the [validation record](docs/VALIDATION.md) for the tested scope.

</details>

## Download & requirements

**[Get the latest stable release →](https://github.com/0399199-stack/eveing-lumiven/releases/latest)** Choose the `.exe` installer with `Setup` in its name, such as `EVEING-Lumiven-Setup-1.2.0-x64.exe`.

| Item | Details |
| :--- | :--- |
| Operating system | Windows 10 22H2 x64 (19045) or Windows 11 x64; earlier Windows 10, 32-bit and ARM64 are not supported |
| Runtime | .NET and Windows App SDK are bundled; no separate Go or .NET installation required |
| Release assets | Windows installer, corresponding source ZIP and `SHA256SUMS` |
| Your data | Upgrades preserve settings, download history and videos. Uninstalling retains user data and downloaded videos |

**Windows 10 validation:** Basic use of an earlier compatible build was confirmed on a real Windows 10 machine. V1.2.0's new features have not been fully retested there. Title-bar and transparency effects fall back where needed; see the [compatibility record](docs/WINDOWS10.md). The installer is currently unsigned.

## Getting started

### Douyin / Xiaohongshu

1. Open **Video parsing** and select the platform.
2. Paste the video share link or complete share text, then choose **Parse and download**.
3. Open **Downloaded** to play the saved video or locate its file.

These platforms do not require enabling WeChat integration or installing its certificate.

### WeChat Channels

1. In **Channels downloads**, enable WeChat downloading and read the explanation of the local certificate, HTTPS traffic handling and current-user proxy. Review and personally confirm any Windows certificate prompt.
2. Reopen Channels in desktop WeChat. Use the added download control to choose a quality and save a video.
3. Track the task in Lumiven, then open **Downloaded** to play or locate the file.
4. Stop the integration when finished. Lumiven restores the previous proxy state and removes the certificate installed for the session; complete any Windows confirmation prompt.

If Channels stops loading, use the repair option in Settings. It handles only connection state saved by Lumiven, without resetting another app's proxy or deleting unrelated certificates. WeChat page structure and proxy behavior may change, so compatibility with every WeChat version is not guaranteed.

<details>
<summary><strong>File management, deletion and installation behavior</strong></summary>

- Choose the save folder in Settings. Importing an existing folder indexes videos without moving them. Files moved or deleted elsewhere are marked missing.
- Card menus and batch selection can remove library records while keeping videos by default. Explicitly choosing and confirming local-file deletion **permanently deletes files without using the Recycle Bin**. Failed records remain if files are in use.
- Refreshing, scanning or old task notifications will not restore removed records. A new download can appear again. Older versions cannot read the upgraded library index; do not downgrade over the newer version.
- The installer adds application files and your chosen shortcuts. It does not install drivers, silently change the proxy or enable startup at login. Stop WeChat integration before uninstalling.

</details>

## Updates

By default, Lumiven checks the official GitHub Releases for a newer stable version at startup, downloads it in the background and verifies it. **You confirm installation.** Manual checks are available in Settings and About; automatic updates can be disabled in Settings. V1.2.0 and later show what's new before installing.

**V1.1.0 / V1.1.1 users can upgrade inside the app.** V1.0.x users need to install a newer version manually first. Finish active downloads and stop WeChat integration before installing. No GitHub account is required. See [update documentation](docs/UPDATES.md).

## Development & documentation

WinUI 3 desktop interface · C# / .NET · Separate Go core for WeChat Channels

| Document | Contents |
| :--- | :--- |
| [Build guide](docs/BUILD.md) | Pinned toolchains, source builds and packaging |
| [Release notes](docs/RELEASE-NOTES.en.md) | What's new in V1.2.0; [full Chinese history](docs/RELEASE-NOTES.md) |
| [Validation record](docs/VALIDATION.md) | Verified results and testing limitations |
| [Windows 10 compatibility](docs/WINDOWS10.md) | System requirements and validation scope |
| [Third-party notices](THIRD-PARTY-NOTICES.md) | Upstream projects, versions and licenses |

Detailed engineering and validation documents are currently maintained in Chinese.

<details>
<summary><strong>Build from source</strong></summary>

Use PowerShell 7 on x64 Windows 11. Pinned tools include .NET SDK 10.0.401, Go 1.27.1, Zig 0.13.0, Node 24.19.0 (tests) and Inno Setup 6.5.4. Download URLs and checksums are recorded in `scripts/toolchain.json`.

```powershell
pwsh ./scripts/Prepare-Tools.ps1 -IncludeInstaller
pwsh ./scripts/Verify-Licenses.ps1
pwsh ./scripts/Build.ps1 -Installer
# On the exact committed source in a clean worktree, create source and checksums:
pwsh ./scripts/Build.ps1 -Installer -SourceArchive
```

Outputs are written to `artifacts/release`. GitHub Actions builds and tests main; a stable release also requires the relevant acceptance checks. See the [build guide](docs/BUILD.md) for details.

</details>

## Open-source acknowledgments

The WeChat Channels core is based on [nobiyou/wx_channel](https://github.com/nobiyou/wx_channel). Douyin and Xiaohongshu parsing is adapted in C# with reference to [douyin-watermark-free-downloader](https://github.com/qgeng1465/douyin-watermark-free-downloader), [parse-video](https://github.com/wujunwei928/parse-video) and [xhs-downloader](https://github.com/Andy-SoulShell/xhs-downloader). Thank you to their authors and the open-source community.

New EVEING code is licensed under **GPL-3.0-only**. Inherited wx_channel files retain their MIT notices; Gopeed, SunnyNet and other components retain their respective licenses. See [LICENSE](LICENSE), [NOTICE](NOTICE), [third-party notices](THIRD-PARTY-NOTICES.md) and [provenance](docs/PROVENANCE.md). NetFilter SDK commercial drivers/APIs are not included. This project is not officially affiliated with the platforms or upstream authors.

## Development & free distribution

Developed and maintained by **EVEING**. Official versions are free, and the source code is public.

If you paid for this software through any channel, please request a refund from the seller or transaction platform immediately.

Get installers and future updates free from the official GitHub repository.

Download only videos you own or are authorized to save. Follow applicable laws and platform rules, and respect copyright and privacy.

**Feedback:** [GitHub Issues](https://github.com/0399199-stack/eveing-lumiven/issues) · [0399199@gmail.com](mailto:0399199@gmail.com)

Include the app version, Windows version, what happened and steps to reproduce it. Do not share passwords, verification codes, cookies or other sensitive information.
