import device_pb from "@chirpstack/chirpstack-api/api/device_pb";

export default defineEventHandler(async (event) => {
  const { DeviceQueueItem, EnqueueDeviceQueueItemRequest, EnqueueDeviceQueueItemResponse } = device_pb;

  // Der Token kommt jetzt (richtigerweise) aus den Environment Variablen
  const apiToken = (process.env.CHIRPSTACK_API_TOKEN ?? "").trim(); 
  console.log("Using API Token:", apiToken ? "****" + apiToken.slice(-4) : "No token provided");
  
  // WICHTIG: Die URL muss genau "api.DeviceService/Enqueue" lauten (nicht chirpstack.api...)
  const serverUrl = "https://chirpstack.jena.de/api.DeviceService/Enqueue";

  const devEui = "058f765deee4c078";

  const item = new DeviceQueueItem();
  // WICHTIG: Die JS/TS Methoden von Protobuf nutzen immer camelCase (setDevEui, nicht set_dev_eui)
  item.setDevEui(devEui);
  item.setFPort(1);
  item.setConfirmed(false);
  item.setData(new Uint8Array([1, 2, 3]));

  const req = new EnqueueDeviceQueueItemRequest();
  req.setQueueItem(item);

  // Serialize the gRPC request payload to binary
  const payload = req.serializeBinary();
  
  // Construct a grpc-web framed message:
  // 1 byte flag (0 = Data) + 4 bytes length (big-endian) + the payload
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
    const grpcMessage = response.headers.get("grpc-message");
    
    // Check for any gRPC-level errors returned by Chirpstack
    if (grpcStatus && grpcStatus !== "0") {
      throw new Error(`gRPC Error ${grpcStatus}: ${grpcMessage || "Unknown"}`);
    }

    if (!response.ok) {
      throw new Error(`HTTP Error: ${response.status} ${response.statusText}`);
    }

    // Decode the grpc-web framed response to get the downlink ID
    const respBuffer = await response.arrayBuffer();
    const respView = new Uint8Array(respBuffer);
    
    // Check if the response has the 5-byte grpc-web frame header
    if (respView.length > 5 && respView[0] === 0) {
      const msgLength = (respView[1] << 24) | (respView[2] << 16) | (respView[3] << 8) | respView[4];
      const msgBytes = respView.slice(5, 5 + msgLength);
      
      const enqueueResp = EnqueueDeviceQueueItemResponse.deserializeBinary(msgBytes);
      const downlinkId = enqueueResp.getId();
      console.log("Downlink has been enqueued with id: " + downlinkId);
      return { success: true, id: downlinkId };
    }
    
    return { success: true };
  } catch (err) {
    console.error("Downlink Error:", err);
    throw createError({
      statusCode: 500,
      statusMessage: String(err),
    });
  }
});