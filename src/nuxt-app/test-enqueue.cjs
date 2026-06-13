const grpc = require("@grpc/grpc-js");
const device_grpc = require("@chirpstack/chirpstack-api/api/device_grpc_pb");
const device_pb = require("@chirpstack/chirpstack-api/api/device_pb");

const testEnqueue = (host) => {
  const creds = grpc.credentials.createInsecure();
  const client = new device_grpc.DeviceServiceClient(host, creds);
  
  const devEui = "058f765deee4c078";
  const apiToken = "2dd17a30-3a59-4f80-acb4-9054dda3a531";

  const metadata = new grpc.Metadata();
  metadata.set("authorization", "Bearer " + apiToken);

  const item = new device_pb.DeviceQueueItem();
  item.setDevEui(devEui);
  item.setFPort(1);
  item.setConfirmed(false);
  item.setData(new Uint8Array([1, 2, 3]));

  const enqueueReq = new device_pb.EnqueueDeviceQueueItemRequest();
  enqueueReq.setQueueItem(item);

  return new Promise((resolve) => {
    client.enqueue(enqueueReq, metadata, (err, resp) => {
      if (err) {
        console.log(`[${host}] Error:`, err.message);
      } else {
        console.log(`[${host}] Success: id=${resp.getId()}`);
      }
      resolve();
    });
  });
};

testEnqueue("chirpstack.jena.de:8080");
