# Builds a single DiskCleanup.exe that runs on Windows 10 and 11 without installing .NET.
# Output: publish\DiskCleanup.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet publish "$root\src\DiskCleanup\DiskCleanup.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o "$root\publish"
Get-Item "$root\publish\DiskCleanup.exe" | Select-Object Name, @{n='SizeMB';e={[math]::Round($_.Length / 1MB, 1)}}
