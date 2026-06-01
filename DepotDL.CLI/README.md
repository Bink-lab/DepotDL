# DepotDL.CLI

A simple C# command-line tool that parses decryption keys and depot mappings from Lua files, matches local manifest files, and calls DepotDownloaderMod to download games.

## Requirements
- **To build from source**: .NET 9 SDK
- **To run the published self-contained binary**: Windows x64 with no separate .NET runtime required (the project is published as self-contained for win-x64 RID)

## How to Build
Run `build.bat` (Windows) or `build.sh` (Linux/macOS) to build the release binary as a self-contained single-file executable.

- **Windows**: The compiled executable will be written to `bin/Release/net9.0/win-x64/publish/`
- **Linux**: The compiled executable will be written to `bin/Release/net9.0/linux-x64/publish/`

Alternatively, build manually using:
```bash
# Windows
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

# Linux
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
```

## How to Use

### 1. Interactive TUI Mode (Recommended)
If you run the tool without any command line arguments, it launches a colored, interactive Terminal User Interface (TUI):

```bash
# Windows
.\DepotDL.CLI.exe

# Linux/macOS
./DepotDL.CLI
```

The TUI will walk you through:
- **Interactive Lua Selector**: Scans the folder and subdirectories for `.lua` config files and lets you pick one using the Up/Down arrow keys.
- **Interactive Depot Checkbox Menu**: Parses the selected Lua file, lists all available depots, and lets you toggle checkboxes (`[x]` and `[ ]`) using Up/Down and the **Spacebar**. Press **Enter** to confirm.
- **Paths and Confirmation**: Asks you to confirm or customize the manifests folder and destination download directory.

### 2. Standard CLI Mode (Bypasses TUI)
For automation and scripts, pass the path to the game's Lua config file via options:

```bash
DepotDL.CLI --lua "path/to/game.lua" --manifests-dir "path/to/manifests/" --output "path/to/download_folder/"
```

### Options
* `-l, --lua <path>` (Required in CLI mode): Path to the game Lua config file.
* `-m, --manifests-dir <dir>` (Optional): Path to a folder containing pre-downloaded `.manifest` files. Files should ideally be named `<depot_id>_<manifest_id>.manifest`.
* `-o, --output <dir>` (Optional): Target directory for download. Defaults to `./downloads/App_<appid>`.
* `-d, --ddmod <path>` (Optional): Direct path to `DepotDownloaderMod.dll`. If not specified, the tool automatically scans relative paths and SFF's `third_party/DDMod/` directory.
* `-n, --dotnet <path>` (Optional): Direct path to `dotnet` executable. If not specified, searches the environment PATH and common runtime paths.
* `--max-downloads <n>` (Optional): Parallel chunk downloads per depot. Defaults to `64` and is clamped to the range `1–128`.

## How It Works
1. Scans the provided Lua file to extract the AppID, depot keys (`addappid`), and manifest IDs (`setManifestid`) using regular expressions.
2. Scans the manifests folder to associate `.manifest` files with the target depots.
3. Generates a temporary `depot_id;key` VDF file.
4. Spawns `DepotDownloaderMod.dll` as a subprocess via the dotnet CLI for each depot.
5. Automatically cleans up the temporary VDF keys file when done or if terminated (e.g. via Ctrl+C).
