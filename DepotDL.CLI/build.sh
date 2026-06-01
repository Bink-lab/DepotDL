#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
echo "Building DepotDownloaderMod..."
dotnet build "../DepotDownloaderMod/DepotDownloaderMod.csproj" -c Release
echo "Publishing DepotDL.CLI as self-contained single-file binary..."
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
cp -r ../DepotDownloaderMod/bin/Release/net9.0/. bin/Release/net9.0/linux-x64/publish/
echo "[SUCCESS] Publish succeeded!"
echo "Executable is located in: bin/Release/net9.0/linux-x64/publish/"
