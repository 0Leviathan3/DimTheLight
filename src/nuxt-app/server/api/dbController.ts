import fs from 'node:fs'
import path from 'node:path'

export default defineEventHandler(async (event) => {
  // 1. Verbindung zur integrierten SQLite-Datenbank holen
  const db = useDatabase()
  const method = getMethod(event) // GET oder POST herausfinden

  // 2. Tabelle anlegen (Standard-SQL, das du kennst!)
  // Wir fügen direkt eine Spalte für die "helligkeit" hinzu
  await db.sql`
    CREATE TABLE IF NOT EXISTS lights_db (
      pid INTEGER PRIMARY KEY AUTOINCREMENT,
      light_point_nr TEXT,
      lat DOUBLE,
      long DOUBLE,
      brightness INTEGER DEFAULT 100
    )
  `
  // Daatenbank mit json Daten befüllen falls leer
  const countResult = await db.sql`SELECT COUNT(*) as count FROM lights_db` as any[]
  const isDbEmpty = countResult[0]?.count === 0

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

  // Daten holen zum Darstellen auf der Karte
  if (method === 'GET') {
    const data = await db.sql`SELECT * FROM lights_db` as any[]
    return data
  }

  // aktualisierte Daten in db sepichern
  if (method === 'POST') {
    const body = await readBody(event)

    await db.sql`
      UPDATE lights_db 
      SET brightness = ${body.brightness} 
      WHERE light_point_nr = ${body.light_point_nr}
    `
    
    return { success: true, message: `Helligkeit für Lampe ${body.light_point_nr} auf ${body.brightness}% gesetzt.` }
  }
})