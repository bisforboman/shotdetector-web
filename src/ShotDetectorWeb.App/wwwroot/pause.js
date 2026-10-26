// The host's 'pause' that .NET's Api.Detect imports (src/ShotDetectorWeb.App/Api.cs): a promise resolved in the worker's
// next task, so that the messages that came in while .NET ran (a stop, a cancel) are handled before the run goes on. A
// MessageChannel's message, not setTimeout(0): nested timeouts are held to at least 4 ms each. worker.js passes it to the
// runtime; tools/app-check.mjs does too under Node (tools/node-host.mjs).
let channel, waiting = [];

export function pause() {
  if (!channel) {
    channel = new MessageChannel();
    // Under Node the open port keeps the process alive: the checks end with process.exit.
    channel.port1.onmessage = () => waiting.shift()();
  }
  return new Promise(resolve => {
    waiting.push(resolve);
    channel.port2.postMessage(null);
  });
}
