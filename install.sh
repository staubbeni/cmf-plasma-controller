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
SYSTEMD_USER_DIR="${HOME}/.config/systemd/user"
SYSTEMD_SERVICE_FILE="${SYSTEMD_USER_DIR}/cmfd.service"

# ─── Uninstall ─────────────────────────────────────────────────────────────
if [[ "${1:-}" == "--uninstall" ]]; then
    echo "▶ Uninstalling CMF Buds controller..."
    systemctl --user stop  cmfd 2>/dev/null || true
    systemctl --user disable cmfd 2>/dev/null || true
    rm -rf  "${PLASMOID_DEST}"
    rm -f   "${DAEMON_BIN}"
    rm -f   "${DBUS_SERVICE_FILE}"
    rm -f   "${SYSTEMD_SERVICE_FILE}"
    systemctl --user daemon-reload 2>/dev/null || true
    kquitapp6 plasmashell 2>/dev/null || true
    sleep 1
    kstart plasmashell &>/dev/null &
    echo "✔ Uninstalled.  Plasma shell restarted."
    exit 0
fi

# ─── Dependency check ──────────────────────────────────────────────────────
echo "▶ Checking dependencies..."

if ! command -v bluetoothctl &>/dev/null; then
    echo "ERROR: bluetoothctl not found. Install BlueZ:"
    echo "         sudo dnf install bluez   # Fedora"
    echo "         sudo apt install bluez   # Debian/Ubuntu"
    exit 1
fi

# ─── Build daemon (or use pre-built binary from release tarball) ───────────
PREBUILT="$(dirname "$0")/cmfd"
if [[ -f "${PREBUILT}" && -x "${PREBUILT}" ]]; then
    echo "▶ Using pre-built binary..."
    mkdir -p "$(dirname "${DAEMON_BIN}")"
    cp "${PREBUILT}" "${DAEMON_BIN}"
    chmod +x "${DAEMON_BIN}"
    echo "  ✔ Daemon installed to ${DAEMON_BIN}"
else
    # dotnet SDK required only when building from source
    if ! command -v dotnet &>/dev/null; then
        echo "ERROR: .NET SDK not found (needed to build from source)."
        echo "Install with: sudo dnf install dotnet-sdk-8.0   # Fedora"
        echo "              sudo apt install dotnet-sdk-8.0   # Debian/Ubuntu"
        echo "Or download the release tarball which includes a pre-built binary."
        exit 1
    fi

    DOTNET_MAJOR=$(dotnet --version | cut -d. -f1)
    if (( DOTNET_MAJOR < 8 )); then
        echo "ERROR: .NET 8+ SDK required to build (found $(dotnet --version))."
        exit 1
    fi

    echo "▶ Building cmfd daemon..."
    dotnet publish "${DAEMON_SRC}/CmfBudsService.csproj" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -o /tmp/cmfd-build

    mkdir -p "$(dirname "${DAEMON_BIN}")"
    cp /tmp/cmfd-build/cmfd "${DAEMON_BIN}"
    chmod +x "${DAEMON_BIN}"
    echo "  ✔ Daemon installed to ${DAEMON_BIN}"
fi

# ─── D-Bus service activation file ────────────────────────────────────────
echo "▶ Installing D-Bus service activation file..."
mkdir -p "${DBUS_SERVICE_DIR}"
cat > "${DBUS_SERVICE_FILE}" <<EOF
[D-BUS Service]
Name=org.kde.cmfbuds
Exec=${DAEMON_BIN}
EOF
echo "  ✔ D-Bus activation: ${DBUS_SERVICE_FILE}"

# ─── Systemd user service ──────────────────────────────────────────────────
echo "▶ Installing systemd user service..."
mkdir -p "${SYSTEMD_USER_DIR}"
cat > "${SYSTEMD_SERVICE_FILE}" <<EOF
[Unit]
Description=CMF Buds Plasma controller daemon
Documentation=https://github.com/staubbeni/cmf-plasma-controller
After=bluetooth.target
Wants=bluetooth.target

[Service]
ExecStart=${DAEMON_BIN}
Restart=on-failure
RestartSec=5
Environment=DBUS_SESSION_BUS_ADDRESS=%I

[Install]
WantedBy=default.target
EOF
systemctl --user daemon-reload
systemctl --user enable --now cmfd 2>/dev/null && \
    echo "  ✔ cmfd started and enabled (journalctl --user -u cmfd)" || \
    echo "  ✔ Systemd service installed (will start on next login)"

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
