import fs from 'node:fs'
import path from 'node:path'

export default defineEventHandler(async () => {
  const db = useDatabase()

  // Tabelle anlegen (gleiche Struktur wie dbController)
  await db.sql`
    CREATE TABLE IF NOT EXISTS lights_db (
      pid INTEGER PRIMARY KEY AUTOINCREMENT,
      light_point_nr TEXT,
      lat DOUBLE,
      long DOUBLE,
      brightness INTEGER DEFAULT 100
    )
  `

  // Datenbank mit JSON-Daten befüllen falls leer
  const countResult = await db.sql`SELECT COUNT(*) as count FROM lights_db`
  const count = (countResult as any)?.rows?.[0]?.count ?? 0
  const isDbEmpty = count === 0

  if (isDbEmpty) {
    const jsonPath = path.resolve(process.cwd(), 'server/data/data_wgs84.json')

    if (fs.existsSync(jsonPath)) {
      const rawData = fs.readFileSync(jsonPath, 'utf-8')
      const lightList = JSON.parse(rawData)

      for (const light of lightList) {
        await db.sql`
          INSERT INTO lights_db (light_point_nr, lat, long)
          VALUES (${light.id_jena}, ${light.lat}, ${light.long})
        `
      }
    }
  }

  // Alle Punkte zurückgeben
  const result = await db.sql`SELECT light_point_nr, lat, long, brightness FROM lights_db`
  return (result as any)?.rows ?? []
})
