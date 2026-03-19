import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as QQC2
import org.kde.plasma.components as PlasmaComponents3
import org.kde.plasma.extras as PlasmaExtras
import org.kde.kirigami as Kirigami

/**
 * FullRepresentation.qml — Nothing-OS-inspired system-tray popup.
 *
 * Layout:
 *   ┌─────────────────────────────────┐
 *   │  ○  CMF Buds Pro 2              │  ← header with device name
 *   │     Connected                   │
 *   ├─────────────────────────────────┤
 *   │  [  Off  ] [ ANC ] [Transparen] │  ← mode buttons
 *   ├─────────────────────────────────┤
 *   │  L ████░░ 72%                   │  ← battery bars
 *   │  R ████░░ 68%                   │
 *   │  ⬡ ███░░░ 55%                   │
 *   └─────────────────────────────────┘
 */
PlasmaExtras.Representation {
    id: root

    // Fixed popup size (respects system DPI via Kirigami.Units)
    implicitWidth:  Kirigami.Units.gridUnit * 18
    implicitHeight: contentColumn.implicitHeight + Kirigami.Units.gridUnit * 2

    // ------------------------------------------------------------------
    // Background blur/translucency — inherits Layan/Kvantum if active
    // ------------------------------------------------------------------
    PlasmaExtras.Background { anchors.fill: parent }

    ColumnLayout {
        id: contentColumn
        anchors {
            fill:    parent
            margins: Kirigami.Units.largeSpacing
        }
        spacing: Kirigami.Units.largeSpacing

        // ----------------------------------------------------------------
        // Header
        // ----------------------------------------------------------------
        RowLayout {
            Layout.fillWidth: true
            spacing: Kirigami.Units.smallSpacing

            // Nothing-OS dot-matrix status indicator
            Rectangle {
                width:  10; height: 10; radius: 5
                color: {
                    switch (dbusHelper.connectionState) {
                    case "connected":   return "#4CAF50"
                    case "connecting":  return "#E6A817"
                    case "error":       return "#F44336"
                    default:            return Kirigami.Theme.disabledTextColor
                    }
                }
                Behavior on color { ColorAnimation { duration: 300 } }
            }

            ColumnLayout {
                spacing: 0

                PlasmaComponents3.Label {
                    text: plasmoid.configuration.deviceName || "CMF Buds"
                    font.bold:    true
                    font.pointSize: Kirigami.Theme.defaultFont.pointSize * 1.05
                    color: Kirigami.Theme.textColor
                }
                PlasmaComponents3.Label {
                    text: {
                        switch (dbusHelper.connectionState) {
                        case "connected":   return qsTr("Connected")
                        case "connecting":  return qsTr("Connecting…")
                        case "error":       return qsTr("Connection error")
                        default:            return qsTr("Disconnected")
                        }
                    }
                    font.pointSize: Kirigami.Theme.smallFont.pointSize
                    color: Kirigami.Theme.disabledTextColor
                }
            }

            Item { Layout.fillWidth: true }

            // Settings button
            PlasmaComponents3.ToolButton {
                icon.name: "configure"
                PlasmaComponents3.ToolTip.text: qsTr("Configure")
                PlasmaComponents3.ToolTip.visible: hovered
                onClicked: plasmoid.internalAction("configure").trigger()
            }
        }

        // ----------------------------------------------------------------
        // Divider
        // ----------------------------------------------------------------
        PlasmaExtras.PlasmaDivider {}

        // ----------------------------------------------------------------
        // ANC Mode buttons
        // ----------------------------------------------------------------
        ColumnLayout {
            Layout.fillWidth: true
            spacing: Kirigami.Units.smallSpacing

            PlasmaComponents3.Label {
                text:  qsTr("Noise Control")
                font.pointSize: Kirigami.Theme.smallFont.pointSize
                color: Kirigami.Theme.disabledTextColor
                // Dot-matrix aesthetic: letter-spaced caption
                font.letterSpacing: 1.5
                font.capitalization: Font.AllUppercase
            }

            RowLayout {
                Layout.fillWidth: true
                spacing: Kirigami.Units.smallSpacing

                Repeater {
                    model: [
                        { mode: "off",          label: qsTr("Off"),          icon: "audio-volume-muted"       },
                        { mode: "anc",          label: qsTr("Noise Cancel"), icon: "audio-input-microphone-muted" },
                        { mode: "transparency", label: qsTr("Transparent"),  icon: "audio-input-microphone"   },
                    ]

                    PlasmaComponents3.Button {
                        required property var modelData
                        Layout.fillWidth: true

                        text:       modelData.label
                        icon.name:  modelData.icon
                        checkable:  true
                        checked:    dbusHelper.currentMode === modelData.mode
                        enabled:    dbusHelper.connectionState === "connected"

                        highlighted: checked
                        // Use system highlight colour when active
                        palette.buttonText: checked
                                            ? Kirigami.Theme.highlightedTextColor
                                            : Kirigami.Theme.textColor

                        onClicked: {
                            if (!checked) return
                            dbusHelper.setAncMode(modelData.mode)
                        }

                        ButtonGroup.group: modeGroup
                    }
                }

                ButtonGroup { id: modeGroup; exclusive: true }
            }
        }

        // ----------------------------------------------------------------
        // Battery indicators (hidden when values are unknown)
        // ----------------------------------------------------------------
        ColumnLayout {
            Layout.fillWidth: true
            spacing: Kirigami.Units.smallSpacing
            visible: dbusHelper.batteryLeft >= 0

            PlasmaComponents3.Label {
                text:  qsTr("Battery")
                font.pointSize: Kirigami.Theme.smallFont.pointSize
                color: Kirigami.Theme.disabledTextColor
                font.letterSpacing:  1.5
                font.capitalization: Font.AllUppercase
            }

            Repeater {
                model: [
                    { label: qsTr("L"), value: dbusHelper.batteryLeft  },
                    { label: qsTr("R"), value: dbusHelper.batteryRight  },
                    { label: qsTr("Case"), value: dbusHelper.batteryCase   },
                ]

                RowLayout {
                    required property var modelData
                    Layout.fillWidth: true
                    spacing: Kirigami.Units.smallSpacing
                    visible: modelData.value >= 0

                    PlasmaComponents3.Label {
                        text:          modelData.label
                        font.bold:     true
                        color:         Kirigami.Theme.textColor
                        Layout.minimumWidth: Kirigami.Units.gridUnit
                    }

                    QQC2.ProgressBar {
                        Layout.fillWidth: true
                        from:  0; to: 100
                        value: modelData.value
                        // Colour shifts red below 20 %
                        palette.highlight: modelData.value <= 20
                                           ? "#F44336"
                                           : Kirigami.Theme.highlightColor
                    }

                    PlasmaComponents3.Label {
                        text:  modelData.value + "%"
                        color: Kirigami.Theme.textColor
                        Layout.minimumWidth: Kirigami.Units.gridUnit * 2
                        horizontalAlignment: Text.AlignRight
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // Error / not-configured notice
        // ----------------------------------------------------------------
        PlasmaComponents3.Label {
            Layout.fillWidth: true
            visible: !plasmoid.configuration.macAddress
            text:    qsTr("No device configured. Open settings to select your CMF Buds.")
            wrapMode: Text.WordWrap
            horizontalAlignment: Text.AlignHCenter
            color:   Kirigami.Theme.disabledTextColor
            font.pointSize: Kirigami.Theme.smallFont.pointSize
        }
    }
}
