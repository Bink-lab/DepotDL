@echo off
pushd "%~dp0"
echo Publishing DepotDL.CLI as self-contained single-file exe...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish failed!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
xcopy "..\third_party\DDMod\*" "bin\Release\net9.0\win-x64\publish\" /E /I /Y >nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to copy DepotDownloaderMod files!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
echo [SUCCESS] Publish succeeded!
echo Files are located in: bin\Release\net9.0\win-x64\publish\
popd
if /I not "%CI%"=="true" pause
