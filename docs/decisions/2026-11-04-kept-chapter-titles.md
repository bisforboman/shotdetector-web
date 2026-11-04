# Chapter titles kept with the kept run (2026-11-04)

The owner's queue of 2026-11-04 (through the coordinator): the Maybe row "Chapter titles kept with the kept run". The
titles typed for a run's YouTube chapters (the run card's Titles) last while the run is shown; a run kept in IndexedDB
(decision 2026-10-30-resume-across-visits.md) and shown again on a later visit comes back without them. One thing the
row doesn't settle.

## What there is

- A run is kept when it is stopped (to resume), or once it is finished and corrected (decision
  2026-11-02-correcting-cuts.md); a finished run without corrections is not kept, and finishing a kept run of the same
  options forgets it.
- `run.js` keeps the titles by the chapter's start in seconds, so a new minimum keeps the titles of the starts it keeps,
  and a resumed run's rest, which adds cuts after the stop, changes only the chapters from the last one before it on.
- The kept record is written once (Firefox's Blob read back from IndexedDB stops answering once its record is written
  again); a correction made later goes under a key of its own (`EDITS_KEY`, `{ saved, edits }`).

## Question: does typing a title keep a finished run?

**A. Titles go with a run that is kept anyway (recommended).** The titles (and the chapters' minimum they were typed
at) are kept with a stopped run, or a finished run kept for its corrections, beside its corrections (the record's
later key), and come back with it, through a resume too. A finished, uncorrected run's titles last while it is shown,
as today.

**B. Titles keep a finished run too.** As A, and a finished run is kept once a title is typed, as for a correction: it
is shown again on a later visit with its titles, until another run is kept, or Forget.

## Answer

The owner, 2026-11-04, through AskUserQuestion: **A**, the titles go with a run that is kept anyway.
