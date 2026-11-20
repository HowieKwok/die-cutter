@echo off
rem ---------------------------------------------------------------------------
rem  Build entry for Mef_VisionFrame.sln
rem  - locates the installed Visual Studio via vswhere (VS2022 / VS2026 both OK)
rem  - builds the requested configuration with the x64 toolchain
rem  - writes a UTF-8 build log next to this script
rem
rem  Usage: build.bat                  Debug, incremental
rem         build.bat debug            Debug, incremental
rem         build.bat release          Release, incremental
rem         build.bat debug rebuild    Debug, clean + build
rem         build.bat release rebuild  Release, clean + build
rem  Exit code: 0 = success
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"
chcp 65001 >nul

set "CONFIG=%~1"
set "ACTION=%~2"

if "%CONFIG%"=="" set "CONFIG=Debug"
if /i "%CONFIG%"=="debug" set "CONFIG=Debug"
if /i "%CONFIG%"=="release" set "CONFIG=Release"

if /i not "%CONFIG%"=="Debug" if /i not "%CONFIG%"=="Release" (
  echo [error] unknown configuration "%CONFIG%", expected debug or release
  exit /b 2
)

set "TARGET=Build"
if /i "%ACTION%"=="rebuild" set "TARGET=Rebuild"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VSCMD="
if exist "%VSWHERE%" (
  for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find Common7\Tools\VsDevCmd.bat`) do set "VSCMD=%%i"
)

if defined VSCMD (
  call "%VSCMD%" -arch=x64 -host_arch=x64 -no_logo
) else (
  echo [warn] VsDevCmd.bat not found, falling back to msbuild from PATH
)

echo === msbuild /t:%TARGET% Configuration=%CONFIG% ===
msbuild "Mef_VisionFrame.sln" /t:%TARGET% /p:Configuration=%CONFIG% /p:Platform="Any CPU" /v:m /nologo "/flp:logfile=build_%CONFIG%.log;encoding=utf-8"
set "RC=%ERRORLEVEL%"
echo === MSBuild exit code = %RC% ===
endlocal & exit /b %RC%