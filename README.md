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
- Windows x64

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

## License

See LICENSE file in project root.
