@echo off
setlocal
cd /d "%~dp0"

echo Building unified bugtopia.dll (works under MelonLoader and BepInEx)...
dotnet build buddy.csproj -c Release
if errorlevel 1 exit /b 1

echo.
echo Done.
echo   bin\Release\bugtopia.dll  -^> auto-deployed to Mods\ and BepInEx\plugins\ (whichever exist)
