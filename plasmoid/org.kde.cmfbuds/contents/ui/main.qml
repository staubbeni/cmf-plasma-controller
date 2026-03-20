import QtQuick
import QtQuick.Layouts
import org.kde.plasma.plasmoid
import org.kde.plasma.components as PlasmaComponents3
import org.kde.kirigami as Kirigami

/**
 * main.qml — Compact (tray icon) representation for the CMF Buds widget.
 *
 * Displays a headset icon with a small coloured dot that reflects status:
 *   • Grey  → Disconnected / connecting
 *   • Red   → Connected but battery < 20 %
 *   • Green → Connected and battery OK
 */
PlasmoidItem {
    id: root

    // Preferred tray icon size
    implicitWidth:  Kirigami.Units.iconSizes.medium
    implicitHeight: Kirigami.Units.iconSizes.medium

    toolTipMainText: plasmoid.title
    toolTipSubText:  dbusHelper.connectionState === "connected"
                     ? qsTr("L: %1%  R: %2%  Case: %3  |  Mode: %4")
                         .arg(dbusHelper.batteryLeft)
                         .arg(dbusHelper.batteryRight)
                         .arg(caseBatteryLevel < 0 ? qsTr("N/A") : caseBatteryLevel + "%")
                         .arg(ancModeLabel)
                     : qsTr("Not connected")

    property int caseBatteryLevel: dbusHelper.batteryCase

    readonly property string ancModeLabel: {
        switch (dbusHelper.ancMode) {
        case "anc_high":     return qsTr("ANC High")
        case "anc_mid":      return qsTr("ANC Mid")
        case "anc_low":      return qsTr("ANC Low")
        case "anc_adaptive": return qsTr("ANC Adaptive")
        case "transparency": return qsTr("Transparency")
        case "off":          return qsTr("Off")
        default:             return dbusHelper.ancMode
        }
    }

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
                if (dbusHelper.connectionState !== "connected")
                    return Kirigami.Theme.disabledTextColor  // grey
                let minBatt = Math.min(
                    dbusHelper.batteryLeft  >= 0 ? dbusHelper.batteryLeft  : 100,
                    dbusHelper.batteryRight >= 0 ? dbusHelper.batteryRight : 100
                )
                if (minBatt < 20) return "#F44336"  // red – low battery
                return "#4CAF50"                    // green – connected & ok
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
