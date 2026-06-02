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
echo Publishing DepotDL.GUI as self-contained single-file exe...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish failed!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
for %%F in (..\DepotDownloaderMod\bin\Release\net9.0\*.*) do (
    if /I not "%%~xF"==".exe" copy /Y "%%F" "bin\Release\net9.0-windows\win-x64\publish\" >nul
)
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to copy DepotDownloaderMod files!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
echo [SUCCESS] Publish succeeded!
echo Files are located in: bin\Release\net9.0-windows\win-x64\publish\
popd
if /I not "%CI%"=="true" pause
