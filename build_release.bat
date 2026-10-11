@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET 8 SDK not found.
    echo Install: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo Building WebsiteViewer v0.0.4 Release...
dotnet build WebsiteViewer.csproj -c Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo.
echo BUILD OK.
echo Output: bin\Release\net8.0-windows\
pause
endlocal
