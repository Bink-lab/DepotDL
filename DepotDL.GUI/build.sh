#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
RID="${1:-linux-x64}"
echo "Building DepotDownloaderMod..."
dotnet build "../DepotDownloaderMod/DepotDownloaderMod.csproj" -c Release
echo "Publishing DepotDL.GUI ($RID)..."
dotnet publish -c Release -r "$RID" --self-contained true /p:PublishSingleFile=true
find ../DepotDownloaderMod/bin/Release/net9.0/ -maxdepth 1 -type f ! -name "*.exe" \
    -exec cp {} "bin/Release/net9.0/$RID/publish/" \;
echo "[SUCCESS] Publish succeeded!"
echo "Executable is located in: bin/Release/net9.0/$RID/publish/"
