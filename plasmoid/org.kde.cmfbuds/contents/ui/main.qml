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
                         .arg(dbusHelper.currentMode)
                     : qsTr("Not connected")

    property int caseBatteryLevel: dbusHelper.batteryCase

    // -------------------------------------------------------------------
    // Compact representation: icon + status dot
    // -------------------------------------------------------------------
    compactRepresentation: Item {
        Kirigami.Icon {
            anchors.fill:   parent
            source:         "audio-headset"
            color:          Kirigami.Theme.textColor
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
                    switch (dbusHelper.currentMode) {
                    case "anc":          return Kirigami.Theme.highlightColor
                    case "transparency": return "#E6A817"
                    default:             return "#4CAF50"
                    }
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
    CmfDbusHelper { id: dbusHelper }
}
