@echo off
setlocal
cd /d "%~dp0.."

rem Builds the MCP bridge as a disk plugin and copies it into the user Plugins folder.
rem It does not rebuild bugtopia.dll and it does not enable the plugin: the first load is the
rem Enabled checkbox under Plugins.

echo Building BugtopiaMcp.dll ...
dotnet build plugins\BugtopiaMcp\BugtopiaMcp.csproj -c Release
if errorlevel 1 exit /b 1

set DEST=%USERPROFILE%\AppData\LocalLow\Bugtopia\Plugins
if not exist "%DEST%" mkdir "%DEST%"
copy /Y plugins\BugtopiaMcp\bin\Release\net6.0\BugtopiaMcp.dll "%DEST%\BugtopiaMcp.dll" >nul
if errorlevel 1 exit /b 1
copy /Y plugins\BugtopiaMcp\bin\Release\net6.0\BugtopiaMcp.pdb "%DEST%\BugtopiaMcp.pdb" >nul

echo.
echo Copied to %DEST%\BugtopiaMcp.dll
echo Enable it under Plugins. The bridge listens only while that plugin is loaded.
echo   Expected after enable:  [Mcp] listening on 127.0.0.1:8770
