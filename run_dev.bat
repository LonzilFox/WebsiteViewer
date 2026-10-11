@echo off
setlocal
cd /d "%~dp0"
title WebsiteViewer v0.0.4 - Dev Runner

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET 8 SDK not found.
    echo Install: https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo ============================================================
echo WebsiteViewer v0.0.4
echo Build then run in Development mode
echo ============================================================
echo.

dotnet build WebsiteViewer.csproj -c Debug
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the compiler output above.
    echo.
    pause
    exit /b 1
)

echo.
echo Starting WebsiteViewer...
echo If the GUI closes unexpectedly, this window will remain open.
echo Crash logs: %%LOCALAPPDATA%%\WebsiteViewer\logs\latest_crash.log
echo.

dotnet run --no-build --project WebsiteViewer.csproj
set "EXITCODE=%ERRORLEVEL%"

echo.
echo ============================================================
echo WebsiteViewer process ended. Exit code: %EXITCODE%
echo Crash logs: %LOCALAPPDATA%\WebsiteViewer\logs\latest_crash.log
echo ============================================================
echo.
pause
exit /b %EXITCODE%
