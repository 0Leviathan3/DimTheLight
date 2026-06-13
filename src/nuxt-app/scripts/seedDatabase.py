import json
import sqlite3
import os

# Pfade relativ zum nuxt-app Ordner
DB_PATH = os.path.join(os.path.dirname(__file__), '..', 'server', 'data', 'db.sqlite')
JSON_PATH = os.path.join(os.path.dirname(__file__), '..', 'server', 'data', 'data_wgs84.json')

# Verbindung zur SQLite-DB herstellen (.data/ Ordner anlegen falls nötig)
os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
conn = sqlite3.connect(DB_PATH)
cursor = conn.cursor()

# Tabelle anlegen (identisch mit dbController.ts)
cursor.execute('''
    CREATE TABLE IF NOT EXISTS lights_db (
        pid INTEGER PRIMARY KEY AUTOINCREMENT,
        light_point_nr TEXT,
        lat REAL,
        long REAL,
        brightness INTEGER DEFAULT 100,
        device_label VARCHAR(16)
    )
''')
# Nachrüsten fehlender Spalten
existing_cols = [row[1] for row in cursor.execute('PRAGMA table_info(lights_db)')]
migrations = {
    'device_label': 'TEXT'
}
for col, definition in migrations.items():
    if col not in existing_cols:
        cursor.execute(f'ALTER TABLE lights_db ADD COLUMN {col} {definition}')
        print(f"Spalte '{col}' hinzugefügt.")

# Prüfen ob in db Daten stehen
cursor.execute('SELECT COUNT(*) FROM lights_db')
count = cursor.fetchone()[0]

if count > 0:
    print(f"DB bereits befüllt ({count} Einträge). Abbruch.")
    conn.close()
    exit()

# JSON laden und einfügen
with open(JSON_PATH, 'r', encoding='utf-8') as f:
    lights = json.load(f)

for light in lights:
    cursor.execute(
        'INSERT INTO lights_db (light_point_nr, lat, long) VALUES (?, ?, ?)',
        (light['id_jena'], light['lat'], light['long'])
    )

conn.commit()
print(f"{len(lights)} Einträge erfolgreich in lights_db eingefügt.")

# Zur Kontrolle: erste 5 Zeilen ausgeben
cursor.execute('SELECT * FROM lights_db LIMIT 5')
rows = cursor.fetchall()
print("\nErste 5 Einträge:")
print(f"{'pid':<5} {'light_point_nr':<15} {'lat':<12} {'long':<12} {'brightness'} {'device_label'}")
for row in rows:
    print(f"{row[0]:<5} {row[1]:<15} {row[2]:<12} {row[3]:<12} {row[4]}")

conn.close()