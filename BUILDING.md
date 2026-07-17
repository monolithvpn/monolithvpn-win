# Building from source

Requires the .NET 9 SDK and Windows (this is a WPF app, Windows-only).

```
dotnet restore
dotnet build -c Release
```

To produce a standalone single-file build:

```
dotnet publish MonolithVpnClient.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin/Release/publish
```

This produces a framework-dependent build - the .NET 9 Desktop Runtime needs to be installed on the machine running it.

## Building the installer

The installer script (`installer/MonolithVPN.iss`) is built with [Inno Setup](https://jrsoftware.org/isinfo.php). With `ISCC.exe` on your PATH:

```
ISCC.exe installer/MonolithVPN.iss /DPublishDir="bin/Release/publish" /DAppVersion="1.0.0"
```

Output lands in `bin/Release/MonolithVPN-Setup-<version>.exe`.

## Notes

- `AllowUnsafeBlocks` is required (used by `WinDivertInterop` to match a native struct layout).
- The app needs to run elevated (installing a WireGuard tunnel service requires admin rights), and expects [WireGuard for Windows](https://www.wireguard.com/install/) to already be installed - this app drives the official client rather than reimplementing the protocol.
