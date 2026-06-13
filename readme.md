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

Notiz an Dennis (Server starten):    HOST=0.0.0.0 pnpm run dev
