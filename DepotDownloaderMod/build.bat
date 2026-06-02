@echo off
pushd "%~dp0"
echo Building DepotDownloaderMod...
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed!
    popd
    if /I not "%CI%"=="true" pause
    exit /b %ERRORLEVEL%
)
echo [SUCCESS] Build succeeded!
echo Files are located in: bin\Release\net9.0\
popd
if /I not "%CI%"=="true" pause
