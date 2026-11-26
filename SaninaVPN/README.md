# Hivox

Desktop (Windows WPF, .NET 10) and Mobile (Android MAUI, .NET 10) VPN client.

## Supported Cores, Modes & Protocols by Platform

| Platform | Routing Cores | Modes | Supported Protocols |
| :--- | :--- | :--- | :--- |
| **Windows (Desktop)** | `Xray-core` + `Sing-box` | System Proxy, TUN (`wintun.dll`, `asInvoker` with on-demand UAC prompt) | `VLESS`, `VMess`, `Trojan`, `Shadowsocks`, `Hysteria2`, `TUIC`, `WireGuard`, `SOCKS5`, `AnyTLS`, `SSH` |
| **Android (Mobile)** | `Xray-core` | VPN / TUN (`VpnService`), Proxy-only | `VLESS`, `VMess`, `Trojan`, `Shadowsocks`, `Hysteria2`, `SOCKS5` |

## Build (Windows Desktop)

```powershell
dotnet publish Hivox.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o publish\release-win-x64
```

### Native Installer (Inno Setup 6)

```powershell
iscc Installer\HivoxSetup.iss
```

### Code Signing & Checksums

- Release artifacts include `github-release\SHA256SUMS.txt` for integrity verification.
- Sign both `Hivox.exe` and `Hivox-Setup-v2.1.0-win-x64.exe` with `signtool.exe` (or Azure Trusted Signing) before public distribution to prevent Windows SmartScreen warnings.

## Requirements (Windows)

- Windows 10 / 11 (x64)
- Administrator elevation (requested via UAC when starting TUN mode; Proxy mode runs without elevation)
