# DepotDL

Download Steam games using depot configurations. Two interfaces available: GUI for ease of use, CLI for automation.

## Projects

### DepotDL.GUI

WPF desktop application with a user-friendly interface.

**Build:**
```bash
cd DepotDL.GUI
build.bat
```

**Run:**
```bash
DepotDL.GUI.exe
```

**Features:**
- Interactive Library, Download, and Settings tabs
- Drag-and-drop Lua configuration support
- Real-time progress tracking
- Checkpoint-based resume capability

### DepotDL.CLI

Command-line tool for scripting and automation.

**Build:**
```bash
cd DepotDL.CLI
build.bat
```

**Run Interactive (Recommended):**
```bash
DepotDL.CLI.exe
```

Launches a terminal UI with file picker, depot selector, and download management.

**Run Automated:**
```bash
DepotDL.CLI.exe --lua "path/to/game.lua" --manifests-dir "path/to/manifests/" --output "path/to/output/"
```

**Options:**
- `-l, --lua <path>` - Game Lua config file (required in CLI mode)
- `-m, --manifests-dir <dir>` - Folder with .manifest files (optional)
- `-o, --output <dir>` - Target directory for downloads (optional)
- `-d, --ddmod <path>` - Path to DepotDownloaderMod.dll (optional)
- `-n, --dotnet <path>` - Path to dotnet executable (optional)
- `--max-downloads <n>` - Parallel chunk downloads per depot (default: 64, range: 1-128)

## Requirements

- .NET 9 SDK (to build)
- .NET 9 Runtime (to run)
- GUI: Windows x64 only (WPF)
- CLI: Windows x64, Linux x64, macOS arm64

## How It Works

1. Parses game configuration files (Lua format)
2. Extracts AppID, depot keys, and manifest IDs
3. Matches local manifest files to target depots
4. Generates temporary VDF configuration
5. Spawns DepotDownloaderMod for each depot
6. Tracks progress and manages downloads

## Build All

```bash
dotnet build -c Release
```

## Production Build

`build-prod.ps1` builds all targets, bakes version + git SHA into assemblies, and drops everything into `dist/`.

```powershell
# full build — CLI (Win/Linux/macOS) + GUI (Win) + Velopack setup + ZIPs
.\build-prod.ps1 -Version 1.2.0

# ZIPs only, no Velopack installer
.\build-prod.ps1 -Version 1.2.0 -SkipVelopack

# publish only, no packaging
.\build-prod.ps1 -Version 1.2.0 -SkipVelopack -SkipPackage
```

Output in `dist/`:
```
DepotDL.CLI-Windows-x64-<version>-<sha>.zip
DepotDL.CLI-Linux-x64-<version>-<sha>.zip
DepotDL.CLI-macOS-arm64-<version>-<sha>.zip
DepotDL.GUI-Windows-x64-<version>-<sha>.zip
setup/   ← Velopack installer + delta patches (Windows)
```

Requires `vpk` for Velopack packaging — installed automatically if missing.

## Archive

Nightly builds are stored at [depotdl-v.s3.filebase.io](https://depotdl-v.s3.filebase.io). Browse the bucket XML to see all available versions, then download directly:

```
https://depotdl-v.s3.filebase.io/DepotDL-nightly-<date>-<time>-<commit>.zip
```

## License

See LICENSE file in project root.
