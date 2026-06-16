@echo off
REM Build curve25519.dll for win-x64 and win-arm64 using Go's crypto/ecdh
REM (BSD-3-Clause). This replaced an earlier C-based build that pulled in
REM the wireguard-tools curve25519 implementation under SPDX dual license
REM GPL-2.0 OR MIT — even though MIT was electable, the GPL clause was
REM unacceptable to legal review, so the implementation was swapped out
REM for Go's standard library which is unambiguously BSD-3-Clause.
REM
REM Source: src\curve25519.go (a thin //export wrapper around
REM   crypto/ecdh.X25519 + crypto/rand). Two C-ABI exports are produced:
REM       curve25519_generate_private_key(unsigned char private_key[32])
REM       curve25519_derive_public_key(unsigned char public_key[32],
REM                                    const unsigned char private_key[32])
REM matching the API the consumer's Curve25519Interop.cs P/Invokes against.
REM
REM Prerequisites:
REM   - Go 1.21+ installed (default path: C:\Program Files\Go\bin\go.exe).
REM   - llvm-mingw cross-toolchain at C:\llvm-mingw\<version>\bin\ with
REM     x86_64-w64-mingw32-gcc.exe and aarch64-w64-mingw32-gcc.exe.
REM     Download from https://github.com/mstorsjo/llvm-mingw/releases —
REM     pick the ucrt-x86_64 variant (single zip, ~250MB, no installer).
REM   - Override GO_EXE and LLVM_MINGW_BIN env vars if your paths differ.

setlocal EnableDelayedExpansion

if "%GO_EXE%"=="" set "GO_EXE=C:\Program Files\Go\bin\go.exe"
if "%LLVM_MINGW_BIN%"=="" (
    for /d %%D in (C:\llvm-mingw\llvm-mingw-*) do set "LLVM_MINGW_BIN=%%D\bin"
)

REM Validate Go by INVOCATION (not file-existence) so the CI flow that
REM uses actions/setup-go can pass GO_EXE=go (bare name resolved from
REM PATH) and the local-dev flow can pass an absolute path like
REM C:\Program Files\Go\bin\go.exe.
"%GO_EXE%" version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Go not callable as "%GO_EXE%". Install from https://go.dev/dl/ or set GO_EXE.
    exit /b 1
)
if not exist "%LLVM_MINGW_BIN%\x86_64-w64-mingw32-gcc.exe" (
    echo ERROR: llvm-mingw not found at "%LLVM_MINGW_BIN%".
    echo Download from https://github.com/mstorsjo/llvm-mingw/releases
    echo or set LLVM_MINGW_BIN to the directory containing
    echo x86_64-w64-mingw32-gcc.exe and aarch64-w64-mingw32-gcc.exe.
    exit /b 1
)

set "HERE=%~dp0"
set "SRC=%HERE%src"

REM ===== x64 =====
echo === Building x64 curve25519.dll (Go + crypto/ecdh) ===
pushd "%SRC%"
set GOOS=windows
set GOARCH=amd64
set CGO_ENABLED=1
set "CC=%LLVM_MINGW_BIN%\x86_64-w64-mingw32-gcc.exe"
"%GO_EXE%" build -buildmode=c-shared -trimpath -ldflags="-s -w" -o "%HERE%win-x64\curve25519.dll" .
if errorlevel 1 ( popd & echo x64 build FAILED & exit /b 1 )
popd
echo OK: %HERE%win-x64\curve25519.dll

REM ===== arm64 (cross-compile from x64 host using aarch64-w64-mingw32-gcc) =====
echo === Building arm64 curve25519.dll (Go + crypto/ecdh) ===
pushd "%SRC%"
set GOOS=windows
set GOARCH=arm64
set CGO_ENABLED=1
set "CC=%LLVM_MINGW_BIN%\aarch64-w64-mingw32-gcc.exe"
"%GO_EXE%" build -buildmode=c-shared -trimpath -ldflags="-s -w" -o "%HERE%win-arm64\curve25519.dll" .
if errorlevel 1 ( popd & echo arm64 build FAILED & exit /b 1 )
popd
echo OK: %HERE%win-arm64\curve25519.dll

echo.
echo === BUILD SUCCESS — both dlls rebuilt with Go (BSD-3-Clause) ===
endlocal
