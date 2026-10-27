// The run's warnings, a card beside the video under the run's: what ShotDetector says the user should know about how the video was read (Api's Detect,
// its warnings: DetectionResult.Warnings), each sentence as the library words it. Shown only when a finished run has
// any; a new file or a new run takes them away. The page adds none of its own.
import { card, on } from '../app.js';

const section = card('Warnings', { place: 'run' });
section.id = 'warnings';
section.hidden = true;
section.setAttribute('role', 'status');
const list = document.createElement('ul');
section.append(list);

function clear() {
  section.hidden = true;
  list.replaceChildren();
}

on('chose', clear);
on('run', clear);
on('done', ({ result: { warnings = [] } }) => {
  clear();
  for (const warning of warnings) {
    const item = document.createElement('li');
    item.textContent = warning;
    list.append(item);
  }
  section.hidden = warnings.length === 0;
});
