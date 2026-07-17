# MonolithVPN for Windows

This is the Windows client for [MonolithVPN](https://monolithvpn.lol), a WireGuard-based VPN service. It's a native WPF app, not a wrapper around a web page, and it doesn't reimplement WireGuard itself — it drives the official [WireGuard for Windows](https://www.wireguard.com/install/) client under the hood the same way a person clicking through the WireGuard GUI would, just automated and wired up to our own account/server system.

We're publishing the source because we'd rather let people check the "no-log" claims against actual code than just ask them to trust a paragraph on a marketing page.

## What's in here

- Connect/disconnect, server list with ping-based sorting, quick connect
- Kill switch and auto-reconnect (mutually exclusive on purpose — see `KillSwitchService.cs` and `ServersView.xaml.cs` for why)
- Split tunneling by IP range, UDP port, or process — send specific traffic around the tunnel instead of through it
- Multi-hop (chains two servers client-side)
- Optional traffic obfuscation for restrictive networks (wraps WireGuard traffic in fake-TCP framing via a bundled [udp2raw](https://github.com/wangyu-/udp2raw-multiplatform))
- Hotspot / Ethernet connection sharing
- A simplified single-card layout as an alternative to the full sidebar UI

## Requirements

- Windows 10 1809+ or Windows 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [WireGuard for Windows](https://www.wireguard.com/install/) installed separately — this app doesn't bundle it
- Admin rights (installing a WireGuard tunnel service needs elevation)

## Building

See [BUILDING.md](BUILDING.md).

## Third-party components

Two prebuilt binaries are bundled (WinDivert and udp2raw), both already open source themselves and unmodified from upstream. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details and licenses.

## License

GPL-3.0 — see [LICENSE](LICENSE).

## A note on what this repo isn't

This is the client only. The backend (account system, node provisioning, payments) isn't part of this release, so you won't be able to stand up your own instance of the whole service from this repo alone — the app expects to talk to `monolithvpn.lol`'s own API. If that changes down the line we'll say so here.
