// The page's worker's host for the App's runtime, under Node (tools/app-check.mjs, tools/replace-check.mjs): the published
// blob-read.js as the 'read' .NET's BlobStream imports, and a FileReaderSync for it, which Node lacks.
//
// Node has no synchronous way to read a Blob, so this FileReaderSync hands each slice to a worker thread, which reads
// it asynchronously into shared memory while this thread waits (Atomics.wait). The worker gets a copy of the slice by
// structured clone, so files are Blobs held in memory (fileBlob): a file-backed Blob (fs.openAsBlob) aborts Node when
// a worker reads its clone (Node 22). The samples are small; large files are measured in a browser (README).
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { Worker } from 'node:worker_threads';

let worker;

class FileReaderSync {
  readAsArrayBuffer(blob) {
    worker ??= new Worker(`
      const { parentPort } = require('node:worker_threads');
      parentPort.on('message', async ({ blob, shared, signal }) => {
        try {
          new Uint8Array(shared).set(new Uint8Array(await blob.arrayBuffer()));
          Atomics.store(signal, 0, 1);
        } catch (e) {
          console.error(e);
          Atomics.store(signal, 0, 2);
        }
        Atomics.notify(signal, 0);
      });`, { eval: true });
    worker.unref();
    const shared = new SharedArrayBuffer(blob.size), signal = new Int32Array(new SharedArrayBuffer(4));
    worker.postMessage({ blob, shared, signal });
    Atomics.wait(signal, 0, 0);
    if (signal[0] !== 1) throw new Error('FileReaderSync (tools/node-host.mjs): the slice could not be read.');
    return new Uint8Array(shared).slice().buffer;
  }
}

// The host module's functions as worker.js gives them, but progress, which each check gives its own.
export async function host(wwwroot) {
  globalThis.FileReaderSync ??= FileReaderSync;
  const { read } = await import(pathToFileURL(resolve(wwwroot, 'blob-read.js')).href);
  return { read };
}

// A file as the page gets one: a Blob (a File is one) of its bytes.
export const fileBlob = path => new Blob([readFileSync(path)]);
