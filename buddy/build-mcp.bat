@echo off
setlocal
cd /d "%~dp0"

rem Development build WITH the MCP agent bridge (docs/MCP_BRIDGE.md).
rem
rem WHY THIS EXISTS AS A SEPARATE SCRIPT: build-all.bat produces the UNIVERSAL flavour, and the
rem bridge is BepInEx-only by decision (buddy.csproj rejects -p:Mcp=true on any other loader). Since
rem the Universal build auto-deploys into BepInEx\plugins too, running build-all.bat after this one
rem silently replaces the bridge build with a non-bridge one - and the symptom is total silence,
rem because #if FEATURE_MCP removes the call sites as well as the code. Run THIS one whenever the
rem game should expose the bridge.

echo Building bugtopia.dll WITH the MCP agent bridge (BepInEx only)...
dotnet build buddy.csproj -c Release -p:Loader=BepInEx -p:Mcp=true
if errorlevel 1 exit /b 1

echo.
echo Done.
echo   bin\BepInEx\Release\bugtopia.dll  -^> auto-deployed to BepInEx\plugins\
echo.
echo   The bridge still does nothing until the marker file exists:
echo     %%USERPROFILE%%\AppData\LocalLow\Bugtopia\mcp
echo   Expected on the next launch:  [Mcp] listening on 127.0.0.1:8770
