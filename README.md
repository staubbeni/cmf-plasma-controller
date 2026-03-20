# CMF Buds Plasma Controller

A native **KDE Plasma 6** system-tray widget for managing **CMF Buds Pro 2** (by Nothing) on Linux.

### Features

- **ANC / Transparency** — Off, High, Mid, Low, Adaptive, Transparency modes
- **Equalizer** — 7 presets (Dirac OPTEO, Rock, Electronic, Pop, Vocals, Classical, Custom) + Custom EQ with bass/mid/treble sliders
- **Ultra Bass** — toggle + level 1–5
- **In-Ear Detection** — enable/disable
- **Low Latency Mode** — gaming mode toggle
- **Gestures** — read and configure pinch/hold actions per earbud
- **Find My Buds** — ring left/right bud
- **Battery** — Left / Right / Case with charging indicators
- **Firmware version** display

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  KDE Plasma Shell                                           │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  org.kde.cmfbuds  Plasmoid  (QML / Plasma 6)         │  │
│  │  • System-tray popup                                  │  │
│  │  • ANC, EQ, Ultra Bass, Gestures, Find My Buds       │  │
│  │  • Battery bars (Left / Right / Case)                 │  │
│  │  • Device picker (first run / settings)               │  │
│  └────────────────┬─────────────────────────────────────┘  │
│                   │  D-Bus (session bus)                    │
│  ┌────────────────▼─────────────────────────────────────┐  │
│  │  cmfd daemon  (C# / .NET 8)                          │  │
│  │  • org.kde.cmfbuds D-Bus service                     │  │
│  │  • Bluetooth RFCOMM socket (AF_BLUETOOTH ch 16)      │  │
│  │  • CMF "Dante" protocol packet builder + parser      │  │
│  │  • 30-second battery poll + full state refresh       │  │
│  │  • BlueZ device discovery via D-Bus                  │  │
│  └────────────────┬─────────────────────────────────────┘  │
│                   │  RFCOMM channel 16                      │
│  ┌────────────────▼──────────┐                             │
│  │  CMF Buds Pro 2 hardware  │                             │
│  └───────────────────────────┘                             │
└─────────────────────────────────────────────────────────────┘
```

### Why C# for the backend?

| Criterion | C# / .NET 8 | Python | C++ / Qt |
|---|---|---|---|
| Bluetooth RFCOMM | P/Invoke raw socket | `bleak` / `socket` | `QBluetoothSocket` |
| D-Bus service | `Tmds.DBus` NuGet | `dbus-python` | `QtDBus` |
| Distribution | `dotnet publish --self-contained` single binary | Requires venv + pip | Compiled, needs Qt dev headers |
| Runtime on Linux | `dotnet-sdk-8.0` (or self-contained) | Built-in | Qt6 (always present on Plasma) |

---

## Protocol Reference (CMF "Dante")

Wire format for every command and response:

| Byte(s) | Field | Value |
|---------|-------|-------|
| 0 | Preamble | `0x55` |
| 1 | Fixed | `0x60` |
| 2 | Fixed | `0x01` |
| 3–4 | Command code | `uint16` little-endian |
| 5 | Payload length | N bytes |
| 6 | Padding | `0x00` |
| 7 | Operation ID | rolling 0–255 |
| 8..8+N-1 | Payload | N bytes |
| 8+N – 9+N | CRC16 | CRC16-MODBUS of bytes 0..8+N-1 |

Response command code = `requestCmd & 0x7FFF` (bit 15 cleared).

Protocol values reverse-engineered from [ear-web](https://github.com/radiance-project/ear-web).

---

## Requirements

- **KDE Plasma 6** on any Linux distribution
- `.NET 8 SDK` or runtime: `sudo dnf install dotnet-sdk-8.0` / `sudo apt install dotnet-sdk-8.0`
- **BlueZ**: usually pre-installed (`bluez` package)
- CMF Buds Pro 2 **paired** via system Bluetooth settings

---

## Installation

```bash
bash install.sh
```

Then:
1. Right-click the system tray → **Add Widgets**
2. Search for **CMF Buds Controller** and add it
3. Open the widget settings and select your device from the paired-device list

### Uninstall

```bash
bash install.sh --uninstall
```

---

## Project Structure

```
cmf-plasma-controller/
├── backend/
│   └── CmfBudsService/
│       ├── CmfBudsService.csproj   # .NET 8 project (publishes as 'cmfd')
│       ├── Program.cs              # Entry point + D-Bus registration
│       ├── Protocol.cs             # CMF packet builder/parser + CRC16
│       ├── BluetoothService.cs     # RFCOMM socket via P/Invoke
│       ├── BluezInterfaces.cs      # BlueZ D-Bus interface bindings
│       ├── DeviceDiscovery.cs      # Paired device enumeration via BlueZ D-Bus
│       ├── ICmfBudsService.cs      # Tmds.DBus interface definition
│       ├── CmfBudsServiceImpl.cs   # Service impl + polling + notification dispatch
│       └── org.kde.cmfbuds.service # D-Bus activation file
├── plasmoid/
│   └── org.kde.cmfbuds/
│       ├── metadata.json
│       └── contents/
│           ├── config/main.xml         # KConfig schema
│           └── ui/
│               ├── main.qml            # Tray icon + compact repr
│               ├── FullRepresentation.qml # Popup UI
│               ├── ConfigPage.qml      # Device picker / settings
│               └── CmfDbusHelper.qml   # D-Bus bridge (QML → cmfd)
├── tests/
│   └── CmfBudsService.Tests/
│       ├── CmfBudsService.Tests.csproj
│       └── ProtocolTests.cs        # xUnit tests for protocol logic
├── install.sh
└── README.md
```

---

## Development

```bash
# Build the daemon
dotnet build backend/CmfBudsService

# Run tests
dotnet test tests/CmfBudsService.Tests

# Run the daemon manually
cmfd --mac XX:XX:XX:XX:XX:XX

# List paired devices
cmfd --list-devices

# Publish self-contained single binary
dotnet publish backend/CmfBudsService -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -o ./dist
```

---

## D-Bus API

Service: `org.kde.cmfbuds` · Object: `/org/kde/cmfbuds`

| Method | Args | Returns |
|---|---|---|
| `SetAncMode` | `string mode` (`off`/`transparency`/`anc_high`/`anc_mid`/`anc_low`/`anc_adaptive`) | — |
| `GetCurrentMode` | — | `string` |
| `GetBatteryLevels` | — | `string` (`"left N\nright N\ncase N"`) |
| `GetChargingStates` | — | `string` (`"left N\nright N\ncase N"`, 0/1) |
| `SetListeningMode` | `int level` (0–6) | — |
| `GetListeningMode` | — | `int` |
| `SetCustomEq` | `int bass, int mid, int treble` (−6..+6) | — |
| `GetCustomEq` | — | `string` (`"bass N\nmid N\ntreble N"`) |
| `SetUltraBass` | `bool enabled, int level` (1–5) | — |
| `GetUltraBass` | — | `string` (`"enabled N\nlevel N"`) |
| `SetInEarDetection` | `bool enabled` | — |
| `GetInEarDetection` | — | `bool` |
| `SetLowLatency` | `bool enabled` | — |
| `GetLowLatency` | — | `bool` |
| `GetGestures` | — | `string[]` (`"side:gestureType:action"`) |
| `SetGesture` | `string side, int gestureType, int action` | — |
| `RingBud` | `string side, bool ringing` | — |
| `GetFirmwareVersion` | — | `string` |
| `GetPairedDevices` | — | `string[]` (`"MAC\|Name"`) |
| `SetMacAddress` | `string mac` | — |
| `GetMacAddress` | — | `string` |
| `GetConnectionState` | — | `string` (`disconnected`/`connecting`/`connected`) |

Signals: `BatteryUpdated`, `ModeChanged`, `ConnectionStateChanged`

---

## License

GPL-2.0-or-later