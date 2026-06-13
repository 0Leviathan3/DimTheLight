const grpc = require("@grpc/grpc-js");
const device_grpc = require("@chirpstack/chirpstack-api/api/device_grpc_pb");
const device_pb = require("@chirpstack/chirpstack-api/api/device_pb");

const testEnqueue = (host, options) => {
  const creds = grpc.credentials.createInsecure();
  const client = new device_grpc.DeviceServiceClient(host, creds, options);
  
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
        console.log(`Error with options ${JSON.stringify(options)}:`, err.message);
      } else {
        console.log(`Success!`);
      }
      client.close();
      resolve();
    });
  });
};

async function run() {
  await testEnqueue("chirpstack.jena.de:8080", {
    "grpc.keepalive_time_ms": 10000,
    "grpc.keepalive_timeout_ms": 5000,
    "grpc.keepalive_permit_without_calls": 1
  });
  await testEnqueue("chirpstack.jena.de:8080", {
    "grpc.enable_http_proxy": 0
  });
}
run();
