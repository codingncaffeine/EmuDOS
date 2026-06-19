@echo off
rem Builds the EmuDOS MT-32 shim (emudos_mt32.dll) from emudos_mt32.cpp + mt32emu.h (munt, LGPL).
rem Requires Visual Studio with the C++ workload. Output: emudos_mt32.dll in this folder.
setlocal
cd /d "%~dp0"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (echo vswhere not found - is Visual Studio installed? & exit /b 1)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -property installationPath`) do set "VSPATH=%%i"
if "%VSPATH%"=="" (echo No Visual Studio installation found & exit /b 1)

call "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" >nul || (echo vcvars64 failed & exit /b 1)

cl /nologo /LD /O2 /EHsc /std:c++17 /wd4244 /wd4267 emudos_mt32.cpp /Fe:emudos_mt32.dll || exit /b 1
del /q emudos_mt32.obj emudos_mt32.exp emudos_mt32.lib >nul 2>&1
echo Built emudos_mt32.dll
