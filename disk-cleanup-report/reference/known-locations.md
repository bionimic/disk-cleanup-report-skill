# Known space users on Windows

Sizes vary a lot. Always measure; this list only says where to look and how
safe each one is. `%U%` is the user folder, `%L%` is `%U%\AppData\Local`.

## Safe to clear (rebuilds itself or is already deleted)

| What | Where | How to clear |
|---|---|---|
| Recycle Bin contents | `X:\$Recycle.Bin` on every drive | The user empties it themselves |
| NVIDIA shader cache | `%L%\NVIDIA\DXCache`, `%L%\NVIDIA\GLCache` | Recycle the contents; games rebuild it |
| DirectX shader cache | `%L%\D3DSCache` | Disk Cleanup, DirectX Shader Cache |
| Windows temp files | `%L%\Temp`, `C:\Windows\Temp` | Recycle contents; in-use files stay |
| Windows Update downloads | `C:\Windows\SoftwareDistribution\Download` | Disk Cleanup, Windows Update Cleanup |
| Crash dumps | `%L%\CrashDumps`, `*.hprof`, `*.dmp` | Recycle |
| Gradle cache | `%U%\.gradle\caches` | Recycle; re-downloads on next build |
| npm cache | `%L%\npm-cache` | Recycle contents, or `npm cache clean --force` |
| pip cache | `%L%\pip\cache` | Recycle, or `pip cache purge` |
| Yarn, pnpm, Composer, NuGet, Dart Pub caches | `%L%\Yarn`, `%L%\pnpm`, `%L%\composer`, `%U%\.nuget\packages`, `%L%\Pub\Cache` | Recycle; re-downloads when needed |
| Used installers | `%U%\Downloads\*.exe`, `*.msi`, `*.zip` | Recycle the ones already installed |

## Your call, check first

| What | Where | Why ask |
|---|---|---|
| Games | Steam `steamapps\common`, Battle.net, Epic, EA, custom folders | Only the user knows what they still play; uninstall from the launcher |
| Game installers and repacks | Torrent download folders | Keep until the game is installed; remove from the torrent client first or it may re-download |
| Local AI models | `%U%\.ollama\models`, `%U%\.cache\huggingface`, `%U%\.cache\torch` | `ollama list`, then `ollama rm <model>` |
| Android emulators | `%U%\.android\avd` | Delete unused ones in Android Studio Device Manager |
| WSL and Docker disks | `%L%\wsl`, `%L%\Docker\wsl` | `docker system prune`; WSL disks hold real data |
| Hibernate file | `C:\hiberfil.sys` | Needed for hibernate and Fast Startup; `powercfg /h off` (admin) removes it |
| Page file | `C:\pagefile.sys`, `D:\pagefile.sys` | Leave alone; Windows manages it |
| Old IDE versions | `C:\Program Files\JetBrains\*`, `%L%\JetBrains\*` | Uninstall the old version from Settings, Apps |
| Game clip recordings | NVIDIA app, Xbox Game Bar capture folders | Personal videos |
| Browser profiles | `%L%\Google\Chrome\User Data`, Edge | Clear cache from the browser, never delete the profile |

## Never touch

`C:\Windows` (beyond Disk Cleanup), `C:\Program Files` (use uninstallers),
`WinSxS`, `System Volume Information`, `pagefile.sys`, and anything inside a
project folder (`.git`, source code, databases).
