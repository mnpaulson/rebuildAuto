@echo off
setlocal

echo ======================================================================
echo   RAGNAROK REBUILD - FLEET ORCHESTRATOR LAUNCHER
echo ======================================================================
echo.

cd /d "%~dp0RebuildOrchestrator"

echo [*] Compiling Orchestrator (Release)...
dotnet build -c Release -v q --nologo

if %ERRORLEVEL% NEQ 0 (
    echo [!] Build failed with error code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

echo [*] Starting Orchestrator Backend at http://localhost:5500 ...
start http://localhost:5500
bin\Release\net8.0\RebuildOrchestrator.exe
