// Starts the .NET runtime under Node with the command line's arguments, and gives Program.cs a way to read and write
// files (Emscripten's file system doesn't see the host's).
import { dotnet } from './_framework/dotnet.js';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const { setModuleImports } = await dotnet.withApplicationArguments(...process.argv.slice(2)).create();
setModuleImports('main.mjs', {
  readFile: path => readFileSync(path).toString('base64'),
  writeFile: (path, text) => {
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, text);
  },
});
await dotnet.run();
