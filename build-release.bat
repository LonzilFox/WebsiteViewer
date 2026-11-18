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

echo Building and publishing WebsiteViewer Release...
set "OUT=%~dp0publish\win-x64"
dotnet publish WebsiteViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%"
if errorlevel 1 (
    echo BUILD FAILED. See the compiler output above.
    pause
    exit /b 1
)
echo BUILD OK.
echo Output: "%OUT%\WebsiteViewer.exe"
pause
exit /b 0
