import QtQuick
import org.kde.plasma.plasmoid

/**
 * CmfDbusHelper.qml
 *
 * Thin QML wrapper around the org.kde.cmfbuds D-Bus session-bus service.
 * The daemon (cmfd) is auto-started via D-Bus service activation when the
 * first method call is made — no manual process management required.
 *
 * Properties exposed to the rest of the UI:
 *   currentMode      – "off" | "anc" | "transparency"
 *   connectionState  – "disconnected" | "connecting" | "connected" | "error"
 *   batteryLeft      – 0–100, or -1 if unknown
 *   batteryRight     – 0–100, or -1 if unknown
 *   batteryCase      – 0–100, or -1 if unknown
 */
QtObject {
    id: helper

    property string currentMode:     "off"
    property string connectionState: "disconnected"
    property int    batteryLeft:     -1
    property int    batteryRight:    -1
    property int    batteryCase:     -1

    // ------------------------------------------------------------------
    // D-Bus interface
    // ------------------------------------------------------------------
    readonly property var _iface: Qt.createQmlObject(`
        import QtQuick
        import org.kde.plasma.core as PlasmaCore

        PlasmaCore.DataSource {
            id: dbusSource
            engine: "org.kde.plasma.dbus"

            // Not used — we drive everything through explicit DBus calls below.
        }
    `, helper, "dbusSource")

    // We use Plasmoid's built-in Qt.dbus call mechanism for method calls.
    // For signals we rely on a polling timer as a safe fallback that works
    // across all Plasma 6 versions without optional C++ plugins.
    readonly property var _bus: Qt.createQmlObject(`
        import QtQuick
        import org.kde.plasma.plasma5support as P5Support

        P5Support.DataSource {
            engine:          "executable"
            connectedSources: []
        }
    `, helper, "execSource")

    // ------------------------------------------------------------------
    // Public API — called from the UI
    // ------------------------------------------------------------------

    function setAncMode(mode) {
        _dbusCall("SetAncMode", [mode])
        currentMode = mode
    }

    function getPairedDevices(callback) {
        _dbusCallWithResult("GetPairedDevices", [], function(result) {
            callback(result || [])
        })
    }

    function setMacAddress(mac) {
        _dbusCall("SetMacAddress", [mac])
    }

    // ------------------------------------------------------------------
    // Polling timer — refreshes battery + state every 65 seconds
    // (slightly offset from the daemon's 60-second poll)
    // ------------------------------------------------------------------
    readonly property var _pollTimer: Timer {
        interval: 65000
        repeat:   true
        running:  true
        onTriggered: helper._refresh()
    }

    // Immediate refresh on creation
    Component.onCompleted: {
        Qt.callLater(_refresh)
    }

    function _refresh() {
        _dbusCallWithResult("GetBatteryLevels", [], function(levels) {
            if (levels && typeof levels === "object") {
                batteryLeft  = levels["left"]  !== undefined ? levels["left"]  : -1
                batteryRight = levels["right"] !== undefined ? levels["right"] : -1
                batteryCase  = levels["case"]  !== undefined ? levels["case"]  : -1
            }
        })
        _dbusCallWithResult("GetCurrentMode", [], function(mode) {
            if (mode) currentMode = mode
        })
        _dbusCallWithResult("GetConnectionState", [], function(state) {
            if (state) connectionState = state
        })
    }

    // ------------------------------------------------------------------
    // Low-level D-Bus call helpers using qdbus subprocess
    // ------------------------------------------------------------------

    function _dbusCall(method, args) {
        let argStr = args.map(a => JSON.stringify(a)).join(" ")
        let cmd = `qdbus org.kde.cmfbuds /org/kde/cmfbuds org.kde.cmfbuds.${method} ${argStr}`
        _runCmd(cmd, null)
    }

    function _dbusCallWithResult(method, args, callback) {
        let argStr = args.map(a => JSON.stringify(a)).join(" ")
        let cmd = `qdbus org.kde.cmfbuds /org/kde/cmfbuds org.kde.cmfbuds.${method} ${argStr}`
        _runCmd(cmd, callback)
    }

    function _runCmd(cmd, callback) {
        let src = _bus
        if (!src) return
        src.connectedSources = []
        let conn = src.newData.connect(function(sourceName, data) {
            src.newData.disconnect(conn)
            src.connectedSources = []
            if (callback && data && data["stdout"])
                callback(_parseResult(data["stdout"].trim()))
            else if (callback)
                callback(null)
        })
        src.connectSource(cmd)
    }

    function _parseResult(raw) {
        if (!raw) return null
        // Try JSON parse first (dict/array results)
        try { return JSON.parse(raw) } catch (_) {}
        // Array of strings (newline-separated)
        if (raw.includes("\n")) return raw.split("\n").filter(l => l)
        return raw
    }
}
