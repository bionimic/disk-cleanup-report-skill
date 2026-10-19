# Disk Cleanup Report (a Claude skill)

Ask Claude **"what's taking up space on my computer?"** and get a short slide
deck that explains it in plain words: which drives are full, which folders and
file types use the space, the biggest single files, and a final slide that
splits everything into **Safe to clear** and **Your call, check first**.

Then, only if you say so, Claude cleans up the items you pick.

## Safety first

- **Scanning is read-only.** Nothing is touched while the report is made.
- **Nothing is deleted without your permission**, item by item.
- **Everything goes to the Recycle Bin**, never deleted for good. You empty the
  bin yourself when you're happy.
- **Checks bin space first.** A full Recycle Bin makes Windows permanently
  erase its oldest items, so the cleanup script stops and asks you to empty it
  instead.
- **Watches for running apps.** It reminds Claude to remove torrents from the
  client first, so they don't start downloading again.

## What's inside

```
disk-cleanup-report/
  SKILL.md                     how Claude runs the scan, report and cleanup
  scripts/scan.py              read-only drive scan (Python 3, no installs)
  scripts/recycle.ps1          sends chosen items to the Recycle Bin, with a bin-space check
  reference/known-locations.md common space users on Windows and how safe each one is
```

## Install

Works with [Claude Code](https://claude.com/claude-code) on Windows.

1. Download this repo (green **Code** button, **Download ZIP**), or clone it:
   ```bash
   git clone https://github.com/bionimic/disk-cleanup-report-skill.git
   ```
2. Copy the `disk-cleanup-report` folder into your skills folder:
   `C:\Users\<you>\.claude\skills\disk-cleanup-report`
3. Open Claude Code and ask: *"Can you make a slide deck explaining what's on my
   computer and what I should delete?"*

Needs Python 3 (`py -3`) and PowerShell, both standard on most Windows PCs.

## Example result

On one gaming and developer PC it found about 185 GB that could go safely:
a forgotten Recycle Bin on D (102 GB), the NVIDIA shader cache (44 GB),
developer caches (24 GB), crash dumps (9 GB), temp files and old installers.

## License

MIT, see [LICENSE](LICENSE).
