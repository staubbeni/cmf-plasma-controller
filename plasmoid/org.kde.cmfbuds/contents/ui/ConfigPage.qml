import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as QQC2
import org.kde.plasma.components as PlasmaComponents3
import org.kde.plasma.extras as PlasmaExtras
import org.kde.kirigami as Kirigami

/**
 * ConfigPage.qml — First-run / settings page.
 *
 * Lets the user:
 *   1. Pick their CMF Buds from a list of paired Bluetooth devices (auto-populated via cmfd).
 *   2. Enter the MAC address manually if auto-detection fails.
 *   3. Adjust poll interval and display preferences.
 */
Kirigami.ScrollablePage {
    id: configPage

    title: qsTr("CMF Buds Settings")

    // Fetched from the daemon on page open
    property var pairedDevices: []
    property bool loading:      false

    Component.onCompleted: fetchDevices()

    function fetchDevices() {
        loading = true
        dbusHelper.getPairedDevices(function(devices) {
            pairedDevices = devices
            loading = false
        })
    }

    ColumnLayout {
        spacing: Kirigami.Units.largeSpacing

        // ----------------------------------------------------------------
        // Device selection
        // ----------------------------------------------------------------
        Kirigami.FormLayout {
            Layout.fillWidth: true

            // Auto-detected list
            ColumnLayout {
                Kirigami.FormData.label: qsTr("Paired devices:")
                Layout.fillWidth: true
                spacing: 0

                PlasmaComponents3.BusyIndicator {
                    running: loading
                    visible: loading
                }

                Repeater {
                    model: pairedDevices
                    delegate: QQC2.RadioButton {
                        required property var modelData
                        required property int index
                        text: {
                            let parts = modelData.split("|")
                            return parts.length >= 2
                                   ? "%1  (%2)".arg(parts[1]).arg(parts[0])
                                   : modelData
                        }
                        checked: {
                            let parts = modelData.split("|")
                            return parts[0] === plasmoid.configuration.macAddress
                        }
                        onClicked: {
                            let parts = modelData.split("|")
                            if (parts.length >= 2) {
                                plasmoid.configuration.macAddress  = parts[0]
                                plasmoid.configuration.deviceName  = parts[1]
                            }
                        }
                    }
                }

                PlasmaComponents3.Label {
                    visible:  !loading && pairedDevices.length === 0
                    text:     qsTr("No paired devices found. Pair your CMF Buds via system Bluetooth settings first.")
                    wrapMode: Text.WordWrap
                    color:    Kirigami.Theme.disabledTextColor
                }
            }

            // Manual MAC entry
            RowLayout {
                Kirigami.FormData.label: qsTr("MAC address:")
                Layout.fillWidth: true

                QQC2.TextField {
                    id: macField
                    Layout.fillWidth: true
                    placeholderText:  "XX:XX:XX:XX:XX:XX"
                    text:             plasmoid.configuration.macAddress
                    inputMethodHints: Qt.ImhPreferUppercase
                    maximumLength:    17
                    validator: RegularExpressionValidator {
                        regularExpression: /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/
                    }
                }

                PlasmaComponents3.Button {
                    text:    qsTr("Apply")
                    enabled: macField.acceptableInput
                    onClicked: {
                        plasmoid.configuration.macAddress = macField.text.toUpperCase()
                    }
                }
            }

            // Device name override
            QQC2.TextField {
                Kirigami.FormData.label: qsTr("Device label:")
                text:             plasmoid.configuration.deviceName
                onEditingFinished: plasmoid.configuration.deviceName = text
            }
        }

        Kirigami.Separator { Layout.fillWidth: true }

        // ----------------------------------------------------------------
        // Display preferences
        // ----------------------------------------------------------------
        Kirigami.FormLayout {
            Layout.fillWidth: true

            QQC2.SpinBox {
                Kirigami.FormData.label: qsTr("Battery poll interval (s):")
                from:  10; to: 300; stepSize: 10
                value: plasmoid.configuration.batteryPollInterval
                onValueModified: plasmoid.configuration.batteryPollInterval = value
            }

            QQC2.CheckBox {
                Kirigami.FormData.label: qsTr("Show battery in tooltip:")
                checked: plasmoid.configuration.showBatteryInTray
                onToggled: plasmoid.configuration.showBatteryInTray = checked
            }
        }

        // ----------------------------------------------------------------
        // Refresh button
        // ----------------------------------------------------------------
        PlasmaComponents3.Button {
            text:    qsTr("Refresh device list")
            icon.name: "view-refresh"
            onClicked: fetchDevices()
        }
    }
}
