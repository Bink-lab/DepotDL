@echo off
pushd "%~dp0"
echo Building DepotDownloaderMod...
dotnet build "..\DepotDownloaderMod\DepotDownloaderMod.csproj" -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] DepotDownloaderMod build failed!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
echo Publishing DepotDL.GUI...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish failed!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
robocopy "..\DepotDownloaderMod\bin\Release\net9.0" "bin\Release\net9.0\win-x64\publish" /XF *.exe /NFL /NJH /NJS /NC /NS /NP
if %ERRORLEVEL% GTR 7 (
    echo [ERROR] Failed to copy DepotDownloaderMod files!
    popd
    if /I not "%CI%"=="true" pause
    exit /b 1
)
echo [SUCCESS] Build complete: bin\Release\net9.0\win-x64\publish\
popd
if /I not "%CI%"=="true" pause
