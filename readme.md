# HACK THE PARADISE - DTL

## Frontend-UI

### Was wir brauchen
- Openstreetmap für alle Lampen
    - Lampen anklickbar
        - Daten über Lampen herausfinden, wenn angeklickt (?)
    - Lampen clustern/entclustern, je nach dem, wie weit man weg ist
- Lampen dimmen
    - slider oder feste Zahlen zum einrasten (30%, 70%)
        - muss man schauen, was die Lampen können
- Regionen festlegen, z.B. Baustelle
    - Eigene Regionen festlegen können
- Login screen
    - Validierung
- Statusinterface der gelesenen Daten
    - aktuell aktive Einstellung vs. im System hinterlegte Einstellung (ist vs soll)
    - evtl. auch mit Historie, um zu sehen, wie sich die Einstellungen über die Zeit verändert haben
    - Timestamps, wann Adapter zuletzt mit LoRaWAN kommuniziert haben
    - (Batteriestatus des Adapters, wenn möglich)
    - Status des Beschreibens des NFC-Chips (erfolgreich oder nicht)

### Wie wir es umsetzen
- Sidebar zum Auswählen was man machen möchte
- Leaflet um Openstreetmap anzuschauen und cluster zu machen
- Authform (vue) für Login
- Daten in GeoJSON vorliegen
    - Koordinatensystem: etrs89
    - Daten mit Python Skript in WGS84 konvertieren, damit sie in Leaflet angezeigt werden können
        - Installation:
            - `sudo apt install python3-py`
            - `sudo apt install python3-pyproj`


- Daten über chirpStack API senden
- API Key liegt unter Tenant -> API Keys
    - Wo ist Unterschied zwischen GRPC_KEY und XXX

- Wir brauchen Daten:
    - astroDim 5 Werte als json senden
    - Helligkeit
    - 3 Keys (chirpStack-intern)
- Daten über LoRaWAN an die Lampen senden


- Es braucht einen Wert 0-100


Notiz an Dennis (Server starten):    HOST=0.0.0.0 pnpm run dev










# Hack The Paradise - Dim The Light

## Beschreibung
*Die Software für das Projekt "Dim The Light" ist eine webbasierte Dashboard-Lösung für eine simple Steuerung und Verwaltung von Straßenlaternen in der Stadt Jena über das LoRaWAN-Netzwerk. Die Helligkeit der Lampen lässt sich eizeln oder gruppiert (Region) über das entwickelte Softwaresystem und den pysischen Adapter einstellen und speichern. Die Software kommuniziert mir dem Jenaer ChirpStack-Server, die als API Schnittstelle zum LoRaWAN-Netzwerk dient. Der Adapter Verbindet das LoRaWAN-Netzwerk über einen Raspberry Pi Pico mit dem integrierten NFC-Modul. Dises beschreibt und ließt von dem OSRAM-Netzteil.*





anzeigen zu lassen, konkrete Straßenlampen nach ihren Jena Leuchtstellen-Nummer zu suchen (z.B. 001-001)

## Systemarchitektur
```
Browser (Vue 3 / Nuxt 4)
    | HTTP / Fetch
Nuxt Nitro Server (Node.js)
    | SQLite (Nitro built-in)
    | gRPC-Web über HTTPS
ChirpStack LoRaWAN Server (chirpstack.jena.de)
    | LoRaWAN Funknetz
Straßenlaternen-Adapter (mit dev_eui)
```

## Features
### Interaktive Karte
- Anzeigen aller Laternenpunkte via Leaflet.js inklusive Marker-Clustering.
- einzelne Laternen ansteuern via klick
- Helligkeiten können über Dashboard eingestellt und gespeichert werden

### LoRaWAN-Sync
- Übertragung von Helligkeitswerten als 1-Byte-Payload über gRPC-Web an ChirpStack

### Regionen-Steuerung
-  man kann Regionen definieren durch Freihand-Auswahl, um mehrere Lampen gleichzeitig anzusteuern und Helligkeitswerte zu setzen

### Soll/Ist-Vergleich
- Falls Anpassungen vorliegen werden diese mit den aktuellen Werten verglichen, um ausschließlich neue Werte zu übertragen.

## Getting Started
t.b.d
### Voraussetzungen
t.b.d



### Umgebungsvariablen
t.b.d


### Installation & Datenaufbereitung
t.b.d


