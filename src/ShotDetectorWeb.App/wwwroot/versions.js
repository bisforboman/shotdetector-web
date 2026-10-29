// The versions in the footer: each package's, read from its assembly in the bundle (Api's Versions), after the
// library's name in "What runs where", and the runtime's after ".NET". Not a panel (nothing of a file or a run): it
// fills the footer's <span class="version"> slots once the worker answers, and leaves them empty if it doesn't.
// tools/app-check.mjs checks the same answer against Directory.Packages.props' pins.
import { call } from './app.js';

const TITLE = "The package's version, read from its assembly in the bundle; the check compares it with the version " +
  'Directory.Packages.props pins.';

try {
  const { runtime, packages } = JSON.parse(await call('Versions'));
  for (const slot of document.querySelectorAll('footer .version[data-package]')) {
    const version = packages[slot.dataset.package];
    if (!version) continue;
    slot.textContent = ` ${version}`;
    slot.title = TITLE;
  }
  const slot = document.querySelector('footer .version[data-runtime]');
  if (slot && runtime) {
    // RuntimeInformation.FrameworkDescription, ".NET 10.0.0": the footer already says .NET.
    slot.textContent = ` ${runtime.replace(/^\.NET\s*/, '')}`;
    slot.title = 'The runtime, as RuntimeInformation.FrameworkDescription says it.';
  }
} catch {
  // A worker that was replaced before it answered, or a bundle without the export: the footer stays as it is.
}
