// The host's 'read' that .NET's BlobStream (src/ShotDetectorWeb.App/BlobStream.cs) imports: part of a Blob (the picked
// File), read synchronously while .NET waits, so the file is read in slices and never copied into memory in whole.
// worker.js passes it to the runtime; tools/app-check.mjs does too under Node, with a FileReaderSync of its own.
let reader;

// Reads view.length bytes of blob at position into view (a .NET Span, valid only during the call): how many it read,
// fewer at the blob's end.
export function read(blob, position, view) {
  reader ??= new FileReaderSync();
  const bytes = new Uint8Array(reader.readAsArrayBuffer(blob.slice(position, position + view.length)));
  view.set(bytes);
  return bytes.length;
}
