# EVEING Lumiven V1.2.0

[简体中文](https://github.com/0399199-stack/eveing-lumiven/releases/tag/v1.2.0) · **English** · [Project overview](../README.en.md)

**Three video sources. One local library.**

V1.2.0 adds Douyin and Xiaohongshu video downloads, Chinese and English interfaces, theme switching and clearer update information.

## Download

| File | Purpose |
| :--- | :--- |
| [Windows x64 installer](https://github.com/0399199-stack/eveing-lumiven/releases/download/v1.2.0/EVEING-Lumiven-Setup-1.2.0-x64.exe) | Install or upgrade Lumiven |
| [Corresponding source ZIP](https://github.com/0399199-stack/eveing-lumiven/releases/download/v1.2.0/EVEING-Lumiven-Source-1.2.0.zip) | Source for the released build |
| [SHA256SUMS](https://github.com/0399199-stack/eveing-lumiven/releases/download/v1.2.0/SHA256SUMS) | Verify downloaded files |

## What's new

- **Video parsing:** Paste a Douyin video share short link or a Xiaohongshu video-note/share link, including complete share text, to parse and download. No manual Cookie entry is required. Restricted content may fail to download.
- **Automatic library entry:** Completed downloads appear in Downloaded, with covers, title/creator search, playback, file location and persistent history. Watermark-free sources are preferred when available; embedded watermarks are not erased.
- **WeChat Channels guidance:** The former Downloads page is now Channels downloads, with certificate and download-control instructions.
- **Appearance and language:** Light, dark and system themes; Chinese and English interfaces; current version in the sidebar.
- **Channels connection repair:** Stop Lumiven integration and restore its saved connection settings. Only Lumiven's proxy/certificate state is handled; Windows security prompts still require your confirmation.
- **Clearer controls:** Startup usage/free-distribution notice, a “Don't show again” option, an official GitHub button and a way to clear finished parsing tasks while retaining videos and library records.
- **Update details:** New-version notes appear on the update page and installation confirmation. Updates continue to use official GitHub Releases, background downloads and SHA-256 verification.

## Fixes & improvements

- Fixed missed library refreshes when completion and scanning overlap, inconsistent state after record-write failures and truncated responses incorrectly marked complete.
- Added fallback video sources for Xiaohongshu, improved timeouts, cancellation and actual-quality recording, and fixed URLs touching adjacent Chinese text.
- Fixed file location for special characters, playback after missing files are restored, stale player callbacks and text showing through the player background.
- Improved theme color consistency, prompts and list interactions while retaining library and batch-removal features.

## Installation & upgrade

- Compatibility target: **Windows 10 22H2 x64 (19045) and Windows 11 x64**. Earlier Windows 10, 32-bit and ARM64 are not supported.
- Windows 10 uses title-bar and transparency fallbacks. Basic use of an earlier compatible build was confirmed on a real Windows 10 machine; V1.2.0's new features have not been fully retested there. See the [compatibility record](WINDOWS10.md).
- .NET and Windows App SDK are bundled; no separate runtime installation is needed.
- **V1.1.0 / V1.1.1 users:** Check for updates in the app. With automatic updates enabled, startup checks and background downloads are automatic; installation requires your confirmation. V1.0.x users should install this version manually.
- Upgrades preserve videos, settings and history. Finish downloads and stop WeChat integration before installing. The installer is unsigned; checksums and corresponding source are provided above.

## Free distribution & feedback

Developed and maintained by **EVEING**. Official versions are free, and the source code is public. If you paid for the software through any channel, please request a refund from the seller or transaction platform immediately. Get installers and future updates from the official GitHub repository.

Save only content you own or are authorized to download. Feedback: [0399199@gmail.com](mailto:0399199@gmail.com).

[Back to the English overview](../README.en.md) · [All releases](https://github.com/0399199-stack/eveing-lumiven/releases)
