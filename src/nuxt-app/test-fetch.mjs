import device_pb from "@chirpstack/chirpstack-api/api/device_pb.js";

async function testFetch() {
  const serverUrl = "https://chirpstack.jena.de/api.DeviceService/Enqueue";
  const devEui = "058f765deee4c078";
  const apiToken = "2dd17a30-3a59-4f80-acb4-9054dda3a531";

  const item = new device_pb.DeviceQueueItem();
  item.setDevEui(devEui);
  item.setFPort(1);
  item.setConfirmed(false);
  item.setData(new Uint8Array([1, 2, 3]));

  const enqueueReq = new device_pb.EnqueueDeviceQueueItemRequest();
  enqueueReq.setQueueItem(item);

  const payload = enqueueReq.serializeBinary();
  
  const buffer = new Uint8Array(5 + payload.length);
  buffer[0] = 0;
  buffer[1] = (payload.length >> 24) & 0xFF;
  buffer[2] = (payload.length >> 16) & 0xFF;
  buffer[3] = (payload.length >> 8) & 0xFF;
  buffer[4] = payload.length & 0xFF;
  buffer.set(payload, 5);

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
  
  if (grpcStatus && grpcStatus !== "0") {
    console.error(`gRPC Error ${grpcStatus}: ${grpcMessage}`);
    return;
  }

  console.log("Success!");
}
testFetch();
