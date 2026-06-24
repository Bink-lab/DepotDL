#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
if [ -z "$1" ]; then
  if [[ "$OSTYPE" == "darwin"* ]]; then
    if [[ "$(uname -m)" == "arm64" ]]; then
      RID="osx-arm64"
    else
      RID="osx-x64"
    fi
  elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    if [[ "$(uname -m)" == "aarch64" ]]; then
      RID="linux-arm64"
    else
      RID="linux-x64"
    fi
  else
    RID="linux-x64"
  fi
else
  RID="$1"
fi
echo "Building DepotDownloaderMod..."
dotnet build "../DepotDownloaderMod/DepotDownloaderMod.csproj" -c Release
echo "Publishing DepotDL.GUI ($RID)..."
dotnet publish -c Release -r "$RID" --self-contained true /p:PublishSingleFile=true
find ../DepotDownloaderMod/bin/Release/net9.0/ -maxdepth 1 -type f ! -name "*.exe" \
    -exec cp {} "bin/Release/net9.0/$RID/publish/" \;
echo "[SUCCESS] Publish succeeded!"
echo "Executable is located in: bin/Release/net9.0/$RID/publish/"
