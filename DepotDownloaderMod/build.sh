#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
echo "Building DepotDownloaderMod..."
dotnet build -c Release
echo "[SUCCESS] Build succeeded!"
echo "Files are located in: bin/Release/net9.0/"
