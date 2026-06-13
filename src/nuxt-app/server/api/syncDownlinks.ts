import device_pb from "@chirpstack/chirpstack-api/api/device_pb";

export default defineEventHandler(async (event) => {
  const db = useDatabase();

  // Find unsynced
  const unsynced = await db.sql`
    SELECT * FROM lights_db 
    WHERE brightness != synced_brightness AND dev_eui IS NOT NULL
  ` as any[];

  // Note: Nuxt's db.sql returns an object with a 'rows' property in some versions (like libSQL),
  // but if we look at dbController.ts:
  // "const data = await db.sql`SELECT * FROM lights_db` as any[]"
  // "return data"
  // So it seems it returns the rows directly or rows are accessed somehow.
  // Wait, in dbController.ts:
  // "const countResult = await db.sql`SELECT COUNT(*) as count FROM lights_db` as any[]"
  // "const isDbEmpty = countResult[0]?.count === 0"
  // This implies the result is an array of rows!
  // BUT in getPoints.ts:
  // "const countResult = await db.sql`SELECT COUNT(*) as count FROM lights_db`"
  // "const count = (countResult as any)?.rows?.[0]?.count ?? 0"
  // "const result = await db.sql`SELECT light_point_nr, lat, long, brightness FROM lights_db`"
  // "return (result as any)?.rows ?? []"
  // It looks like useDatabase in nitro returns { rows: [...] } actually, and dbController might have a small bug if it assumes array.
  
  let rows = [];
  if (Array.isArray(unsynced)) {
    rows = unsynced;
  } else if (unsynced && typeof unsynced === 'object' && 'rows' in unsynced) {
    rows = (unsynced as any).rows;
  }

  if (!rows || rows.length === 0) {
    return { success: true, message: "Keine geänderten Daten zu senden." };
  }

  const { DeviceQueueItem, EnqueueDeviceQueueItemRequest, EnqueueDeviceQueueItemResponse } = device_pb;
  const apiToken = (process.env.CHIRPSTACK_API_TOKEN ?? "").trim(); 
  const serverUrl = "https://chirpstack.jena.de/api.DeviceService/Enqueue";

  let successCount = 0;

  for (const light of rows) {
    const item = new DeviceQueueItem();
    item.setDevEui(light.dev_eui);
    item.setFPort(1);
    item.setConfirmed(false);
    const numericBrightness = Number(light.brightness)
    item.setData(new Uint8Array([numericBrightness]));

    const req = new EnqueueDeviceQueueItemRequest();
    req.setQueueItem(item);
    const payload = req.serializeBinary();
    
    const buffer = new Uint8Array(5 + payload.length);
    buffer[0] = 0;
    buffer[1] = (payload.length >> 24) & 0xFF;
    buffer[2] = (payload.length >> 16) & 0xFF;
    buffer[3] = (payload.length >> 8) & 0xFF;
    buffer[4] = payload.length & 0xFF;
    buffer.set(payload, 5);

    try {
      const response = await fetch(serverUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/grpc-web+proto",
          "X-Grpc-Web": "1",
          "Authorization": "Bearer " + apiToken
        },
        body: buffer
      });

      const grpcStatus = response.headers.get("grpc-status");
      if (grpcStatus && grpcStatus !== "0") {
        console.error(`gRPC Error ${grpcStatus} for devEui ${light.dev_eui}`);
        continue;
      }

      if (response.ok) {
        // Mark as synced
        const numericBrightness = Number(light.brightness)
        await db.sql`
          UPDATE lights_db 
          SET synced_brightness = ${numericBrightness} 
          WHERE light_point_nr = ${light.light_point_nr}
        `;
        successCount++;
      }
    } catch (err) {
      console.error(`Error syncing devEui ${light.dev_eui}:`, err);
    }
  }

  if (successCount === 0 && rows.length > 0) {
    throw createError({
      statusCode: 500,
      statusMessage: `Fehler beim Synchronisieren: 0 von ${rows.length} Lampen aktualisiert. Bitte Server-Logs prüfen.`
    });
  } else if (successCount < rows.length) {
    return { 
      success: true, 
      message: `Teilweise erfolgreich: Nur ${successCount} von ${rows.length} Lampen aktualisiert.` 
    };
  }

  return { 
    success: true, 
    message: `${successCount} von ${rows.length} geänderten Lampen erfolgreich aktualisiert.` 
  };
});
