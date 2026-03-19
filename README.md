# CMF Buds Plasma Controller

A native **KDE Plasma 6** system-tray widget for managing **CMF Buds Pro 2** (by Nothing) on Fedora Linux.

Control ANC/Transparency modes and monitor Left/Right/Case battery levels without a browser or phone app.

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  KDE Plasma Shell                                       │
│  ┌──────────────────────────────────────────────────┐  │
│  │  org.kde.cmfbuds  Plasmoid  (QML / Plasma 6)     │  │
│  │  • system-tray popup                              │  │
│  │  • ANC mode buttons (Off / ANC / Transparency)   │  │
│  │  • Battery bars (Left / Right / Case)             │  │
│  │  • First-run device picker                        │  │
│  └────────────────┬─────────────────────────────────┘  │
│                   │  D-Bus (session bus)                │
│  ┌────────────────▼─────────────────────────────────┐  │
│  │  cmfd daemon  (C# / .NET 8)                      │  │
│  │  • org.kde.cmfbuds D-Bus service                 │  │
│  │  • Bluetooth RFCOMM socket (AF_BLUETOOTH ch 15)  │  │
│  │  • CMF "Dante" protocol packet builder           │  │
│  │  • 60-second battery polling loop                │  │
│  │  • BlueZ device discovery via bluetoothctl       │  │
│  └────────────────┬─────────────────────────────────┘  │
│                   │  RFCOMM channel 15                  │
│  ┌────────────────▼──────────┐                         │
│  │  CMF Buds Pro 2 hardware  │                         │
│  └───────────────────────────┘                         │
└─────────────────────────────────────────────────────────┘
```

### Why C# for the backend?

| Criterion | C# / .NET 8 | Python | C++ / Qt |
|---|---|---|---|
| Bluetooth RFCOMM | P/Invoke raw socket | `bleak` / `socket` | `QBluetoothSocket` |
| D-Bus service | `Tmds.DBus` NuGet | `dbus-python` | `QtDBus` |
| Distribution | `dotnet publish --self-contained` single binary | Requires venv + pip | Compiled, needs Qt dev headers |
| Runtime on Fedora | `dotnet-sdk-8.0` (or self-contained) | Built-in | Qt6 (always present on Plasma) |

C# was chosen because:
1. `Tmds.DBus` provides a clean, typed interface for exposing D-Bus services.  
2. `dotnet publish --self-contained` creates a single executable — no venv, no pip.  
3. Async/await + `SemaphoreSlim` make the RFCOMM polling loop safe and readable.  
4. Strong typing catches protocol mistakes at compile time.

---

## Protocol Reference (CMF "Dante")

| Field    | Byte | Value |
|----------|------|-------|
| Preamble | 0    | `0x55` |
| Command  | 1    | `0x60` (ANC) / `0x61` (battery poll) |
| Mode     | 2    | `0x00` Off / `0x01` ANC / `0x02` Transparency |
| Checksum | last | XOR of all preceding bytes |

Battery response: `[0x55, 0x61, left%, right%, case%, checksum]`

---

## Requirements

- **Fedora 40+** with KDE Plasma 6
- `.NET 8 SDK`: `sudo dnf install dotnet-sdk-8.0`
- **BlueZ**: `sudo dnf install bluez` (usually pre-installed)
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
│       ├── Protocol.cs             # CMF packet builder/parser
│       ├── BluetoothService.cs     # RFCOMM socket via P/Invoke
│       ├── DeviceDiscovery.cs      # Paired device enumeration (bluetoothctl)
│       ├── ICmfBudsService.cs      # Tmds.DBus interface definition
│       ├── CmfBudsServiceImpl.cs   # Service impl + battery polling loop
│       └── org.kde.cmfbuds.service # D-Bus activation file
├── plasmoid/
│   └── org.kde.cmfbuds/
│       ├── metadata.json
│       └── contents/
│           ├── config/main.xml         # KConfig schema
│           └── ui/
│               ├── main.qml            # Tray icon + compact repr
│               ├── FullRepresentation.qml # Popup: mode buttons + battery
│               ├── ConfigPage.qml      # First-run / settings page
│               └── CmfDbusHelper.qml   # D-Bus bridge (QML → cmfd)
├── tests/
│   └── CmfBudsService.Tests/
│       ├── CmfBudsService.Tests.csproj
│       └── ProtocolTests.cs        # 21 xUnit tests for protocol logic
├── install.sh
└── README.md
```

---

## Development

```bash
# Build the daemon
dotnet build backend/CmfBudsService

# Run tests (21 xUnit tests)
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
| `SetAncMode` | `string mode` | — |
| `GetCurrentMode` | — | `string` |
| `GetBatteryLevels` | — | `{left,right,case: int}` |
| `GetPairedDevices` | — | `string[]` (`"MAC\|Name"`) |
| `SetMacAddress` | `string mac` | — |
| `GetMacAddress` | — | `string` |
| `GetConnectionState` | — | `string` |

Signals: `BatteryUpdated`, `ModeChanged`, `ConnectionStateChanged`

---

## Theme Compatibility

- `Kirigami.Theme.backgroundColor` — panel backgrounds  
- `Kirigami.Theme.highlightColor` — active ANC mode buttons  
- `PlasmaExtras.Background` — blur/translucency, **compatible with Layan/Kvantum**

---

## License

GPL-2.0-or-later