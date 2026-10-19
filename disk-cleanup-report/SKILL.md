---
name: disk-cleanup-report
description: Scan a Windows PC's drives, explain what is taking up space (folders, file types, biggest files) as a slide deck or report, and recommend what to delete. Use when the user wants to free up disk space, asks what is on their computer, or asks what they can safely delete. Never deletes anything without explicit, per-item permission, and only ever sends files to the Recycle Bin.
---

# Disk cleanup report

Help the user understand what is on their computer and free space safely.
The job has two halves: a read-only report, then (only if the user says so)
a careful cleanup.

## Golden rule: never delete without permission

- The report phase is read-only. Scanning never changes a file.
- Delete nothing until the user names what may go. Approval covers only the
  items they named, never "similar" items.
- Never permanently delete. Use `scripts/recycle.ps1`, which sends items to the
  Recycle Bin. Emptying the bin is the user's own final step: tell them how,
  never do it for them.
- Before recycling, the script checks the drive's Recycle Bin has room. A full
  bin makes Windows silently and permanently erase its oldest items, so if the
  bin cannot hold the new items, stop and ask the user to empty it first.
- Never recycle the folder you are working in, the session's own temp files,
  or anything a running download or app is still using.

## 1. Scan (read only)

1. Drive totals:
   `Get-PSDrive -PSProvider FileSystem`
2. Full scan of each drive with the bundled script (Python 3, standard library
   only). It skips junctions and symlinks so nothing is counted twice:
   `py -3 scripts/scan.py C:\ scanC.json`
   It records folders over 1 GB (4 levels deep), totals per file extension and
   the 100 biggest files. A 1 to 2 TB drive takes a few minutes.
3. Hidden app data hides most of the surprises. Scan it deeper:
   `py -3 scripts/scan.py C:\Users\<name>\AppData scanA.json`
4. Check the well-known space users listed in `reference/known-locations.md`,
   including the size of each drive's Recycle Bin (`X:\$Recycle.Bin`).

Read the numbers, never guess. If a figure goes on a slide, it came from a scan.

## 2. Report

Build a short deck (or page) the user can read in two minutes:

1. Cover: drives, total used, the date, "nothing was deleted".
2. The big picture: one bar per drive, used versus free. Say honestly whether
   space is actually tight.
3. One slide per drive: the top folders in plain words ("phone emulators",
   not ".android\avd").
4. Inside AppData, if it is large.
5. File types grouped by meaning: game data, programs and libraries, virtual
   disks, caches, video, documents.
6. The ten biggest single files, with what each one is.
7. Last slide, the recommendation, in two lists:
   - **Safe to clear**: things that rebuild themselves or are already deleted
     (Recycle Bin contents, shader caches, package manager caches, temp files,
     crash dumps, installers already used).
   - **Your call, check first**: things only the user can judge (games, AI
     models, emulators, installers for games not yet installed, the hibernate
     file, old app versions, recordings).
   Put "how to clear it" for each item in the speaker notes.

Before showing any total, check each item's size again on disk (for example,
total the actual installer files in Downloads, do not estimate them).

## 3. Clean up (only after the user says which items)

1. Confirm the exact list back to the user if anything is ambiguous.
2. Check what could hold files open (torrent clients, IDEs, games, Docker). A
   torrent that still lists a folder may download it again once it is gone:
   ask the user to remove it from the client first.
3. Recycle with the bundled script, listing each path:
   `powershell -File scripts/recycle.ps1 -Paths "C:\path\one","C:\path\two"`
   It reports what moved, what was left behind because it was in use, and
   refuses if a bin is too small.
4. Report a table of what moved, then tell the user to empty the Recycle Bin
   (right-click it, Empty Recycle Bin) to get the space back.
5. After they empty it, show the new free space per drive.

## Notes on common questions

- **Hibernate file (`C:\hiberfil.sys`)**: holds the contents of RAM when the
  PC hibernates, and powers Fast Startup. Removing it (`powercfg /h off`, run
  as admin by the user) turns hibernate off. Only suggest it to people who
  never hibernate.
- **PC will not sleep**: read `Kernel-Power` events 42 (entering sleep), 107
  (resumed) and 1 (Power-Troubleshooter, wake source) from the System log,
  `powercfg /lastwake` and `powercfg /devicequery wake_armed`.
  `powercfg /requests` (admin) lists what blocks automatic sleep.
