#!/bin/bash
echo "Publishing DepotDL.CLI as self-contained single-file binary..."
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
if [ $? -ne 0 ]; then
    echo "[ERROR] Publish failed!"
    exit 1
fi
echo "[SUCCESS] Publish succeeded!"
echo "Executable is located in: bin/Release/net9.0/linux-x64/publish/"
