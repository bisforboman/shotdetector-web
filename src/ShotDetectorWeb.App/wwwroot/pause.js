// The host's 'pause' that .NET's Api.Detect imports (src/ShotDetectorWeb.App/Api.cs): a promise resolved in the worker's
// next task, so that the messages that came in while .NET ran (a stop, a cancel, a pause) are handled before the run
// goes on. A MessageChannel's message, not setTimeout(0): nested timeouts are held to at least 4 ms each. worker.js
// passes it to the runtime; tools/app-check.mjs does too under Node (tools/node-host.mjs).
// hold() pauses the run (backlog "Pause and resume a run"): the pauses that come while it is held don't resolve, so
// .NET's run simply waits at its next one, nothing cancelled or ended, until release() resolves them. A stop or a
// cancel while held is told to .NET first, then released, so the run ends at the pause it waits at (worker.js).
let channel, waiting = [], held = false, parked = [], onHeld = () => {};

export function pause(framesRead) {
  if (!channel) {
    channel = new MessageChannel();
    // Under Node the open port keeps the process alive: the checks end with process.exit.
    channel.port1.onmessage = () => {
      const [resolve, frames] = waiting.shift();
      if (!held) return resolve();
      parked.push(resolve);
      onHeld(frames);
    };
  }
  return new Promise(resolve => {
    waiting.push([resolve, framesRead]);
    channel.port2.postMessage(null);
  });
}

// f(framesRead) when the run comes to be held at a pause: the frames read by then, the frame a stop ends it at.
export function whenHeld(f) {
  onHeld = f;
}

// The run waits at its next pause until release().
export function hold() {
  held = true;
}

// The run goes on from the pause it waits at, if any.
export function release() {
  held = false;
  for (const resolve of parked.splice(0)) resolve();
}
