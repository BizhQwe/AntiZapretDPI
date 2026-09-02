@echo off
setlocal

rem =====================================================================
rem  AntiZapretDPI installer build script (Inno Setup)
rem
rem  Usage:
rem    build-installer.bat                 -> version 1.0.0, self-contained single-file
rem    build-installer.bat 1.2.0           -> custom version
rem
rem  Requires:
rem    - .NET 10 SDK (dotnet)
rem    - Inno Setup 6+ (ISCC.exe in PATH or Program Files)
rem =====================================================================

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"

set "ROOT=%~dp0..\.."
for %%i in ("%ROOT%") do set "ROOT=%%~fi"

set "CSPROJ=%ROOT%\src\AntiZapretDPI.Desktop\AntiZapretDPI.Desktop.csproj"
set "ISS=%ROOT%\src\AntiZapretDPI.Installer\installer.iss"
set "PUBLISH=%ROOT%\artifacts\publish"
set "DIST=%ROOT%\dist"
set "ICON=%ROOT%\src\AntiZapretDPI.Desktop\Assets\Icons\AntiZapretDPI-Icon-Multi.ico"
set "LICENSE=%ROOT%\LICENSE"

rem ---------- 0. Clean publish dir -----------------------------------------
echo.
echo === Clean publish dir ===================================================
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"

rem ---------- 1. Publish ---------------------------------------------------
echo.
echo === Publish self-contained single-file ==================================
dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none -p:DebugSymbols=false ^
  -o "%PUBLISH%"
if errorlevel 1 (
    echo [ERROR] dotnet publish failed.
    exit /b 1
)

rem ---------- 2. Find ISCC ------------------------------------------------
echo.
echo === Find ISCC ==========================================================
set "ISCC="
for /f "delims=" %%i in ('where iscc.exe 2^>nul') do if not defined ISCC set "ISCC=%%i"
if not defined ISCC if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "C:\Program Files (x86)\Inno Setup 7\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 7\ISCC.exe"
if not defined ISCC if exist "C:\Program Files\Inno Setup 7\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
if not defined ISCC (
    echo [ERROR] Inno Setup ^(ISCC.exe^) not found.
    echo         Install it from https://jrsoftware.org/isinfo.php
    exit /b 1
)
echo Using: %ISCC%

rem ---------- 3. Compile installer -----------------------------------------
echo.
echo === Compile installer ==================================================
if not exist "%DIST%" mkdir "%DIST%"
set "OUT=AntiZapretDPI-Setup-%VERSION%.exe"
echo Output: %DIST%\%OUT%

"%ISCC%" /DSourceDir="%PUBLISH%" /DAppVersion="%VERSION%" /DAppIcon="%ICON%" /DLicenseFile="%LICENSE%" /DSelfContained "%ISS%"
if errorlevel 1 (
    echo [ERROR] ISCC failed.
    exit /b 1
)

rem ---------- 4. Summary ---------------------------------------------------
echo.
echo === Done ===============================================================
echo Installer: %DIST%\%OUT%
echo.
echo Silent install:
echo   %OUT% /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
echo Silent uninstall:
echo   "C:\Program Files\AntiZapretDPI\Uninstall.exe" /VERYSILENT /SUPPRESSMSGBOXES

endlocal
exit /b 0
