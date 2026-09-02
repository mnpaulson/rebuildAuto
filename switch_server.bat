@echo off
setlocal enabledelayedexpansion
title Server Target Switcher

set "CONFIG_FILE=C:\games\RagnarokRebuild\RebuildClient_Data\StreamingAssets\serverconfig.txt"
set "LOCAL_WS=ws://localhost:5000/ws"
set "REMOTE_WS=ws://138.197.151.167:5000/ws"

echo ======================================================
echo          Rebuild Server Target Switcher
echo ======================================================
echo.

if not exist "%CONFIG_FILE%" (
    echo [!] Error: Config file not found at %CONFIG_FILE%
    pause
    exit /b 1
)

set /p CURRENT_URL=<"%CONFIG_FILE%"
echo Current Client Target: %CURRENT_URL%
echo.
echo Select Target Server:
echo   [1] Local Server  (%LOCAL_WS%)
echo   [2] Remote Server (%REMOTE_WS%)
echo   [Q] Quit / Cancel
echo.

set /p "choice=Enter choice [1, 2, Q]: "

if "%choice%"=="1" (
    echo %LOCAL_WS%> "%CONFIG_FILE%"
    echo [*] Switched client target to LOCAL (%LOCAL_WS%).
) else if "%choice%"=="2" (
    echo %REMOTE_WS%> "%CONFIG_FILE%"
    echo [*] Switched client target to REMOTE (%REMOTE_WS%).
) else (
    echo [*] No changes made.
)

echo.
pause
