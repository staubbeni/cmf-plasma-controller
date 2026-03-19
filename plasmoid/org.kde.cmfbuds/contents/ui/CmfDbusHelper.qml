import QtQuick
import org.kde.plasma.plasmoid
import org.kde.plasma.plasma5support as P5Support

/**
 * CmfDbusHelper.qml
 *
 * Thin QML wrapper around the org.kde.cmfbuds D-Bus session-bus service.
 * The daemon (cmfd) is auto-started via D-Bus service activation when the
 * first method call is made — no manual process management required.
 *
 * Properties exposed to the rest of the UI:
 *   ancMode              – "off" | "transparency" | "anc_high" | "anc_mid" | "anc_low" | "anc_adaptive"
 *   connectionState      – "disconnected" | "connecting" | "connected" | "error"
 *   batteryLeft/Right/Case – 0-100, or -1 if unknown
 *   batteryXxxCharging   – true when the bud/case is on charge
 *   listeningMode        – 0-6 (0=default preset … 5=preset, 6=custom)
 *   customEq             – {bass, mid, treble} each -6 to +6
 *   ultraBassEnabled     – bool
 *   ultraBassLevel       – 1-5
 *   inEarEnabled         – bool
 *   lowLatencyEnabled    – bool
 *   firmwareVersion      – version string e.g. "0.0.8.8"
 *   gestures             – array of "side:gestureType:action" strings
 */
QtObject {
    id: helper

    // ── ANC ─────────────────────────────────────────────────────────────────
    property string ancMode:         "off"
    property string connectionState: "disconnected"

    // Derived helpers used by the ANC sub-panel
    readonly property bool   ancIsNc:       ancMode.startsWith("anc_")
    readonly property string ancStrength:   ancMode.startsWith("anc_") ? ancMode.substring(4) : "high"

    // ── Battery ──────────────────────────────────────────────────────────────
    property int    batteryLeft:          -1
    property int    batteryRight:         -1
    property int    batteryCase:          -1
    property bool   batteryLeftCharging:  false
    property bool   batteryRightCharging: false
    property bool   batteryCaseCharging:  false

    // ── EQ ───────────────────────────────────────────────────────────────────
    property int    listeningMode:   0
    property var    customEq:        ({ bass: 0, mid: 0, treble: 0 })

    // ── Ultra Bass ───────────────────────────────────────────────────────────
    property bool   ultraBassEnabled: false
    property int    ultraBassLevel:   1

    // ── Quick Settings ───────────────────────────────────────────────────────
    property bool   inEarEnabled:      false
    property bool   lowLatencyEnabled: false
    property string firmwareVersion:   ""

    // ── Gestures ─────────────────────────────────────────────────────────────
    property var    gestures: []

    // ── Drive all comms via qdbus-qt6 subprocess ─────────────────────────────
    // Set this to plasmoid.configuration.macAddress from outside
    property string macAddress: ""
    onMacAddressChanged: {
        if (macAddress) {
            _dbusCall("SetMacAddress", [macAddress])
            // Poll repeatedly for a few seconds to catch the connect transition
            _quickPollCount = 10
            _quickPollTimer.restart()
        }
    }

    // Fast-poll after MAC set: check every 1.5s up to 10 times
    property int _quickPollCount: 0
    property Timer _quickPollTimer: Timer {
        interval: 1500
        repeat:   true
        onTriggered: {
            helper._pollCore()
            helper._quickPollCount -= 1
            if (helper._quickPollCount <= 0) stop()
        }
    }

    // Pending result callbacks, keyed by command string.
    property var _pendingCallbacks: ({})

    // Debug: last raw stdout received (visible in widget if needed)
    property string lastDebugOutput: ""

    property P5Support.DataSource _bus: P5Support.DataSource {
        engine: "executable"
        connectedSources: []
        onNewData: function(sourceName, data) {
            _bus.disconnectSource(sourceName)
            let stdout = (data && data["stdout"]) ? data["stdout"].trim() : ""
            let stderr = (data && data["stderr"]) ? data["stderr"].trim() : ""
            helper.lastDebugOutput = "CMD: " + sourceName + "\nOUT: " + stdout + (stderr ? "\nERR: " + stderr : "")
            let cb = helper._pendingCallbacks[sourceName]
            if (cb !== undefined) {
                let next = {}
                for (let k in helper._pendingCallbacks)
                    if (k !== sourceName) next[k] = helper._pendingCallbacks[k]
                helper._pendingCallbacks = next
                cb(stdout ? helper._parseResult(stdout) : null)
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    function setAncMode(mode) {
        _dbusCall("SetAncMode", [mode])
        ancMode = mode
    }

    function setListeningMode(level) {
        _dbusCall("SetListeningMode", [level])
        listeningMode = level
    }

    function setCustomEq(bass, mid, treble) {
        _dbusCall("SetCustomEq", [bass, mid, treble])
        customEq = { bass: bass, mid: mid, treble: treble }
    }

    function setUltraBass(enabled, level) {
        _dbusCall("SetUltraBass", [enabled, level])
        ultraBassEnabled = enabled
        ultraBassLevel   = level
    }

    function setInEarDetection(enabled) {
        _dbusCall("SetInEarDetection", [enabled])
        inEarEnabled = enabled
    }

    function setLowLatency(enabled) {
        _dbusCall("SetLowLatency", [enabled])
        lowLatencyEnabled = enabled
    }

    function ringBud(side, ringing) {
        _dbusCall("RingBud", [side, ringing])
    }

    function setGesture(side, gestureType, action) {
        _dbusCall("SetGesture", [side, gestureType, action])
    }

    function getPairedDevices(callback) {
        _dbusCallWithResult("GetPairedDevices", [], function(result) {
            callback(result || [])
        })
    }

    function setMacAddress(mac) {
        _dbusCall("SetMacAddress", [mac])
    }

    // ── Polling ───────────────────────────────────────────────────────────────

    readonly property var _pollTimer: Timer {
        interval: 30000
        repeat:   true
        running:  true
        onTriggered: helper._pollCore()
    }

    // Slower full-state timer: picks up phone-side changes every 30s
    readonly property var _fullStateTimer: Timer {
        interval: 30000
        repeat:   true
        running:  true
        onTriggered: {
            if (helper.connectionState === "connected")
                helper._fetchFullState()
        }
    }

    // Track previous connection state to detect "just connected" transitions
    property string _prevConnState: ""

    Component.onCompleted: {
        if (macAddress)
            _dbusCall("SetMacAddress", [macAddress])
        Qt.callLater(_pollCore)
    }

    function _pollCore() {
        _dbusCallWithResult("GetConnectionState", [], function(state) {
            if (!state) return
            let justConnected = (state === "connected" && _prevConnState !== "connected")
            _prevConnState  = state
            connectionState = state
            if (justConnected)
                Qt.callLater(_fetchFullState)
        })
        _dbusCallWithResult("GetBatteryLevels", [], function(raw) {
            if (!raw) return
            let map = _parseDictLines(raw)
            batteryLeft  = map["left"]  !== undefined ? map["left"]  : -1
            batteryRight = map["right"] !== undefined ? map["right"] : -1
            batteryCase  = map["case"]  !== undefined ? map["case"]  : -1
        })
        _dbusCallWithResult("GetChargingStates", [], function(raw) {
            if (!raw) return
            let map = _parseDictLines(raw)
            batteryLeftCharging  = (map["left"]  || 0) !== 0
            batteryRightCharging = (map["right"] || 0) !== 0
            batteryCaseCharging  = (map["case"]  || 0) !== 0
        })
        _dbusCallWithResult("GetCurrentMode", [], function(mode) {
            if (mode) ancMode = mode
        })
    }

    function _fetchFullState() {
        _dbusCallWithResult("GetListeningMode", [], function(v) {
            if (v !== null && v !== undefined) listeningMode = parseInt(v) || 0
        })
        _dbusCallWithResult("GetCustomEq", [], function(raw) {
            if (!raw) return
            let map = _parseDictLines(raw)
            customEq = {
                bass:   map["bass"]   !== undefined ? map["bass"]   : 0,
                mid:    map["mid"]    !== undefined ? map["mid"]    : 0,
                treble: map["treble"] !== undefined ? map["treble"] : 0,
            }
        })
        _dbusCallWithResult("GetUltraBass", [], function(raw) {
            if (!raw) return
            let map = _parseDictLines(raw)
            ultraBassEnabled = (map["enabled"] || 0) !== 0
            ultraBassLevel   = map["level"]  !== undefined ? map["level"]  : 1
        })
        _dbusCallWithResult("GetInEarDetection", [], function(v) {
            if (v !== null && v !== undefined) inEarEnabled = (String(v).trim() === "true")
        })
        _dbusCallWithResult("GetLowLatency", [], function(v) {
            if (v !== null && v !== undefined) lowLatencyEnabled = (String(v).trim() === "true")
        })
        _dbusCallWithResult("GetFirmwareVersion", [], function(v) {
            if (v) firmwareVersion = String(v).trim()
        })
        _dbusCallWithResult("GetGestures", [], function(result) {
            gestures = Array.isArray(result) ? result : (result ? [result] : [])
        })
    }

    // ── Low-level D-Bus call helpers ──────────────────────────────────────────

    function _dbusCall(method, args) {
        let argStr = args.map(a => typeof a === "string" ? `"${a}"` : String(a)).join(" ")
        let cmd = `qdbus-qt6 org.kde.cmfbuds /org/kde/cmfbuds org.kde.cmfbuds.${method} ${argStr}`
        _bus.connectSource(cmd)
    }

    function _dbusCallWithResult(method, args, callback) {
        let argStr = args.map(a => typeof a === "string" ? `"${a}"` : String(a)).join(" ")
        let cmd = `qdbus-qt6 org.kde.cmfbuds /org/kde/cmfbuds org.kde.cmfbuds.${method} ${argStr}`
        // If same command already in-flight, chain callbacks
        if (_pendingCallbacks[cmd] !== undefined) {
            let prev = _pendingCallbacks[cmd]
            let next = {}
            for (let k in _pendingCallbacks) next[k] = _pendingCallbacks[k]
            next[cmd] = function(r) { prev(r); callback(r) }
            _pendingCallbacks = next
        } else {
            let next = {}
            for (let k in _pendingCallbacks) next[k] = _pendingCallbacks[k]
            next[cmd] = callback
            _pendingCallbacks = next
            _bus.connectSource(cmd)
        }
    }

    function _parseResult(raw) {
        if (!raw) return null
        try { return JSON.parse(raw) } catch (_) {}
        if (raw.includes("\n")) return raw.split("\n").filter(l => l)
        return raw
    }

    // Parse "key\tvalue" or "key  value" lines into an object with integer values
    function _parseDictLines(raw) {
        let lines = Array.isArray(raw) ? raw : String(raw).split("\n")
        let map = {}
        for (let line of lines) {
            let parts = line.trim().split(/\s+/)
            if (parts.length >= 2) map[parts[0]] = parseInt(parts[1])
        }
        return map
    }
}
