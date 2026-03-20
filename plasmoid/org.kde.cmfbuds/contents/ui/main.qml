import QtQuick
import QtQuick.Layouts
import org.kde.plasma.plasmoid
import org.kde.plasma.components as PlasmaComponents3
import org.kde.kirigami as Kirigami

/**
 * main.qml — Compact (tray icon) representation for the CMF Buds widget.
 *
 * Displays a headset icon with a small coloured dot that reflects the
 * current ANC mode:
 *   • Green  → Off
 *   • Blue   → ANC active
 *   • Amber  → Transparency active
 *   • Red    → Disconnected / error
 */
PlasmoidItem {
    id: root

    // Preferred tray icon size
    implicitWidth:  Kirigami.Units.iconSizes.medium
    implicitHeight: Kirigami.Units.iconSizes.medium

    toolTipMainText: plasmoid.title
    toolTipSubText:  dbusHelper.connectionState === "connected"
                     ? qsTr("L: %1%  R: %2%  Case: %3%  |  Mode: %4")
                         .arg(dbusHelper.batteryLeft)
                         .arg(dbusHelper.batteryRight)
                         .arg(caseBatteryLevel)
                         .arg(dbusHelper.ancMode)
                     : qsTr("Not connected")

    property int caseBatteryLevel: dbusHelper.batteryCase

    // -------------------------------------------------------------------
    // Compact representation: icon + status dot
    // -------------------------------------------------------------------
    compactRepresentation: Item {
        MouseArea {
            anchors.fill: parent
            onClicked: root.expanded = !root.expanded
        }

        Kirigami.Icon {
            anchors.fill: parent
            source:       Qt.resolvedUrl("assets/ear_icon.png")
        }

        // Status dot (bottom-right corner)
        Rectangle {
            width:  6; height: 6
            radius: 3
            anchors {
                right:  parent.right
                bottom: parent.bottom
                margins: 1
            }
            color: {
                switch (dbusHelper.connectionState) {
                case "connected":
                    if (dbusHelper.ancIsNc)          return Kirigami.Theme.highlightColor
                    if (dbusHelper.ancMode === "transparency") return "#E6A817"
                    return "#4CAF50"
                case "connecting": return "#E6A817"
                case "error":      return "#F44336"
                default:           return Kirigami.Theme.disabledTextColor
                }
            }
        }
    }

    // -------------------------------------------------------------------
    // Full popup: FullRepresentation.qml
    // -------------------------------------------------------------------
    fullRepresentation: FullRepresentation { id: fullRep }

    // -------------------------------------------------------------------
    // D-Bus helper object (session bus, auto-starts cmfd via activation)
    // -------------------------------------------------------------------
    CmfDbusHelper {
        id: dbusHelper
        macAddress: plasmoid.configuration.macAddress
    }
}
