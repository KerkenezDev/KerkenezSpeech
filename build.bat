@echo off
setlocal enabledelayedexpansion

echo =========================================================
echo   KerkenezSpeech - Automated Build and Packaging Script
echo =========================================================
echo.

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK is not found in PATH.
    echo Please install .NET 8.0 SDK or higher.
    pause
    exit /b 1
)

echo [1/4] Restoring project dependencies...
dotnet restore KerkenezSpeech.slnx
if %errorlevel% neq 0 (
    echo [ERROR] Restore failed.
    pause
    exit /b %errorlevel%
)

echo.
echo [2/4] Compiling KerkenezSpeech (Release)...
dotnet build KerkenezSpeech.csproj -c Release --no-restore
if %errorlevel% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b %errorlevel%
)

echo.
echo [3/4] Publishing single-file binary to .\publish\...
dotnet publish KerkenezSpeech.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if %errorlevel% neq 0 (
    echo [ERROR] Publish failed.
    pause
    exit /b %errorlevel%
)

echo.
echo [4/4] Building Installer (embedding KerkenezSpeech payload)...
dotnet build Installer/Installer.csproj -c Release
if %errorlevel% neq 0 (
    echo [ERROR] Installer build failed.
    pause
    exit /b %errorlevel%
)

echo.
echo =========================================================
echo   [SUCCESS] KerkenezSpeech Build Completed!
echo =========================================================
echo.
echo Standalone Executable:
echo   .\publish\KerkenezSpeech.exe
echo.
echo Standalone Installer:
echo   .\Installer\bin\Release\net8.0-windows\Installer.exe
echo.
pause
