import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as QQC2
import org.kde.plasma.components as PlasmaComponents3
import org.kde.plasma.extras as PlasmaExtras
import org.kde.kirigami as Kirigami

PlasmaExtras.Representation {
    id: root

    // ── State ────────────────────────────────────────────────────────────────
    property var    pairedDevices: []
    property bool   setupLoading:  false
    property string gestureSide:   "left"
    property bool   ringingLeft:   false
    property bool   ringingRight:  false
    // Remembers the MAC address while the setup panel is open (gear button clears macAddress)
    property string _settingsMac:  plasmoid.configuration.macAddress

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

    // EQ debounce
    Timer {
        id: eqDebounce
        interval: 300
        onTriggered: dbusHelper.setCustomEq(bassSlider.value, midSlider.value, trebleSlider.value)
    }

    // Sliders declared here so they are accessible from ColumnLayout/Repeater
    QQC2.Slider { id: bassSlider;   visible: false; from: -6; to: 6; stepSize: 1
        value: dbusHelper.customEq.bass   || 0; onMoved: eqDebounce.restart() }
    QQC2.Slider { id: midSlider;    visible: false; from: -6; to: 6; stepSize: 1
        value: dbusHelper.customEq.mid    || 0; onMoved: eqDebounce.restart() }
    QQC2.Slider { id: trebleSlider; visible: false; from: -6; to: 6; stepSize: 1
        value: dbusHelper.customEq.treble || 0; onMoved: eqDebounce.restart() }

    // ── Scroll container ─────────────────────────────────────────────────────
    QQC2.ScrollView {
        id: scrollView
        anchors.fill: parent
        contentWidth: availableWidth
        contentHeight: mainCol.implicitHeight
        clip: true
        QQC2.ScrollBar.horizontal.policy: QQC2.ScrollBar.AlwaysOff

        ColumnLayout {
            id: mainCol
            width: scrollView.availableWidth
            spacing: 0

            // ════════════════════════════════════════════════════════════════
            // SETUP PANEL
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.margins: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing
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
                        checked: {
                            let p = modelData.split("|")
                            return p[0] === root._settingsMac
                        }
                        onClicked: {
                            let p = modelData.split("|")
                            if (p.length >= 2) {
                                plasmoid.configuration.macAddress = p[0]
                                plasmoid.configuration.deviceName = p[1]
                            } else {
                                plasmoid.configuration.macAddress = modelData
                            }
                            root._settingsMac = plasmoid.configuration.macAddress
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
                    text: qsTr("Refresh")
                    icon.name: "view-refresh"
                    onClicked: refreshDevices()
                }
            }

            // ════════════════════════════════════════════════════════════════
            // DEVICE HEADER — art + name + battery
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                spacing: 0

                // Device name + connection state
                RowLayout {
                    Layout.fillWidth: true
                    Layout.leftMargin:   Kirigami.Units.largeSpacing
                    Layout.rightMargin:  Kirigami.Units.largeSpacing
                    Layout.bottomMargin: Kirigami.Units.smallSpacing
                    spacing: Kirigami.Units.smallSpacing

                    Rectangle {
                        width: 8; height: 8; radius: 4
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
                    PlasmaComponents3.Label {
                        text: plasmoid.configuration.deviceName || "CMF Buds"
                        font.bold: true
                    }
                    PlasmaComponents3.Label {
                        visible: dbusHelper.connectionState !== "connected"
                        text: {
                            switch (dbusHelper.connectionState) {
                            case "connecting": return qsTr("Connecting…")
                            case "error":      return qsTr("Error")
                            default:           return qsTr("Disconnected")
                            }
                        }
                        font.pointSize: Kirigami.Theme.smallFont.pointSize
                        color: Kirigami.Theme.disabledTextColor
                    }
                    Item { Layout.fillWidth: true }
                    PlasmaComponents3.Label {
                        visible: dbusHelper.firmwareVersion !== "" && dbusHelper.connectionState === "connected"
                        text: dbusHelper.firmwareVersion
                        font.pointSize: Kirigami.Theme.smallFont.pointSize
                        color: Kirigami.Theme.disabledTextColor
                    }
                    PlasmaComponents3.ToolButton {
                        icon.name: "configure"
                        PlasmaComponents3.ToolTip.text: qsTr("Change device")
                        PlasmaComponents3.ToolTip.visible: hovered
                        onClicked: {
                            root._settingsMac = plasmoid.configuration.macAddress
                            plasmoid.configuration.macAddress = ""
                        }
                    }
                }

                // Compact battery strip
                RowLayout {
                    visible: dbusHelper.batteryLeft >= 0 && dbusHelper.connectionState === "connected"
                    Layout.fillWidth: true
                    Layout.leftMargin:   Kirigami.Units.largeSpacing
                    Layout.rightMargin:  Kirigami.Units.largeSpacing
                    Layout.bottomMargin: Kirigami.Units.smallSpacing
                    spacing: Kirigami.Units.largeSpacing

                    Repeater {
                        model: [
                            { label: qsTr("L"),    value: dbusHelper.batteryLeft,  charging: dbusHelper.batteryLeftCharging  },
                            { label: qsTr("R"),    value: dbusHelper.batteryRight, charging: dbusHelper.batteryRightCharging },
                            { label: qsTr("Case"), value: dbusHelper.batteryCase,  charging: dbusHelper.batteryCaseCharging  },
                        ]
                        RowLayout {
                            required property var modelData
                            visible: modelData.value >= 0
                            spacing: 4

                            Kirigami.Icon {
                                source: modelData.charging ? "battery-charging-symbolic"
                                    : (modelData.value > 60 ? "battery-100-symbolic"
                                    : modelData.value > 30 ? "battery-060-symbolic"
                                    :                        "battery-020-symbolic")
                                implicitWidth:  Kirigami.Units.iconSizes.small
                                implicitHeight: Kirigami.Units.iconSizes.small
                            }
                            PlasmaComponents3.Label {
                                text: modelData.label + " " + modelData.value + "%"
                                font.pointSize: Kirigami.Theme.smallFont.pointSize
                                color: modelData.value <= 20 ? "#F44336" : Kirigami.Theme.textColor
                            }
                        }
                    }
                }
            }

            Kirigami.Separator { visible: !!plasmoid.configuration.macAddress; Layout.fillWidth: true }

            // ════════════════════════════════════════════════════════════════
            // NOISE CONTROL
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.topMargin:    Kirigami.Units.largeSpacing
                Layout.leftMargin:   Kirigami.Units.largeSpacing
                Layout.rightMargin:  Kirigami.Units.largeSpacing
                Layout.bottomMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Noise Control")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.capitalization: Font.AllUppercase
                    font.letterSpacing: 1.2
                }

                QQC2.ButtonGroup { id: ancModeGroup }
                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing

                    Repeater {
                        model: [
                            { mode: "anc",         label: qsTr("Noise Cancel"), icon: "assets/anc_on.svg"          },
                            { mode: "transparency", label: qsTr("Transparent"),  icon: "assets/anc_transparent.svg" },
                            { mode: "off",          label: qsTr("Off"),          icon: "assets/anc_off.svg"         },
                        ]
                        delegate: Item {
                            required property var modelData
                            Layout.fillWidth: true
                            implicitHeight: cardCol.implicitHeight + Kirigami.Units.largeSpacing * 2

                            property bool isChecked: modelData.mode === "anc"
                                ? dbusHelper.ancIsNc
                                : dbusHelper.ancMode === modelData.mode

                            Rectangle {
                                anchors.fill: parent
                                radius: Kirigami.Units.cornerRadius
                                color: isChecked
                                    ? Qt.rgba(Kirigami.Theme.highlightColor.r,
                                              Kirigami.Theme.highlightColor.g,
                                              Kirigami.Theme.highlightColor.b, 0.18)
                                    : (mouseArea.containsMouse ? Qt.rgba(Kirigami.Theme.textColor.r,
                                                                          Kirigami.Theme.textColor.g,
                                                                          Kirigami.Theme.textColor.b, 0.06)
                                                               : "transparent")
                                border.color: isChecked ? Kirigami.Theme.highlightColor : "transparent"
                                border.width: 1
                                Behavior on color { ColorAnimation { duration: 150 } }
                            }

                            ColumnLayout {
                                id: cardCol
                                anchors.centerIn: parent
                                spacing: 4

                                Image {
                                    Layout.alignment: Qt.AlignHCenter
                                    Layout.preferredWidth: 40
                                    Layout.preferredHeight: 40
                                    source: Qt.resolvedUrl(modelData.icon)
                                    sourceSize.width: 40
                                    sourceSize.height: 40
                                    fillMode: Image.PreserveAspectFit
                                    opacity: isChecked ? 1.0 : 0.4
                                    Behavior on opacity { NumberAnimation { duration: 150 } }
                                }
                                PlasmaComponents3.Label {
                                    Layout.alignment: Qt.AlignHCenter
                                    text: modelData.label
                                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                                    color: isChecked ? Kirigami.Theme.highlightColor : Kirigami.Theme.textColor
                                    Behavior on color { ColorAnimation { duration: 150 } }
                                }
                            }

                            MouseArea {
                                id: mouseArea
                                anchors.fill: parent
                                hoverEnabled: true
                                cursorShape: Qt.PointingHandCursor
                                enabled: dbusHelper.connectionState === "connected"
                                onClicked: {
                                    dbusHelper.setAncMode(
                                        modelData.mode === "anc" ? "anc_" + dbusHelper.ancStrength : modelData.mode)
                                }
                            }
                        }
                    }
                }

                // ANC strength sub-row
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
                            Layout.fillWidth: true; Layout.preferredWidth: 1; Layout.minimumWidth: 0
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

            Kirigami.Separator { visible: !!plasmoid.configuration.macAddress; Layout.fillWidth: true }

            // ════════════════════════════════════════════════════════════════
            // EQUALIZER
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.topMargin:    Kirigami.Units.largeSpacing
                Layout.leftMargin:   Kirigami.Units.largeSpacing
                Layout.rightMargin:  Kirigami.Units.largeSpacing
                Layout.bottomMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Equalizer")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.capitalization: Font.AllUppercase
                    font.letterSpacing: 1.2
                }

                QQC2.ButtonGroup { id: eqGroup }
                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: [
                            { level: 0, label: qsTr("Dirac OPTEO") },
                            { level: 1, label: qsTr("Rock")        },
                            { level: 2, label: qsTr("Electronic")  },
                            { level: 3, label: qsTr("Pop")         },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true; Layout.preferredWidth: 1; Layout.minimumWidth: 0
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
                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: [
                            { level: 4, label: qsTr("Vocals")    },
                            { level: 5, label: qsTr("Classical") },
                            { level: 6, label: qsTr("Custom")    },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true; Layout.preferredWidth: 1; Layout.minimumWidth: 0
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
                    Item { Layout.fillWidth: true; Layout.preferredWidth: 1 }
                }

                // Custom EQ sliders
                ColumnLayout {
                    visible: dbusHelper.listeningMode === 6
                    Layout.fillWidth: true
                    spacing: 2

                    RowLayout {
                        Layout.fillWidth: true; spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label { text: qsTr("Bass");   Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                        QQC2.Slider {
                            id: bassSliderInline
                            Layout.fillWidth: true; from: -6; to: 6; stepSize: 1
                            value: bassSlider.value
                            onMoved: { bassSlider.value = value; eqDebounce.restart() }
                        }
                        PlasmaComponents3.Label {
                            text: bassSliderInline.value > 0 ? "+" + bassSliderInline.value : String(bassSliderInline.value)
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2; horizontalAlignment: Text.AlignRight
                        }
                    }
                    RowLayout {
                        Layout.fillWidth: true; spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label { text: qsTr("Mid");    Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                        QQC2.Slider {
                            id: midSliderInline
                            Layout.fillWidth: true; from: -6; to: 6; stepSize: 1
                            value: midSlider.value
                            onMoved: { midSlider.value = value; eqDebounce.restart() }
                        }
                        PlasmaComponents3.Label {
                            text: midSliderInline.value > 0 ? "+" + midSliderInline.value : String(midSliderInline.value)
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2; horizontalAlignment: Text.AlignRight
                        }
                    }
                    RowLayout {
                        Layout.fillWidth: true; spacing: Kirigami.Units.smallSpacing
                        PlasmaComponents3.Label { text: qsTr("Treble"); Layout.minimumWidth: Kirigami.Units.gridUnit * 3.5 }
                        QQC2.Slider {
                            id: trebleSliderInline
                            Layout.fillWidth: true; from: -6; to: 6; stepSize: 1
                            value: trebleSlider.value
                            onMoved: { trebleSlider.value = value; eqDebounce.restart() }
                        }
                        PlasmaComponents3.Label {
                            text: trebleSliderInline.value > 0 ? "+" + trebleSliderInline.value : String(trebleSliderInline.value)
                            Layout.minimumWidth: Kirigami.Units.gridUnit * 2; horizontalAlignment: Text.AlignRight
                        }
                    }
                }
            }

            Kirigami.Separator { visible: !!plasmoid.configuration.macAddress; Layout.fillWidth: true }

            // ════════════════════════════════════════════════════════════════
            // ULTRA BASS
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.topMargin:    Kirigami.Units.largeSpacing
                Layout.leftMargin:   Kirigami.Units.largeSpacing
                Layout.rightMargin:  Kirigami.Units.largeSpacing
                Layout.bottomMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing

                    Kirigami.Icon {
                        source: Qt.resolvedUrl(dbusHelper.ultraBassEnabled ? "assets/bass_on.svg" : "assets/bass_off.svg")
                        implicitWidth: 20; implicitHeight: 20
                        color: Kirigami.Theme.textColor
                        opacity: dbusHelper.ultraBassEnabled ? 1.0 : 0.45
                    }
                    PlasmaComponents3.Label {
                        text: qsTr("Ultra Bass")
                        font.pointSize: Kirigami.Theme.smallFont.pointSize
                        color: Kirigami.Theme.disabledTextColor
                        font.capitalization: Font.AllUppercase
                        font.letterSpacing: 1.2
                        Layout.fillWidth: true
                    }
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

            Kirigami.Separator { visible: !!plasmoid.configuration.macAddress; Layout.fillWidth: true }

            // ════════════════════════════════════════════════════════════════
            // QUICK SETTINGS
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.topMargin:    Kirigami.Units.largeSpacing
                Layout.leftMargin:   Kirigami.Units.largeSpacing
                Layout.rightMargin:  Kirigami.Units.largeSpacing
                Layout.bottomMargin: Kirigami.Units.largeSpacing
                spacing: 2

                PlasmaComponents3.Label {
                    text: qsTr("Quick Settings")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.capitalization: Font.AllUppercase
                    font.letterSpacing: 1.2
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
            }

            Kirigami.Separator { visible: !!plasmoid.configuration.macAddress; Layout.fillWidth: true }

            // ════════════════════════════════════════════════════════════════
            // GESTURES
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                id: gestureSection
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.topMargin:    Kirigami.Units.largeSpacing
                Layout.leftMargin:   Kirigami.Units.largeSpacing
                Layout.rightMargin:  Kirigami.Units.largeSpacing
                Layout.bottomMargin: Kirigami.Units.largeSpacing
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
                    font.capitalization: Font.AllUppercase
                    font.letterSpacing: 1.2
                }

                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    QQC2.ButtonGroup { id: gestureSideGroup }
                    PlasmaComponents3.Button {
                        Layout.preferredWidth: 1; Layout.fillWidth: true; Layout.minimumWidth: 0; text: qsTr("Left")
                        checkable: true; checked: root.gestureSide === "left"
                        highlighted: checked
                        palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                        onClicked: root.gestureSide = "left"
                        QQC2.ButtonGroup.group: gestureSideGroup
                    }
                    PlasmaComponents3.Button {
                        Layout.preferredWidth: 1; Layout.fillWidth: true; Layout.minimumWidth: 0; text: qsTr("Right")
                        checkable: true; checked: root.gestureSide === "right"
                        highlighted: checked
                        palette.buttonText: checked ? Kirigami.Theme.highlightedTextColor : Kirigami.Theme.textColor
                        onClicked: root.gestureSide = "right"
                        QQC2.ButtonGroup.group: gestureSideGroup
                    }
                }

                Repeater {
                    model: [
                        { type: 2, label: qsTr("Double Pinch"),        actions: [2,8,9,11,1],  labels: [qsTr("Play/Pause"),qsTr("Skip Back"),qsTr("Skip Forward"),qsTr("Voice Assistant"),qsTr("No action")] },
                        { type: 3, label: qsTr("Triple Pinch"),        actions: [8,9,11,1],    labels: [qsTr("Skip Back"),qsTr("Skip Forward"),qsTr("Voice Assistant"),qsTr("No action")] },
                        { type: 7, label: qsTr("Pinch & Hold"),        actions: [10,11,1],     labels: [qsTr("Noise Control"),qsTr("Voice Assistant"),qsTr("No action")] },
                        { type: 9, label: qsTr("Double Pinch & Hold"), actions: [18,19,11,1],  labels: [qsTr("Vol Up"),qsTr("Vol Down"),qsTr("Voice Assistant"),qsTr("No action")] },
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

            Kirigami.Separator { visible: !!plasmoid.configuration.macAddress; Layout.fillWidth: true }

            // ════════════════════════════════════════════════════════════════
            // FIND MY BUDS
            // ════════════════════════════════════════════════════════════════
            ColumnLayout {
                visible: !!plasmoid.configuration.macAddress
                Layout.fillWidth: true
                Layout.topMargin:    Kirigami.Units.largeSpacing
                Layout.leftMargin:   Kirigami.Units.largeSpacing
                Layout.rightMargin:  Kirigami.Units.largeSpacing
                Layout.bottomMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing

                PlasmaComponents3.Label {
                    text: qsTr("Find My Buds")
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                    font.capitalization: Font.AllUppercase
                    font.letterSpacing: 1.2
                }
                RowLayout {
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: [
                            { side: "left",  label: qsTr("Left"),  ringing: root.ringingLeft  },
                            { side: "right", label: qsTr("Right"), ringing: root.ringingRight },
                        ]
                        PlasmaComponents3.Button {
                            required property var modelData
                            Layout.fillWidth: true
                            text: modelData.ringing ? qsTr("Stop %1").arg(modelData.label) : qsTr("Ring %1").arg(modelData.label)
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

            Item { Layout.preferredHeight: Kirigami.Units.largeSpacing }

        } // ColumnLayout
    } // ScrollView
}
