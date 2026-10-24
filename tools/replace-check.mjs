// A bundle published with a replaced ShotDetector.FastYuv (tools/check.sh replace, README "Licensing") must run the
// replacement: its converter's constructor throws MARK, so Detect has to fail with that message.
//   node tools/replace-check.mjs <the publish's wwwroot> <sample.mp4>
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const MARK = 'This is the replaced ShotDetector.FastYuv.';
const [wwwroot, sample] = process.argv.slice(2);
const { dotnet } = await import(pathToFileURL(resolve(wwwroot, '_framework/dotnet.js')).href);
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
setModuleImports('host', { progress: () => {} });
const { Open, Detect } = (await getAssemblyExports(getConfig().mainAssemblyName)).ShotDetectorWeb.App.Api;
await runMain();

Open(readFileSync(sample));
let message;
try {
  Detect(JSON.stringify({ detector: 'content' }));
  message = '(no exception: the package\'s FastYuv ran)';
} catch (e) {
  message = e.message.split('\n')[0];
}
const ok = message === MARK;
console.error(ok ? `The replaced FastYuv ran: "${message}"` : `Not the replaced FastYuv: ${message}`);
process.exit(ok ? 0 : 1);
