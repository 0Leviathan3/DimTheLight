const grpc = require("@grpc/grpc-js");
const device_grpc = require("@chirpstack/chirpstack-api/api/device_grpc_pb");
const device_pb = require("@chirpstack/chirpstack-api/api/device_pb");

const testApiCallSsl = (host) => {
  const creds = grpc.credentials.createSsl();
  const client = new device_grpc.DeviceServiceClient(host, creds);
  
  const devEui = "058f765deee4c078";
  const apiToken = "2dd17a30-3a59-4f80-acb4-9054dda3a531";

  const metadata = new grpc.Metadata();
  metadata.set("authorization", "Bearer " + apiToken);

  const getReq = new device_pb.GetDeviceRequest();
  getReq.setDevEui(devEui);

  return new Promise((resolve) => {
    client.get(getReq, metadata, (err, resp) => {
      if (err) {
        console.log(`[${host} (SSL)] Error:`, err.message);
      } else {
        console.log(`[${host} (SSL)] Success:`, resp.toObject());
      }
      resolve();
    });
  });
};

async function run() {
  await testApiCallSsl("chirpstack.jena.de:443");
  await testApiCallSsl("chirpstack.jena.de:8080");
}
run();
