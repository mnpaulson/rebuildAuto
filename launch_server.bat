@echo off
setlocal
title Ragnarok Rebuild Server Launcher

echo ======================================================================
echo             RAGNAROK REBUILD - LOCAL GAME SERVER LAUNCHER
echo ======================================================================
echo.

:: 1. Ensure .NET 9 SDK in PATH
set "PATH=%USERPROFILE%\.dotnet;%PATH%"

echo [*] .NET SDK:
dotnet --version
echo.

:: 2. Check for port 5000 availability
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    echo [!] Warning: Port 5000 appears to be in use by PID %%a.
)

:: 3. Launch the Server
cd /d "%~dp0RoRebuildServer\RoRebuildServer"

echo [*] Starting RoRebuildServer on http://localhost:5000 (ws://localhost:5000/ws)...
echo [*] Press Ctrl+C in this window to stop the server.
echo.

dotnet run --launch-profile RoRebuildServer

pause
