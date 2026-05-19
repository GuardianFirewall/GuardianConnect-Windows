# curve25519.dll — Third-Party License Notices

The `curve25519.dll` shipped in `win-x64/` and `win-arm64/` is built from
`src/curve25519.go` — a thin Go wrapper around the Go standard library's
`crypto/ecdh` and `crypto/rand` packages — compiled with the Go toolchain
in `buildmode=c-shared` mode and linked through llvm-mingw.

## Licenses in the resulting binary

The compiled DLL statically incorporates:

1. **Our wrapper code** (`src/curve25519.go`) — **BSD-3-Clause** (the
   project license; see `LICENSE` at the repo root).
2. **Go standard library** including `crypto/ecdh`, `crypto/rand`,
   `runtime`, etc. — **BSD-3-Clause** (Copyright (c) 2009 The Go Authors).
   See https://go.dev/LICENSE for the canonical text.
3. **llvm-mingw runtime startup code** (very small amount of C runtime
   needed for `c-shared` linking on Windows): a mix of **MIT** (llvm
   project) and **public domain / zlib-style** (mingw-w64 runtime).
   See https://github.com/mstorsjo/llvm-mingw for details.

**There is no GPL, LGPL, or other copyleft code in the resulting DLL.**

## History

An earlier version of this directory contained a C implementation
imported from `wireguard-tools` (`curve25519.c`, `curve25519-fiat32.h`,
`curve25519-hacl64.h`) which carried the SPDX dual-license expression
`GPL-2.0 OR MIT`. While the dual-license model permits a downstream
recipient to elect MIT alone (and we would have done so), the presence
of the GPL clause in the source files was deemed unacceptable by legal
review. Those files have been removed from the repository entirely
(commit-level removal, not just a license election) and replaced with
the Go-based implementation documented here. No code path in the
shipping DLL traces back to the dual-licensed sources.

## License attribution required when redistributing

The MIT-licensed portions of the runtime require the standard MIT
notice + copyright to be included in any distribution. The BSD-3-Clause
licensed Go standard library requires its copyright notice + the
BSD-3-Clause text. We satisfy both by:

- Shipping this `NOTICES.md` alongside the DLL in our SDK NuGet package.
- The actual copyright notices and license texts of the Go standard
  library are reproduced upstream at https://go.dev/LICENSE and we
  reference them by URL here rather than duplicating the text. If
  the legal team prefers verbatim inline copies, see the Go source
  tree's `LICENSE` and `PATENTS` files for the canonical text.

## Reproducing the build

See `build.cmd` in this directory. The build is deterministic given the
same Go version and llvm-mingw version. Versions used for the current
checked-in binaries are recorded in commit messages when the DLLs are
rebuilt.
