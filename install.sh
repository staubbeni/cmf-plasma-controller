#!/usr/bin/env bash
# install.sh — Install CMF Buds Plasma 6 widget + daemon on Fedora KDE
# Usage: bash install.sh [--uninstall]
set -euo pipefail

PLASMOID_ID="org.kde.cmfbuds"
PLASMOID_SRC="$(dirname "$0")/plasmoid/${PLASMOID_ID}"
PLASMOID_DEST="${HOME}/.local/share/plasma/plasmoids/${PLASMOID_ID}"
DAEMON_SRC="$(dirname "$0")/backend/CmfBudsService"
DAEMON_BIN="${HOME}/.local/bin/cmfd"
DBUS_SERVICE_DIR="${HOME}/.local/share/dbus-1/services"
DBUS_SERVICE_FILE="${DBUS_SERVICE_DIR}/org.kde.cmfbuds.service"

# ─── Uninstall ─────────────────────────────────────────────────────────────
if [[ "${1:-}" == "--uninstall" ]]; then
    echo "▶ Uninstalling CMF Buds controller..."
    rm -rf  "${PLASMOID_DEST}"
    rm -f   "${DAEMON_BIN}"
    rm -f   "${DBUS_SERVICE_FILE}"
    kquitapp6 plasmashell 2>/dev/null || true
    sleep 1
    kstart plasmashell &>/dev/null &
    echo "✔ Uninstalled.  Plasma shell restarted."
    exit 0
fi

# ─── Dependency check ──────────────────────────────────────────────────────
echo "▶ Checking dependencies..."

if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK not found."
    echo "Install with: sudo dnf install dotnet-sdk-8.0"
    exit 1
fi

DOTNET_MAJOR=$(dotnet --version | cut -d. -f1)
if (( DOTNET_MAJOR < 8 )); then
    echo "ERROR: .NET 8+ required (found $(dotnet --version))."
    echo "Install with: sudo dnf install dotnet-sdk-8.0"
    exit 1
fi

if ! command -v bluetoothctl &>/dev/null; then
    echo "ERROR: bluetoothctl not found. Install BlueZ: sudo dnf install bluez"
    exit 1
fi

# ─── Build daemon ──────────────────────────────────────────────────────────
echo "▶ Building cmfd daemon..."
dotnet publish "${DAEMON_SRC}/CmfBudsService.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -p:PublishSingleFile=true \
    -o /tmp/cmfd-build

mkdir -p "$(dirname "${DAEMON_BIN}")"
cp /tmp/cmfd-build/cmfd "${DAEMON_BIN}"
chmod +x "${DAEMON_BIN}"
echo "  ✔ Daemon installed to ${DAEMON_BIN}"

# ─── D-Bus service activation file ────────────────────────────────────────
echo "▶ Installing D-Bus service activation file..."
mkdir -p "${DBUS_SERVICE_DIR}"
cat > "${DBUS_SERVICE_FILE}" <<EOF
[D-BUS Service]
Name=org.kde.cmfbuds
Exec=${DAEMON_BIN}
EOF
echo "  ✔ D-Bus activation: ${DBUS_SERVICE_FILE}"

# ─── Install Plasmoid ──────────────────────────────────────────────────────
echo "▶ Installing plasmoid..."
mkdir -p "${PLASMOID_DEST}"
cp -r "${PLASMOID_SRC}/." "${PLASMOID_DEST}/"
echo "  ✔ Plasmoid installed to ${PLASMOID_DEST}"

# ─── Restart Plasma shell ─────────────────────────────────────────────────
echo "▶ Restarting Plasma shell..."
kquitapp6 plasmashell 2>/dev/null || true
sleep 1
kstart plasmashell &>/dev/null &
echo "  ✔ Plasma shell restarted."

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo " CMF Buds Controller installed successfully!"
echo ""
echo " Next steps:"
echo "   1. Right-click the system tray → Add Widgets"
echo "   2. Search for 'CMF Buds Controller' and add it"
echo "   3. Open the widget settings and select your device"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
