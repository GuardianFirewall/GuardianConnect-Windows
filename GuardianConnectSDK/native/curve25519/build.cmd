@echo off
REM Build curve25519.dll for win-x64 and win-arm64 with the STATIC CRT (/MT)
REM so the resulting dll has zero runtime dependency on vcruntime140.dll /
REM ucrtbase.dll / msvcp140.dll. Earlier dynamic-CRT (/MD) builds shipped
REM fine on dev boxes but failed to LoadLibrary on clean Windows installs
REM that don't have the VC++ 2015-2022 Redistributable.
REM
REM Outputs the dll into win-x64\curve25519.dll and win-arm64\curve25519.dll
REM (overwriting any prior build). Source = src\curve25519.c (upstream
REM wireguard-tools, unmodified) + src\curve25519_windows.c (Windows
REM BCryptGenRandom wrapper, exports the two-function API).
REM
REM Run from a regular cmd prompt — the script invokes VsDevCmd itself for
REM each target arch. Verifies via dumpbin /imports that the result has no
REM CRT dll imports before declaring success.

setlocal EnableDelayedExpansion

set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Professional"
if not exist "%VSROOT%\Common7\Tools\VsDevCmd.bat" (
    echo ERROR: VsDevCmd.bat not found at "%VSROOT%". Edit VSROOT in this script.
    exit /b 1
)

set "HERE=%~dp0"
set "SRC=%HERE%src"
set "BUILD=%HERE%build"

if not exist "%BUILD%" mkdir "%BUILD%"

REM ===== x64 =====
echo === Building x64 curve25519.dll with /MT (static CRT) ===
if not exist "%BUILD%\x64" mkdir "%BUILD%\x64"
pushd "%BUILD%\x64"
call "%VSROOT%\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 ( popd & echo VsDevCmd x64 init failed & exit /b 1 )

clang-cl --target=x86_64-pc-windows-msvc /MT /O2 /LD ^
    /Fe:curve25519.dll ^
    "%SRC%\curve25519.c" "%SRC%\curve25519_windows.c"
if errorlevel 1 ( popd & echo x64 build failed & exit /b 1 )

echo --- x64 imports ---
dumpbin /imports curve25519.dll | findstr /i "\.dll$\|^.....Image has the following dependencies" || ver >nul
dumpbin /imports curve25519.dll | findstr /i "vcruntime msvcp ucrt" >nul
if not errorlevel 1 (
    echo ERROR: x64 dll still imports CRT dlls. /MT did not take effect.
    popd
    exit /b 1
)
copy /Y curve25519.dll "%HERE%win-x64\curve25519.dll" >nul
echo OK: %HERE%win-x64\curve25519.dll updated.
popd

REM ===== arm64 (cross-compile from x64 host) =====
echo === Building arm64 curve25519.dll with /MT (static CRT) ===
if not exist "%BUILD%\arm64" mkdir "%BUILD%\arm64"
pushd "%BUILD%\arm64"
call "%VSROOT%\Common7\Tools\VsDevCmd.bat" -arch=arm64 -host_arch=x64 >nul
if errorlevel 1 ( popd & echo VsDevCmd arm64 init failed & exit /b 1 )

clang-cl --target=aarch64-pc-windows-msvc /MT /O2 /LD ^
    /Fe:curve25519.dll ^
    "%SRC%\curve25519.c" "%SRC%\curve25519_windows.c"
if errorlevel 1 ( popd & echo arm64 build failed & exit /b 1 )

echo --- arm64 imports ---
dumpbin /imports curve25519.dll | findstr /i "\.dll$\|^.....Image has the following dependencies" || ver >nul
dumpbin /imports curve25519.dll | findstr /i "vcruntime msvcp ucrt" >nul
if not errorlevel 1 (
    echo ERROR: arm64 dll still imports CRT dlls. /MT did not take effect.
    popd
    exit /b 1
)
copy /Y curve25519.dll "%HERE%win-arm64\curve25519.dll" >nul
echo OK: %HERE%win-arm64\curve25519.dll updated.
popd

echo.
echo === BUILD SUCCESS — both dlls rebuilt with /MT ===
endlocal
