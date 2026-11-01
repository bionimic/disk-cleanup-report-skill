# Disk Cleanup

A Windows app (10 and 11) that shows what is filling your drives and lets you
**tick exactly which files and folders to remove**. Nothing goes until you
confirm, and everything goes to the **Recycle Bin**, never deleted for good.
At the end it asks whether you also want to empty the bin.

The repo also contains the original Claude skill the app grew out of, see
[Claude skill](#claude-skill) below.

## What it does

- **Overview**: every fixed drive with used and free space and the size of its Recycle Bin.
- **Quick clean**: the usual space users (temp files, shader caches, crash dumps, npm, pip, Gradle, NuGet, Composer caches, old installers in Downloads, AI models, emulators, Docker and WSL disks), measured and grouped as **Safe to clear** or **Your call, check first**. One button ticks everything safe.
- **Browse folders**: a tree of your drive, biggest first, with a checkbox on every folder and file.
- **Biggest files**: the 300 largest single files.
- **File types**: how much space each file type uses.
- **Review screen**: the exact list that will move, with sizes and warnings, before anything happens.
- **Run as administrator**: one click restarts the app elevated so it can read and clear protected folders such as `C:\Windows\Temp`.

## Safety

- Scanning is **read only**.
- Only **items you tick** are touched, and only after the review screen.
- **Recycle Bin only.** Windows restores them if you change your mind.
- **Protected items cannot be ticked**: `C:\Windows`, Program Files, `WinSxS`, `System Volume Information`, `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, browser profiles, user registry files, the app's own folder, and every `.git` folder. Drive roots, user profiles and git project folders cannot be removed as one piece, you pick items inside them.
- **Checked again at delete time.** The protection rules run on every path at the moment of moving, not only when the tree is built.
- **Bin room check.** A full Recycle Bin makes Windows erase items permanently. The app reads each drive's bin limit and refuses to move more than fits. If the limit cannot be read, you must tick a box accepting the risk.
- **Fixed drives only.** USB and network drives have no Recycle Bin, so they are never listed.
- **Long paths are refused**, because Windows would delete them for good.
- **OneDrive warning.** Removing files from a OneDrive folder also removes them online.
- **Emptying the bin is a separate, per-drive step** with a confirmation that says the bin may hold older items too.

## Get it

Download `DiskCleanup.exe` from the [Releases](../../releases) page and run it. It is a single file and needs nothing installed.

Windows SmartScreen may warn on first run because the file is not code signed. Choose **More info**, then **Run anyway**.

## Build it yourself

Needs the .NET 8 SDK.

```powershell
.\publish.ps1          # builds publish\DiskCleanup.exe (single file, self contained)
dotnet test tests\DiskCleanup.Tests
```

Layout:

```
src/DiskCleanup/              the WPF app
  Services/PathSafety.cs      what may never be removed
  Services/DiskScanner.cs     read-only scan
  Services/RecycleService.cs  plan, bin checks, Recycle Bin move
  Services/BinService.cs      bin size, bin limit, empty bin
tests/DiskCleanup.Tests/      unit tests
disk-cleanup-report/          the original Claude skill
```

## Claude skill

`disk-cleanup-report/` is a [Claude Code](https://claude.com/claude-code) skill.
Ask Claude **"what's taking up space on my computer?"** and it makes a slide
deck explaining the space, then cleans only what you approve, using the same
Recycle Bin rules as the app. To install it, copy the `disk-cleanup-report`
folder to `C:\Users\<you>\.claude\skills\disk-cleanup-report`. It needs
Python 3 (`py -3`) and PowerShell.

## License

MIT, see [LICENSE](LICENSE).
