const device_pb = require("@chirpstack/chirpstack-api/api/device_pb");
const item = new device_pb.DeviceQueueItem();
console.log(Object.keys(Object.getPrototypeOf(item)).filter(k => k.startsWith('set')));
