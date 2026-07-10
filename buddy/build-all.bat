@echo off
setlocal
cd /d "%~dp0"

echo Building unified bugtopia.dll (works under MelonLoader and BepInEx)...
echo   (unified needs BOTH loaders installed; for one loader use -p:Loader=BepInEx or -p:Loader=MelonLoader)
dotnet build buddy.csproj -c Release
if errorlevel 1 exit /b 1

echo.
echo Done.
echo   bin\Universal\Release\bugtopia.dll  -^> auto-deployed to Mods\ and BepInEx\plugins\ (whichever exist)
