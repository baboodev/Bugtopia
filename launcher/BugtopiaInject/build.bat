@echo off
rem Builds bugtopia_inject.dll (x64). See docs/plans/2026-08-27-bepinex-injector.md section 4.3.
rem Static CRT on purpose: this DLL is loaded into someone else's process, and a dependency on the
rem VC redist being installed there is a failure mode with no good diagnostic.
setlocal
cd /d "%~dp0"

rem Program Files paths are copied into plain variables first: "(x86)" contains a ')', which closes
rem a for /f or if block early when expanded inside one. Same reason vswhere is not used here - it
rem lives under that path and quoting it through a for /f block is a losing game.
set "PF=%ProgramFiles%"
set "PFX=%ProgramFiles(x86)%"

set "VCVARS=%BUGTOPIA_VCVARS%"
for %%v in (18 17 2022 2019) do (
    for %%e in (Community Professional Enterprise BuildTools) do (
        if not defined VCVARS if exist "%PF%\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvars64.bat" set "VCVARS=%PF%\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvars64.bat"
        if not defined VCVARS if exist "%PFX%\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvars64.bat" set "VCVARS=%PFX%\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvars64.bat"
    )
)

if not defined VCVARS (
    echo MSVC x64 build tools not found.
    echo Install "Desktop development with C++", or set BUGTOPIA_VCVARS to your vcvars64.bat.
    exit /b 1
)
echo Toolchain: %VCVARS%

call "%VCVARS%" >nul
if errorlevel 1 (
    echo vcvars64.bat failed.
    exit /b 1
)

if not exist bin mkdir bin

cl /nologo /W4 /WX /O2 /MT /GS- /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS ^
   /Fobin\ /Fdbin\ ^
   bugtopia_inject.c ^
   /link /DLL /OUT:bin\bugtopia_inject.dll user32.lib
if errorlevel 1 exit /b 1

echo.
echo   bin\bugtopia_inject.dll
