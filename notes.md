
## Systemübersicht: Hack The Paradise – Dim The Light (DTL)

Ein webbasiertes Dashboard zur Verwaltung von Straßenlaternen in Jena. Benutzer können Laternen auf einer Karte sehen, Helligkeit anpassen, Regionen definieren und Änderungen per LoRaWAN an die echten Geräte senden.

---

### Architektur-Überblick

```
Browser (Vue 3 / Nuxt 4)
    ↕ HTTP/Fetch
Nuxt Nitro Server (Node.js)
    ↕ SQLite (Nitro built-in)
    ↕ gRPC-Web over HTTPS
ChirpStack LoRaWAN Server (chirpstack.jena.de)
    ↕ LoRaWAN Radio
Straßenlaternen-Adapter (mit dev_eui)
```

---

### Datenpipeline (einmalige Vorbereitung)

1. **Rohdaten**: Leuchtstelle2.geojson – Positionen aller Laternen im Koordinatensystem ETRS89/EPSG:25832
2. **Konvertierung**: convertData.py transformiert via `pyproj` nach WGS84 → erzeugt data_wgs84.json
3. **DB-Seeding**: Beim ersten Start liest dbController.ts diese JSON und befüllt die SQLite-Tabelle `lights_db`

---

### Datenbankschema (`lights_db`)

| Spalte | Typ | Bedeutung |
|---|---|---|
| `pid` | INTEGER PK | Interne ID |
| `light_point_nr` | TEXT | Laternen-Nummer (z.B. `001-001`) |
| `lat` / `long` | DOUBLE | WGS84-Position |
| `brightness` | INTEGER | **Gewünschte** Helligkeit (0–100) |
| `dev_eui` | TEXT | LoRaWAN Device EUI (16-stellig hex) |
| `synced_brightness` | INTEGER | Zuletzt **übertragene** Helligkeit (-1 = nie) |

Die Differenz zwischen `brightness` und `synced_brightness` zeigt an, was noch nicht an die Lampe gesendet wurde.

---

### Server-API-Endpunkte

| Endpunkt | Methode | Funktion |
|---|---|---|
| /api/dbController | GET | Alle Lampen-Daten zurückgeben |
| /api/dbController | POST | Helligkeit für eine/mehrere Lampen in DB setzen |
| /api/getPoints | GET | Vereinfachte Punkt-Liste (lat/long/brightness) |
| /api/enqueue_downlink | POST | Einzelnen Downlink an ChirpStack senden (Test) |
| /api/syncDownlinks | POST | Alle ungesyncten Änderungen an alle Lampen senden |

---

### Frontend-Seiten und Komponenten

- **index.vue** – Startseite mit Suchfeld + Leaflet-Karte
- **regionen.vue** – Regionen per Lasso auf der Karte zeichnen, benennen, Helligkeit setzen, Sync auslösen. Regionen werden in `localStorage` gespeichert.
- **settings/index.vue** – Profilformular (nicht funktional) + manueller Downlink-Test
- **LeafletMap.client.vue** – Karte mit Marker-Clustering, Klick auf Marker öffnet Lampen-Info, Lasso-Zeichentool (Freehand-Polygon), gespeicherte Regionen als farbige Flächen

---

### ChirpStack-Integration

- Protokoll: **gRPC-Web** (framed binary über HTTPS), keine direkte gRPC-Verbindung
- Auth: `Authorization: Bearer <CHIRPSTACK_API_TOKEN>` (Umgebungsvariable)
- Payload: **1 Byte** (Wert 0–100 = Helligkeit), gesendet auf FPort 1
- Die gRPC-Protobuf-Objekte kommen aus `@chirpstack/chirpstack-api`

---

### Offene Punkte & Probleme

**Kritisch / Funktionslücken:**

1. **Dev-EUI-Mapping fehlt fast vollständig** – In dbController.ts sind nur 2 Lampen (`001-001`, `001-002`) mit einer dev_eui versehen. Alle anderen haben `NULL` → `syncDownlinks` überträgt diese nie. Es fehlt eine vollständige Zuordnung `light_point_nr → dev_eui`.

2. **Keine Authentifizierung** – Es gibt kein Login, keinen Schutz der API-Endpunkte. Die "Abmelden"-Schaltfläche im UserMenu tut nichts.

3. **Regionen nur in `localStorage`** – Beim Browserwechsel oder Löschen des Speichers gehen alle definierten Regionen verloren. Kein Server-seitiges Speichern.

4. **Kein Uplink / Status-Lesen** – Laut Readme soll es Statusanzeige geben (Ist vs. Soll, Timestamps, Batteriezustand). Das ist komplett nicht implementiert – es gibt nur einen Weg (Befehle senden), aber kein Lesen von Gerätedaten.

5. **AstroDim nicht implementiert** – Readme nennt „astroDim 5 Werte als JSON senden". Aktuell wird nur ein Helligkeitsbyte gesendet.

**Technische Schulden / Bugs:**

6. **Inkonsistenter DB-Response-Format**: dbController.ts behandelt `db.sql`-Ergebnis als direkte Array (`countResult[0]?.count`), während getPoints.ts `.rows` erwartet. Einer der beiden ist falsch – je nach Nitro-Version kann das Bugs verursachen. Die Karte in LeafletMap.client.vue erwartet außerdem `response.rows` von `dbController`, aber der gibt das Ergebnis direkt zurück.

7. **getPoints.ts ist veraltet** – Hat das alte Schema ohne `dev_eui`/`synced_brightness`, dupliziert die Tabelleninitialisierung, und könnte Konflikte verursachen. Könnte zugunsten von `dbController` entfernt werden.

8. **seedDatabase.py ist out-of-sync** – Nutzt `device_label` statt `dev_eui`, schreibt in einen anderen Pfad als Nitro's DB. Das Skript und die echte DB divergieren.

9. **Nicht existierende Route**: useDashboard.ts hat Shortcut `g-c → /lampen`, diese Seite existiert nicht.

10. **`grpcurl`-Binary im Repo** – Die Datei grpcurl ist ein ausführbares Binary direkt im Projektordner. Gehört nicht ins Repo.

11. **Kein `.env.example`** – `CHIRPSTACK_API_TOKEN` ist nicht dokumentiert.

---

### Was ich noch brauche / Fragen

Um die Analyse zu vervollständigen, wäre hilfreich:

- **Vollständige dev_eui ↔ Laternennummer-Zuordnung** – Habt ihr eine Liste oder Tabelle, welche physische Lampe welche dev_eui hat?
- **ChirpStack-Zugangsdaten-Struktur** – Was ist der Unterschied zwischen `GRPC_KEY` und dem API-Token (der Kommentar im Readme ist noch offen)?
- **AstroDim-Datenformat** – Wie sollen die 5 Werte konkret im Payload kodiert sein (Bytes, JSON, Protobuf-Felder)?
- **Authentifizierungs-Anforderung** – Soll es wirklich einen Login geben, oder reicht ein einfacher Zugriffsschutz (z.B. Basic Auth auf Proxy-Ebene)?