import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as QQC2
import org.kde.plasma.components as PlasmaComponents3
import org.kde.plasma.extras as PlasmaExtras
import org.kde.kirigami as Kirigami

PlasmaExtras.Representation {
    id: root

    // ── Setup panel state ────────────────────────────────────────────────────
    property var  pairedDevices: []
    property bool setupLoading:  false

    // ── Gesture panel state ──────────────────────────────────────────────────
    property string gestureSide: "left"

    // ── Ring-bud button state ────────────────────────────────────────────────
    property bool ringingLeft:  false
    property bool ringingRight: false

    function refreshDevices() {
        setupLoading = true
        dbusHelper.getPairedDevices(function(devices) {
            pairedDevices = devices || []
            setupLoading = false
        })
    }

    Component.onCompleted: {
        if (!plasmoid.configuration.macAddress)
            refreshDevices()
    }

    // ── EQ debounce timer ────────────────────────────────────────────────────
    Timer {
        id: eqDebounce
        interval: 300
        onTriggered: dbusHelper.setCustomEq(bassSlider.value, midSlider.value, trebleSlider.value)
    }

    // ── Scroll container — fills whatever size the applet is given ───────────
    QQC2.ScrollView {
        id: scrollView
        anchors.fill: parent
        contentWidth: availableWidth
        contentHeight: mainColumn.implicitHeight + Kirigami.Units.largeSpacing * 2
        clip: true
        QQC2.ScrollBar.horizontal.policy: QQC2.ScrollBar.AlwaysOff

        ColumnLayout {
            id: mainColumn
            width: scrollView.availableWidth
            spacing: Kirigami.Units.largeSpacing

            // top padding
            Item { Layout.preferredHeight: Kirigami.Units.smallSpacing }

            // ════════════════════════════════════════════════════════════════
            // SETUP PANEL — shown when no device is configured
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing
                visible: !plasmoid.configuration.macAddress
                onVisibleChanged: if (visible) refreshDevices()

                PlasmaComponents3.Label {
                    Layout.fillWidth: true
                    text: qsTr("Select your CMF Buds")
                    font.bold: true
                    horizontalAlignment: Text.AlignHCenter
                }
                PlasmaComponents3.Label {
                    Layout.fillWidth: true
                    text: qsTr("Choose a paired device below, or enter the MAC address manually.")
                    wrapMode: Text.WordWrap
                    horizontalAlignment: Text.AlignHCenter
                    color: Kirigami.Theme.disabledTextColor
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                }
                Kirigami.Separator { Layout.fillWidth: true }
                PlasmaComponents3.BusyIndicator {
                    Layout.alignment: Qt.AlignHCenter
                    running: setupLoading; visible: setupLoading
                    implicitHeight: Kirigami.Units.gridUnit * 2
                }
                Repeater {
                    model: pairedDevices
                    delegate: QQC2.RadioButton {
                        required property var modelData
                        Layout.fillWidth: true
                        text: {
                            let p = modelData.split("|")
                            return p.length >= 2 ? p[1] + "  (" + p[0] + ")" : modelData
                        }
                        onClicked: {
                            let p = modelData.split("|")
                            if (p.length >= 2) {
                                plasmoid.configuration.macAddress  = p[0]
                                plasmoid.configuration.deviceName  = p[1]
                            } else {
                                plasmoid.configuration.macAddress = modelData
                            }
                        }
                    }
                }
                PlasmaComponents3.Label {
                    visible: !setupLoading && pairedDevices.length === 0
                    Layout.fillWidth: true
                    text: qsTr("No paired devices found. Pair your CMF Buds via Bluetooth settings first.")
                    wrapMode: Text.WordWrap
                    color: Kirigami.Theme.disabledTextColor
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                }
                Kirigami.Separator { Layout.fillWidth: true }
                RowLayout {
                    Layout.fillWidth: true
                    QQC2.TextField {
                        id: macField
                        Layout.fillWidth: true
                        placeholderText: "XX:XX:XX:XX:XX:XX"
                        inputMethodHints: Qt.ImhPreferUppercase
                        maximumLength: 17
                        validator: RegularExpressionValidator {
                            regularExpression: /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/
                        }
                    }
                    PlasmaComponents3.Button {
                        text: qsTr("Set")
                        enabled: macField.acceptableInput
                        onClicked: {
                            plasmoid.configuration.macAddress = macField.text.toUpperCase()
                            plasmoid.configuration.deviceName = "CMF Buds"
                        }
                    }
                }
                PlasmaComponents3.Button {
                    Layout.alignment: Qt.AlignHCenter
                    text: qsTr("Refresh device list")
                    icon.name: "view-refresh"
                    onClicked: refreshDevices()
                }
            }

            // ════════════════════════════════════════════════════════════════
            // HEADER — device name + connection status
            // ════════════════════════════════════════════════════════════════
            RowLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                Rectangle {
                    width: 10; height: 10; radius: 5
                    color: {
                        switch (dbusHelper.connectionState) {
                        case "connected":  return "#4CAF50"
                        case "connecting": return "#E6A817"
                        case "error":      return "#F44336"
                        default:           return Kirigami.Theme.disabledTextColor
                        }
                    }
                    Behavior on color { ColorAnimation { duration: 300 } }
                }
                ColumnLayout {
                    spacing: 0
                    PlasmaComponents3.Label {
                        text: plasmoid.configuration.deviceName || "CMF Buds"
                        font.bold: true
                    }
                    PlasmaComponents3.Label {
                        text: {
                            switch (dbusHelper.connectionState) {
                            case "connected":  return qsTr("Connected")
                            case "connecting": return qsTr("Connecting…")
                            case "error":      return qsTr("Connection error")
                            default:           return qsTr("Disconnected")
                            }
                        }
                        font.pointSize: Kirigami.Theme.smallFont.pointSize
                        color: Kirigami.Theme.disabledTextColor
                    }
                }
                Item { Layout.fillWidth: true }
                PlasmaComponents3.ToolButton {
                    icon.name: "configure"
                    PlasmaComponents3.ToolTip.text: qsTr("Change device")
                    PlasmaComponents3.ToolTip.visible: hovered
                    onClicked: plasmoid.configuration.macAddress = ""
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // NOISE CONTROL
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Noise Control")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.letterSpacing: 1.5
                    font.capitalization: Font.AllUppercase
                }

                QQC2.ButtonGroup { id: ancModeGroup }
                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: [
                            { mode: "anc",         label: qsTr("Noise Cancel"), icon: "audio-input-microphone-muted" },
                            { mode: "transparency", label: qsTr("Transparent"),  icon: "audio-input-microphone"       },
                            { mode: "off",          label: qsTr("Off"),          icon: "audio-volume-muted"           },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true
                            text: modelData.label
                            icon.name: modelData.icon
                            checkable: true
                            checked: modelData.mode === "anc" ? dbusHelper.ancIsNc
                                                              : dbusHelper.ancMode === modelData.mode
                            enabled: dbusHelper.connectionState === "connected"
                            highlighted: checked
                            palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                            onClicked: dbusHelper.setAncMode(
                                modelData.mode === "anc" ? "anc_" + dbusHelper.ancStrength : modelData.mode)
                            QQC2.ButtonGroup.group: ancModeGroup
                        }
                    }
                }

                RowLayout {
                    visible: dbusHelper.ancIsNc && dbusHelper.connectionState === "connected"
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    QQC2.ButtonGroup { id: ancStrengthGroup }
                    Repeater {
                        model: [
                            { strength: "high",     label: qsTr("High")     },
                            { strength: "mid",      label: qsTr("Mid")      },
                            { strength: "low",      label: qsTr("Low")      },
                            { strength: "adaptive", label: qsTr("Adaptive") },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true
                            text: modelData.label
                            checkable: true
                            checked: dbusHelper.ancStrength === modelData.strength
                            highlighted: checked
                            palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                            onClicked: dbusHelper.setAncMode("anc_" + modelData.strength)
                            QQC2.ButtonGroup.group: ancStrengthGroup
                        }
                    }
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress && dbusHelper.batteryLeft >= 0
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // BATTERY
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress && dbusHelper.batteryLeft >= 0
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Battery")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.letterSpacing: 1.5
                    font.capitalization: Font.AllUppercase
                }

                Repeater {
                    model: [
                        { label: "L",    value: dbusHelper.batteryLeft,  charging: dbusHelper.batteryLeftCharging  },
                        { label: "R",    value: dbusHelper.batteryRight, charging: dbusHelper.batteryRightCharging },
                        { label: "Case", value: dbusHelper.batteryCase,  charging: dbusHelper.batteryCaseCharging  },
                    ]
                    RowLayout {
                        required property var modelData
                        Layout.fillWidth: true
                        spacing: Kirigami.Units.smallSpacing
                        visible: modelData.value >= 0

                        PlasmaComponents3.Label {
                            text: modelData.label + (modelData.charging ? " ⚡" : "")
                            font.bold: true
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2.5
                        }
                        QQC2.ProgressBar {
                            Layout.fillWidth: true
                            from: 0; to: 100
                            value: modelData.value
                            palette.highlight: modelData.value <= 20 ? "#F44336" : Kirigami.Theme.highlightColor
                        }
                        PlasmaComponents3.Label {
                            text: modelData.value + "%"
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2.5
                            horizontalAlignment: Text.AlignRight
                        }
                    }
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // EQUALIZER
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Equalizer")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.letterSpacing: 1.5
                    font.capitalization: Font.AllUppercase
                }

                QQC2.ButtonGroup { id: eqGroup }
                GridLayout {
                    Layout.fillWidth: true
                    columns: 4
                    rowSpacing:    Kirigami.Units.smallSpacing
                    columnSpacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: [
                            { level: 0, label: qsTr("Dirac OPTEO")     },
                            { level: 1, label: qsTr("Rock")            },
                            { level: 2, label: qsTr("Electronic")      },
                            { level: 3, label: qsTr("Pop")             },
                            { level: 4, label: qsTr("Enhance Vocals")  },
                            { level: 5, label: qsTr("Classical")       },
                            { level: 6, label: qsTr("Custom")          },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true
                            text: modelData.label
                            checkable: true
                            checked: dbusHelper.listeningMode === modelData.level
                            enabled: dbusHelper.connectionState === "connected"
                            highlighted: checked
                            palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                            onClicked: dbusHelper.setListeningMode(modelData.level)
                            QQC2.ButtonGroup.group: eqGroup
                        }
                    }
                }

                ColumnLayout {
                    visible: dbusHelper.listeningMode === 6
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing

                    RowLayout {
                        Layout.fillWidth: true; spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label { text: qsTr("Bass");   Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                        QQC2.Slider {
                            id: bassSlider
                            Layout.fillWidth: true; from: -6; to: 6; stepSize: 1
                            value: dbusHelper.customEq.bass || 0
                            onMoved: eqDebounce.restart()
                        }
                        PlasmaComponents3.Label {
                            text: bassSlider.value > 0 ? "+" + bassSlider.value : String(bassSlider.value)
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2; horizontalAlignment: Text.AlignRight
                        }
                    }
                    RowLayout {
                        Layout.fillWidth: true; spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label { text: qsTr("Mid");    Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                        QQC2.Slider {
                            id: midSlider
                            Layout.fillWidth: true; from: -6; to: 6; stepSize: 1
                            value: dbusHelper.customEq.mid || 0
                            onMoved: eqDebounce.restart()
                        }
                        PlasmaComponents3.Label {
                            text: midSlider.value > 0 ? "+" + midSlider.value : String(midSlider.value)
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2; horizontalAlignment: Text.AlignRight
                        }
                    }
                    RowLayout {
                        Layout.fillWidth: true; spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label { text: qsTr("Treble"); Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                        QQC2.Slider {
                            id: trebleSlider
                            Layout.fillWidth: true; from: -6; to: 6; stepSize: 1
                            value: dbusHelper.customEq.treble || 0
                            onMoved: eqDebounce.restart()
                        }
                        PlasmaComponents3.Label {
                            text: trebleSlider.value > 0 ? "+" + trebleSlider.value : String(trebleSlider.value)
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2; horizontalAlignment: Text.AlignRight
                        }
                    }
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // ULTRA BASS
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                RowLayout {
                    Layout.fillWidth: true
                    PlasmaComponents3.Label { text: qsTr("Ultra Bass"); font.bold: true; Layout.fillWidth: true }
                    QQC2.Switch {
                        checked: dbusHelper.ultraBassEnabled
                        enabled: dbusHelper.connectionState === "connected"
                        onToggled: dbusHelper.setUltraBass(checked, dbusHelper.ultraBassLevel)
                    }
                }
                RowLayout {
                    visible: dbusHelper.ultraBassEnabled
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    PlasmaComponents3.Label { text: qsTr("Level"); Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                    QQC2.Slider {
                        id: ultraBassSlider
                        Layout.fillWidth: true
                        from: 1; to: 5; stepSize: 1
                        snapMode: QQC2.Slider.SnapAlways
                        value: dbusHelper.ultraBassLevel
                        onMoved: dbusHelper.setUltraBass(true, value)
                    }
                    PlasmaComponents3.Label {
                        text: ultraBassSlider.value
                        Layout.minimumWidth: Kirigami.Units.gridUnit * 2
                        horizontalAlignment: Text.AlignRight
                    }
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // QUICK SETTINGS
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Quick Settings")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.letterSpacing: 1.5
                    font.capitalization: Font.AllUppercase
                }
                RowLayout {
                    Layout.fillWidth: true
                    PlasmaComponents3.Label { text: qsTr("In-Ear Detection"); Layout.fillWidth: true }
                    QQC2.Switch {
                        checked: dbusHelper.inEarEnabled
                        enabled: dbusHelper.connectionState === "connected"
                        onToggled: dbusHelper.setInEarDetection(checked)
                    }
                }
                RowLayout {
                    Layout.fillWidth: true
                    PlasmaComponents3.Label { text: qsTr("Low Latency Mode"); Layout.fillWidth: true }
                    QQC2.Switch {
                        checked: dbusHelper.lowLatencyEnabled
                        enabled: dbusHelper.connectionState === "connected"
                        onToggled: dbusHelper.setLowLatency(checked)
                    }
                }
                RowLayout {
                    visible: dbusHelper.firmwareVersion !== ""
                    Layout.fillWidth: true
                    PlasmaComponents3.Label { text: qsTr("Firmware"); color: Kirigami.Theme.disabledTextColor; Layout.fillWidth: true }
                    PlasmaComponents3.Label { text: dbusHelper.firmwareVersion; color: Kirigami.Theme.disabledTextColor }
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // GESTURES
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                id: gestureSection
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                function currentGestureAction(gestureType) {
                    let prefix = root.gestureSide + ":" + gestureType + ":"
                    for (let g of dbusHelper.gestures)
                        if (String(g).startsWith(prefix))
                            return parseInt(String(g).split(":")[2])
                    return -1
                }

                PlasmaComponents3.Label {
                    text: qsTr("Gestures")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.letterSpacing: 1.5
                    font.capitalization: Font.AllUppercase
                }

                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    QQC2.ButtonGroup { id: gestureSideGroup }
                    PlasmaComponents3.Button {
                        Layout.fillWidth: true; text: qsTr("Left")
                        checkable: true; checked: root.gestureSide === "left"
                        highlighted: checked
                        palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                        onClicked: root.gestureSide = "left"
                        QQC2.ButtonGroup.group: gestureSideGroup
                    }
                    PlasmaComponents3.Button {
                        Layout.fillWidth: true; text: qsTr("Right")
                        checkable: true; checked: root.gestureSide === "right"
                        highlighted: checked
                        palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                        onClicked: root.gestureSide = "right"
                        QQC2.ButtonGroup.group: gestureSideGroup
                    }
                }

                Repeater {
                    model: [
                        { type: 2, label: qsTr("Double Pinch"),        actions: [2,8,9,11,1],  labels: [qsTr("Play/Pause"), qsTr("Skip Back"), qsTr("Skip Forward"), qsTr("Voice Assistant"), qsTr("No action")] },
                        { type: 3, label: qsTr("Triple Pinch"),        actions: [8,9,11,1],    labels: [qsTr("Skip Back"), qsTr("Skip Forward"), qsTr("Voice Assistant"), qsTr("No action")] },
                        { type: 7, label: qsTr("Pinch & Hold"),        actions: [10,11,1],     labels: [qsTr("Noise Control"), qsTr("Voice Assistant"), qsTr("No action")] },
                        { type: 9, label: qsTr("Double Pinch & Hold"), actions: [18,19,11,1],  labels: [qsTr("Vol Up"), qsTr("Vol Down"), qsTr("Voice Assistant"), qsTr("No action")] },
                    ]
                    RowLayout {
                        required property var modelData
                        Layout.fillWidth: true
                        spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label {
                            text: modelData.label
                            elide: Text.ElideRight
                            Layout.fillWidth: true
                        }
                        QQC2.ComboBox {
                            model: modelData.labels
                            enabled: dbusHelper.connectionState === "connected"
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 8
                            currentIndex: {
                                let a = gestureSection.currentGestureAction(modelData.type)
                                let i = modelData.actions.indexOf(a)
                                return i >= 0 ? i : 0
                            }
                            onActivated: (i) => dbusHelper.setGesture(root.gestureSide, modelData.type, modelData.actions[i])
                        }
                    }
                }
            }

            Kirigami.Separator {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
            }

            // ════════════════════════════════════════════════════════════════
            // FIND MY BUDS
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.leftMargin:  Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Find My Buds")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.letterSpacing: 1.5
                    font.capitalization: Font.AllUppercase
                }
                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: [
                            { side: "left",  ringing: root.ringingLeft  },
                            { side: "right", ringing: root.ringingRight },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true
                            text: modelData.ringing
                                ? qsTr("Stop %1").arg(modelData.side === "left" ? qsTr("Left") : qsTr("Right"))
                                : qsTr("Ring %1").arg(modelData.side === "left" ? qsTr("Left") : qsTr("Right"))
                            icon.name: modelData.ringing ? "media-playback-stop" : "audio-speakers-symbolic"
                            enabled: dbusHelper.connectionState === "connected"
                            highlighted: modelData.ringing
                            palette.buttonText: modelData.ringing ? "#ffffff" : Kirigami.Theme.textColor
                            background: Rectangle {
                                color: modelData.ringing ? "#7f1d1d" : "transparent"
                                radius: Kirigami.Units.cornerRadius
                            }
                            onClicked: {
                                if (modelData.side === "left") root.ringingLeft  = !root.ringingLeft
                                else                           root.ringingRight = !root.ringingRight
                                dbusHelper.ringBud(modelData.side,
                                    modelData.side === "left" ? root.ringingLeft : root.ringingRight)
                            }
                        }
                    }
                }
            }

            // bottom padding
            Item { Layout.preferredHeight: Kirigami.Units.largeSpacing }

        } // ColumnLayout
    } // ScrollView
}
