@echo off
setlocal
cd /d "%~dp0"

@rem To compile MelonLoader target, uncomment the lines below:
@rem echo Building MelonLoader...
@rem dotnet build buddy.csproj -c Release -p:Loader=MelonLoader
@rem if errorlevel 1 exit /b 1
@rem echo.

echo Building BepInEx...
dotnet build buddy.csproj -c Release -p:Loader=BepInEx
if errorlevel 1 exit /b 1

echo.
echo Done.
@rem echo   MelonLoader: bin\MelonLoader\Release\bugtopia.dll  -^> copy to Mods\
echo   BepInEx:     bin\BepInEx\Release\bugtopia.dll      -^> copy to BepInEx\plugins\
