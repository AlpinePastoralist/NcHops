@echo off
cd /d "%~dp0"
REM Clear the Platform environment variable to avoid MCD build path
set Platform=
echo Cleaning build artifacts...
dotnet clean NCHops.csproj -c Debug
dotnet build NCHops.csproj -c Debug
if %errorlevel% neq 0 (
    echo Build failed!
    pause
    exit /b 1
)
dotnet run --project NCHops.csproj -c Debug