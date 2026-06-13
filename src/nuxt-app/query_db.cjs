const sqlite3 = require('sqlite3').verbose();
const db = new sqlite3.Database('/home/constantin/HackTheParadise/src/nuxt-app/.data/db.sqlite');

db.all("SELECT dev_eui, brightness, synced_brightness FROM lights_db WHERE dev_eui IS NOT NULL", (err, rows) => {
    if (err) throw err;
    console.log(rows);
});
db.close();
