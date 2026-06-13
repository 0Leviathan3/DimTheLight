const grpc = require("@grpc/grpc-js");
const device_grpc = require("@chirpstack/chirpstack-api/api/device_grpc_pb");

const testConnection = (host, secure) => {
  console.log(`Testing ${host} with secure=${secure}`);
  const creds = secure ? grpc.credentials.createSsl() : grpc.credentials.createInsecure();
  const client = new device_grpc.DeviceServiceClient(host, creds);
  
  return new Promise((resolve) => {
    client.waitForReady(Date.now() + 5000, (err) => {
      if (err) {
        console.log(`Failed to connect to ${host} (secure=${secure}):`, err.message);
      } else {
        console.log(`Successfully connected to ${host} (secure=${secure})`);
      }
      client.close();
      resolve();
    });
  });
};

async function run() {
  await testConnection("chirpstack.jena.de", false);
  await testConnection("chirpstack.jena.de:8080", false);
  await testConnection("chirpstack.jena.de:443", true);
}
run();
