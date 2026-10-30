// The host's 'parts', 'append' and 'blob' that .NET's BlobWriter (src/ShotDetectorWeb.App/BlobWriter.cs) imports: text
// .NET writes in pieces (the stats CSV, Api's StatsCsv) made into a Blob here, each piece copied out as it comes, so the
// whole text is never one string in .NET's heap (README "What the heap holds"). worker.js passes them to the runtime;
// tools/app-check.mjs does too under Node.

// A new, empty list of parts.
export const parts = () => [];

// Adds a copy of view (a .NET Span, valid only during the call) to parts.
export function append(parts, view) {
  parts.push(view.slice());
}

// The Blob of parts, of type.
export const blob = (parts, type) => new Blob(parts, { type });
