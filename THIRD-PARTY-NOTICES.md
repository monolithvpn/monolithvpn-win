# Third-party components

This app bundles two prebuilt, unmodified binaries from other open source projects. Neither was written or modified by this project - they're included as-is so the two optional features that depend on them work without a separate manual install.

## WinDivert

- Project: https://github.com/basil00/WinDivert
- Used for: the Gaming tab's port/process-based traffic redirect (`PortRedirectService`, `AppSplitTunnelService`)
- License: dual LGPLv3 / GPLv2 (see `Assets/windivert/WinDivert-LICENSE.txt`, included verbatim)
- Files: `Assets/windivert/WinDivert.dll`, `Assets/windivert/WinDivert64.sys`

## udp2raw

- Project: https://github.com/wangyu-/udp2raw-multiplatform
- Used for: the optional traffic-obfuscation feature (`ObfuscationService`)
- License: GPL-3.0, per the upstream repository
- File: `Assets/udp2raw/udp2raw_mp.exe`

Everything else in this repository is original to this project and covered by the LICENSE file at the repository root.
