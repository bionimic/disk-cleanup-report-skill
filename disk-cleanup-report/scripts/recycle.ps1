<#
Sends the given files or folders to the Recycle Bin. Never deletes permanently.

Usage:
  powershell -File recycle.ps1 -Paths "C:\a","C:\b"
  powershell -File recycle.ps1 -Paths "C:\Users\me\AppData\Local\Temp" -Contents -Exclude claude

-Contents  recycles what is inside each folder but keeps the folder itself.
-Exclude   names of direct children to skip when -Contents is used.

Before moving anything it checks that each drive's Recycle Bin has room.
A full bin makes Windows permanently erase its oldest items, so the script
stops instead and asks you to empty the bin first.
#>
param(
  [Parameter(Mandatory = $true)][string[]]$Paths,
  [switch]$Contents,
  [string[]]$Exclude = @()
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Bin {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  struct SHFILEOPSTRUCT {
    public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo;
    public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
  }
  [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
  static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
  // FO_DELETE with FOF_ALLOWUNDO (recycle), FOF_NOCONFIRMATION, FOF_SILENT,
  // FOF_NOERRORUI and FOF_WANTNUKEWARNING (warn instead of erasing for good).
  public static int Recycle(string path) {
    var op = new SHFILEOPSTRUCT();
    op.wFunc = 3;
    op.pFrom = path + "\0\0";
    op.fFlags = (ushort)(0x40 | 0x10 | 0x4 | 0x400 | 0x4000);
    return SHFileOperation(ref op);
  }
}
"@

function Get-Size($p) {
  if (Test-Path -LiteralPath $p -PathType Leaf) { return (Get-Item -LiteralPath $p -Force).Length }
  $s = (Get-ChildItem -LiteralPath $p -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
  if ($s) { $s } else { 0 }
}

function Get-BinRoom($letter) {
  $vol = Get-Volume -DriveLetter $letter -ErrorAction SilentlyContinue
  if (-not $vol) { return $null }
  $guid = ($vol.UniqueId -replace '.*Volume(\{[^}]+\}).*', '$1')
  $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\$guid"
  $p = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
  if (-not $p) { return $null }
  if ($p.NukeOnDelete -eq 1) { return -1 }
  $max = [int64]$p.MaxCapacity * 1MB
  $used = (Get-ChildItem -LiteralPath "$($letter):\`$Recycle.Bin" -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
  return $max - [int64]$used
}

# Expand the list of items to move.
$items = foreach ($p in $Paths) {
  if (-not (Test-Path -LiteralPath $p)) { Write-Warning "Not found, skipped: $p"; continue }
  if ($Contents -and (Test-Path -LiteralPath $p -PathType Container)) {
    Get-ChildItem -LiteralPath $p -Force | Where-Object { $Exclude -notcontains $_.Name } | ForEach-Object FullName
  } else { $p }
}
$sized = foreach ($i in $items) { [pscustomobject]@{ Path = $i; Size = (Get-Size $i); Drive = $i.Substring(0, 1).ToUpper() } }

# Refuse to overflow any bin.
foreach ($g in ($sized | Group-Object Drive)) {
  $need = ($g.Group | Measure-Object Size -Sum).Sum
  $room = Get-BinRoom $g.Name
  if ($room -eq -1) { throw "Drive $($g.Name): its Recycle Bin is set to delete files immediately. Stopping." }
  if ($null -ne $room -and $need -gt $room) {
    throw ("Drive {0}: the Recycle Bin has room for {1:N1} GB but these items are {2:N1} GB. Empty the bin first, then run again." -f $g.Name, ($room / 1e9), ($need / 1e9))
  }
}

$moved = 0; $left = 0
foreach ($s in $sized) {
  [void][Bin]::Recycle($s.Path)
  $rest = if (Test-Path -LiteralPath $s.Path) { Get-Size $s.Path } else { 0 }
  $moved += $s.Size - $rest; $left += $rest
  "{0,8:N2} GB moved  {1,8:N2} GB in use  {2}" -f (($s.Size - $rest) / 1e9), ($rest / 1e9), $s.Path
}
"Total: {0:N2} GB moved to the Recycle Bin, {1:N2} GB left because it was in use." -f ($moved / 1e9), ($left / 1e9)
"Empty the Recycle Bin to get the space back."
