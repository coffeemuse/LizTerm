# LizTerm Milestone 1: Walking Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A self-contained Avalonia app that spawns a bundled, dependency-free b3270, connects to a profile's host, renders the live 3270 screen in the IBM 3270 font, accepts keyboard input, and shows a plain-language status bar, backed by replayable protocol tests.

**Architecture:** Three projects. `LizTerm.Core` holds the domain (rendered screen cells, session interface, profiles) and knows nothing about b3270. `LizTerm.Backend.B3270` spawns b3270 in JSON mode, parses its indications into Core state, and correlates run results. `LizTerm.App` is the Avalonia UI talking only to Core. The b3270 protocol shapes in this plan were verified against x3270 4.5ga6 source (`include/b3270proto.h`, `Common/b3270/*.c`) and live runs.

**Tech Stack:** .NET 10 SDK (10.0.201 installed), Avalonia 12.1.2, CommunityToolkit.Mvvm 8.4.2, System.Text.Json, xunit.v3 3.2.2 (VSTest mode via xunit.runner.visualstudio 3.1.4 and Microsoft.NET.Test.Sdk 17.14.1), Avalonia.Headless.XUnit 12.1.2, x3270 suite 4.5ga6 (b3270), rbanffy 3270font v3.0.1.

**Spec:** `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`

## Global Constraints

- Target framework `net10.0` everywhere; nullable and implicit usings enabled; no NativeAOT.
- Avalonia packages pinned to `12.1.2`; CommunityToolkit.Mvvm `8.4.2`; central package management via `Directory.Packages.props`.
- x3270 source pinned to `suite3270-4.5ga6-src.tgz`, SHA256 `06faf5ce883852258cc6a2a4da9fe5ce023e97d01e50625ff36f4a01ea703468`, from `https://downloads.sourceforge.net/project/x3270/x3270/4.5ga6/suite3270-4.5ga6-src.tgz`.
- b3270 binaries must have no dynamic dependencies outside the OS (macOS: only `/usr/lib/*` and `/System/Library/*`).
- Font: `3270-Regular.otf`, internal family name `IBM 3270`, SIL OFL 1.1; ship its license text beside it.
- Runtime-specific native asset path: `runtimes/<rid>/native/b3270` (`b3270.exe` on Windows); development override env var `LIZTERM_B3270_PATH`; wire log env var `LIZTERM_WIRE_LOG`; integration host env var `LIZTERM_TEST_HOST`.
- Core never references Avalonia or b3270 names. App never references `LizTerm.Backend.B3270` except in the session factory.
- Rows and columns are zero-based in Core; b3270 reports them one-based. Convert at the backend boundary only.
- Milestone 1 scope: connect, render, keyboard, status bar, profile picker and editor, command-line startup. Out of scope here (later plans): selection and clipboard, file transfer, splash, blink, Linux and Windows native builds, CI, packaging.
- Deviation from spec 4.6, adopted here: a session is constructed for one profile (`Profile` property, parameterless `ConnectAsync`). One window is one session is one profile.
- Commit after every task with the message shown; use `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` as the last line of each commit message.

---

## File Structure

```
LizTerm.sln
Directory.Build.props                      shared TFM/nullable/CPM switches
Directory.Packages.props                   every package version, once
.gitignore                                 add native/cache, native/build-tmp, native/out
native/build/fetch-source.sh               download + checksum + extract x3270 source
native/build/build-macos.sh                static-OpenSSL b3270 for the host arch
native/build/verify-macos.sh               otool gate: only system dylibs allowed
native/build/build-playback.sh             dev tool for recording fixtures
tools/record-fixture.sh                    replay a .trc through b3270, save jsonl
src/LizTerm.Core/LizTerm.Core.csproj
src/LizTerm.Core/Screen/HostColor.cs       16 host colors + Default
src/LizTerm.Core/Screen/CellRendition.cs   [Flags] graphic renditions
src/LizTerm.Core/Screen/Cell.cs            record struct
src/LizTerm.Core/Screen/CursorPosition.cs  record struct
src/LizTerm.Core/Screen/ScreenSnapshot.cs  immutable grid
src/LizTerm.Core/Screen/ScreenBuffer.cs    mutable grid the backend owns
src/LizTerm.Core/Session/ConnectionState.cs
src/LizTerm.Core/Session/TlsInfo.cs
src/LizTerm.Core/Session/KeyboardLock.cs
src/LizTerm.Core/Session/KeyboardStatus.cs
src/LizTerm.Core/Session/TerminalKey.cs
src/LizTerm.Core/Session/SessionProfile.cs
src/LizTerm.Core/Session/BackendFault.cs
src/LizTerm.Core/Session/Exceptions.cs     ConnectionFailedException, EmulatorActionException, BackendUnavailableException
src/LizTerm.Core/Session/IEmulatorSession.cs
src/LizTerm.Core/Profiles/ProfileStore.cs  JSON files per profile
src/LizTerm.Core/Profiles/ProfileJsonContext.cs
src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj
src/LizTerm.Backend.B3270/Protocol/Indications.cs      parsed indication records
src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs one JSON line -> Indication
src/LizTerm.Backend.B3270/Protocol/RunOperation.cs     actions -> JSON line
src/LizTerm.Backend.B3270/Protocol/ActionMap.cs        TerminalKey -> action
src/LizTerm.Backend.B3270/Protocol/ColorNames.cs       color/gr string parsing
src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs
src/LizTerm.Backend.B3270/Process/IB3270Process.cs
src/LizTerm.Backend.B3270/Process/B3270ChildProcess.cs
src/LizTerm.Backend.B3270/Process/B3270Locator.cs
src/LizTerm.Backend.B3270/WireLog.cs
src/LizTerm.Backend.B3270/B3270Session.cs
src/LizTerm.App/LizTerm.App.csproj
src/LizTerm.App/Program.cs
src/LizTerm.App/App.axaml, App.axaml.cs                 lifetime, window management
src/LizTerm.App/Assets/Fonts/3270-Regular.otf, LICENSE-3270font.txt
src/LizTerm.App/Rendering/CellGeometry.cs               pure layout math
src/LizTerm.App/Rendering/Palette.cs                    HostColor -> brushes
src/LizTerm.App/Controls/TerminalScreen.cs              custom control
src/LizTerm.App/Keyboard/DefaultKeymap.cs
src/LizTerm.App/Status/StatusFormatter.cs
src/LizTerm.App/Startup/StartupArguments.cs
src/LizTerm.App/SessionFactory.cs
src/LizTerm.App/ViewModels/SessionViewModel.cs
src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs
src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs
src/LizTerm.App/Views/SessionWindow.axaml(.cs)
src/LizTerm.App/Views/ProfilePickerWindow.axaml(.cs)
src/LizTerm.App/Views/ProfileEditorWindow.axaml(.cs)
tests/LizTerm.Core.Tests/...
tests/LizTerm.Backend.B3270.Tests/...  (+ Fixtures/*.jsonl, Fakes/FakeB3270Process.cs)
tests/LizTerm.App.Tests/...            (+ Fakes/FakeEmulatorSession.cs)
tests/LizTerm.Integration.Tests/LiveHostTests.cs
```

---

### Task 1: Native b3270 build for macOS with static OpenSSL

**Files:**
- Create: `native/build/fetch-source.sh`
- Create: `native/build/build-macos.sh`
- Create: `native/build/verify-macos.sh`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `native/out/osx-<arm64|x64>/b3270`, an executable with only system dynamic libraries. Later tasks copy it into the app output.

- [ ] **Step 1: Add ignore rules**

Append to `.gitignore`:

```
# LizTerm native build products
native/cache/
native/build-tmp/
native/out/
```

- [ ] **Step 2: Write the fetch script**

`native/build/fetch-source.sh`:

```bash
#!/usr/bin/env bash
# Downloads and verifies the pinned x3270 source tarball, extracts it into $1.
set -euo pipefail
DEST=${1:?usage: fetch-source.sh <dest-dir>}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=4.5ga6
SHA256=06faf5ce883852258cc6a2a4da9fe5ce023e97d01e50625ff36f4a01ea703468
URL="https://downloads.sourceforge.net/project/x3270/x3270/$VERSION/suite3270-$VERSION-src.tgz"
CACHE="$ROOT/native/cache"
TGZ="$CACHE/suite3270-$VERSION-src.tgz"
mkdir -p "$CACHE"
if [ ! -f "$TGZ" ]; then
  echo "Downloading $URL" >&2
  curl -fsSL -o "$TGZ" "$URL"
fi
echo "$SHA256  $TGZ" | shasum -a 256 -c - >&2
rm -rf "$DEST"
mkdir -p "$DEST"
tar xzf "$TGZ" -C "$DEST"
# The tarball extracts to suite3270-4.5 (major.minor only).
echo "$DEST/suite3270-4.5"
```

- [ ] **Step 3: Write the verify script**

`native/build/verify-macos.sh`:

```bash
#!/usr/bin/env bash
# Fails if the binary links anything outside macOS system libraries.
set -euo pipefail
BIN=${1:?usage: verify-macos.sh <binary>}
BAD=$(otool -L "$BIN" | tail -n +2 | awk '{print $1}' | grep -v -E '^(/usr/lib/|/System/Library/)' || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN has non-system dynamic dependencies:" >&2
  echo "$BAD" >&2
  exit 1
fi
echo "OK: $BIN links only system libraries"
otool -L "$BIN"
"$BIN" --version | head -3
```

- [ ] **Step 4: Write the build script**

`native/build/build-macos.sh`:

```bash
#!/usr/bin/env bash
# Builds b3270 for the host architecture against static OpenSSL archives.
# Requires: Xcode command line tools, Homebrew openssl@3 (or OPENSSL_PREFIX).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
ARCH=$(uname -m)                     # arm64 or x86_64
case "$ARCH" in
  arm64)  RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "unsupported arch $ARCH" >&2; exit 1 ;;
esac
BUILD="$ROOT/native/build-tmp/$RID"
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
OPENSSL_PREFIX=${OPENSSL_PREFIX:-$(brew --prefix openssl@3)}
# Stage a directory that contains ONLY static archives so the linker cannot pick dylibs.
STAGE="$BUILD/openssl-static"
rm -rf "$STAGE"
mkdir -p "$STAGE/lib"
ln -s "$OPENSSL_PREFIX/include" "$STAGE/include"
cp "$OPENSSL_PREFIX/lib/libssl.a" "$OPENSSL_PREFIX/lib/libcrypto.a" "$STAGE/lib/"
cd "$SRC"
./configure --enable-b3270 \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  --with-openssl="$STAGE" > "$BUILD/configure.log" 2>&1
make -j"$(sysctl -n hw.ncpu)" > "$BUILD/make.log" 2>&1
BIN=$(find obj -type f -name b3270 -perm +111 | head -1)
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270"
chmod +x "$OUT/b3270"
"$ROOT/native/build/verify-macos.sh" "$OUT/b3270"
```

- [ ] **Step 5: Run it**

```bash
chmod +x native/build/*.sh && native/build/build-macos.sh
```

Expected: last lines show `OK: .../native/out/osx-arm64/b3270 links only system libraries`, an `otool -L` list containing only `/usr/lib/libiconv.2.dylib`, `/usr/lib/libexpat.1.dylib`, `/usr/lib/libSystem.B.dylib`, and `b3270 v4.5ga6 ...` with `TLS provider: OpenSSL 3.x`.

- [ ] **Step 6: Prove TLS works in the static binary**

```bash
cd native/build-tmp && openssl req -x509 -newkey rsa:2048 -nodes -keyout t.key -out t.crt -days 2 -subj '/CN=localhost' 2>/dev/null && (openssl s_server -accept 4717 -cert t.crt -key t.key -quiet >/dev/null 2>&1 &) && sleep 1 && ( printf '%s\n' '{"run":{"r-tag":"v","actions":[{"action":"Set","args":["verifyHostCert","false"]}]}}' '{"run":{"r-tag":"c","actions":[{"action":"Open","args":["L:127.0.0.1:4717"]}]}}'; sleep 3; printf '%s\n' '{"run":{"r-tag":"q","actions":[{"action":"Quit"}]}}' ) | ../out/osx-$(uname -m | sed 's/x86_64/x64/')/b3270 -json -utf8 | grep '"tls"'; pkill -f 's_server -accept 4717'; cd ../..
```

Expected: a line like `{"tls":{"secure":true,"verified":false,"session":"Version: TLSv1.3 ...`. (The connection then fails because s_server is not a telnet host; that is fine.)

- [ ] **Step 7: Commit**

```bash
git add .gitignore native/build && git commit -m "Add macOS static b3270 build scripts

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Solution scaffold with Core project and test project

**Files:**
- Create: `LizTerm.sln`, `Directory.Build.props`, `Directory.Packages.props`
- Create: `src/LizTerm.Core/LizTerm.Core.csproj`
- Create: `tests/LizTerm.Core.Tests/LizTerm.Core.Tests.csproj`, `tests/LizTerm.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: the build and test commands every later task uses: `dotnet build LizTerm.sln` and `dotnet test LizTerm.sln`.

- [ ] **Step 1: Write the shared props**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Avalonia" Version="12.1.2" />
    <PackageVersion Include="Avalonia.Desktop" Version="12.1.2" />
    <PackageVersion Include="Avalonia.Themes.Fluent" Version="12.1.2" />
    <PackageVersion Include="Avalonia.Fonts.Inter" Version="12.1.2" />
    <PackageVersion Include="Avalonia.Diagnostics" Version="12.1.2" />
    <PackageVersion Include="Avalonia.Headless.XUnit" Version="12.1.2" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the Core project and test project**

`src/LizTerm.Core/LizTerm.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <InternalsVisibleTo Include="LizTerm.Core.Tests" />
  </ItemGroup>
</Project>
```

`tests/LizTerm.Core.Tests/LizTerm.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/LizTerm.Core/LizTerm.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/LizTerm.Core.Tests/SmokeTests.cs`:

```csharp
namespace LizTerm.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Test_project_runs() => Assert.True(true);
}
```

- [ ] **Step 3: Create the solution and add projects**

```bash
dotnet new sln -n LizTerm && dotnet sln add src/LizTerm.Core/LizTerm.Core.csproj tests/LizTerm.Core.Tests/LizTerm.Core.Tests.csproj
```

- [ ] **Step 4: Build and run the smoke test**

```bash
dotnet test LizTerm.sln
```

Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Scaffold solution with Core project and tests

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Core screen model

**Files:**
- Create: `src/LizTerm.Core/Screen/HostColor.cs`, `CellRendition.cs`, `Cell.cs`, `CursorPosition.cs`, `ScreenSnapshot.cs`, `ScreenBuffer.cs`
- Test: `tests/LizTerm.Core.Tests/Screen/ScreenBufferTests.cs`, `ScreenSnapshotTests.cs`

**Interfaces:**
- Produces:
  - `enum HostColor { Default, NeutralBlack, Blue, Red, Pink, Green, Turquoise, Yellow, NeutralWhite, Black, DeepBlue, Orange, Purple, PaleGreen, PaleTurquoise, Grey, White }`
  - `[Flags] enum CellRendition { None=0, Underline=1, Blink=2, Highlight=4, Selectable=8, Reverse=16, Wide=32, Order=64, PrivateUse=128, NoCopy=256, Wrap=512, LeftHalf=1024, RightHalf=2048 }`
  - `readonly record struct Cell(Rune Character, HostColor Foreground, HostColor Background, CellRendition Rendition)` with `static Cell Blank(HostColor fg, HostColor bg)`
  - `readonly record struct CursorPosition(int Row, int Column, bool Visible)` (zero-based)
  - `sealed class ScreenSnapshot`: `int Rows`, `int Columns`, `CursorPosition Cursor`, `Cell this[int row, int column]`, `ReadOnlySpan<Cell> Row(int row)`, `string GetText(int row, int column, int length)`, `string RowText(int row)`, `string ToText()`, `static ScreenSnapshot Empty(int rows, int columns)`
  - `sealed class ScreenBuffer(int rows, int columns)`: `Rows`, `Columns`, `Cursor`, `void Resize(int rows, int columns, HostColor fg, HostColor bg)`, `void Erase(HostColor fg, HostColor bg)`, `void SetText(int row, int column, string text, HostColor? fg, HostColor? bg, CellRendition? rendition)`, `void SetAttributes(int row, int column, int count, HostColor? fg, HostColor? bg, CellRendition? rendition)`, `void SetCursor(CursorPosition cursor)`, `ScreenSnapshot Snapshot()`

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Screen/ScreenBufferTests.cs`:

```csharp
using System.Text;
using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenBufferTests
{
    [Fact]
    public void New_buffer_is_blank_with_default_colors()
    {
        var buffer = new ScreenBuffer(24, 80);
        var snap = buffer.Snapshot();
        Assert.Equal(24, snap.Rows);
        Assert.Equal(80, snap.Columns);
        Assert.Equal(new Rune(' '), snap[0, 0].Character);
        Assert.Equal(HostColor.NeutralWhite, snap[0, 0].Foreground);
        Assert.Equal(HostColor.NeutralBlack, snap[0, 0].Background);
        Assert.False(snap.Cursor.Visible);
    }

    [Fact]
    public void SetText_writes_characters_and_given_attributes()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(1, 1, "Field:", HostColor.Red, null, CellRendition.Underline);
        var snap = buffer.Snapshot();
        Assert.Equal("Field:", snap.GetText(1, 1, 6));
        Assert.Equal(HostColor.Red, snap[1, 3].Foreground);
        Assert.Equal(HostColor.NeutralBlack, snap[1, 3].Background);
        Assert.Equal(CellRendition.Underline, snap[1, 3].Rendition);
        Assert.Equal(new Rune(' '), snap[1, 7].Character);
    }

    [Fact]
    public void SetText_leaves_absent_attributes_unchanged()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetAttributes(0, 0, 80, HostColor.Blue, HostColor.Red, CellRendition.Reverse);
        buffer.SetText(0, 5, "abc", null, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("abc", snap.GetText(0, 5, 3));
        Assert.Equal(HostColor.Blue, snap[0, 6].Foreground);
        Assert.Equal(HostColor.Red, snap[0, 6].Background);
        Assert.Equal(CellRendition.Reverse, snap[0, 6].Rendition);
    }

    [Fact]
    public void SetAttributes_keeps_characters()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 0, "hello", null, null, null);
        buffer.SetAttributes(2, 0, 5, HostColor.Green, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("hello", snap.GetText(2, 0, 5));
        Assert.Equal(HostColor.Green, snap[2, 4].Foreground);
        Assert.Equal(HostColor.NeutralWhite, snap[2, 5].Foreground);
    }

    [Fact]
    public void SetText_truncates_at_end_of_row()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(0, 78, "abcd", null, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("ab", snap.GetText(0, 78, 2));
        Assert.Equal(new Rune(' '), snap[1, 0].Character);
    }

    [Fact]
    public void Erase_blanks_everything_with_given_colors_and_keeps_size()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(3, 3, "xyz", HostColor.Red, null, null);
        buffer.Erase(HostColor.Blue, HostColor.NeutralBlack);
        var snap = buffer.Snapshot();
        Assert.Equal(new Rune(' '), snap[3, 3].Character);
        Assert.Equal(HostColor.Blue, snap[3, 3].Foreground);
        Assert.Equal(CellRendition.None, snap[3, 3].Rendition);
        Assert.Equal(24, snap.Rows);
    }

    [Fact]
    public void Resize_changes_dimensions_and_blanks()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.Resize(43, 80, HostColor.Blue, HostColor.NeutralBlack);
        var snap = buffer.Snapshot();
        Assert.Equal(43, snap.Rows);
        Assert.Equal(HostColor.Blue, snap[42, 79].Foreground);
    }

    [Fact]
    public void Snapshot_is_independent_of_later_mutation()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(0, 0, "one", null, null, null);
        var first = buffer.Snapshot();
        buffer.SetText(0, 0, "two", null, null, null);
        Assert.Equal("one", first.GetText(0, 0, 3));
        Assert.Equal("two", buffer.Snapshot().GetText(0, 0, 3));
    }

    [Fact]
    public void SetCursor_is_reflected_in_snapshot()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetCursor(new CursorPosition(1, 8, true));
        Assert.Equal(new CursorPosition(1, 8, true), buffer.Snapshot().Cursor);
    }
}
```

`tests/LizTerm.Core.Tests/Screen/ScreenSnapshotTests.cs`:

```csharp
using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenSnapshotTests
{
    [Fact]
    public void RowText_and_ToText_join_characters()
    {
        var buffer = new ScreenBuffer(2, 4);
        buffer.SetText(0, 0, "ab", null, null, null);
        buffer.SetText(1, 2, "cd", null, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("ab  ", snap.RowText(0));
        Assert.Equal("ab  \n  cd", snap.ToText());
    }

    [Fact]
    public void Empty_has_requested_size()
    {
        var snap = ScreenSnapshot.Empty(32, 80);
        Assert.Equal(32, snap.Rows);
        Assert.Equal(80, snap.Row(31).Length);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test LizTerm.sln
```

Expected: build errors, `ScreenBuffer` not found.

- [ ] **Step 3: Implement the model**

`src/LizTerm.Core/Screen/HostColor.cs`:

```csharp
namespace LizTerm.Core.Screen;

/// <summary>The 16 IBM host colors plus Default, in b3270's naming order.</summary>
public enum HostColor
{
    Default,
    NeutralBlack,
    Blue,
    Red,
    Pink,
    Green,
    Turquoise,
    Yellow,
    NeutralWhite,
    Black,
    DeepBlue,
    Orange,
    Purple,
    PaleGreen,
    PaleTurquoise,
    Grey,
    White,
}
```

`src/LizTerm.Core/Screen/CellRendition.cs`:

```csharp
namespace LizTerm.Core.Screen;

[Flags]
public enum CellRendition
{
    None = 0,
    Underline = 1,
    Blink = 2,
    Highlight = 4,
    Selectable = 8,
    Reverse = 16,
    Wide = 32,
    Order = 64,
    PrivateUse = 128,
    NoCopy = 256,
    Wrap = 512,
    LeftHalf = 1024,
    RightHalf = 2048,
}
```

`src/LizTerm.Core/Screen/Cell.cs`:

```csharp
using System.Text;

namespace LizTerm.Core.Screen;

public readonly record struct Cell(Rune Character, HostColor Foreground, HostColor Background, CellRendition Rendition)
{
    public static readonly Rune Space = new(' ');

    public static Cell Blank(HostColor foreground, HostColor background) =>
        new(Space, foreground, background, CellRendition.None);
}
```

`src/LizTerm.Core/Screen/CursorPosition.cs`:

```csharp
namespace LizTerm.Core.Screen;

/// <summary>Zero-based cursor position.</summary>
public readonly record struct CursorPosition(int Row, int Column, bool Visible)
{
    public static readonly CursorPosition Hidden = new(0, 0, false);
}
```

`src/LizTerm.Core/Screen/ScreenSnapshot.cs`:

```csharp
using System.Text;

namespace LizTerm.Core.Screen;

public sealed class ScreenSnapshot
{
    private readonly Cell[] _cells;

    public ScreenSnapshot(int rows, int columns, Cell[] cells, CursorPosition cursor)
    {
        if (cells.Length != rows * columns)
            throw new ArgumentException($"Expected {rows * columns} cells, got {cells.Length}", nameof(cells));
        Rows = rows;
        Columns = columns;
        _cells = cells;
        Cursor = cursor;
    }

    public int Rows { get; }
    public int Columns { get; }
    public CursorPosition Cursor { get; }

    public Cell this[int row, int column] => _cells[row * Columns + column];

    public ReadOnlySpan<Cell> Row(int row) => _cells.AsSpan(row * Columns, Columns);

    public string GetText(int row, int column, int length)
    {
        var sb = new StringBuilder(length);
        var end = Math.Min(column + length, Columns);
        for (var c = column; c < end; c++)
            sb.Append(this[row, c].Character.ToString());
        return sb.ToString();
    }

    public string RowText(int row) => GetText(row, 0, Columns);

    public string ToText()
    {
        var lines = new string[Rows];
        for (var r = 0; r < Rows; r++) lines[r] = RowText(r);
        return string.Join('\n', lines);
    }

    public static ScreenSnapshot Empty(int rows, int columns)
    {
        var cells = new Cell[rows * columns];
        Array.Fill(cells, Cell.Blank(HostColor.NeutralWhite, HostColor.NeutralBlack));
        return new ScreenSnapshot(rows, columns, cells, CursorPosition.Hidden);
    }
}
```

`src/LizTerm.Core/Screen/ScreenBuffer.cs`:

```csharp
namespace LizTerm.Core.Screen;

/// <summary>Mutable screen the backend owns. Not thread-safe; one writer.</summary>
public sealed class ScreenBuffer
{
    private Cell[] _cells;

    public ScreenBuffer(int rows, int columns)
    {
        _cells = [];
        Resize(rows, columns, HostColor.NeutralWhite, HostColor.NeutralBlack);
    }

    public int Rows { get; private set; }
    public int Columns { get; private set; }
    public CursorPosition Cursor { get; private set; } = CursorPosition.Hidden;

    public void Resize(int rows, int columns, HostColor foreground, HostColor background)
    {
        if (rows <= 0 || columns <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        Rows = rows;
        Columns = columns;
        _cells = new Cell[rows * columns];
        Erase(foreground, background);
    }

    public void Erase(HostColor foreground, HostColor background) =>
        Array.Fill(_cells, Cell.Blank(foreground, background));

    public void SetText(int row, int column, string text, HostColor? foreground, HostColor? background, CellRendition? rendition)
    {
        var col = column;
        foreach (var rune in text.EnumerateRunes())
        {
            if (col >= Columns) break;
            var index = Index(row, col);
            var cell = _cells[index];
            _cells[index] = new Cell(rune, foreground ?? cell.Foreground, background ?? cell.Background, rendition ?? cell.Rendition);
            col++;
        }
    }

    public void SetAttributes(int row, int column, int count, HostColor? foreground, HostColor? background, CellRendition? rendition)
    {
        var end = Math.Min(column + count, Columns);
        for (var col = column; col < end; col++)
        {
            var index = Index(row, col);
            var cell = _cells[index];
            _cells[index] = cell with
            {
                Foreground = foreground ?? cell.Foreground,
                Background = background ?? cell.Background,
                Rendition = rendition ?? cell.Rendition,
            };
        }
    }

    public void SetCursor(CursorPosition cursor) => Cursor = cursor;

    public ScreenSnapshot Snapshot() => new(Rows, Columns, (Cell[])_cells.Clone(), Cursor);

    private int Index(int row, int column)
    {
        if ((uint)row >= (uint)Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)column >= (uint)Columns) throw new ArgumentOutOfRangeException(nameof(column));
        return row * Columns + column;
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test LizTerm.sln
```

Expected: all pass (11 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add Core screen model: cells, snapshot, buffer

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Core session types and interface

**Files:**
- Create: `src/LizTerm.Core/Session/ConnectionState.cs`, `TlsInfo.cs`, `KeyboardLock.cs`, `KeyboardStatus.cs`, `TerminalKey.cs`, `SessionProfile.cs`, `BackendFault.cs`, `Exceptions.cs`, `IEmulatorSession.cs`
- Test: `tests/LizTerm.Core.Tests/Session/SessionTypesTests.cs`

**Interfaces:**
- Produces (used verbatim by backend and app):

```csharp
public enum ConnectionState { Disconnected, Reconnecting, Resolving, TcpPending, TlsPending, TlsPasswordPending, ProxyPending, TelnetPending, ConnectedNvt, ConnectedNvtCharMode, Connected3270, ConnectedUnbound, ConnectedENvt, ConnectedSscp, ConnectedTn3270E }
public static class ConnectionStateExtensions { bool IsConnected(this ConnectionState); }
public sealed record TlsInfo(bool Secure, bool? Verified, string? SessionInfo, string? HostCertificate);
public enum KeyboardLock { Unlocked, NotConnected, WaitingForHost, TerminalWait, Deferred, MinusFunction, ProtectedField, NumericOnly, Overflow, Dbcs, Scrolled, Disabled, FieldWait, FileTransfer, Unknown }
public sealed record KeyboardStatus(KeyboardLock Lock, string? LockDetail, bool InsertMode, bool Typeahead, string? LuName) { static KeyboardStatus Initial; }
public enum TerminalKey { Enter, Clear, PF1 ... PF24 (contiguous, in order), PA1, PA2, PA3, Attn, SysReq, Reset, Tab, BackTab, Home, EraseEof, EraseInput, Delete, Backspace, Insert, Dup, FieldMark, Newline, Up, Down, Left, Right }
public sealed record SessionProfile { string Name = ""; string Host = ""; int Port = 23; bool UseTls; bool VerifyCertificate = true; int Model = 2; bool Extended = true; string CodePage = "cp037"; string? LuName; }
public sealed record BackendFault(string Message, IReadOnlyList<string> StderrTail, int? ExitCode);
public sealed class ConnectionFailedException(IReadOnlyList<string> lines) : Exception { IReadOnlyList<string> Lines; }
public sealed class EmulatorActionException(string message) : Exception;
public sealed class BackendUnavailableException(string message) : Exception;
public interface IEmulatorSession : IAsyncDisposable
{
    SessionProfile Profile { get; }
    ScreenSnapshot CurrentScreen { get; }
    ConnectionState ConnectionState { get; }
    TlsInfo? Tls { get; }
    KeyboardStatus KeyboardStatus { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task SendKeyAsync(TerminalKey key);
    Task TypeTextAsync(string text);
    Task PasteTextAsync(string text);
    Task MoveCursorAsync(int row, int column);
    event EventHandler<ScreenSnapshot>? ScreenUpdated;
    event EventHandler<KeyboardStatus>? StatusChanged;
    event EventHandler<ConnectionState>? ConnectionChanged;
    event EventHandler<BackendFault>? Faulted;
    event EventHandler<string>? HostMessage;
}
```

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Session/SessionTypesTests.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class SessionTypesTests
{
    [Theory]
    [InlineData(ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.TcpPending, false)]
    [InlineData(ConnectionState.TelnetPending, false)]
    [InlineData(ConnectionState.ConnectedNvt, true)]
    [InlineData(ConnectionState.Connected3270, true)]
    [InlineData(ConnectionState.ConnectedTn3270E, true)]
    public void IsConnected_is_true_only_for_connected_states(ConnectionState state, bool expected) =>
        Assert.Equal(expected, state.IsConnected());

    [Fact]
    public void PF_keys_are_contiguous_so_arithmetic_works()
    {
        Assert.Equal(TerminalKey.PF12, TerminalKey.PF1 + 11);
        Assert.Equal(TerminalKey.PF24, TerminalKey.PF1 + 23);
    }

    [Fact]
    public void Profile_defaults_match_spec()
    {
        var p = new SessionProfile { Name = "x", Host = "h" };
        Assert.Equal(23, p.Port);
        Assert.False(p.UseTls);
        Assert.True(p.VerifyCertificate);
        Assert.Equal(2, p.Model);
        Assert.True(p.Extended);
        Assert.Equal("cp037", p.CodePage);
        Assert.Null(p.LuName);
    }

    [Fact]
    public void Initial_keyboard_status_is_not_connected() =>
        Assert.Equal(KeyboardLock.NotConnected, KeyboardStatus.Initial.Lock);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test LizTerm.sln
```

Expected: build errors for missing types.

- [ ] **Step 3: Implement the types**

`src/LizTerm.Core/Session/ConnectionState.cs`:

```csharp
namespace LizTerm.Core.Session;

/// <summary>Host session states, in the order b3270 progresses through them.</summary>
public enum ConnectionState
{
    Disconnected,
    Reconnecting,
    Resolving,
    TcpPending,
    TlsPending,
    TlsPasswordPending,
    ProxyPending,
    TelnetPending,
    ConnectedNvt,
    ConnectedNvtCharMode,
    Connected3270,
    ConnectedUnbound,
    ConnectedENvt,
    ConnectedSscp,
    ConnectedTn3270E,
}

public static class ConnectionStateExtensions
{
    public static bool IsConnected(this ConnectionState state) => state >= ConnectionState.ConnectedNvt;
}
```

`src/LizTerm.Core/Session/TlsInfo.cs`:

```csharp
namespace LizTerm.Core.Session;

public sealed record TlsInfo(bool Secure, bool? Verified, string? SessionInfo, string? HostCertificate);
```

`src/LizTerm.Core/Session/KeyboardLock.cs`:

```csharp
namespace LizTerm.Core.Session;

public enum KeyboardLock
{
    Unlocked,
    NotConnected,
    WaitingForHost,
    TerminalWait,
    Deferred,
    MinusFunction,
    ProtectedField,
    NumericOnly,
    Overflow,
    Dbcs,
    Scrolled,
    Disabled,
    FieldWait,
    FileTransfer,
    Unknown,
}
```

`src/LizTerm.Core/Session/KeyboardStatus.cs`:

```csharp
namespace LizTerm.Core.Session;

public sealed record KeyboardStatus(KeyboardLock Lock, string? LockDetail, bool InsertMode, bool Typeahead, string? LuName)
{
    public static readonly KeyboardStatus Initial = new(KeyboardLock.NotConnected, null, false, false, null);
}
```

`src/LizTerm.Core/Session/TerminalKey.cs`:

```csharp
namespace LizTerm.Core.Session;

public enum TerminalKey
{
    Enter,
    Clear,
    PF1, PF2, PF3, PF4, PF5, PF6, PF7, PF8, PF9, PF10, PF11, PF12,
    PF13, PF14, PF15, PF16, PF17, PF18, PF19, PF20, PF21, PF22, PF23, PF24,
    PA1, PA2, PA3,
    Attn,
    SysReq,
    Reset,
    Tab,
    BackTab,
    Home,
    EraseEof,
    EraseInput,
    Delete,
    Backspace,
    Insert,
    Dup,
    FieldMark,
    Newline,
    Up,
    Down,
    Left,
    Right,
}
```

`src/LizTerm.Core/Session/SessionProfile.cs`:

```csharp
namespace LizTerm.Core.Session;

public sealed record SessionProfile
{
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 23;
    public bool UseTls { get; init; }
    public bool VerifyCertificate { get; init; } = true;
    /// <summary>3278/3279 model number, 2 through 5.</summary>
    public int Model { get; init; } = 2;
    public bool Extended { get; init; } = true;
    public string CodePage { get; init; } = "cp037";
    public string? LuName { get; init; }
}
```

`src/LizTerm.Core/Session/BackendFault.cs`:

```csharp
namespace LizTerm.Core.Session;

public sealed record BackendFault(string Message, IReadOnlyList<string> StderrTail, int? ExitCode);
```

`src/LizTerm.Core/Session/Exceptions.cs`:

```csharp
namespace LizTerm.Core.Session;

/// <summary>The host connection could not be established; Lines is the emulator's explanation.</summary>
public sealed class ConnectionFailedException(IReadOnlyList<string> lines)
    : Exception(string.Join(" ", lines))
{
    public IReadOnlyList<string> Lines { get; } = lines;
}

/// <summary>An emulator action was rejected (for example, a key while the keyboard is locked).</summary>
public sealed class EmulatorActionException(string message) : Exception(message);

/// <summary>The emulator engine is missing, not executable, or too old.</summary>
public sealed class BackendUnavailableException(string message) : Exception(message);
```

`src/LizTerm.Core/Session/IEmulatorSession.cs`:

```csharp
using LizTerm.Core.Screen;

namespace LizTerm.Core.Session;

/// <summary>
/// One terminal session bound to one profile. Events are raised on a backend thread,
/// in order; the caller marshals to its UI thread.
/// </summary>
public interface IEmulatorSession : IAsyncDisposable
{
    SessionProfile Profile { get; }
    ScreenSnapshot CurrentScreen { get; }
    ConnectionState ConnectionState { get; }
    TlsInfo? Tls { get; }
    KeyboardStatus KeyboardStatus { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task SendKeyAsync(TerminalKey key);
    Task TypeTextAsync(string text);
    Task PasteTextAsync(string text);
    /// <summary>Zero-based row and column.</summary>
    Task MoveCursorAsync(int row, int column);

    event EventHandler<ScreenSnapshot>? ScreenUpdated;
    event EventHandler<KeyboardStatus>? StatusChanged;
    event EventHandler<ConnectionState>? ConnectionChanged;
    event EventHandler<BackendFault>? Faulted;
    /// <summary>Informational or error text from the emulator or host, for display.</summary>
    event EventHandler<string>? HostMessage;
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test LizTerm.sln
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add Core session types and IEmulatorSession

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: ProfileStore

**Files:**
- Create: `src/LizTerm.Core/Profiles/ProfileStore.cs`, `src/LizTerm.Core/Profiles/ProfileJsonContext.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`

**Interfaces:**
- Produces: `sealed class ProfileStore(string directory)` with `static string DefaultDirectory()`, `IReadOnlyList<SessionProfile> LoadAll()` (sorted by name, unreadable files skipped), `void Save(SessionProfile profile)`, `void Delete(string name)`, `static string FileNameFor(string name)`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`:

```csharp
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Profiles;

public class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void LoadAll_on_missing_directory_is_empty()
    {
        var store = new ProfileStore(_dir);
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Save_then_LoadAll_round_trips_every_field()
    {
        var store = new ProfileStore(_dir);
        var profile = new SessionProfile
        {
            Name = "TK5", Host = "mvs.local", Port = 3270, UseTls = true, VerifyCertificate = false,
            Model = 4, Extended = false, CodePage = "bracket", LuName = "LU01",
        };
        store.Save(profile);
        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(profile, loaded);
    }

    [Fact]
    public void LoadAll_sorts_by_name_and_skips_invalid_files()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "zeta", Host = "z" });
        store.Save(new SessionProfile { Name = "alpha", Host = "a" });
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        var names = store.LoadAll().Select(p => p.Name).ToArray();
        Assert.Equal(["alpha", "zeta"], names);
    }

    [Fact]
    public void Delete_removes_the_profile()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "gone", Host = "g" });
        store.Delete("gone");
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void FileNameFor_sanitizes_unsafe_characters()
    {
        Assert.Equal("my_host_prod.json", ProfileStore.FileNameFor("my/host:prod"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test LizTerm.sln
```

Expected: build errors, `ProfileStore` not found.

- [ ] **Step 3: Implement**

`src/LizTerm.Core/Profiles/ProfileJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionProfile))]
internal partial class ProfileJsonContext : JsonSerializerContext;
```

`src/LizTerm.Core/Profiles/ProfileStore.cs`:

```csharp
using System.Text.Json;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>One JSON file per profile in a directory.</summary>
public sealed class ProfileStore(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory()
    {
        string root;
        if (OperatingSystem.IsMacOS())
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else if (OperatingSystem.IsWindows())
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        else
            root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "LizTerm", "profiles");
    }

    public IReadOnlyList<SessionProfile> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var profiles = new List<SessionProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var profile = JsonSerializer.Deserialize(File.ReadAllText(file), ProfileJsonContext.Default.SessionProfile);
                if (profile is not null && profile.Name.Length > 0) profiles.Add(profile);
            }
            catch (JsonException)
            {
                // Skip unreadable files; the user can delete them by hand.
            }
        }
        return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Save(SessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name", nameof(profile));
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.SessionProfile);
        File.WriteAllText(Path.Combine(Directory, FileNameFor(profile.Name)), json);
    }

    public void Delete(string name)
    {
        var path = Path.Combine(Directory, FileNameFor(name));
        if (File.Exists(path)) File.Delete(path);
    }

    public static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars) + ".json";
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test LizTerm.sln
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add ProfileStore with JSON files per profile

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Backend project and indication parser

**Files:**
- Create: `src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj`
- Create: `src/LizTerm.Backend.B3270/Protocol/Indications.cs`, `src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs`
- Create: `tests/LizTerm.Backend.B3270.Tests/LizTerm.Backend.B3270.Tests.csproj`, `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs`

**Interfaces:**
- Produces: `abstract record Indication` and the sealed records listed in the code below; `static bool IndicationParser.TryParse(string line, out Indication indication)`. Every b3270 output line is a JSON object with exactly one property whose name is the indication name. `initialize` wraps an array of nested indications.

- [ ] **Step 1: Create the projects**

`src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../LizTerm.Core/LizTerm.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="LizTerm.Backend.B3270.Tests" />
    <InternalsVisibleTo Include="LizTerm.Integration.Tests" />
  </ItemGroup>
</Project>
```

`tests/LizTerm.Backend.B3270.Tests/LizTerm.Backend.B3270.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln add src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj tests/LizTerm.Backend.B3270.Tests/LizTerm.Backend.B3270.Tests.csproj
```

- [ ] **Step 2: Write the failing tests**

Every sample line below was captured from b3270 4.5ga6 in JSON mode.

`tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs`:

```csharp
using LizTerm.Backend.B3270.Protocol;

namespace LizTerm.Backend.B3270.Tests.Protocol;

public class IndicationParserTests
{
    private static T Parse<T>(string line) where T : Indication
    {
        Assert.True(IndicationParser.TryParse(line, out var indication), "parse failed");
        return Assert.IsType<T>(indication);
    }

    [Fact]
    public void Parses_initialize_with_nested_hello_and_screen_mode()
    {
        var line = """{"initialize":[{"hello":{"version":"4.5.6","build":"b3270 v4.5ga6 Mon Jul 27 22:37:48 UTC 2026 brew","copyright":"..."}},{"screen-mode":{"model":4,"rows":43,"columns":80,"color":true,"oversize":false,"extended":true}},{"setting":{"name":"codePage","value":"bracket","cause":"none"}}]}""";
        var init = Parse<InitializeIndication>(line);
        Assert.Equal(3, init.Items.Count);
        var hello = Assert.IsType<HelloIndication>(init.Items[0]);
        Assert.Equal("4.5.6", hello.Version);
        var mode = Assert.IsType<ScreenModeIndication>(init.Items[1]);
        Assert.Equal(43, mode.Rows);
        var setting = Assert.IsType<SettingIndication>(init.Items[2]);
        Assert.Equal("codePage", setting.Name);
        Assert.Equal("bracket", setting.Value);
    }

    [Fact]
    public void Parses_screen_mode()
    {
        var mode = Parse<ScreenModeIndication>("""{"screen-mode":{"model":2,"rows":24,"columns":80,"color":true,"oversize":false,"extended":true}}""");
        Assert.Equal(2, mode.Model);
        Assert.Equal(24, mode.Rows);
        Assert.Equal(80, mode.Columns);
        Assert.True(mode.Color);
        Assert.True(mode.Extended);
    }

    [Fact]
    public void Parses_erase()
    {
        var erase = Parse<EraseIndication>("""{"erase":{"logical-rows":43,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}}""");
        Assert.Equal(43, erase.LogicalRows);
        Assert.Equal("blue", erase.Fg);
        Assert.Equal("neutralBlack", erase.Bg);
        var bare = Parse<EraseIndication>("""{"erase":{}}""");
        Assert.Null(bare.LogicalRows);
        Assert.Null(bare.Fg);
    }

    [Fact]
    public void Parses_screen_with_rows_and_cursor()
    {
        var line = """{"screen":{"cursor":{"enabled":true,"row":2,"column":9},"rows":[{"row":1,"changes":[{"column":2,"fg":"red","count":1},{"column":3,"fg":"neutralBlack","bg":"red","text":"_______________________________________________"},{"column":50,"fg":"neutralBlack","bg":"red","count":31}]},{"row":2,"changes":[{"column":2,"text":"Field:"},{"column":8,"fg":"red","count":73}]}]}}""";
        var screen = Parse<ScreenIndication>(line);
        Assert.NotNull(screen.Cursor);
        Assert.True(screen.Cursor!.Enabled);
        Assert.Equal(2, screen.Cursor.Row);
        Assert.Equal(9, screen.Cursor.Column);
        Assert.NotNull(screen.Rows);
        Assert.Equal(2, screen.Rows!.Count);
        var row1 = screen.Rows[0];
        Assert.Equal(1, row1.Row);
        Assert.Equal(3, row1.Changes.Count);
        Assert.Equal(2, row1.Changes[0].Column);
        Assert.Equal(1, row1.Changes[0].Count);
        Assert.Equal("red", row1.Changes[0].Fg);
        Assert.Null(row1.Changes[0].Text);
        Assert.Equal("neutralBlack", row1.Changes[1].Fg);
        Assert.Equal("red", row1.Changes[1].Bg);
        Assert.StartsWith("____", row1.Changes[1].Text);
        Assert.Null(row1.Changes[1].Gr);
        Assert.Equal("Field:", screen.Rows[1].Changes[0].Text);
    }

    [Fact]
    public void Parses_screen_with_only_cursor()
    {
        var screen = Parse<ScreenIndication>("""{"screen":{"cursor":{"enabled":false}}}""");
        Assert.NotNull(screen.Cursor);
        Assert.False(screen.Cursor!.Enabled);
        Assert.Null(screen.Cursor.Row);
        Assert.Null(screen.Rows);
    }

    [Fact]
    public void Parses_change_with_gr()
    {
        var screen = Parse<ScreenIndication>("""{"screen":{"rows":[{"row":7,"changes":[{"column":1,"fg":"neutralWhite","gr":"highlight,selectable","text":" Welcome to"}]}]}}""");
        Assert.Equal("highlight,selectable", screen.Rows![0].Changes[0].Gr);
    }

    [Fact]
    public void Parses_oia_with_and_without_value()
    {
        var lock1 = Parse<OiaIndication>("""{"oia":{"field":"lock","value":"not-connected"}}""");
        Assert.Equal("lock", lock1.Field);
        Assert.Equal("not-connected", lock1.Value);
        var lock2 = Parse<OiaIndication>("""{"oia":{"field":"lock"}}""");
        Assert.Null(lock2.Value);
        var insert = Parse<OiaIndication>("""{"oia":{"field":"insert","value":true}}""");
        Assert.Equal("true", insert.Value);
        var lu = Parse<OiaIndication>("""{"oia":{"field":"lu","value":"IBM0TEQO"}}""");
        Assert.Equal("IBM0TEQO", lu.Value);
    }

    [Fact]
    public void Parses_connection()
    {
        var c = Parse<ConnectionIndication>("""{"connection":{"state":"connected-tn3270e","host":"127.0.0.1","cause":"ui"}}""");
        Assert.Equal("connected-tn3270e", c.State);
        Assert.Equal("127.0.0.1", c.Host);
        var d = Parse<ConnectionIndication>("""{"connection":{"state":"not-connected"}}""");
        Assert.Equal("not-connected", d.State);
        Assert.Null(d.Host);
    }

    [Fact]
    public void Parses_tls()
    {
        var t = Parse<TlsIndication>("""{"tls":{"secure":true,"verified":false,"session":"Version: TLSv1.3\nCipher: TLS_AES_256_GCM_SHA384","host-cert":"Subject: CN = localhost"}}""");
        Assert.True(t.Secure);
        Assert.False(t.Verified);
        Assert.StartsWith("Version: TLSv1.3", t.Session);
        Assert.Equal("Subject: CN = localhost", t.HostCert);
        var off = Parse<TlsIndication>("""{"tls":{"secure":false}}""");
        Assert.False(off.Secure);
        Assert.Null(off.Verified);
    }

    [Fact]
    public void Parses_run_result_success_and_failure()
    {
        var ok = Parse<RunResultIndication>("""{"run-result":{"r-tag":"t1","success":true,"text":["3279-4-E"],"text-err":[false],"time":0}}""");
        Assert.Equal("t1", ok.Tag);
        Assert.True(ok.Success);
        Assert.Equal(["3279-4-E"], ok.Text);
        var bare = Parse<RunResultIndication>("""{"run-result":{"r-tag":"c","success":true,"time":0.784}}""");
        Assert.Empty(bare.Text);
        var fail = Parse<RunResultIndication>("""{"run-result":{"r-tag":"t4","success":false,"text":["Connection failed:","nonexistent.invalid/23:","nodename nor servname provided, or not known"],"text-err":[true,true,true],"time":0.039}}""");
        Assert.False(fail.Success);
        Assert.Equal(3, fail.Text.Count);
        Assert.True(fail.TextErr[0]);
    }

    [Fact]
    public void Parses_popup_and_ui_error()
    {
        var popup = Parse<PopupIndication>("""{"popup":{"type":"connection-error","text":"Host unreachable","retrying":false}}""");
        Assert.Equal("connection-error", popup.Type);
        Assert.Equal("Host unreachable", popup.Text);
        var err = Parse<UiErrorIndication>("""{"ui-error":{"fatal":false,"text":"Element 0: Not an object","operation":"run","member":"actions"}}""");
        Assert.False(err.Fatal);
        Assert.Equal("run", err.Operation);
    }

    [Fact]
    public void Parses_ft()
    {
        var ft = Parse<FtIndication>("""{"ft":{"state":"running","bytes":4096,"cause":"ui"}}""");
        Assert.Equal("running", ft.State);
        Assert.Equal(4096, ft.Bytes);
        var done = Parse<FtIndication>("""{"ft":{"state":"complete","success":true,"text":"Transfer complete, 12 bytes","cause":"ui"}}""");
        Assert.True(done.Success);
    }

    [Fact]
    public void Unknown_indications_are_reported_by_name()
    {
        var u = Parse<UnknownIndication>("""{"stats":{"bytes-received":107,"records-received":1}}""");
        Assert.Equal("stats", u.Name);
        Assert.IsType<UnknownIndication>(Parse<UnknownIndication>("""{"bell":{}}"""));
    }

    [Fact]
    public void Malformed_lines_return_false()
    {
        Assert.False(IndicationParser.TryParse("not json", out _));
        Assert.False(IndicationParser.TryParse("", out _));
        Assert.False(IndicationParser.TryParse("[1,2]", out _));
        Assert.False(IndicationParser.TryParse("{}", out _));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: build errors for missing types.

- [ ] **Step 4: Implement the records and parser**

`src/LizTerm.Backend.B3270/Protocol/Indications.cs`:

```csharp
namespace LizTerm.Backend.B3270.Protocol;

/// <summary>A message from b3270 to the UI. Names and fields follow include/b3270proto.h in x3270 4.5.</summary>
public abstract record Indication;

public sealed record InitializeIndication(IReadOnlyList<Indication> Items) : Indication;
public sealed record HelloIndication(string Version, string Build) : Indication;
public sealed record ScreenModeIndication(int Model, int Rows, int Columns, bool Color, bool Oversize, bool Extended) : Indication;
public sealed record EraseIndication(int? LogicalRows, int? LogicalColumns, string? Fg, string? Bg) : Indication;
public sealed record CursorIndication(bool Enabled, int? Row, int? Column);
/// <summary>One run of cells starting at Column (1-based). Text runs carry Text; attribute-only runs carry Count.</summary>
public sealed record ChangeIndication(int Column, string? Text, int? Count, string? Fg, string? Bg, string? Gr);
public sealed record RowIndication(int Row, IReadOnlyList<ChangeIndication> Changes);
public sealed record ScreenIndication(CursorIndication? Cursor, IReadOnlyList<RowIndication>? Rows) : Indication;
/// <summary>Value is normalized to a string ("true"/"false" for booleans); null means the field is cleared.</summary>
public sealed record OiaIndication(string Field, string? Value) : Indication;
public sealed record ConnectionIndication(string State, string? Host, string? Cause) : Indication;
public sealed record TlsIndication(bool Secure, bool? Verified, string? Session, string? HostCert) : Indication;
public sealed record RunResultIndication(string? Tag, bool Success, IReadOnlyList<string> Text, IReadOnlyList<bool> TextErr, bool Abort) : Indication;
public sealed record PopupIndication(string Type, string Text, bool Retrying, bool Error) : Indication;
public sealed record UiErrorIndication(bool Fatal, string Text, string? Operation, string? Member) : Indication;
public sealed record FtIndication(string State, bool? Success, string? Text, long? Bytes, string? Cause) : Indication;
public sealed record SettingIndication(string Name, string? Value, string? Cause) : Indication;
public sealed record UnknownIndication(string Name) : Indication;
```

`src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs`:

```csharp
using System.Text.Json;

namespace LizTerm.Backend.B3270.Protocol;

public static class IndicationParser
{
    public static bool TryParse(string line, out Indication indication)
    {
        indication = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            using var props = root.EnumerateObject();
            if (!props.MoveNext()) return false;
            var first = props.Current;
            indication = Parse(first.Name, first.Value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Indication Parse(string name, JsonElement body) => name switch
    {
        "initialize" => new InitializeIndication(ParseArray(body)),
        "hello" => new HelloIndication(Str(body, "version") ?? "", Str(body, "build") ?? ""),
        "screen-mode" => new ScreenModeIndication(Int(body, "model") ?? 0, Int(body, "rows") ?? 0, Int(body, "columns") ?? 0,
            Bool(body, "color") ?? false, Bool(body, "oversize") ?? false, Bool(body, "extended") ?? false),
        "erase" => new EraseIndication(Int(body, "logical-rows"), Int(body, "logical-columns"), Str(body, "fg"), Str(body, "bg")),
        "screen" => ParseScreen(body),
        "oia" => new OiaIndication(Str(body, "field") ?? "", Scalar(body, "value")),
        "connection" => new ConnectionIndication(Str(body, "state") ?? "", Str(body, "host"), Str(body, "cause")),
        "tls" => new TlsIndication(Bool(body, "secure") ?? false, Bool(body, "verified"), Str(body, "session"), Str(body, "host-cert")),
        "run-result" => new RunResultIndication(Str(body, "r-tag"), Bool(body, "success") ?? false,
            StringList(body, "text"), BoolList(body, "text-err"), Bool(body, "abort") ?? false),
        "popup" => new PopupIndication(Str(body, "type") ?? "", Str(body, "text") ?? "", Bool(body, "retrying") ?? false, Bool(body, "error") ?? false),
        "ui-error" => new UiErrorIndication(Bool(body, "fatal") ?? false, Str(body, "text") ?? "", Str(body, "operation"), Str(body, "member")),
        "ft" => new FtIndication(Str(body, "state") ?? "", Bool(body, "success"), Str(body, "text"), Long(body, "bytes"), Str(body, "cause")),
        "setting" => new SettingIndication(Str(body, "name") ?? "", Scalar(body, "value"), Str(body, "cause")),
        _ => new UnknownIndication(name),
    };

    private static IReadOnlyList<Indication> ParseArray(JsonElement array)
    {
        var items = new List<Indication>();
        if (array.ValueKind != JsonValueKind.Array) return items;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            using var props = element.EnumerateObject();
            if (props.MoveNext()) items.Add(Parse(props.Current.Name, props.Current.Value));
        }
        return items;
    }

    private static ScreenIndication ParseScreen(JsonElement body)
    {
        CursorIndication? cursor = null;
        if (body.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.Object)
            cursor = new CursorIndication(Bool(c, "enabled") ?? false, Int(c, "row"), Int(c, "column"));

        List<RowIndication>? rows = null;
        if (body.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
        {
            rows = [];
            foreach (var row in rowsElement.EnumerateArray())
            {
                var changes = new List<ChangeIndication>();
                if (row.TryGetProperty("changes", out var changesElement) && changesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ch in changesElement.EnumerateArray())
                        changes.Add(new ChangeIndication(Int(ch, "column") ?? 1, Str(ch, "text"), Int(ch, "count"),
                            Str(ch, "fg"), Str(ch, "bg"), Str(ch, "gr")));
                }
                rows.Add(new RowIndication(Int(row, "row") ?? 1, changes));
            }
        }
        return new ScreenIndication(cursor, rows);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : null;

    /// <summary>String, number, or boolean as text; null when absent or JSON null.</summary>
    private static string? Scalar(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static IReadOnlyList<string> StringList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return [];
        return v.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
    }

    private static IReadOnlyList<bool> BoolList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return [];
        return v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.True).ToList();
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: all 14 pass.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add b3270 backend project and indication parser

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Protocol mappers: colors, renditions, actions, run operation, host string

**Files:**
- Create: `src/LizTerm.Backend.B3270/Protocol/ColorNames.cs`, `ActionMap.cs`, `RunOperation.cs`, `HostStringBuilder.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/Protocol/MappersTests.cs`

**Interfaces:**
- Produces:
  - `static HostColor ColorNames.ParseColor(string? name)` (null or unknown → `HostColor.Default`), `static CellRendition ColorNames.ParseRendition(string? gr)` (null or `"default"` → `None`)
  - `sealed record B3270Action(string Name, params string[] Args)`
  - `static B3270Action ActionMap.ForKey(TerminalKey key)`
  - `static string RunOperation.Serialize(string tag, IReadOnlyList<B3270Action> actions)` producing one line without a trailing newline
  - `static string HostStringBuilder.Build(SessionProfile profile)`, `static string HostStringBuilder.ModelArgument(SessionProfile profile)` (e.g. `3279-2-E`)

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.B3270.Tests/Protocol/MappersTests.cs`:

```csharp
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests.Protocol;

public class MappersTests
{
    [Theory]
    [InlineData("neutralBlack", HostColor.NeutralBlack)]
    [InlineData("blue", HostColor.Blue)]
    [InlineData("neutralWhite", HostColor.NeutralWhite)]
    [InlineData("paleTurquoise", HostColor.PaleTurquoise)]
    [InlineData("default", HostColor.Default)]
    [InlineData(null, HostColor.Default)]
    [InlineData("nonsense", HostColor.Default)]
    public void ParseColor_maps_b3270_names(string? name, HostColor expected) =>
        Assert.Equal(expected, ColorNames.ParseColor(name));

    [Theory]
    [InlineData(null, CellRendition.None)]
    [InlineData("default", CellRendition.None)]
    [InlineData("underline", CellRendition.Underline)]
    [InlineData("highlight,selectable", CellRendition.Highlight | CellRendition.Selectable)]
    [InlineData("reverse,blink,order,private-use,no-copy,wrap,left-half,right-half,wide", CellRendition.Reverse | CellRendition.Blink | CellRendition.Order | CellRendition.PrivateUse | CellRendition.NoCopy | CellRendition.Wrap | CellRendition.LeftHalf | CellRendition.RightHalf | CellRendition.Wide)]
    public void ParseRendition_maps_comma_lists(string? gr, CellRendition expected) =>
        Assert.Equal(expected, ColorNames.ParseRendition(gr));

    [Theory]
    [InlineData(TerminalKey.Enter, "Enter", new string[0])]
    [InlineData(TerminalKey.PF1, "PF", new[] { "1" })]
    [InlineData(TerminalKey.PF24, "PF", new[] { "24" })]
    [InlineData(TerminalKey.PA3, "PA", new[] { "3" })]
    [InlineData(TerminalKey.Backspace, "BackSpace", new string[0])]
    [InlineData(TerminalKey.EraseEof, "EraseEOF", new string[0])]
    [InlineData(TerminalKey.Insert, "ToggleInsert", new string[0])]
    [InlineData(TerminalKey.BackTab, "BackTab", new string[0])]
    [InlineData(TerminalKey.SysReq, "SysReq", new string[0])]
    public void ActionMap_names_match_x3270_actions(TerminalKey key, string name, string[] args)
    {
        var action = ActionMap.ForKey(key);
        Assert.Equal(name, action.Name);
        Assert.Equal(args, action.Args);
    }

    [Fact]
    public void Every_TerminalKey_has_an_action()
    {
        foreach (var key in Enum.GetValues<TerminalKey>())
            Assert.NotEmpty(ActionMap.ForKey(key).Name);
    }

    [Fact]
    public void RunOperation_serializes_tag_and_actions()
    {
        var json = RunOperation.Serialize("7", [new B3270Action("PF", "3"), new B3270Action("Enter")]);
        Assert.Equal("""{"run":{"r-tag":"7","actions":[{"action":"PF","args":["3"]},{"action":"Enter"}]}}""", json);
        Assert.DoesNotContain('\n', json);
    }

    [Fact]
    public void RunOperation_escapes_text()
    {
        var json = RunOperation.Serialize("1", [new B3270Action("String", "say \"hi\"\\")]);
        Assert.Equal("""{"run":{"r-tag":"1","actions":[{"action":"String","args":["say \"hi\"\\"]}]}}""", json);
    }

    [Fact]
    public void HostString_plain()
    {
        var p = new SessionProfile { Name = "a", Host = "mvs.example", Port = 3270 };
        Assert.Equal("mvs.example:3270", HostStringBuilder.Build(p));
    }

    [Fact]
    public void HostString_tls_and_lu()
    {
        var p = new SessionProfile { Name = "a", Host = "mvs.example", Port = 992, UseTls = true, LuName = "LU01" };
        Assert.Equal("L:LU01@mvs.example:992", HostStringBuilder.Build(p));
    }

    [Fact]
    public void HostString_brackets_ipv6()
    {
        var p = new SessionProfile { Name = "a", Host = "::1", Port = 23 };
        Assert.Equal("[::1]:23", HostStringBuilder.Build(p));
    }

    [Theory]
    [InlineData(2, true, "3279-2-E")]
    [InlineData(4, false, "3279-4")]
    [InlineData(5, true, "3279-5-E")]
    public void ModelArgument_formats_model(int model, bool extended, string expected)
    {
        var p = new SessionProfile { Name = "a", Host = "h", Model = model, Extended = extended };
        Assert.Equal(expected, HostStringBuilder.ModelArgument(p));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: build errors.

- [ ] **Step 3: Implement**

`src/LizTerm.Backend.B3270/Protocol/ColorNames.cs`:

```csharp
using LizTerm.Core.Screen;

namespace LizTerm.Backend.B3270.Protocol;

public static class ColorNames
{
    private static readonly Dictionary<string, HostColor> Colors = new(StringComparer.Ordinal)
    {
        ["neutralBlack"] = HostColor.NeutralBlack,
        ["blue"] = HostColor.Blue,
        ["red"] = HostColor.Red,
        ["pink"] = HostColor.Pink,
        ["green"] = HostColor.Green,
        ["turquoise"] = HostColor.Turquoise,
        ["yellow"] = HostColor.Yellow,
        ["neutralWhite"] = HostColor.NeutralWhite,
        ["black"] = HostColor.Black,
        ["deepBlue"] = HostColor.DeepBlue,
        ["orange"] = HostColor.Orange,
        ["purple"] = HostColor.Purple,
        ["paleGreen"] = HostColor.PaleGreen,
        ["paleTurquoise"] = HostColor.PaleTurquoise,
        ["grey"] = HostColor.Grey,
        ["white"] = HostColor.White,
    };

    private static readonly Dictionary<string, CellRendition> Renditions = new(StringComparer.Ordinal)
    {
        ["underline"] = CellRendition.Underline,
        ["blink"] = CellRendition.Blink,
        ["highlight"] = CellRendition.Highlight,
        ["selectable"] = CellRendition.Selectable,
        ["reverse"] = CellRendition.Reverse,
        ["wide"] = CellRendition.Wide,
        ["order"] = CellRendition.Order,
        ["private-use"] = CellRendition.PrivateUse,
        ["no-copy"] = CellRendition.NoCopy,
        ["wrap"] = CellRendition.Wrap,
        ["left-half"] = CellRendition.LeftHalf,
        ["right-half"] = CellRendition.RightHalf,
    };

    public static HostColor ParseColor(string? name) =>
        name is not null && Colors.TryGetValue(name, out var color) ? color : HostColor.Default;

    public static CellRendition ParseRendition(string? gr)
    {
        if (string.IsNullOrEmpty(gr) || gr == "default") return CellRendition.None;
        var result = CellRendition.None;
        foreach (var part in gr.Split(','))
            if (Renditions.TryGetValue(part.Trim(), out var flag)) result |= flag;
        return result;
    }
}
```

`src/LizTerm.Backend.B3270/Protocol/ActionMap.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Protocol;

public sealed record B3270Action(string Name, params string[] Args);

public static class ActionMap
{
    public static B3270Action ForKey(TerminalKey key)
    {
        if (key >= TerminalKey.PF1 && key <= TerminalKey.PF24)
            return new B3270Action("PF", (key - TerminalKey.PF1 + 1).ToString());
        if (key >= TerminalKey.PA1 && key <= TerminalKey.PA3)
            return new B3270Action("PA", (key - TerminalKey.PA1 + 1).ToString());
        return key switch
        {
            TerminalKey.Enter => new("Enter"),
            TerminalKey.Clear => new("Clear"),
            TerminalKey.Attn => new("Attn"),
            TerminalKey.SysReq => new("SysReq"),
            TerminalKey.Reset => new("Reset"),
            TerminalKey.Tab => new("Tab"),
            TerminalKey.BackTab => new("BackTab"),
            TerminalKey.Home => new("Home"),
            TerminalKey.EraseEof => new("EraseEOF"),
            TerminalKey.EraseInput => new("EraseInput"),
            TerminalKey.Delete => new("Delete"),
            TerminalKey.Backspace => new("BackSpace"),
            TerminalKey.Insert => new("ToggleInsert"),
            TerminalKey.Dup => new("Dup"),
            TerminalKey.FieldMark => new("FieldMark"),
            TerminalKey.Newline => new("Newline"),
            TerminalKey.Up => new("Up"),
            TerminalKey.Down => new("Down"),
            TerminalKey.Left => new("Left"),
            TerminalKey.Right => new("Right"),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No b3270 action for key"),
        };
    }
}
```

`src/LizTerm.Backend.B3270/Protocol/RunOperation.cs`:

```csharp
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LizTerm.Backend.B3270.Protocol;

public static class RunOperation
{
    public static string Serialize(string tag, IReadOnlyList<B3270Action> actions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        // Relaxed escaping keeps quotes as \" and leaves UTF-8 text alone (b3270 runs with -utf8).
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("run");
            writer.WriteString("r-tag", tag);
            writer.WriteStartArray("actions");
            foreach (var action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString("action", action.Name);
                if (action.Args.Length > 0)
                {
                    writer.WriteStartArray("args");
                    foreach (var arg in action.Args) writer.WriteStringValue(arg);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
```

`src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs`:

```csharp
using System.Text;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Protocol;

/// <summary>Builds x3270 host syntax: [prefix:...][lu@]host[:port]. L: = TLS tunnel.</summary>
public static class HostStringBuilder
{
    public static string Build(SessionProfile profile)
    {
        var sb = new StringBuilder();
        if (profile.UseTls) sb.Append("L:");
        if (!string.IsNullOrWhiteSpace(profile.LuName)) sb.Append(profile.LuName.Trim()).Append('@');
        var host = profile.Host.Trim();
        sb.Append(host.Contains(':') ? $"[{host}]" : host);
        sb.Append(':').Append(profile.Port);
        return sb.ToString();
    }

    public static string ModelArgument(SessionProfile profile) =>
        $"3279-{profile.Model}{(profile.Extended ? "-E" : "")}";
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add b3270 protocol mappers and run operation builder

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Process abstraction, child process, locator, and test fake

**Files:**
- Create: `src/LizTerm.Backend.B3270/Process/IB3270Process.cs`, `B3270ChildProcess.cs`, `B3270Locator.cs`
- Create: `tests/LizTerm.Backend.B3270.Tests/Fakes/FakeB3270Process.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs`, `tests/LizTerm.Backend.B3270.Tests/Fakes/FakeB3270ProcessTests.cs`

**Interfaces:**
- Produces:

```csharp
public interface IB3270Process : IDisposable
{
    void Start(IReadOnlyList<string> arguments);
    TextReader StandardOutput { get; }
    TextWriter StandardInput { get; }
    Task<int> WaitForExitAsync();
    IReadOnlyList<string> StderrTail { get; }
    void Kill();
}
public sealed class B3270ChildProcess(string executablePath) : IB3270Process;
public static class B3270Locator
{
    const string EnvironmentOverride = "LIZTERM_B3270_PATH";
    static string FileName { get; }             // "b3270" or "b3270.exe"
    static string Find();                       // uses env var + AppContext.BaseDirectory
    static string Find(string? overridePath, string baseDirectory);
}
// test-only
public sealed class FakeB3270Process : IB3270Process
{
    bool Started; IReadOnlyList<string> StartedArguments;
    void Emit(string line);                     // queue a stdout line
    void Exit(int code);                        // end stdout and complete WaitForExitAsync
    IReadOnlyList<string> InputLines;           // lines written to stdin so far
    Task<string> WaitForInputAsync(Func<string,bool> predicate, TimeSpan timeout);
    Func<string, IReadOnlyList<string>>? RunResponder;   // default: success run-result for the r-tag
    bool AutoInitialize = true;                 // emit a minimal initialize on Start
}
```

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs`:

```csharp
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests.Process;

public class B3270LocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-loc-" + Guid.NewGuid().ToString("N"));

    public B3270LocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeExecutable(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void Override_path_wins()
    {
        var path = MakeExecutable("custom/b3270");
        Assert.Equal(path, B3270Locator.Find(path, _dir));
    }

    [Fact]
    public void Finds_runtime_native_folder()
    {
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var path = MakeExecutable(Path.Combine("runtimes", rid, "native", B3270Locator.FileName));
        Assert.Equal(path, B3270Locator.Find(null, _dir));
    }

    [Fact]
    public void Falls_back_to_base_directory()
    {
        var path = MakeExecutable(B3270Locator.FileName);
        Assert.Equal(path, B3270Locator.Find(null, _dir));
    }

    [Fact]
    public void Missing_binary_reports_where_it_looked()
    {
        var ex = Assert.Throws<BackendUnavailableException>(() => B3270Locator.Find(null, _dir));
        Assert.Contains(_dir, ex.Message);
        Assert.Contains("LIZTERM_B3270_PATH", ex.Message);
    }

    [Fact]
    public void Non_executable_binary_is_reported_distinctly()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "b3270");
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var ex = Assert.Throws<BackendUnavailableException>(() => B3270Locator.Find(null, _dir));
        Assert.Contains("chmod +x", ex.Message);
    }
}
```

`tests/LizTerm.Backend.B3270.Tests/Fakes/FakeB3270ProcessTests.cs`:

```csharp
using LizTerm.Backend.B3270.Tests.Fakes;

namespace LizTerm.Backend.B3270.Tests.Fakes;

public class FakeB3270ProcessTests
{
    [Fact]
    public async Task Emitted_lines_are_readable_and_exit_ends_the_stream()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Start(["-json"]);
        fake.Emit("one");
        fake.Emit("two");
        fake.Exit(0);
        Assert.Equal("one", fake.StandardOutput.ReadLine());
        Assert.Equal("two", fake.StandardOutput.ReadLine());
        Assert.Null(fake.StandardOutput.ReadLine());
        Assert.Equal(0, await fake.WaitForExitAsync());
    }

    [Fact]
    public async Task Input_lines_are_captured_and_runs_get_default_results()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Start([]);
        fake.StandardInput.Write("""{"run":{"r-tag":"5","actions":[{"action":"Enter"}]}}""" + "\n");
        var line = await fake.WaitForInputAsync(l => l.Contains("Enter"), TimeSpan.FromSeconds(1));
        Assert.Contains("\"r-tag\":\"5\"", line);
        Assert.Equal("""{"run-result":{"r-tag":"5","success":true,"time":0}}""", fake.StandardOutput.ReadLine());
    }

    [Fact]
    public void AutoInitialize_emits_hello_first()
    {
        var fake = new FakeB3270Process();
        fake.Start([]);
        var first = fake.StandardOutput.ReadLine();
        Assert.NotNull(first);
        Assert.StartsWith("{\"initialize\":[{\"hello\":", first);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: build errors.

- [ ] **Step 3: Implement the interface, child process, and locator**

`src/LizTerm.Backend.B3270/Process/IB3270Process.cs`:

```csharp
namespace LizTerm.Backend.B3270.Process;

/// <summary>A b3270 process with line-oriented stdin/stdout. Real implementation spawns a child; tests use a fake.</summary>
public interface IB3270Process : IDisposable
{
    void Start(IReadOnlyList<string> arguments);
    TextReader StandardOutput { get; }
    TextWriter StandardInput { get; }
    Task<int> WaitForExitAsync();
    IReadOnlyList<string> StderrTail { get; }
    void Kill();
}
```

`src/LizTerm.Backend.B3270/Process/B3270ChildProcess.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

public sealed class B3270ChildProcess(string executablePath) : IB3270Process
{
    private const int StderrTailSize = 50;
    private readonly ConcurrentQueue<string> _stderr = new();
    private System.Diagnostics.Process? _process;

    public TextReader StandardOutput => _process?.StandardOutput ?? throw NotStarted();
    public TextWriter StandardInput => _process?.StandardInput ?? throw NotStarted();
    public IReadOnlyList<string> StderrTail => _stderr.ToArray();

    public void Start(IReadOnlyList<string> arguments)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        try
        {
            _process = System.Diagnostics.Process.Start(psi) ?? throw new BackendUnavailableException($"Could not start {executablePath}");
        }
        catch (Exception ex) when (ex is not BackendUnavailableException)
        {
            throw new BackendUnavailableException($"Could not start {executablePath}: {ex.Message}");
        }
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _stderr.Enqueue(e.Data);
            while (_stderr.Count > StderrTailSize) _stderr.TryDequeue(out _);
        };
        _process.BeginErrorReadLine();
        _process.StandardInput.AutoFlush = true;
    }

    public async Task<int> WaitForExitAsync()
    {
        if (_process is null) throw NotStarted();
        await _process.WaitForExitAsync();
        return _process.ExitCode;
    }

    public void Kill()
    {
        try { _process?.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already exited */ }
    }

    public void Dispose()
    {
        Kill();
        _process?.Dispose();
    }

    private static InvalidOperationException NotStarted() => new("Process not started");
}
```

`src/LizTerm.Backend.B3270/Process/B3270Locator.cs`:

```csharp
using System.Runtime.InteropServices;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

public static class B3270Locator
{
    public const string EnvironmentOverride = "LIZTERM_B3270_PATH";

    public static string FileName => OperatingSystem.IsWindows() ? "b3270.exe" : "b3270";

    public static string Find() =>
        Find(Environment.GetEnvironmentVariable(EnvironmentOverride), AppContext.BaseDirectory);

    public static string Find(string? overridePath, string baseDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath);
        candidates.Add(Path.Combine(baseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName));
        candidates.Add(Path.Combine(baseDirectory, FileName));

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(candidate);
                if ((mode & UnixFileMode.UserExecute) == 0)
                    throw new BackendUnavailableException(
                        $"The emulator engine at {candidate} is not executable. Run: chmod +x \"{candidate}\"");
            }
            return candidate;
        }

        throw new BackendUnavailableException(
            "The emulator engine (b3270) was not found. Looked in:\n  " + string.Join("\n  ", candidates) +
            $"\nSet {EnvironmentOverride} to a b3270 executable to override.");
    }
}
```

- [ ] **Step 4: Implement the fake**

`tests/LizTerm.Backend.B3270.Tests/Fakes/FakeB3270Process.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Process;

namespace LizTerm.Backend.B3270.Tests.Fakes;

public sealed partial class FakeB3270Process : IB3270Process
{
    public const string MinimalInitialize =
        """{"initialize":[{"hello":{"version":"4.5.6","build":"fake b3270"}},{"screen-mode":{"model":2,"rows":24,"columns":80,"color":true,"oversize":false,"extended":true}},{"erase":{"logical-rows":24,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}},{"oia":{"field":"lock","value":"not-connected"}}]}""";

    private readonly BlockingCollection<string> _stdout = new();
    private readonly List<string> _stdin = [];
    private readonly object _lock = new();
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeB3270Process()
    {
        StandardOutput = new QueueReader(_stdout);
        StandardInput = new LineWriter(OnInputLine);
    }

    public bool AutoInitialize { get; init; } = true;
    public bool Started { get; private set; }
    public IReadOnlyList<string> StartedArguments { get; private set; } = [];
    public Func<string, IReadOnlyList<string>>? RunResponder { get; set; }
    public TextReader StandardOutput { get; }
    public TextWriter StandardInput { get; }
    public IReadOnlyList<string> StderrTail => ["fake stderr line"];

    public IReadOnlyList<string> InputLines
    {
        get { lock (_lock) return _stdin.ToArray(); }
    }

    public void Start(IReadOnlyList<string> arguments)
    {
        Started = true;
        StartedArguments = arguments;
        if (AutoInitialize) Emit(MinimalInitialize);
    }

    public void Emit(string line)
    {
        if (!_stdout.IsAddingCompleted) _stdout.Add(line);
    }

    public void Exit(int code)
    {
        if (!_stdout.IsAddingCompleted) _stdout.CompleteAdding();
        _exit.TrySetResult(code);
    }

    public Task<int> WaitForExitAsync() => _exit.Task;

    public void Kill() => Exit(-1);

    public void Dispose() => Exit(0);

    public async Task<string> WaitForInputAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = InputLines.FirstOrDefault(predicate);
            if (match is not null) return match;
            await Task.Delay(10);
        }
        throw new TimeoutException("No matching input line within " + timeout);
    }

    private void OnInputLine(string line)
    {
        lock (_lock) _stdin.Add(line);
        if (RunResponder is not null)
        {
            foreach (var reply in RunResponder(line)) Emit(reply);
            return;
        }
        var tag = TagRegex().Match(line);
        if (tag.Success)
            Emit($$"""{"run-result":{"r-tag":"{{tag.Groups[1].Value}}","success":true,"time":0}}""");
    }

    [GeneratedRegex("\"r-tag\":\"([^\"]+)\"")]
    private static partial Regex TagRegex();

    private sealed class QueueReader(BlockingCollection<string> queue) : TextReader
    {
        public override string? ReadLine() => queue.TryTake(out var line, Timeout.Infinite) ? line : null;
    }

    private sealed class LineWriter(Action<string> onLine) : TextWriter
    {
        private readonly StringBuilder _pending = new();
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                var line = _pending.ToString();
                _pending.Clear();
                onLine(line);
            }
            else if (value != '\r')
            {
                _pending.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var c in value) Write(c);
        }
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: all pass (the non-executable test is skipped on Windows by returning early).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add b3270 process abstraction, child process, locator, and fake

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: B3270Session lifecycle: start, hello, run correlation, faults, wire log

**Files:**
- Create: `src/LizTerm.Backend.B3270/WireLog.cs`, `src/LizTerm.Backend.B3270/B3270Session.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class WireLog(TextWriter writer) : IDisposable
{
    static WireLog? FromEnvironment();            // LIZTERM_WIRE_LOG=path, append mode
    void Inbound(string line); void Outbound(string line);   // "<ISO-8601> < line" / "> line"
}
public sealed class B3270Session(SessionProfile profile, Func<IB3270Process> processFactory, WireLog? wireLog = null) : IEmulatorSession
{
    static readonly Version MinimumVersion = new(4, 2, 0);
    static IReadOnlyList<string> BuildArguments(SessionProfile profile);   // -json -utf8 -model 3279-2-E -codepage cp037
    internal Task StartProcessAsync(CancellationToken ct);                // spawns, waits for hello (10 s), checks version
    internal Task<RunResultIndication> RunAsync(params B3270Action[] actions);   // throws EmulatorActionException on failure
    internal Task<RunResultIndication> RunRawAsync(IReadOnlyList<B3270Action> actions);  // never throws on failure
}
```

Indication-to-state mapping (screen, OIA, connection) is Task 10; this task handles `initialize`, `hello`, `run-result`, `ui-error`, and process exit. Unhandled indications go through a `HandleIndication` method that Task 10 fills in.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs`:

```csharp
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionLifecycleTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "host.example", Port = 3270, Model = 3, CodePage = "bracket" };

    [Fact]
    public void BuildArguments_uses_json_utf8_model_and_codepage()
    {
        Assert.Equal(["-json", "-utf8", "-model", "3279-3-E", "-codepage", "bracket"], B3270Session.BuildArguments(Profile));
    }

    [Fact]
    public async Task Start_spawns_with_arguments_and_waits_for_hello()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        Assert.True(fake.Started);
        Assert.Contains("-json", fake.StartedArguments);
        Assert.Equal(24, session.CurrentScreen.Rows);
    }

    [Fact]
    public async Task Start_rejects_old_versions()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        var session = new B3270Session(Profile, () => fake);
        fake.Emit("""{"initialize":[{"hello":{"version":"4.1.0","build":"old"}}]}""");
        var ex = await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
        Assert.Contains("4.1.0", ex.Message);
    }

    [Fact]
    public async Task Start_times_out_without_hello()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        var session = new B3270Session(Profile, () => fake) { StartupTimeout = TimeSpan.FromMilliseconds(200) };
        await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Run_correlates_results_by_tag_even_out_of_order()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);

        var first = session.RunAsync(new B3270Action("Enter"));
        var second = session.RunAsync(new B3270Action("PF", "3"));
        var firstLine = await fake.WaitForInputAsync(l => l.Contains("\"Enter\""), TimeSpan.FromSeconds(1));
        var secondLine = await fake.WaitForInputAsync(l => l.Contains("\"PF\""), TimeSpan.FromSeconds(1));
        var firstTag = System.Text.RegularExpressions.Regex.Match(firstLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        var secondTag = System.Text.RegularExpressions.Regex.Match(secondLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(firstTag, secondTag);

        fake.Emit($$"""{"run-result":{"r-tag":"{{secondTag}}","success":true,"text":["pf"],"time":0}}""");
        fake.Emit($$"""{"run-result":{"r-tag":"{{firstTag}}","success":true,"text":["enter"],"time":0}}""");
        Assert.Equal(["pf"], (await second).Text);
        Assert.Equal(["enter"], (await first).Text);
    }

    [Fact]
    public async Task Run_failure_throws_with_emulator_text()
    {
        var fake = new FakeB3270Process
        {
            RunResponder = line =>
            {
                var tag = System.Text.RegularExpressions.Regex.Match(line, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
                return [$$"""{"run-result":{"r-tag":"{{tag}}","success":false,"text":["Keyboard locked"],"text-err":[true],"time":0}}"""];
            },
        };
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        var ex = await Assert.ThrowsAsync<EmulatorActionException>(() => session.RunAsync(new B3270Action("Enter")));
        Assert.Equal("Keyboard locked", ex.Message);
        var raw = await session.RunRawAsync([new B3270Action("Enter")]);
        Assert.False(raw.Success);
    }

    [Fact]
    public async Task Unexpected_exit_faults_session_and_pending_runs()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        BackendFault? fault = null;
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => { fault = f; faulted.TrySetResult(f); };
        var pending = session.RunAsync(new B3270Action("Enter"));
        await fake.WaitForInputAsync(l => l.Contains("Enter"), TimeSpan.FromSeconds(1));

        fake.Exit(137);

        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(137, fault!.ExitCode);
        Assert.Contains("fake stderr line", fault.StderrTail);
        await Assert.ThrowsAsync<BackendUnavailableException>(() => pending);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    [Fact]
    public async Task Dispose_sends_quit_and_does_not_fault()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        var faulted = false;
        session.Faulted += (_, _) => faulted = true;
        fake.RunResponder = line => { if (line.Contains("Quit")) fake.Exit(0); return []; };
        await session.DisposeAsync();
        Assert.Contains(fake.InputLines, l => l.Contains("\"Quit\""));
        await Task.Delay(50);
        Assert.False(faulted);
    }

    [Fact]
    public async Task Wire_log_records_both_directions()
    {
        var log = new StringWriter();
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake, new WireLog(log));
        await session.StartProcessAsync(CancellationToken.None);
        await session.RunAsync(new B3270Action("Enter"));
        var text = log.ToString();
        Assert.Contains(" < {\"initialize\"", text);
        Assert.Contains(" > {\"run\":", text);
        Assert.Contains(" < {\"run-result\"", text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: build errors.

- [ ] **Step 3: Implement WireLog**

`src/LizTerm.Backend.B3270/WireLog.cs`:

```csharp
using System.Globalization;

namespace LizTerm.Backend.B3270;

/// <summary>Records every protocol line in both directions. Used for bug reports and as replay fixtures.</summary>
public sealed class WireLog(TextWriter writer) : IDisposable
{
    public const string EnvironmentVariable = "LIZTERM_WIRE_LOG";
    private readonly object _lock = new();

    public static WireLog? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        return new WireLog(new StreamWriter(path, append: true));
    }

    public void Inbound(string line) => Write('<', line);
    public void Outbound(string line) => Write('>', line);

    private void Write(char direction, string line)
    {
        lock (_lock)
        {
            writer.Write(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(direction);
            writer.Write(' ');
            writer.WriteLine(line);
            writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock) writer.Dispose();
    }
}
```

- [ ] **Step 4: Implement B3270Session (lifecycle part)**

`src/LizTerm.Backend.B3270/B3270Session.cs`:

```csharp
using System.Collections.Concurrent;
using LizTerm.Backend.B3270.Process;
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270;

public sealed class B3270Session : IEmulatorSession
{
    public static readonly Version MinimumVersion = new(4, 2, 0);

    private readonly Func<IB3270Process> _processFactory;
    private readonly WireLog? _wireLog;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunResultIndication>> _pending = new();
    private readonly ScreenBuffer _buffer = new(24, 80);
    private IB3270Process? _process;
    private Thread? _readerThread;
    private TaskCompletionSource<HelloIndication>? _hello;
    private int _tagCounter;
    private volatile bool _shuttingDown;

    public B3270Session(SessionProfile profile, Func<IB3270Process> processFactory, WireLog? wireLog = null)
    {
        Profile = profile;
        _processFactory = processFactory;
        _wireLog = wireLog;
        CurrentScreen = _buffer.Snapshot();
    }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public SessionProfile Profile { get; }
    public ScreenSnapshot CurrentScreen { get; private set; }
    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
    public TlsInfo? Tls { get; private set; }
    public KeyboardStatus KeyboardStatus { get; private set; } = KeyboardStatus.Initial;

    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<BackendFault>? Faulted;
    public event EventHandler<string>? HostMessage;

    public static IReadOnlyList<string> BuildArguments(SessionProfile profile) =>
        ["-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage];

    // ---- lifecycle ----

    internal async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        if (_process is not null) return;
        var process = _processFactory();
        _hello = new TaskCompletionSource<HelloIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process = process;
        process.Start(BuildArguments(Profile));
        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "b3270-reader" };
        _readerThread.Start();

        HelloIndication hello;
        try
        {
            hello = await _hello.Task.WaitAsync(StartupTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            TearDown();
            throw new BackendUnavailableException($"b3270 did not answer within {StartupTimeout.TotalSeconds:0} seconds. stderr: {string.Join(" | ", process.StderrTail)}");
        }
        catch (BackendUnavailableException)
        {
            TearDown();
            throw;
        }

        if (!Version.TryParse(hello.Version, out var version) || version < MinimumVersion)
        {
            TearDown();
            throw new BackendUnavailableException($"b3270 version {hello.Version} is too old; {MinimumVersion} or newer is required.");
        }
    }

    private void ReadLoop()
    {
        var process = _process!;
        try
        {
            while (process.StandardOutput.ReadLine() is { } line)
            {
                _wireLog?.Inbound(line);
                if (!IndicationParser.TryParse(line, out var indication)) continue;
                try { Handle(indication); }
                catch (Exception ex) { HostMessage?.Invoke(this, "Internal error handling emulator output: " + ex.Message); }
            }
        }
        catch (Exception ex) when (_shuttingDown || ex is ObjectDisposedException or IOException)
        {
            // Stream closed during shutdown.
        }
        OnProcessEnded(process);
    }

    private void OnProcessEnded(IB3270Process process)
    {
        int? exitCode = null;
        var exitTask = process.WaitForExitAsync();
        if (exitTask.Wait(TimeSpan.FromSeconds(2))) exitCode = exitTask.Result;

        var fault = new BackendFault("The emulator engine (b3270) exited unexpectedly.", process.StderrTail, exitCode);
        foreach (var tag in _pending.Keys.ToArray())
            if (_pending.TryRemove(tag, out var tcs))
                tcs.TrySetException(new BackendUnavailableException(fault.Message));
        _hello?.TrySetException(new BackendUnavailableException(fault.Message + " stderr: " + string.Join(" | ", process.StderrTail)));

        SetConnectionState(ConnectionState.Disconnected);
        if (!_shuttingDown) Faulted?.Invoke(this, fault);
    }

    private void TearDown()
    {
        _shuttingDown = true;
        _process?.Kill();
        _process?.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null) return;
        _shuttingDown = true;
        try
        {
            WriteLine(RunOperation.Serialize("quit", [new B3270Action("Quit")]));
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            _process.Kill();
        }
        _process.Dispose();
        _process = null;
        _wireLog?.Dispose();
    }

    // ---- running actions ----

    internal Task<RunResultIndication> RunAsync(params B3270Action[] actions) => RunAsync(actions, throwOnFailure: true);

    internal Task<RunResultIndication> RunRawAsync(IReadOnlyList<B3270Action> actions) => RunAsync(actions, throwOnFailure: false);

    private async Task<RunResultIndication> RunAsync(IReadOnlyList<B3270Action> actions, bool throwOnFailure)
    {
        if (_process is null) throw new InvalidOperationException("The session has not been started.");
        var tag = Interlocked.Increment(ref _tagCounter).ToString();
        var tcs = new TaskCompletionSource<RunResultIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[tag] = tcs;
        WriteLine(RunOperation.Serialize(tag, actions));
        var result = await tcs.Task;
        if (throwOnFailure && !result.Success)
            throw new EmulatorActionException(string.Join("\n", result.Text));
        return result;
    }

    private void WriteLine(string line)
    {
        var process = _process ?? throw new InvalidOperationException("The session has not been started.");
        lock (_writeLock)
        {
            process.StandardInput.Write(line);
            process.StandardInput.Write('\n');
            process.StandardInput.Flush();
        }
        _wireLog?.Outbound(line);
    }

    // ---- indications ----

    private void Handle(Indication indication)
    {
        switch (indication)
        {
            case InitializeIndication init:
                foreach (var item in init.Items) Handle(item);
                break;
            case HelloIndication hello:
                _hello?.TrySetResult(hello);
                break;
            case RunResultIndication result when result.Tag is not null && _pending.TryRemove(result.Tag, out var tcs):
                tcs.TrySetResult(result);
                break;
            case UiErrorIndication error:
                HostMessage?.Invoke(this, "Protocol error: " + error.Text);
                break;
            default:
                HandleStateIndication(indication);
                break;
        }
    }

    /// <summary>Screen, OIA, connection, TLS, popup handling. Filled in by the next task.</summary>
    private void HandleStateIndication(Indication indication)
    {
    }

    private void SetConnectionState(ConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        ConnectionChanged?.Invoke(this, state);
    }

    // ---- IEmulatorSession actions (completed in the next task) ----

    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DisconnectAsync() => throw new NotImplementedException();
    public Task SendKeyAsync(TerminalKey key) => throw new NotImplementedException();
    public Task TypeTextAsync(string text) => throw new NotImplementedException();
    public Task PasteTextAsync(string text) => throw new NotImplementedException();
    public Task MoveCursorAsync(int row, int column) => throw new NotImplementedException();
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: all pass. The `Start_spawns...` test asserts 24 rows, which the initial buffer already has; screen-mode application comes next task.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add B3270Session lifecycle, run correlation, and wire log

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Indication handling, session actions, fixture recording, replay test

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (replace `HandleStateIndication` and the `NotImplementedException` methods)
- Create: `native/build/build-playback.sh`, `tools/record-fixture.sh`
- Create: `tests/LizTerm.Backend.B3270.Tests/Fixtures/ibmlink-help.jsonl` (recorded), `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionStateTests.cs`, `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`

**Interfaces:**
- Consumes: `ScreenBuffer`, `ColorNames`, `ActionMap`, `HostStringBuilder` from earlier tasks.
- Produces: fully working `B3270Session : IEmulatorSession`. Mapping rules:
  - `screen-mode` → `Resize(rows, columns, Blue, NeutralBlack)` and publish.
  - `erase` → `Erase(fg ?? NeutralWhite, bg ?? NeutralBlack)`; if logical rows/columns present and differ from the buffer → `Resize` first; publish.
  - `screen` → for each row (1-based) and change (column 1-based): text runs call `SetText(row-1, column-1, text, fg?, bg?, gr?)`; count runs call `SetAttributes(row-1, column-1, count, fg?, bg?, gr?)`; absent attribute strings mean unchanged (pass null). Then cursor if present: `Visible = enabled`, and row/column updated only when given (so `enabled:false` keeps the last position). Publish once per screen indication.
  - `oia` field `lock`: value null → `Unlocked`; `not-connected`; `syswait` → `WaitingForHost`; `twait` → `TerminalWait`; `deferred`; `minus` → `MinusFunction`; `oerr protected` → `ProtectedField`; `oerr numeric` → `NumericOnly`; `oerr overflow` → `Overflow`; `oerr dbcs` → `Dbcs`; starts with `scrolled` → `Scrolled`; `disabled`; `field` → `FieldWait`; `file-transfer` → `FileTransfer`; anything else → `Unknown` with `LockDetail` = value. Field `insert` → `InsertMode` (value `"true"`), `typeahead` → `Typeahead`, `lu` → `LuName` (null clears). Other fields ignored. Raise `StatusChanged` when the record changes.
  - `connection` state → `ConnectionState` by name: `not-connected`→Disconnected, `reconnecting`, `resolving`, `tcp-pending`, `tls-pending`, `tls-password-pending`, `proxy-pending`, `telnet-pending`, `connected-nvt`, `connected-nvt-charmode`, `connected-3270`, `connected-unbound`, `connected-e-nvt`, `connected-sscp`, `connected-tn3270e`. Unknown → Disconnected. When it becomes Disconnected, `Tls` resets to null.
  - `tls` → `Tls = new TlsInfo(secure, verified, session, hostCert)`.
  - `popup` → `HostMessage(text)` (all types).
  - `ConnectAsync`: start process if needed; `Set(verifyHostCert, true|false)`; `Connect(hostString)` via `RunRawAsync`; failure → `ConnectionFailedException(result.Text)`.
  - `DisconnectAsync`: `Disconnect` if the process is running, else no-op.
  - `SendKeyAsync` → `ActionMap.ForKey`; `TypeTextAsync(text)` → `String(text with backslashes doubled)` because x3270's String interprets backslash escapes; `PasteTextAsync(text)` → `PasteString(text)`; `MoveCursorAsync(r,c)` → `MoveCursor(r, c)` (zero-origin action).

- [ ] **Step 1: Write the state tests**

`tests/LizTerm.Backend.B3270.Tests/B3270SessionStateTests.cs`:

```csharp
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionStateTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23, UseTls = true, VerifyCertificate = false };

    private static async Task<(B3270Session Session, FakeB3270Process Fake)> StartAsync()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        return (session, fake);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Screen_mode_resizes_and_publishes()
    {
        var (session, fake) = await StartAsync();
        ScreenSnapshot? published = null;
        session.ScreenUpdated += (_, s) => published = s;
        fake.Emit("""{"screen-mode":{"model":4,"rows":43,"columns":80,"color":true,"oversize":false,"extended":true}}""");
        await WaitUntilAsync(() => session.CurrentScreen.Rows == 43, "resize");
        Assert.NotNull(published);
        Assert.Equal(43, published!.Rows);
    }

    [Fact]
    public async Task Screen_changes_apply_text_attributes_and_cursor()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"cursor":{"enabled":true,"row":2,"column":9},"rows":[{"row":1,"changes":[{"column":2,"fg":"red","count":1},{"column":3,"fg":"neutralBlack","bg":"red","text":"___"},{"column":6,"fg":"neutralBlack","bg":"red","count":2}]},{"row":2,"changes":[{"column":2,"text":"Field:"},{"column":8,"fg":"red","gr":"underline","count":3}]}]}}""");
        await WaitUntilAsync(() => session.CurrentScreen.GetText(1, 1, 6) == "Field:", "screen text");
        var s = session.CurrentScreen;
        Assert.Equal(new CursorPosition(1, 8, true), s.Cursor);
        Assert.Equal(HostColor.Red, s[0, 1].Foreground);
        Assert.Equal(new System.Text.Rune(' '), s[0, 1].Character);
        Assert.Equal("___", s.GetText(0, 2, 3));
        Assert.Equal(HostColor.Red, s[0, 3].Background);
        Assert.Equal(HostColor.NeutralBlack, s[0, 3].Foreground);
        Assert.Equal(HostColor.Red, s[0, 6].Background);
        Assert.Equal(HostColor.Blue, s[1, 1].Foreground);
        Assert.Equal(CellRendition.Underline, s[1, 8].Rendition);
        Assert.Equal(HostColor.Red, s[1, 9].Foreground);
        Assert.Equal(CellRendition.None, s[1, 10].Rendition);
    }

    [Fact]
    public async Task Cursor_only_screen_hides_cursor()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"cursor":{"enabled":true,"row":1,"column":1}}}""");
        await WaitUntilAsync(() => session.CurrentScreen.Cursor.Visible, "cursor on");
        fake.Emit("""{"screen":{"cursor":{"enabled":false}}}""");
        await WaitUntilAsync(() => !session.CurrentScreen.Cursor.Visible, "cursor off");
        Assert.Equal(new CursorPosition(0, 0, false), session.CurrentScreen.Cursor);
    }

    [Fact]
    public async Task Erase_blanks_with_given_colors()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"rows":[{"row":1,"changes":[{"column":1,"text":"abc"}]}]}}""");
        await WaitUntilAsync(() => session.CurrentScreen.GetText(0, 0, 3) == "abc", "text");
        fake.Emit("""{"erase":{"logical-rows":24,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}}""");
        await WaitUntilAsync(() => session.CurrentScreen.GetText(0, 0, 3) == "   ", "erase");
        Assert.Equal(HostColor.Blue, session.CurrentScreen[0, 0].Foreground);
    }

    [Theory]
    [InlineData("""{"oia":{"field":"lock","value":"syswait"}}""", KeyboardLock.WaitingForHost)]
    [InlineData("""{"oia":{"field":"lock","value":"oerr protected"}}""", KeyboardLock.ProtectedField)]
    [InlineData("""{"oia":{"field":"lock","value":"oerr numeric"}}""", KeyboardLock.NumericOnly)]
    [InlineData("""{"oia":{"field":"lock","value":"scrolled 3"}}""", KeyboardLock.Scrolled)]
    [InlineData("""{"oia":{"field":"lock","value":"twait"}}""", KeyboardLock.TerminalWait)]
    [InlineData("""{"oia":{"field":"lock","value":"field"}}""", KeyboardLock.FieldWait)]
    [InlineData("""{"oia":{"field":"lock","value":"something-new"}}""", KeyboardLock.Unknown)]
    [InlineData("""{"oia":{"field":"lock"}}""", KeyboardLock.Unlocked)]
    public async Task Oia_lock_maps_to_keyboard_lock(string line, KeyboardLock expected)
    {
        var (session, fake) = await StartAsync();
        KeyboardStatus? status = null;
        session.StatusChanged += (_, s) => status = s;
        fake.Emit(line);
        await WaitUntilAsync(() => session.KeyboardStatus.Lock == expected, "lock " + expected);
        Assert.NotNull(status);
    }

    [Fact]
    public async Task Oia_insert_typeahead_and_lu()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"oia":{"field":"insert","value":true}}""");
        fake.Emit("""{"oia":{"field":"typeahead","value":true}}""");
        fake.Emit("""{"oia":{"field":"lu","value":"IBM0TEQO"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.LuName == "IBM0TEQO", "lu");
        Assert.True(session.KeyboardStatus.InsertMode);
        Assert.True(session.KeyboardStatus.Typeahead);
        fake.Emit("""{"oia":{"field":"lu"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.LuName is null, "lu cleared");
    }

    [Fact]
    public async Task Connection_and_tls_map_and_reset()
    {
        var (session, fake) = await StartAsync();
        var states = new List<ConnectionState>();
        session.ConnectionChanged += (_, s) => states.Add(s);
        fake.Emit("""{"connection":{"state":"tcp-pending","host":"h","cause":"ui"}}""");
        fake.Emit("""{"connection":{"state":"tls-pending","host":"h","cause":"ui"}}""");
        fake.Emit("""{"tls":{"secure":true,"verified":false,"session":"Version: TLSv1.3","host-cert":"CN = h"}}""");
        fake.Emit("""{"connection":{"state":"connected-tn3270e","host":"h","cause":"ui"}}""");
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.ConnectedTn3270E, "connected");
        Assert.Equal([ConnectionState.TcpPending, ConnectionState.TlsPending, ConnectionState.ConnectedTn3270E], states);
        Assert.True(session.Tls!.Secure);
        Assert.False(session.Tls.Verified);
        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "disconnected");
        Assert.Null(session.Tls);
    }

    [Fact]
    public async Task Popup_raises_host_message()
    {
        var (session, fake) = await StartAsync();
        string? message = null;
        session.HostMessage += (_, m) => message = m;
        fake.Emit("""{"popup":{"type":"connection-error","text":"Host unreachable","retrying":false}}""");
        await WaitUntilAsync(() => message == "Host unreachable", "popup");
    }

    [Fact]
    public async Task Connect_sets_verify_then_connects_with_host_string()
    {
        var (session, fake) = await StartAsync();
        await session.ConnectAsync();
        var lines = fake.InputLines;
        Assert.Contains(lines, l => l.Contains("\"Set\"") && l.Contains("\"verifyHostCert\",\"false\""));
        Assert.Contains(lines, l => l.Contains("\"Connect\"") && l.Contains("\"L:h:23\""));
    }

    [Fact]
    public async Task Connect_failure_throws_connection_failed()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            var tag = System.Text.RegularExpressions.Regex.Match(line, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
            return line.Contains("\"Connect\"")
                ? [$$"""{"run-result":{"r-tag":"{{tag}}","success":false,"text":["Connection failed:","h/23:","Connection refused"],"text-err":[true,true,true],"time":0.01}}"""]
                : [$$"""{"run-result":{"r-tag":"{{tag}}","success":true,"time":0}}"""];
        };
        var session = new B3270Session(Profile, () => fake);
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync());
        Assert.Equal(["Connection failed:", "h/23:", "Connection refused"], ex.Lines);
    }

    [Fact]
    public async Task Actions_map_to_expected_lines()
    {
        var (session, fake) = await StartAsync();
        await session.SendKeyAsync(TerminalKey.PF3);
        await session.TypeTextAsync(@"a\b");
        await session.PasteTextAsync("line1\nline2");
        await session.MoveCursorAsync(4, 10);
        await session.DisconnectAsync();
        var lines = fake.InputLines;
        Assert.Contains(lines, l => l.Contains("""{"action":"PF","args":["3"]}"""));
        Assert.Contains(lines, l => l.Contains("""{"action":"String","args":["a\\\\b"]}"""));
        Assert.Contains(lines, l => l.Contains("""{"action":"PasteString","args":["line1\nline2"]}"""));
        Assert.Contains(lines, l => l.Contains("""{"action":"MoveCursor","args":["4","10"]}"""));
        Assert.Contains(lines, l => l.Contains("""{"action":"Disconnect"}"""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: failures (NotImplementedException, state not updated).

- [ ] **Step 3: Implement the state handling and actions**

Replace the placeholder `HandleStateIndication` and the six `NotImplementedException` methods in `src/LizTerm.Backend.B3270/B3270Session.cs` with:

```csharp
    private static readonly Dictionary<string, ConnectionState> ConnectionStates = new(StringComparer.Ordinal)
    {
        ["not-connected"] = ConnectionState.Disconnected,
        ["reconnecting"] = ConnectionState.Reconnecting,
        ["resolving"] = ConnectionState.Resolving,
        ["tcp-pending"] = ConnectionState.TcpPending,
        ["tls-pending"] = ConnectionState.TlsPending,
        ["tls-password-pending"] = ConnectionState.TlsPasswordPending,
        ["proxy-pending"] = ConnectionState.ProxyPending,
        ["telnet-pending"] = ConnectionState.TelnetPending,
        ["connected-nvt"] = ConnectionState.ConnectedNvt,
        ["connected-nvt-charmode"] = ConnectionState.ConnectedNvtCharMode,
        ["connected-3270"] = ConnectionState.Connected3270,
        ["connected-unbound"] = ConnectionState.ConnectedUnbound,
        ["connected-e-nvt"] = ConnectionState.ConnectedENvt,
        ["connected-sscp"] = ConnectionState.ConnectedSscp,
        ["connected-tn3270e"] = ConnectionState.ConnectedTn3270E,
    };

    private void HandleStateIndication(Indication indication)
    {
        switch (indication)
        {
            case ScreenModeIndication mode:
                _buffer.Resize(mode.Rows, mode.Columns, HostColor.Blue, HostColor.NeutralBlack);
                Publish();
                break;
            case EraseIndication erase:
                if (erase.LogicalRows is { } rows && erase.LogicalColumns is { } cols && (rows != _buffer.Rows || cols != _buffer.Columns))
                    _buffer.Resize(rows, cols, HostColor.NeutralWhite, HostColor.NeutralBlack);
                _buffer.Erase(
                    erase.Fg is null ? HostColor.NeutralWhite : ColorNames.ParseColor(erase.Fg),
                    erase.Bg is null ? HostColor.NeutralBlack : ColorNames.ParseColor(erase.Bg));
                Publish();
                break;
            case ScreenIndication screen:
                ApplyScreen(screen);
                Publish();
                break;
            case OiaIndication oia:
                ApplyOia(oia);
                break;
            case ConnectionIndication connection:
                var state = ConnectionStates.GetValueOrDefault(connection.State, ConnectionState.Disconnected);
                if (state == ConnectionState.Disconnected) Tls = null;
                SetConnectionState(state);
                break;
            case TlsIndication tls:
                Tls = new TlsInfo(tls.Secure, tls.Verified, tls.Session, tls.HostCert);
                break;
            case PopupIndication popup:
                HostMessage?.Invoke(this, popup.Text);
                break;
        }
    }

    private void ApplyScreen(ScreenIndication screen)
    {
        if (screen.Rows is not null)
        {
            foreach (var row in screen.Rows)
            {
                var r = row.Row - 1;
                if (r < 0 || r >= _buffer.Rows) continue;
                foreach (var change in row.Changes)
                {
                    var c = change.Column - 1;
                    if (c < 0 || c >= _buffer.Columns) continue;
                    HostColor? fg = change.Fg is null ? null : ColorNames.ParseColor(change.Fg);
                    HostColor? bg = change.Bg is null ? null : ColorNames.ParseColor(change.Bg);
                    CellRendition? gr = change.Gr is null ? null : ColorNames.ParseRendition(change.Gr);
                    if (change.Text is not null)
                        _buffer.SetText(r, c, change.Text, fg, bg, gr);
                    else if (change.Count is { } count)
                        _buffer.SetAttributes(r, c, count, fg, bg, gr);
                }
            }
        }
        if (screen.Cursor is { } cursor)
        {
            // enabled:false hides the cursor but keeps its last position; enabled:true without a position re-shows it.
            var current = _buffer.Cursor;
            var row = cursor.Row is { } cr ? cr - 1 : current.Row;
            var column = cursor.Column is { } cc ? cc - 1 : current.Column;
            _buffer.SetCursor(new CursorPosition(row, column, cursor.Enabled));
        }
    }

    private void ApplyOia(OiaIndication oia)
    {
        var status = KeyboardStatus;
        switch (oia.Field)
        {
            case "lock":
                var (lockState, detail) = MapLock(oia.Value);
                status = status with { Lock = lockState, LockDetail = detail };
                break;
            case "insert":
                status = status with { InsertMode = oia.Value == "true" };
                break;
            case "typeahead":
                status = status with { Typeahead = oia.Value == "true" };
                break;
            case "lu":
                status = status with { LuName = oia.Value };
                break;
            default:
                return;
        }
        if (status == KeyboardStatus) return;
        KeyboardStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    private static (KeyboardLock Lock, string? Detail) MapLock(string? value)
    {
        if (value is null) return (KeyboardLock.Unlocked, null);
        if (value.StartsWith("scrolled", StringComparison.Ordinal)) return (KeyboardLock.Scrolled, value);
        return value switch
        {
            "not-connected" => (KeyboardLock.NotConnected, null),
            "syswait" => (KeyboardLock.WaitingForHost, null),
            "twait" => (KeyboardLock.TerminalWait, null),
            "deferred" => (KeyboardLock.Deferred, null),
            "minus" => (KeyboardLock.MinusFunction, null),
            "oerr protected" => (KeyboardLock.ProtectedField, null),
            "oerr numeric" => (KeyboardLock.NumericOnly, null),
            "oerr overflow" => (KeyboardLock.Overflow, null),
            "oerr dbcs" => (KeyboardLock.Dbcs, null),
            "disabled" => (KeyboardLock.Disabled, null),
            "field" => (KeyboardLock.FieldWait, null),
            "file-transfer" => (KeyboardLock.FileTransfer, null),
            _ => (KeyboardLock.Unknown, value),
        };
    }

    private void Publish()
    {
        CurrentScreen = _buffer.Snapshot();
        ScreenUpdated?.Invoke(this, CurrentScreen);
    }

    // ---- IEmulatorSession actions ----

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await StartProcessAsync(cancellationToken);
        await RunAsync(new B3270Action("Set", "verifyHostCert", Profile.VerifyCertificate ? "true" : "false"));
        var result = await RunRawAsync([new B3270Action("Connect", HostStringBuilder.Build(Profile))]);
        if (!result.Success) throw new ConnectionFailedException(result.Text);
    }

    public Task DisconnectAsync() =>
        _process is null ? Task.CompletedTask : RunRawAsync([new B3270Action("Disconnect")]);

    public Task SendKeyAsync(TerminalKey key) => RunAsync(ActionMap.ForKey(key));

    /// <summary>x3270's String() interprets backslash escapes, so literal backslashes are doubled.</summary>
    public Task TypeTextAsync(string text) => RunAsync(new B3270Action("String", text.Replace("\\", "\\\\")));

    public Task PasteTextAsync(string text) => RunAsync(new B3270Action("PasteString", text));

    public Task MoveCursorAsync(int row, int column) =>
        RunAsync(new B3270Action("MoveCursor", row.ToString(), column.ToString()));
```

- [ ] **Step 4: Run the state tests**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: all pass.

- [ ] **Step 5: Write the playback build script and fixture recorder**

`native/build/build-playback.sh` (developer tool; builds x3270's `playback` from the pinned source):

```bash
#!/usr/bin/env bash
# Builds x3270's playback tool (replays .trc host traces over a socket) into native/build-tmp/playback/.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
BUILD="$ROOT/native/build-tmp/playback"
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"
./configure --enable-playback --disable-x3270 --disable-c3270 --disable-s3270 --disable-b3270 \
  --disable-tcl3270 --disable-pr3287 --disable-x3270if --disable-mitm > "$BUILD/configure.log" 2>&1
make -j4 > "$BUILD/make.log" 2>&1
BIN=$(find obj -type f -name playback -perm +111 | head -1)
cp "$BIN" "$BUILD/playback"
echo "$BUILD/playback"
echo "Traces are under $SRC/*/Test/*.trc"
```

`tools/record-fixture.sh`:

```bash
#!/usr/bin/env bash
# Replays an x3270 .trc host trace through b3270 and saves b3270's JSON output as a replay fixture.
# usage: tools/record-fixture.sh <trace.trc> <out.jsonl> [model, default 3279-2-E]
set -euo pipefail
TRACE=${1:?trace file}
OUT=${2:?output jsonl}
MODEL=${3:-3279-2-E}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
PLAYBACK="$ROOT/native/build-tmp/playback/playback"
[ -x "$PLAYBACK" ] || "$ROOT/native/build/build-playback.sh" > /dev/null
B3270=${LIZTERM_B3270_PATH:-}
[ -n "$B3270" ] || B3270=$(ls "$ROOT"/native/out/*/b3270 2>/dev/null | head -1)
[ -n "$B3270" ] || B3270=$(command -v b3270)
[ -n "$B3270" ] || { echo "no b3270 found; build it or set LIZTERM_B3270_PATH" >&2; exit 1; }
PORT=$((20000 + RANDOM % 10000))
( (sleep 3; echo e; sleep 6; echo q) | "$PLAYBACK" -w -p "$PORT" "$TRACE" > /dev/null 2>&1 & )
sleep 0.5
( printf '%s\n' "{\"run\":{\"r-tag\":\"connect\",\"actions\":[{\"action\":\"Open\",\"args\":[\"127.0.0.1:$PORT\"]}]}}"
  sleep 10
  printf '%s\n' '{"run":{"r-tag":"quit","actions":[{"action":"Quit"}]}}' ) \
  | "$B3270" -json -utf8 -model "$MODEL" > "$OUT"
echo "Recorded $(wc -l < "$OUT") lines to $OUT"
```

- [ ] **Step 6: Record the fixture**

```bash
chmod +x native/build/build-playback.sh tools/record-fixture.sh && tools/record-fixture.sh native/build-tmp/playback/src/suite3270-4.5/s3270/Test/ibmlink_help.trc tests/LizTerm.Backend.B3270.Tests/Fixtures/ibmlink-help.jsonl
```

(The first run builds playback, which takes about a minute; if `native/build-tmp/playback/src` does not exist yet, run `native/build/build-playback.sh` first and then the recorder.) Expected: `Recorded 45 lines to ...` (count may vary by a few). Check the content:

```bash
grep -c '"screen":{' tests/LizTerm.Backend.B3270.Tests/Fixtures/ibmlink-help.jsonl && grep -o 'SVM0201P' tests/LizTerm.Backend.B3270.Tests/Fixtures/ibmlink-help.jsonl | head -1
```

Expected: a positive count and `SVM0201P`.

Add `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`:

```markdown
# Replay fixtures

Each `.jsonl` file is raw b3270 standard output (one JSON indication per line), recorded with
`tools/record-fixture.sh` by replaying an x3270 test trace through b3270 4.5ga6.
The traces come from the x3270 source distribution (BSD-3-Clause, Copyright Paul Mattes).

- `ibmlink-help.jsonl`: `s3270/Test/ibmlink_help.trc`, model 3279-2-E. A full 24x80 IBMLink welcome
  screen with highlighted/selectable regions; cursor ends at row 21, column 13 (1-based).
```

- [ ] **Step 7: Write the replay test**

`tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`:

```csharp
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class ReplayTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public async Task Ibmlink_help_screen_replays_to_expected_state()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("ibmlink-help.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var session = new B3270Session(new SessionProfile { Name = "replay", Host = "127.0.0.1" }, () => fake);
        var states = new List<ConnectionState>();
        var screens = 0;
        session.ConnectionChanged += (_, s) => states.Add(s);
        session.ScreenUpdated += (_, _) => screens++;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(CancellationToken.None);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var screen = session.CurrentScreen;
        Assert.Equal(24, screen.Rows);
        Assert.Equal(80, screen.Columns);
        Assert.Equal("SVM0201P", screen.GetText(0, 1, 8));
        Assert.Equal("SYSTEM: IBM0SM23", screen.GetText(1, 1, 16));
        // The recording ends with Quit, after which b3270 hides the cursor but its position is kept.
        Assert.Equal(new CursorPosition(20, 12, false), screen.Cursor);
        Assert.Equal(HostColor.NeutralWhite, screen[6, 0].Foreground);
        Assert.True(screen[6, 0].Rendition.HasFlag(CellRendition.Highlight));
        Assert.Contains(ConnectionState.Connected3270, states);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
        Assert.True(screens > 0);
    }
}
```

- [ ] **Step 8: Run all backend tests**

```bash
dotnet test tests/LizTerm.Backend.B3270.Tests
```

Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "Complete B3270Session state mapping with replay fixture

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: App scaffold, embedded font, cell geometry, and screen rendering

**Files:**
- Create: `src/LizTerm.App/LizTerm.App.csproj`, `app.manifest`, `Program.cs`, `App.axaml`, `App.axaml.cs`
- Create: `src/LizTerm.App/Assets/Fonts/3270-Regular.otf`, `src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt`
- Create: `src/LizTerm.App/Rendering/CellGeometry.cs`, `src/LizTerm.App/Rendering/Palette.cs`, `src/LizTerm.App/Controls/TerminalScreen.cs`
- Create: `tests/LizTerm.App.Tests/LizTerm.App.Tests.csproj`, `tests/LizTerm.App.Tests/TestAppBuilder.cs`, `tests/LizTerm.App.Tests/Rendering/CellGeometryTests.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenLayoutTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct CellGeometry(double CellWidth, double CellHeight, double FontSize, double OriginX, double OriginY)` with `static CellGeometry Fit(double availableWidth, double availableHeight, int rows, int columns, double advancePerEm, double lineHeightPerEm)`, `(int Row, int Column)? HitTest(double x, double y, int rows, int columns)`, `Rect CellRect(int row, int column)`
  - `static class Palette`: `IBrush Background`, `IBrush Brush(HostColor color, bool bright)`, `Color ColorOf(HostColor color)`
  - `sealed class TerminalScreen : Control` with `StyledProperty<ScreenSnapshot?> SnapshotProperty`, `ScreenSnapshot? Snapshot`, `static FontFamily TerminalFont`, `internal CellGeometry LastGeometry`. Input events are added in Task 12.

- [ ] **Step 1: Create the App project**

`src/LizTerm.App/LizTerm.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="Avalonia.Diagnostics">
      <IncludeAssets Condition="'$(Configuration)' != 'Debug'">None</IncludeAssets>
      <PrivateAssets Condition="'$(Configuration)' != 'Debug'">All</PrivateAssets>
    </PackageReference>
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../LizTerm.Core/LizTerm.Core.csproj" />
    <ProjectReference Include="../LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj" />
  </ItemGroup>

  <!-- Bundle the locally built b3270 for the host runtime, when native/build/build-macos.sh has run. -->
  <ItemGroup Condition="Exists('../../native/out/$(NETCoreSdkRuntimeIdentifier)')">
    <None Include="../../native/out/$(NETCoreSdkRuntimeIdentifier)/b3270*"
          Link="runtimes/$(NETCoreSdkRuntimeIdentifier)/native/%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="LizTerm.App.Tests" />
  </ItemGroup>
</Project>
```

`src/LizTerm.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="0.1.0.0" name="LizTerm.App"/>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

`src/LizTerm.App/Program.cs`:

```csharp
using Avalonia;

namespace LizTerm.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

`src/LizTerm.App/App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LizTerm.App.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

`src/LizTerm.App/App.axaml.cs` (window management is added in Task 14):

```csharp
using Avalonia;
using Avalonia.Markup.Xaml;

namespace LizTerm.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
```

```bash
dotnet sln add src/LizTerm.App/LizTerm.App.csproj
```

- [ ] **Step 2: Add the font and its license**

The font is installed on the development Mac at `~/Library/Fonts/3270-Regular.otf` (version 3.0.1, family name `IBM 3270`). If it is not present, download the v3.0.1 release archive from `https://github.com/rbanffy/3270font/releases/tag/v3.0.1` and take `3270-Regular.otf` from it.

```bash
mkdir -p src/LizTerm.App/Assets/Fonts && cp ~/Library/Fonts/3270-Regular.otf src/LizTerm.App/Assets/Fonts/3270-Regular.otf && curl -fsSL -o src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt https://raw.githubusercontent.com/rbanffy/3270font/v3.0.1/LICENSE.txt && python3 -c "from fontTools.ttLib import TTFont; f=TTFont('src/LizTerm.App/Assets/Fonts/3270-Regular.otf'); print(f['name'].getDebugName(1), '|', f['name'].getDebugName(5))"
```

Expected: `IBM 3270 | Version 3.0.1` and a license file starting with the SIL Open Font License text. (If `fontTools` is missing, `pip3 install fonttools`, or skip the check.)

- [ ] **Step 3: Create the App test project**

`tests/LizTerm.App.Tests/LizTerm.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Avalonia.Headless.XUnit" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/LizTerm.App/LizTerm.App.csproj" />
  </ItemGroup>
</Project>
```

`tests/LizTerm.App.Tests/TestAppBuilder.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using LizTerm.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LizTerm.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LizTerm.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
```

```bash
dotnet sln add tests/LizTerm.App.Tests/LizTerm.App.Tests.csproj
```

- [ ] **Step 4: Write the failing tests**

`tests/LizTerm.App.Tests/Rendering/CellGeometryTests.cs`:

```csharp
using LizTerm.App.Rendering;

namespace LizTerm.App.Tests.Rendering;

public class CellGeometryTests
{
    // A hypothetical font: advance 0.6 em, line height 1.2 em.
    private const double Advance = 0.6, Line = 1.2;

    [Fact]
    public void Fit_is_limited_by_width_when_window_is_wide_and_short()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        Assert.Equal(16, g.FontSize);                 // floor(min(800/48, 600/28.8)) = floor(min(16.67, 20.83))
        Assert.Equal(9.6, g.CellWidth, 6);
        Assert.Equal(19.2, g.CellHeight, 6);
        Assert.Equal(16, g.OriginX, 6);               // (800 - 768) / 2
        Assert.Equal(69.6, g.OriginY, 6);             // (600 - 460.8) / 2
    }

    [Fact]
    public void Fit_is_limited_by_height_when_window_is_tall()
    {
        var g = CellGeometry.Fit(400, 1000, 24, 80, Advance, Line);
        Assert.Equal(8, g.FontSize);                  // floor(min(400/48, 1000/28.8)) = floor(8.33)
    }

    [Fact]
    public void Fit_never_goes_below_one_point()
    {
        var g = CellGeometry.Fit(10, 10, 24, 80, Advance, Line);
        Assert.Equal(1, g.FontSize);
    }

    [Fact]
    public void Fit_with_invalid_input_is_default()
    {
        Assert.Equal(default, CellGeometry.Fit(800, 600, 0, 80, Advance, Line));
        Assert.Equal(default, CellGeometry.Fit(800, 600, 24, 80, 0, Line));
    }

    [Fact]
    public void HitTest_maps_points_to_cells_and_rejects_margins()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        Assert.Equal((3, 5), g.HitTest(16 + 9.6 * 5 + 1, 69.6 + 19.2 * 3 + 1, 24, 80));
        Assert.Equal((0, 0), g.HitTest(16.5, 70, 24, 80));
        Assert.Null(g.HitTest(2, 300, 24, 80));       // left margin
        Assert.Null(g.HitTest(400, 5, 24, 80));       // top margin
        Assert.Null(g.HitTest(799, 599, 24, 80));     // bottom-right margin
    }

    [Fact]
    public void CellRect_places_cells_on_the_grid()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        var rect = g.CellRect(2, 10);
        Assert.Equal(16 + 96, rect.X, 6);
        Assert.Equal(69.6 + 38.4, rect.Y, 6);
        Assert.Equal(9.6, rect.Width, 6);
    }
}
```

`tests/LizTerm.App.Tests/Controls/TerminalScreenLayoutTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenLayoutTests
{
    [AvaloniaFact]
    public void Geometry_fits_the_grid_inside_the_window()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();

        var g = screen.LastGeometry;
        Assert.True(g.FontSize > 0);
        Assert.True(g.CellWidth * 80 <= 800.01, $"width {g.CellWidth * 80}");
        Assert.True(g.CellHeight * 24 <= 600.01, $"height {g.CellHeight * 24}");
    }

    [AvaloniaFact]
    public void Changing_snapshot_size_recomputes_geometry()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        var before = screen.LastGeometry;
        screen.Snapshot = ScreenSnapshot.Empty(43, 80);
        window.UpdateLayout();
        Assert.True(screen.LastGeometry.CellHeight * 43 <= 600.01);
        Assert.NotEqual(before, screen.LastGeometry);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: build errors.

- [ ] **Step 6: Implement geometry, palette, and the control**

`src/LizTerm.App/Rendering/CellGeometry.cs`:

```csharp
using Avalonia;

namespace LizTerm.App.Rendering;

/// <summary>Scale-to-fit layout of a rows x columns character grid inside an area. Pure math; no Avalonia rendering.</summary>
public readonly record struct CellGeometry(double CellWidth, double CellHeight, double FontSize, double OriginX, double OriginY)
{
    /// <param name="advancePerEm">Glyph advance width divided by font size, measured once from the font.</param>
    /// <param name="lineHeightPerEm">Line height divided by font size.</param>
    public static CellGeometry Fit(double availableWidth, double availableHeight, int rows, int columns, double advancePerEm, double lineHeightPerEm)
    {
        if (rows <= 0 || columns <= 0 || advancePerEm <= 0 || lineHeightPerEm <= 0) return default;
        var byWidth = availableWidth / (columns * advancePerEm);
        var byHeight = availableHeight / (rows * lineHeightPerEm);
        var fontSize = Math.Max(1, Math.Floor(Math.Min(byWidth, byHeight)));
        var cellWidth = fontSize * advancePerEm;
        var cellHeight = fontSize * lineHeightPerEm;
        var originX = Math.Max(0, (availableWidth - cellWidth * columns) / 2);
        var originY = Math.Max(0, (availableHeight - cellHeight * rows) / 2);
        return new CellGeometry(cellWidth, cellHeight, fontSize, originX, originY);
    }

    public (int Row, int Column)? HitTest(double x, double y, int rows, int columns)
    {
        if (CellWidth <= 0 || CellHeight <= 0) return null;
        var column = (int)Math.Floor((x - OriginX) / CellWidth);
        var row = (int)Math.Floor((y - OriginY) / CellHeight);
        if (row < 0 || row >= rows || column < 0 || column >= columns) return null;
        return (row, column);
    }

    public Rect CellRect(int row, int column) =>
        new(OriginX + column * CellWidth, OriginY + row * CellHeight, CellWidth, CellHeight);
}
```

`src/LizTerm.App/Rendering/Palette.cs`:

```csharp
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Fixed v1 color scheme following IBM host color names. Theming is deferred.</summary>
public static class Palette
{
    public static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 0));

    private static readonly Dictionary<HostColor, Color> Colors = new()
    {
        [HostColor.Default] = Color.FromRgb(0xF0, 0xF0, 0xF0),
        [HostColor.NeutralBlack] = Color.FromRgb(0x00, 0x00, 0x00),
        [HostColor.Blue] = Color.FromRgb(0x5C, 0x8A, 0xFF),
        [HostColor.Red] = Color.FromRgb(0xFF, 0x50, 0x50),
        [HostColor.Pink] = Color.FromRgb(0xFF, 0x70, 0xFF),
        [HostColor.Green] = Color.FromRgb(0x50, 0xFF, 0x50),
        [HostColor.Turquoise] = Color.FromRgb(0x50, 0xFF, 0xFF),
        [HostColor.Yellow] = Color.FromRgb(0xFF, 0xFF, 0x50),
        [HostColor.NeutralWhite] = Color.FromRgb(0xF0, 0xF0, 0xF0),
        [HostColor.Black] = Color.FromRgb(0x00, 0x00, 0x00),
        [HostColor.DeepBlue] = Color.FromRgb(0x20, 0x20, 0xB0),
        [HostColor.Orange] = Color.FromRgb(0xFF, 0xA0, 0x40),
        [HostColor.Purple] = Color.FromRgb(0xB0, 0x60, 0xFF),
        [HostColor.PaleGreen] = Color.FromRgb(0xA0, 0xFF, 0xA0),
        [HostColor.PaleTurquoise] = Color.FromRgb(0xA0, 0xFF, 0xFF),
        [HostColor.Grey] = Color.FromRgb(0xA0, 0xA0, 0xA0),
        [HostColor.White] = Color.FromRgb(0xFF, 0xFF, 0xFF),
    };

    private static readonly Dictionary<(HostColor, bool), IBrush> Cache = new();

    public static Color ColorOf(HostColor color) => Colors[color];

    /// <param name="bright">Intensified (highlight) rendition: blend 35% toward white.</param>
    public static IBrush Brush(HostColor color, bool bright)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((color, bright), out var brush)) return brush;
            var c = ColorOf(color);
            if (bright)
                c = Color.FromRgb(Blend(c.R), Blend(c.G), Blend(c.B));
            brush = new ImmutableSolidColorBrush(c);
            Cache[(color, bright)] = brush;
            return brush;
        }
    }

    private static byte Blend(byte channel) => (byte)(channel + (255 - channel) * 0.35);
}
```

`src/LizTerm.App/Controls/TerminalScreen.cs`:

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Controls;

/// <summary>Draws a ScreenSnapshot scaled to fit, in the IBM 3270 font. Rows are drawn as runs of equal style.</summary>
public sealed class TerminalScreen : Control
{
    public static readonly StyledProperty<ScreenSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenSnapshot?>(nameof(Snapshot));

    public static readonly FontFamily TerminalFont = FontFamily.Parse("avares://LizTerm.App/Assets/Fonts#IBM 3270");

    private readonly Typeface _typeface = new(TerminalFont);
    private double _advancePerEm;
    private double _lineHeightPerEm;

    static TerminalScreen()
    {
        AffectsRender<TerminalScreen>(SnapshotProperty);
        AffectsArrange<TerminalScreen>(SnapshotProperty);
        FocusableProperty.OverrideDefaultValue<TerminalScreen>(true);
    }

    public ScreenSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    internal CellGeometry LastGeometry { get; private set; }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateGeometry(finalSize);
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Palette.Background, bounds);
        var snapshot = Snapshot;
        if (snapshot is null) return;

        UpdateGeometry(bounds.Size);
        var g = LastGeometry;
        if (g.FontSize <= 0) return;

        for (var row = 0; row < snapshot.Rows; row++)
        {
            var cells = snapshot.Row(row);
            var col = 0;
            while (col < cells.Length)
            {
                var start = col;
                var style = cells[col];
                while (col < cells.Length && SameStyle(cells[col], style)) col++;
                DrawRun(context, snapshot, row, start, col - start, style, g);
            }
        }
        DrawCursor(context, snapshot, g);
    }

    private void UpdateGeometry(Size size)
    {
        var snapshot = Snapshot;
        if (snapshot is null) { LastGeometry = default; return; }
        EnsureMetrics();
        LastGeometry = CellGeometry.Fit(size.Width, size.Height, snapshot.Rows, snapshot.Columns, _advancePerEm, _lineHeightPerEm);
    }

    private void EnsureMetrics()
    {
        if (_advancePerEm > 0) return;
        const double probeSize = 100;
        var probe = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, probeSize, Brushes.White);
        _advancePerEm = probe.Width > 0 ? probe.Width / probeSize : 0.6;
        _lineHeightPerEm = probe.Height > 0 ? probe.Height / probeSize : 1.2;
    }

    private static bool SameStyle(in Cell a, in Cell b) =>
        a.Foreground == b.Foreground && a.Background == b.Background && a.Rendition == b.Rendition;

    private void DrawRun(DrawingContext context, ScreenSnapshot snapshot, int row, int start, int length, Cell style, CellGeometry g)
    {
        var rect = new Rect(g.OriginX + start * g.CellWidth, g.OriginY + row * g.CellHeight, length * g.CellWidth, g.CellHeight);
        var reverse = style.Rendition.HasFlag(CellRendition.Reverse);
        var fg = ResolveForeground(reverse ? style.Background : style.Foreground);
        var bg = ResolveBackground(reverse ? style.Foreground : style.Background);

        if (bg != HostColor.NeutralBlack)
            context.FillRectangle(Palette.Brush(bg, false), rect);

        var text = snapshot.GetText(row, start, length);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var bright = style.Rendition.HasFlag(CellRendition.Highlight);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, g.FontSize, Palette.Brush(fg, bright));
            context.DrawText(formatted, rect.TopLeft);
        }

        if (style.Rendition.HasFlag(CellRendition.Underline))
        {
            var y = Math.Round(rect.Bottom) - 1.5;
            context.DrawLine(new Pen(Palette.Brush(fg, false)), new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private void DrawCursor(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        var cursor = snapshot.Cursor;
        if (!cursor.Visible || cursor.Row >= snapshot.Rows || cursor.Column >= snapshot.Columns) return;
        var cell = snapshot[cursor.Row, cursor.Column];
        var rect = g.CellRect(cursor.Row, cursor.Column);
        var fg = ResolveForeground(cell.Foreground);
        context.FillRectangle(Palette.Brush(fg, false), rect);
        var ch = cell.Character.ToString();
        if (!string.IsNullOrWhiteSpace(ch))
        {
            var formatted = new FormattedText(ch, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, g.FontSize, Palette.Brush(HostColor.NeutralBlack, false));
            context.DrawText(formatted, rect.TopLeft);
        }
    }

    private static HostColor ResolveForeground(HostColor color) => color == HostColor.Default ? HostColor.NeutralWhite : color;
    private static HostColor ResolveBackground(HostColor color) => color == HostColor.Default ? HostColor.NeutralBlack : color;
}
```

- [ ] **Step 7: Run tests**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: all pass. If `AffectsArrange` does not exist on `Control` in this Avalonia version, replace it with an override of `OnPropertyChanged` that calls `InvalidateArrange()` when `change.Property == SnapshotProperty`.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "Add Avalonia app scaffold, 3270 font, and terminal screen rendering

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 12: Keyboard and mouse input on the screen control

**Files:**
- Create: `src/LizTerm.App/Keyboard/DefaultKeymap.cs`
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs` (add events and input overrides)
- Test: `tests/LizTerm.App.Tests/Keyboard/DefaultKeymapTests.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs`

**Interfaces:**
- Produces:
  - `static bool DefaultKeymap.TryMap(Key key, KeyModifiers modifiers, out TerminalKey terminalKey)`
  - On `TerminalScreen`: `event EventHandler<TerminalKey>? KeyRequested`, `event EventHandler<string>? TextEntered`, `event EventHandler<(int Row, int Column)>? CellClicked`

Default mapping (spec 6.5): Enter→Enter; F1..F12→PF1..PF12; Shift+F1..F12→PF13..PF24; Escape→Reset; Tab→Tab; Shift+Tab→BackTab; Insert→Insert; Home→Home; End→EraseEof; Delete→Delete; Backspace→Backspace; arrows; PageUp→PA1; PageDown→PA2. Anything with Control or Meta (Command) held is not mapped, leaving it for app shortcuts.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Keyboard/DefaultKeymapTests.cs`:

```csharp
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Keyboard;

public class DefaultKeymapTests
{
    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None, TerminalKey.Enter)]
    [InlineData(Key.F1, KeyModifiers.None, TerminalKey.PF1)]
    [InlineData(Key.F12, KeyModifiers.None, TerminalKey.PF12)]
    [InlineData(Key.F1, KeyModifiers.Shift, TerminalKey.PF13)]
    [InlineData(Key.F12, KeyModifiers.Shift, TerminalKey.PF24)]
    [InlineData(Key.Escape, KeyModifiers.None, TerminalKey.Reset)]
    [InlineData(Key.Tab, KeyModifiers.None, TerminalKey.Tab)]
    [InlineData(Key.Tab, KeyModifiers.Shift, TerminalKey.BackTab)]
    [InlineData(Key.Insert, KeyModifiers.None, TerminalKey.Insert)]
    [InlineData(Key.Home, KeyModifiers.None, TerminalKey.Home)]
    [InlineData(Key.End, KeyModifiers.None, TerminalKey.EraseEof)]
    [InlineData(Key.Delete, KeyModifiers.None, TerminalKey.Delete)]
    [InlineData(Key.Back, KeyModifiers.None, TerminalKey.Backspace)]
    [InlineData(Key.Up, KeyModifiers.None, TerminalKey.Up)]
    [InlineData(Key.Down, KeyModifiers.None, TerminalKey.Down)]
    [InlineData(Key.Left, KeyModifiers.None, TerminalKey.Left)]
    [InlineData(Key.Right, KeyModifiers.None, TerminalKey.Right)]
    [InlineData(Key.PageUp, KeyModifiers.None, TerminalKey.PA1)]
    [InlineData(Key.PageDown, KeyModifiers.None, TerminalKey.PA2)]
    public void Maps_default_keys(Key key, KeyModifiers modifiers, TerminalKey expected)
    {
        Assert.True(DefaultKeymap.TryMap(key, modifiers, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.C, KeyModifiers.Control)]
    [InlineData(Key.F1, KeyModifiers.Control)]
    [InlineData(Key.Enter, KeyModifiers.Meta)]
    [InlineData(Key.LeftShift, KeyModifiers.Shift)]
    public void Leaves_text_and_shortcut_keys_unmapped(Key key, KeyModifiers modifiers) =>
        Assert.False(DefaultKeymap.TryMap(key, modifiers, out _));
}
```

`tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenInputTests
{
    private static (Window Window, TerminalScreen Screen) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        screen.Focus();
        return (window, screen);
    }

    [AvaloniaFact]
    public void Function_keys_and_shift_tab_raise_KeyRequested()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal([TerminalKey.PF3, TerminalKey.BackTab, TerminalKey.Enter], keys);
    }

    [AvaloniaFact]
    public void Tab_stays_on_the_screen_control()
    {
        var (window, screen) = Show();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.True(screen.IsFocused);
    }

    [AvaloniaFact]
    public void Text_input_raises_TextEntered()
    {
        var (window, screen) = Show();
        var texts = new List<string>();
        screen.TextEntered += (_, t) => texts.Add(t);
        window.KeyTextInput("abc");
        Assert.Equal(["abc"], texts);
    }

    [AvaloniaFact]
    public void Click_raises_CellClicked_with_zero_based_cell()
    {
        var (window, screen) = Show();
        (int Row, int Column)? clicked = null;
        screen.CellClicked += (_, c) => clicked = c;
        var g = screen.LastGeometry;
        var rect = g.CellRect(5, 12);
        window.MouseDown(new Avalonia.Point(rect.Center.X, rect.Center.Y), MouseButton.Left);
        window.MouseUp(new Avalonia.Point(rect.Center.X, rect.Center.Y), MouseButton.Left);
        Assert.Equal((5, 12), clicked);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: build errors (`DefaultKeymap`, events missing).

- [ ] **Step 3: Implement the keymap**

`src/LizTerm.App/Keyboard/DefaultKeymap.cs`:

```csharp
using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The built-in physical-key to 3270-key mapping. Not user-editable in v1.</summary>
public static class DefaultKeymap
{
    public static bool TryMap(Key key, KeyModifiers modifiers, out TerminalKey terminalKey)
    {
        terminalKey = default;
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) return false;
        var shift = (modifiers & KeyModifiers.Shift) != 0;

        if (key >= Key.F1 && key <= Key.F12)
        {
            terminalKey = TerminalKey.PF1 + (key - Key.F1) + (shift ? 12 : 0);
            return true;
        }

        TerminalKey? mapped = key switch
        {
            Key.Enter => TerminalKey.Enter,
            Key.Escape => TerminalKey.Reset,
            Key.Tab => shift ? TerminalKey.BackTab : TerminalKey.Tab,
            Key.Insert => TerminalKey.Insert,
            Key.Home => TerminalKey.Home,
            Key.End => TerminalKey.EraseEof,
            Key.Delete => TerminalKey.Delete,
            Key.Back => TerminalKey.Backspace,
            Key.Up => TerminalKey.Up,
            Key.Down => TerminalKey.Down,
            Key.Left => TerminalKey.Left,
            Key.Right => TerminalKey.Right,
            Key.PageUp => TerminalKey.PA1,
            Key.PageDown => TerminalKey.PA2,
            _ => null,
        };
        if (mapped is null) return false;
        terminalKey = mapped.Value;
        return true;
    }
}
```

- [ ] **Step 4: Add input handling to the control**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, add these usings at the top:

```csharp
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
```

Add inside the class, after the `Snapshot` property:

```csharp
    public event EventHandler<TerminalKey>? KeyRequested;
    public event EventHandler<string>? TextEntered;
    public event EventHandler<(int Row, int Column)>? CellClicked;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DefaultKeymap.TryMap(e.Key, e.KeyModifiers, out var key))
        {
            KeyRequested?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text))
        {
            TextEntered?.Invoke(this, e.Text);
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.HitTest(position.X, position.Y, snapshot.Rows, snapshot.Columns) is { } cell)
        {
            CellClicked?.Invoke(this, cell);
            e.Handled = true;
        }
    }
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: all pass. If `Tab_stays_on_the_screen_control` fails because focus moved, set `KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None)` in the control's constructor and re-run.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add default keymap and terminal screen input events

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: Status formatting and the session view model

**Files:**
- Create: `src/LizTerm.App/Status/StatusFormatter.cs`, `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Create: `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`
- Test: `tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs`, `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs`

**Interfaces:**
- Produces:
  - `static class StatusFormatter` with constants `PadlockGlyph = "\uE0A2"`, `ReadyGlyph = "✓"`, `BlockedGlyph = "✕"` and methods `string Connection(ConnectionState state, string host)`, `string Tls(TlsInfo? tls)`, `string Keyboard(KeyboardStatus status)`, `string Insert(bool on)`, `string Cursor(CursorPosition cursor)` (`"01/001"` style, 1-based), `string Model(SessionProfile profile, string? luName)`, `string Fault(BackendFault fault)`
  - `partial class SessionViewModel : ObservableObject, IAsyncDisposable` with `SessionViewModel(IEmulatorSession session, Action<Action> dispatch)`; observable properties `Screen`, `ConnectionText`, `TlsText`, `KeyboardText`, `InsertText`, `CursorText`, `ModelText`, `ErrorMessage`, `IsConnected`; `Title`; `Profile`; commands `ConnectCommand`, `DisconnectCommand`, `SendKeyCommand` (parameter `TerminalKey`), `DismissErrorCommand`; methods `Task TypeTextAsync(string)`, `Task MoveCursorAsync(int,int)`
  - test-only `FakeEmulatorSession : IEmulatorSession` with settable state, `List<string> Calls`, `Exception? ConnectException`, `Exception? ActionException`, and `Raise*` helpers

- [ ] **Step 1: Write the fake session**

`tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`:

```csharp
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeEmulatorSession : IEmulatorSession
{
    public SessionProfile Profile { get; init; } = new() { Name = "Fake", Host = "fake.host", Port = 3270 };
    public ScreenSnapshot CurrentScreen { get; set; } = ScreenSnapshot.Empty(24, 80);
    public ConnectionState ConnectionState { get; set; }
    public TlsInfo? Tls { get; set; }
    public KeyboardStatus KeyboardStatus { get; set; } = KeyboardStatus.Initial;
    public List<string> Calls { get; } = [];
    public Exception? ConnectException { get; set; }
    public Exception? ActionException { get; set; }

    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<BackendFault>? Faulted;
    public event EventHandler<string>? HostMessage;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("connect");
        return ConnectException is null ? Task.CompletedTask : Task.FromException(ConnectException);
    }

    public Task DisconnectAsync() => Record("disconnect");
    public Task SendKeyAsync(TerminalKey key) => Record("key:" + key);
    public Task TypeTextAsync(string text) => Record("type:" + text);
    public Task PasteTextAsync(string text) => Record("paste:" + text);
    public Task MoveCursorAsync(int row, int column) => Record($"move:{row},{column}");

    public ValueTask DisposeAsync()
    {
        Calls.Add("dispose");
        return ValueTask.CompletedTask;
    }

    public void RaiseScreen(ScreenSnapshot screen) { CurrentScreen = screen; ScreenUpdated?.Invoke(this, screen); }
    public void RaiseStatus(KeyboardStatus status) { KeyboardStatus = status; StatusChanged?.Invoke(this, status); }
    public void RaiseConnection(ConnectionState state, TlsInfo? tls = null) { ConnectionState = state; Tls = tls; ConnectionChanged?.Invoke(this, state); }
    public void RaiseFault(BackendFault fault) => Faulted?.Invoke(this, fault);
    public void RaiseHostMessage(string message) => HostMessage?.Invoke(this, message);

    private Task Record(string call)
    {
        Calls.Add(call);
        return ActionException is null ? Task.CompletedTask : Task.FromException(ActionException);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs`:

```csharp
using LizTerm.App.Status;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Status;

public class StatusFormatterTests
{
    [Theory]
    [InlineData(ConnectionState.Disconnected, "Not connected")]
    [InlineData(ConnectionState.Resolving, "Looking up mvs.local")]
    [InlineData(ConnectionState.TcpPending, "Connecting to mvs.local")]
    [InlineData(ConnectionState.TlsPending, "Securing connection to mvs.local")]
    [InlineData(ConnectionState.TelnetPending, "Negotiating with mvs.local")]
    [InlineData(ConnectionState.Connected3270, "Connected to mvs.local (TN3270)")]
    [InlineData(ConnectionState.ConnectedTn3270E, "Connected to mvs.local (TN3270E)")]
    [InlineData(ConnectionState.ConnectedNvt, "Connected to mvs.local (NVT)")]
    public void Connection_text_is_plain_language(ConnectionState state, string expected) =>
        Assert.Equal(expected, StatusFormatter.Connection(state, "mvs.local"));

    [Fact]
    public void Tls_text_uses_padlock_glyph_and_verification()
    {
        Assert.Equal("", StatusFormatter.Tls(null));
        Assert.Equal("", StatusFormatter.Tls(new TlsInfo(false, null, null, null)));
        Assert.Equal("\uE0A2 TLS, certificate verified", StatusFormatter.Tls(new TlsInfo(true, true, null, null)));
        Assert.Equal("\uE0A2 TLS, certificate not verified", StatusFormatter.Tls(new TlsInfo(true, false, null, null)));
    }

    [Theory]
    [InlineData(KeyboardLock.Unlocked, "✓ Ready")]
    [InlineData(KeyboardLock.NotConnected, "✕ Not connected")]
    [InlineData(KeyboardLock.WaitingForHost, "✕ Waiting for host")]
    [InlineData(KeyboardLock.ProtectedField, "✕ Protected field, press Esc")]
    [InlineData(KeyboardLock.NumericOnly, "✕ Numbers only here, press Esc")]
    [InlineData(KeyboardLock.Overflow, "✕ Field is full, press Esc")]
    [InlineData(KeyboardLock.MinusFunction, "✕ Not available here, press Esc")]
    public void Keyboard_text_is_plain_language(KeyboardLock lockState, string expected) =>
        Assert.Equal(expected, StatusFormatter.Keyboard(KeyboardStatus.Initial with { Lock = lockState }));

    [Fact]
    public void Unknown_lock_shows_detail()
    {
        var status = KeyboardStatus.Initial with { Lock = KeyboardLock.Unknown, LockDetail = "weird-state" };
        Assert.Equal("✕ weird-state", StatusFormatter.Keyboard(status));
    }

    [Fact]
    public void Cursor_is_one_based_and_padded() =>
        Assert.Equal("21/013", StatusFormatter.Cursor(new CursorPosition(20, 12, true)));

    [Fact]
    public void Insert_and_model_texts()
    {
        Assert.Equal("INS", StatusFormatter.Insert(true));
        Assert.Equal("", StatusFormatter.Insert(false));
        var profile = new SessionProfile { Name = "a", Host = "h", Model = 4 };
        Assert.Equal("Model 4-E", StatusFormatter.Model(profile, null));
        Assert.Equal("Model 4-E  LU IBM0TEQO", StatusFormatter.Model(profile, "IBM0TEQO"));
    }

    [Fact]
    public void Fault_text_mentions_exit_code_and_stderr()
    {
        var text = StatusFormatter.Fault(new BackendFault("The emulator engine (b3270) exited unexpectedly.", ["a", "b", "c", "d"], 137));
        Assert.Contains("exit code 137", text);
        Assert.Contains("b | c | d", text);
        Assert.Contains("LIZTERM_WIRE_LOG", text);
    }
}
```

`tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs`:

```csharp
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create()
    {
        var session = new FakeEmulatorSession();
        return (new SessionViewModel(session, action => action()), session);
    }

    [Fact]
    public void Initial_state_reflects_the_session()
    {
        var (vm, session) = Create();
        Assert.Equal("Fake - fake.host", vm.Title);
        Assert.Same(session.CurrentScreen, vm.Screen);
        Assert.Equal("Not connected", vm.ConnectionText);
        Assert.Equal("✕ Not connected", vm.KeyboardText);
        Assert.Equal("Model 2-E", vm.ModelText);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public void Screen_event_updates_screen_and_cursor_text()
    {
        var (vm, session) = Create();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetCursor(new CursorPosition(3, 9, true));
        var snapshot = buffer.Snapshot();
        session.RaiseScreen(snapshot);
        Assert.Same(snapshot, vm.Screen);
        Assert.Equal("04/010", vm.CursorText);
    }

    [Fact]
    public void Connection_event_updates_texts_and_flag()
    {
        var (vm, session) = Create();
        session.RaiseConnection(ConnectionState.ConnectedTn3270E, new TlsInfo(true, true, null, null));
        Assert.True(vm.IsConnected);
        Assert.Equal("Connected to fake.host (TN3270E)", vm.ConnectionText);
        Assert.Equal("\uE0A2 TLS, certificate verified", vm.TlsText);
    }

    [Fact]
    public void Status_event_updates_keyboard_insert_and_model()
    {
        var (vm, session) = Create();
        session.RaiseStatus(new KeyboardStatus(KeyboardLock.Unlocked, null, true, false, "LU01"));
        Assert.Equal("✓ Ready", vm.KeyboardText);
        Assert.Equal("INS", vm.InsertText);
        Assert.Equal("Model 2-E  LU LU01", vm.ModelText);
    }

    [Fact]
    public async Task Connect_command_calls_session_and_reports_failure()
    {
        var (vm, session) = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect"], session.Calls);
        Assert.Null(vm.ErrorMessage);

        session.ConnectException = new ConnectionFailedException(["Connection failed:", "Connection refused"]);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("Connection failed: Connection refused", vm.ErrorMessage);
    }

    [Fact]
    public async Task Backend_unavailable_is_reported()
    {
        var (vm, session) = Create();
        session.ConnectException = new BackendUnavailableException("b3270 not found");
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("b3270 not found", vm.ErrorMessage);
    }

    [Fact]
    public async Task Key_text_and_cursor_calls_are_forwarded()
    {
        var (vm, session) = Create();
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.PF3);
        await vm.TypeTextAsync("abc");
        await vm.MoveCursorAsync(2, 5);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.Equal(["key:PF3", "type:abc", "move:2,5", "disconnect"], session.Calls);
    }

    [Fact]
    public async Task Rejected_actions_are_swallowed_because_the_OIA_explains_them()
    {
        var (vm, session) = Create();
        session.ActionException = new EmulatorActionException("Keyboard locked");
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.Enter);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Fault_and_host_messages_show_and_can_be_dismissed()
    {
        var (vm, session) = Create();
        session.RaiseHostMessage("Host unreachable");
        Assert.Equal("Host unreachable", vm.ErrorMessage);
        vm.DismissErrorCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        session.RaiseFault(new BackendFault("b3270 died", [], 1));
        Assert.Contains("b3270 died", vm.ErrorMessage);
    }

    [Fact]
    public async Task Dispose_disposes_the_session()
    {
        var (vm, session) = Create();
        await vm.DisposeAsync();
        Assert.Contains("dispose", session.Calls);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: build errors.

- [ ] **Step 4: Implement the formatter and view model**

`src/LizTerm.App/Status/StatusFormatter.cs`:

```csharp
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Status;

/// <summary>Plain-language status text. Glyphs are ones the IBM 3270 font encodes: its padlock (U+E0A2) and ordinary check/cross marks.</summary>
public static class StatusFormatter
{
    public const string PadlockGlyph = "\uE0A2";
    public const string ReadyGlyph = "✓";
    public const string BlockedGlyph = "✕";

    public static string Connection(ConnectionState state, string host) => state switch
    {
        ConnectionState.Disconnected => "Not connected",
        ConnectionState.Reconnecting => $"Reconnecting to {host}",
        ConnectionState.Resolving => $"Looking up {host}",
        ConnectionState.TcpPending => $"Connecting to {host}",
        ConnectionState.TlsPending => $"Securing connection to {host}",
        ConnectionState.TlsPasswordPending => "Waiting for TLS key password",
        ConnectionState.ProxyPending => $"Connecting to {host} through proxy",
        ConnectionState.TelnetPending => $"Negotiating with {host}",
        ConnectionState.ConnectedNvt or ConnectionState.ConnectedNvtCharMode or ConnectionState.ConnectedENvt => $"Connected to {host} (NVT)",
        ConnectionState.Connected3270 => $"Connected to {host} (TN3270)",
        ConnectionState.ConnectedUnbound => $"Connected to {host} (TN3270E, unbound)",
        ConnectionState.ConnectedSscp => $"Connected to {host} (SSCP-LU)",
        ConnectionState.ConnectedTn3270E => $"Connected to {host} (TN3270E)",
        _ => state.ToString(),
    };

    public static string Tls(TlsInfo? tls) => tls is { Secure: true }
        ? $"{PadlockGlyph} TLS, certificate {(tls.Verified == true ? "verified" : "not verified")}"
        : "";

    public static string Keyboard(KeyboardStatus status) => status.Lock switch
    {
        KeyboardLock.Unlocked => $"{ReadyGlyph} Ready",
        KeyboardLock.NotConnected => $"{BlockedGlyph} Not connected",
        KeyboardLock.WaitingForHost => $"{BlockedGlyph} Waiting for host",
        KeyboardLock.TerminalWait => $"{BlockedGlyph} Please wait",
        KeyboardLock.Deferred => $"{BlockedGlyph} Waiting for host",
        KeyboardLock.MinusFunction => $"{BlockedGlyph} Not available here, press Esc",
        KeyboardLock.ProtectedField => $"{BlockedGlyph} Protected field, press Esc",
        KeyboardLock.NumericOnly => $"{BlockedGlyph} Numbers only here, press Esc",
        KeyboardLock.Overflow => $"{BlockedGlyph} Field is full, press Esc",
        KeyboardLock.Dbcs => $"{BlockedGlyph} Invalid double-byte input, press Esc",
        KeyboardLock.Scrolled => $"{BlockedGlyph} Scrolled back",
        KeyboardLock.Disabled => $"{BlockedGlyph} Keyboard disabled",
        KeyboardLock.FieldWait => $"{BlockedGlyph} Waiting for field",
        KeyboardLock.FileTransfer => $"{BlockedGlyph} File transfer in progress",
        _ => $"{BlockedGlyph} {status.LockDetail ?? "Locked"}",
    };

    public static string Insert(bool on) => on ? "INS" : "";

    public static string Cursor(CursorPosition cursor) => $"{cursor.Row + 1:D2}/{cursor.Column + 1:D3}";

    public static string Model(SessionProfile profile, string? luName)
    {
        var model = $"Model {profile.Model}{(profile.Extended ? "-E" : "")}";
        return luName is null ? model : $"{model}  LU {luName}";
    }

    public static string Fault(BackendFault fault)
    {
        var tail = string.Join(" | ", fault.StderrTail.TakeLast(3));
        var code = fault.ExitCode?.ToString() ?? "unknown";
        var detail = tail.Length > 0 ? $" Last output: {tail}." : "";
        return $"{fault.Message} (exit code {code}).{detail} Set LIZTERM_WIRE_LOG to a file path and reproduce to capture a log.";
    }
}
```

`src/LizTerm.App/ViewModels/SessionViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Status;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IEmulatorSession _session;
    private readonly Action<Action> _dispatch;

    [ObservableProperty] private ScreenSnapshot? _screen;
    [ObservableProperty] private string _connectionText = "";
    [ObservableProperty] private string _tlsText = "";
    [ObservableProperty] private string _keyboardText = "";
    [ObservableProperty] private string _insertText = "";
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _modelText = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConnected;

    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch)
    {
        _session = session;
        _dispatch = dispatch;

        session.ScreenUpdated += (_, s) => _dispatch(() => ApplyScreen(s));
        session.StatusChanged += (_, k) => _dispatch(() => ApplyStatus(k));
        session.ConnectionChanged += (_, c) => _dispatch(() => ApplyConnection(c));
        session.Faulted += (_, f) => _dispatch(() => ErrorMessage = StatusFormatter.Fault(f));
        session.HostMessage += (_, m) => _dispatch(() => ErrorMessage = m);

        ApplyScreen(session.CurrentScreen);
        ApplyStatus(session.KeyboardStatus);
        ApplyConnection(session.ConnectionState);
    }

    public SessionProfile Profile => _session.Profile;
    public string Title => $"{Profile.Name} - {Profile.Host}";

    private void ApplyScreen(ScreenSnapshot snapshot)
    {
        Screen = snapshot;
        CursorText = StatusFormatter.Cursor(snapshot.Cursor);
    }

    private void ApplyStatus(KeyboardStatus status)
    {
        KeyboardText = StatusFormatter.Keyboard(status);
        InsertText = StatusFormatter.Insert(status.InsertMode);
        ModelText = StatusFormatter.Model(Profile, status.LuName);
    }

    private void ApplyConnection(ConnectionState state)
    {
        IsConnected = state.IsConnected();
        ConnectionText = StatusFormatter.Connection(state, Profile.Host);
        TlsText = StatusFormatter.Tls(_session.Tls);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ErrorMessage = null;
        try
        {
            await _session.ConnectAsync();
        }
        catch (ConnectionFailedException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BackendUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task DisconnectAsync() => Guard(_session.DisconnectAsync());

    [RelayCommand]
    private Task SendKeyAsync(TerminalKey key) => Guard(_session.SendKeyAsync(key));

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    public Task TypeTextAsync(string text) => Guard(_session.TypeTextAsync(text));

    public Task MoveCursorAsync(int row, int column) => Guard(_session.MoveCursorAsync(row, column));

    /// <summary>Rejected actions are not errors to show: b3270 already explains them through the keyboard lock.</summary>
    private async Task Guard(Task action)
    {
        try
        {
            await action;
        }
        catch (EmulatorActionException)
        {
        }
        catch (InvalidOperationException)
        {
            // Session not started yet; nothing to send to.
        }
        catch (BackendUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add status formatter and session view model

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 14: Windows, profile picker and editor, startup arguments, app lifetime

**Files:**
- Create: `src/LizTerm.App/Startup/StartupArguments.cs`, `src/LizTerm.App/SessionFactory.cs`
- Create: `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`, `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`
- Create: `src/LizTerm.App/Views/SessionWindow.axaml(.cs)`, `ProfilePickerWindow.axaml(.cs)`, `ProfileEditorWindow.axaml(.cs)`
- Modify: `src/LizTerm.App/App.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Consumes: `SessionViewModel`, `TerminalScreen`, `ProfileStore`, `B3270Session`, `B3270ChildProcess`, `B3270Locator`, `WireLog`.
- Produces:
  - `sealed record StartupArguments(string? ProfileName, string? Host, int? Port)` with `static StartupArguments Parse(IReadOnlyList<string> args)` and `SessionProfile? Resolve(IReadOnlyList<SessionProfile> profiles)`
  - `static IEmulatorSession SessionFactory.Create(SessionProfile profile)`
  - `partial class ProfilePickerViewModel(ProfileStore store, Action<SessionProfile> openSession, Func<SessionProfile?, Task<SessionProfile?>> editProfile, Action quit)` with `ObservableCollection<SessionProfile> Profiles`, `SelectedProfile`, `Reload()`, commands `Connect`, `New`, `Edit`, `Delete`, `Quit`
  - `partial class ProfileEditorViewModel(SessionProfile? existing)` with editable fields, `int[] Models`, `string? ValidationMessage`, `SessionProfile? TryBuild()`
  - `App.OpenSession(SessionProfile)`, `App.ShowPicker()`, `App.Quit()`

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs`:

```csharp
using LizTerm.App.Startup;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Startup;

public class StartupArgumentsTests
{
    [Fact]
    public void No_arguments_means_picker() => Assert.Equal(new StartupArguments(null, null, null), StartupArguments.Parse([]));

    [Fact]
    public void Bare_word_is_a_profile_name() => Assert.Equal(new StartupArguments("TK5", null, null), StartupArguments.Parse(["TK5"]));

    [Theory]
    [InlineData("mvs.local", "mvs.local", null)]
    [InlineData("mvs.local:3270", "mvs.local", 3270)]
    [InlineData("localhost:3270", "localhost", 3270)]
    [InlineData("localhost", "localhost", null)]
    [InlineData("[::1]:23", "::1", 23)]
    [InlineData("[fe80::1]", "fe80::1", null)]
    public void Host_forms_are_recognized(string arg, string host, int? port)
    {
        var parsed = StartupArguments.Parse([arg]);
        Assert.Null(parsed.ProfileName);
        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
    }

    [Fact]
    public void Resolve_finds_profile_case_insensitively()
    {
        var profiles = new[] { new SessionProfile { Name = "TK5", Host = "mvs.local" } };
        Assert.Same(profiles[0], StartupArguments.Parse(["tk5"]).Resolve(profiles));
        Assert.Null(StartupArguments.Parse(["missing"]).Resolve(profiles));
    }

    [Fact]
    public void Resolve_builds_an_ad_hoc_profile_for_hosts()
    {
        var profile = StartupArguments.Parse(["mvs.local:3270"]).Resolve([]);
        Assert.NotNull(profile);
        Assert.Equal("mvs.local:3270", profile!.Name);
        Assert.Equal("mvs.local", profile.Host);
        Assert.Equal(3270, profile.Port);
        Assert.Equal(23, StartupArguments.Parse(["mvs.local"]).Resolve([])!.Port);
    }
}
```

`tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`:

```csharp
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ProfileViewModelsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-vm-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _store;

    public ProfileViewModelsTests() => _store = new ProfileStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Editor_defaults_and_tls_port_flip()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal("23", vm.PortText);
        Assert.Equal(2, vm.Model);
        Assert.True(vm.Extended);
        Assert.Equal("cp037", vm.CodePage);
        vm.UseTls = true;
        Assert.Equal("992", vm.PortText);
        vm.UseTls = false;
        Assert.Equal("23", vm.PortText);
    }

    [Fact]
    public void Editor_validates_required_fields_and_port()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Null(vm.TryBuild());
        Assert.Contains("name", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        vm.Name = "x";
        Assert.Null(vm.TryBuild());
        Assert.Contains("host", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        vm.Host = "h";
        vm.PortText = "99999";
        Assert.Null(vm.TryBuild());
        Assert.Contains("port", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        vm.PortText = "3270";
        var built = vm.TryBuild();
        Assert.NotNull(built);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(3270, built!.Port);
    }

    [Fact]
    public void Editor_round_trips_an_existing_profile()
    {
        var original = new SessionProfile { Name = "TK5", Host = "mvs", Port = 3270, UseTls = true, VerifyCertificate = false, Model = 5, Extended = false, CodePage = "bracket", LuName = "LU1" };
        var vm = new ProfileEditorViewModel(original);
        Assert.Equal(original, vm.TryBuild());
    }

    [Fact]
    public async Task Picker_lists_creates_edits_and_deletes()
    {
        _store.Save(new SessionProfile { Name = "b", Host = "b.host" });
        _store.Save(new SessionProfile { Name = "a", Host = "a.host" });
        SessionProfile? opened = null;
        var quit = false;
        SessionProfile? toReturn = new SessionProfile { Name = "c", Host = "c.host" };
        var vm = new ProfilePickerViewModel(_store, p => opened = p, _ => Task.FromResult(toReturn), () => quit = true);

        Assert.Equal(["a", "b"], vm.Profiles.Select(p => p.Name));
        Assert.False(vm.ConnectCommand.CanExecute(null));

        vm.SelectedProfile = vm.Profiles[1];
        Assert.True(vm.ConnectCommand.CanExecute(null));
        vm.ConnectCommand.Execute(null);
        Assert.Equal("b", opened!.Name);

        await vm.NewCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c"], vm.Profiles.Select(p => p.Name));
        Assert.Equal("c", vm.SelectedProfile!.Name);

        toReturn = new SessionProfile { Name = "c2", Host = "c.host" };
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c2"], vm.Profiles.Select(p => p.Name));
        Assert.Equal(3, _store.LoadAll().Count);

        vm.SelectedProfile = vm.Profiles[0];
        vm.DeleteCommand.Execute(null);
        Assert.Equal(["b", "c2"], vm.Profiles.Select(p => p.Name));

        vm.QuitCommand.Execute(null);
        Assert.True(quit);
    }

    [Fact]
    public async Task Cancelled_editor_changes_nothing()
    {
        var vm = new ProfilePickerViewModel(_store, _ => { }, _ => Task.FromResult<SessionProfile?>(null), () => { });
        await vm.NewCommand.ExecuteAsync(null);
        Assert.Empty(vm.Profiles);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: build errors.

- [ ] **Step 3: Implement startup arguments, factory, and view models**

`src/LizTerm.App/Startup/StartupArguments.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>Command line: no argument opens the picker; a saved profile name connects to it; host[:port] or [ipv6][:port] connects ad hoc.</summary>
public sealed record StartupArguments(string? ProfileName, string? Host, int? Port)
{
    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0])) return new StartupArguments(null, null, null);
        var arg = args[0].Trim();

        if (arg.StartsWith('['))
        {
            var close = arg.IndexOf(']');
            if (close > 1)
            {
                var rest = arg[(close + 1)..];
                int? p = rest.StartsWith(':') && int.TryParse(rest[1..], out var parsed) ? parsed : null;
                return new StartupArguments(null, arg[1..close], p);
            }
        }

        var lastColon = arg.LastIndexOf(':');
        if (lastColon > 0 && arg.IndexOf(':') == lastColon && int.TryParse(arg[(lastColon + 1)..], out var port))
            return new StartupArguments(null, arg[..lastColon], port);

        if (arg.Contains('.') || arg.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return new StartupArguments(null, arg, null);

        return new StartupArguments(arg, null, null);
    }

    public SessionProfile? Resolve(IReadOnlyList<SessionProfile> profiles)
    {
        if (ProfileName is not null)
            return profiles.FirstOrDefault(p => p.Name.Equals(ProfileName, StringComparison.OrdinalIgnoreCase));
        if (Host is not null)
            return new SessionProfile { Name = Port is null ? Host : $"{Host}:{Port}", Host = Host, Port = Port ?? 23 };
        return null;
    }
}
```

`src/LizTerm.App/SessionFactory.cs`:

```csharp
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>The only place the app names the b3270 backend.</summary>
public static class SessionFactory
{
    public static IEmulatorSession Create(SessionProfile profile) =>
        new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find()), WireLog.FromEnvironment());
}
```

`src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class ProfileEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _portText = "23";
    [ObservableProperty] private bool _useTls;
    [ObservableProperty] private bool _verifyCertificate = true;
    [ObservableProperty] private int _model = 2;
    [ObservableProperty] private bool _extended = true;
    [ObservableProperty] private string _codePage = "cp037";
    [ObservableProperty] private string _luName = "";
    [ObservableProperty] private string? _validationMessage;

    public int[] Models { get; } = [2, 3, 4, 5];
    public bool IsNew { get; }

    public ProfileEditorViewModel(SessionProfile? existing)
    {
        IsNew = existing is null;
        if (existing is null) return;
        _name = existing.Name;
        _host = existing.Host;
        _portText = existing.Port.ToString();
        _useTls = existing.UseTls;
        _verifyCertificate = existing.VerifyCertificate;
        _model = existing.Model;
        _extended = existing.Extended;
        _codePage = existing.CodePage;
        _luName = existing.LuName ?? "";
    }

    partial void OnUseTlsChanged(bool value)
    {
        if (value && PortText == "23") PortText = "992";
        else if (!value && PortText == "992") PortText = "23";
    }

    public SessionProfile? TryBuild()
    {
        if (string.IsNullOrWhiteSpace(Name)) { ValidationMessage = "Give the profile a name."; return null; }
        if (string.IsNullOrWhiteSpace(Host)) { ValidationMessage = "Enter the host name or address."; return null; }
        if (!int.TryParse(PortText.Trim(), out var port) || port < 1 || port > 65535) { ValidationMessage = "Port must be a number from 1 to 65535."; return null; }
        if (string.IsNullOrWhiteSpace(CodePage)) { ValidationMessage = "Enter a code page, for example cp037."; return null; }
        ValidationMessage = null;
        return new SessionProfile
        {
            Name = Name.Trim(),
            Host = Host.Trim(),
            Port = port,
            UseTls = UseTls,
            VerifyCertificate = VerifyCertificate,
            Model = Model,
            Extended = Extended,
            CodePage = CodePage.Trim(),
            LuName = string.IsNullOrWhiteSpace(LuName) ? null : LuName.Trim(),
        };
    }
}
```

`src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class ProfilePickerViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly Action<SessionProfile> _openSession;
    private readonly Func<SessionProfile?, Task<SessionProfile?>> _editProfile;
    private readonly Action _quit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand))]
    private SessionProfile? _selectedProfile;

    public ObservableCollection<SessionProfile> Profiles { get; } = [];

    /// <param name="editProfile">Shows the editor for an existing profile (or null for a new one); returns null when cancelled.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile> openSession, Func<SessionProfile?, Task<SessionProfile?>> editProfile, Action quit)
    {
        _store = store;
        _openSession = openSession;
        _editProfile = editProfile;
        _quit = quit;
        Reload();
    }

    public bool HasSelection => SelectedProfile is not null;

    public void Reload()
    {
        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in _store.LoadAll()) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == selectedName);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Connect() => _openSession(SelectedProfile!);

    [RelayCommand]
    private async Task NewAsync()
    {
        var created = await _editProfile(null);
        if (created is null) return;
        _store.Save(created);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == created.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        var original = SelectedProfile!;
        var edited = await _editProfile(original);
        if (edited is null) return;
        if (!edited.Name.Equals(original.Name, StringComparison.OrdinalIgnoreCase)) _store.Delete(original.Name);
        _store.Save(edited);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == edited.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        _store.Delete(SelectedProfile!.Name);
        SelectedProfile = null;
        Reload();
    }

    [RelayCommand]
    private void Quit() => _quit();
}
```

- [ ] **Step 4: Run the view model tests**

```bash
dotnet test tests/LizTerm.App.Tests
```

Expected: all pass (the windows are not needed for these tests).

- [ ] **Step 5: Write the windows**

`src/LizTerm.App/Views/SessionWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        xmlns:controls="using:LizTerm.App.Controls"
        xmlns:core="using:LizTerm.Core.Session"
        x:Class="LizTerm.App.Views.SessionWindow"
        x:DataType="vm:SessionViewModel"
        Title="{Binding Title}"
        Width="960" Height="680" MinWidth="480" MinHeight="360"
        Background="Black">
  <DockPanel>
    <Menu DockPanel.Dock="Top">
      <MenuItem Header="_File">
        <MenuItem Header="_New Session..." Click="OnNewSessionClick" />
        <Separator />
        <MenuItem Header="_Connect" Command="{Binding ConnectCommand}" />
        <MenuItem Header="_Disconnect" Command="{Binding DisconnectCommand}" />
        <Separator />
        <MenuItem Header="C_lose" Click="OnCloseClick" />
      </MenuItem>
      <MenuItem Header="_Keys">
        <MenuItem Header="Clear" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Clear}" />
        <MenuItem Header="Reset" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Reset}" />
        <MenuItem Header="Attn" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Attn}" />
        <MenuItem Header="SysReq" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.SysReq}" />
        <Separator />
        <MenuItem Header="PA1" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PA1}" />
        <MenuItem Header="PA2" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PA2}" />
        <MenuItem Header="PA3" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PA3}" />
        <Separator />
        <MenuItem Header="PF13" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF13}" />
        <MenuItem Header="PF14" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF14}" />
        <MenuItem Header="PF15" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF15}" />
        <MenuItem Header="PF16" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF16}" />
        <MenuItem Header="PF17" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF17}" />
        <MenuItem Header="PF18" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF18}" />
        <MenuItem Header="PF19" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF19}" />
        <MenuItem Header="PF20" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF20}" />
        <MenuItem Header="PF21" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF21}" />
        <MenuItem Header="PF22" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF22}" />
        <MenuItem Header="PF23" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF23}" />
        <MenuItem Header="PF24" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF24}" />
      </MenuItem>
    </Menu>

    <Border DockPanel.Dock="Bottom" Background="#181818" Padding="10,4">
      <Grid ColumnDefinitions="Auto,16,Auto,*,Auto,16,Auto,16,Auto,16,Auto">
        <TextBlock Grid.Column="0" Text="{Binding ConnectionText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#C0C0C0" />
        <TextBlock Grid.Column="2" Text="{Binding TlsText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#C0C0C0" />
        <TextBlock Grid.Column="4" Text="{Binding KeyboardText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#E0E0E0" />
        <TextBlock Grid.Column="6" Text="{Binding InsertText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#FFFF80" />
        <TextBlock Grid.Column="8" Text="{Binding CursorText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#C0C0C0" />
        <TextBlock Grid.Column="10" Text="{Binding ModelText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#C0C0C0" />
      </Grid>
    </Border>

    <Border DockPanel.Dock="Bottom" Background="#5A2020" Padding="10,6"
            IsVisible="{Binding ErrorMessage, Converter={x:Static ObjectConverters.IsNotNull}}">
      <DockPanel>
        <Button DockPanel.Dock="Right" Content="Dismiss" Command="{Binding DismissErrorCommand}" />
        <TextBlock Text="{Binding ErrorMessage}" Foreground="White" TextWrapping="Wrap" VerticalAlignment="Center" />
      </DockPanel>
    </Border>

    <controls:TerminalScreen x:Name="Screen" Snapshot="{Binding Screen}" />
  </DockPanel>
</Window>
```

`src/LizTerm.App/Views/SessionWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

public partial class SessionWindow : Window
{
    public SessionWindow()
    {
        InitializeComponent();
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyCommand.ExecuteAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Opened += (_, _) => Screen.Focus();
    }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnNewSessionClick(object? sender, RoutedEventArgs e) => (Avalonia.Application.Current as App)?.ShowPicker();
}
```

`src/LizTerm.App/Views/ProfileEditorWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        x:Class="LizTerm.App.Views.ProfileEditorWindow"
        x:DataType="vm:ProfileEditorViewModel"
        Title="Session Profile" Width="440" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <StackPanel Margin="16" Spacing="10">
    <Grid ColumnDefinitions="120,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto" >
      <TextBlock Grid.Row="0" Text="Name" VerticalAlignment="Center" />
      <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding Name}" />
      <TextBlock Grid.Row="1" Text="Host" VerticalAlignment="Center" />
      <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding Host}" Watermark="mvs.example or 192.168.1.10" />
      <TextBlock Grid.Row="2" Text="Port" VerticalAlignment="Center" />
      <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding PortText}" Width="100" HorizontalAlignment="Left" />
      <TextBlock Grid.Row="3" Text="Security" VerticalAlignment="Center" />
      <StackPanel Grid.Row="3" Grid.Column="1" Spacing="4">
        <CheckBox Content="Use TLS" IsChecked="{Binding UseTls}" />
        <CheckBox Content="Verify host certificate" IsChecked="{Binding VerifyCertificate}" IsEnabled="{Binding UseTls}" />
      </StackPanel>
      <TextBlock Grid.Row="4" Text="Model" VerticalAlignment="Center" />
      <StackPanel Grid.Row="4" Grid.Column="1" Orientation="Horizontal" Spacing="12">
        <ComboBox ItemsSource="{Binding Models}" SelectedItem="{Binding Model}" Width="80" />
        <CheckBox Content="Extended data stream" IsChecked="{Binding Extended}" />
      </StackPanel>
      <TextBlock Grid.Row="5" Text="Code page" VerticalAlignment="Center" />
      <TextBox Grid.Row="5" Grid.Column="1" Text="{Binding CodePage}" Width="140" HorizontalAlignment="Left" />
      <TextBlock Grid.Row="6" Text="LU name" VerticalAlignment="Center" />
      <TextBox Grid.Row="6" Grid.Column="1" Text="{Binding LuName}" Width="140" HorizontalAlignment="Left" Watermark="optional" />
    </Grid>
    <TextBlock Text="{Binding ValidationMessage}" Foreground="#FF8080" IsVisible="{Binding ValidationMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button Content="Cancel" Click="OnCancelClick" />
      <Button Content="Save" Click="OnSaveClick" IsDefault="True" />
    </StackPanel>
  </StackPanel>
</Window>
```

`src/LizTerm.App/Views/ProfileEditorWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class ProfileEditorWindow : Window
{
    public ProfileEditorWindow() : this(null) { }

    public ProfileEditorWindow(SessionProfile? existing)
    {
        InitializeComponent();
        DataContext = new ProfileEditorViewModel(existing);
        Title = existing is null ? "New Session Profile" : $"Edit {existing.Name}";
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.TryBuild() is { } profile) Close(profile);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
```

`src/LizTerm.App/Views/ProfilePickerWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        xmlns:core="using:LizTerm.Core.Session"
        x:Class="LizTerm.App.Views.ProfilePickerWindow"
        x:DataType="vm:ProfilePickerViewModel"
        Title="LizTerm" Width="520" Height="400" MinWidth="400" MinHeight="300"
        WindowStartupLocation="CenterScreen">
  <DockPanel Margin="16">
    <TextBlock DockPanel.Dock="Top" Text="Sessions" FontSize="20" Margin="0,0,0,10" />
    <StackPanel DockPanel.Dock="Right" Margin="12,0,0,0" Spacing="8" Width="120">
      <Button Content="Connect" Command="{Binding ConnectCommand}" HorizontalAlignment="Stretch" IsDefault="True" />
      <Button Content="New..." Command="{Binding NewCommand}" HorizontalAlignment="Stretch" />
      <Button Content="Edit..." Command="{Binding EditCommand}" HorizontalAlignment="Stretch" />
      <Button Content="Delete" Command="{Binding DeleteCommand}" HorizontalAlignment="Stretch" />
      <Button Content="Quit" Command="{Binding QuitCommand}" HorizontalAlignment="Stretch" Margin="0,24,0,0" />
    </StackPanel>
    <ListBox ItemsSource="{Binding Profiles}" SelectedItem="{Binding SelectedProfile}" DoubleTapped="OnDoubleTapped">
      <ListBox.ItemTemplate>
        <DataTemplate x:DataType="core:SessionProfile">
          <StackPanel>
            <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
            <TextBlock Foreground="#A0A0A0" FontSize="12">
              <Run Text="{Binding Host}" /><Run Text=":" /><Run Text="{Binding Port}" />
            </TextBlock>
          </StackPanel>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Window>
```

`src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class ProfilePickerWindow : Window
{
    public ProfilePickerWindow()
    {
        InitializeComponent();
    }

    public ProfilePickerWindow(ProfileStore store, Action<SessionProfile> openSession, Action quit) : this()
    {
        DataContext = new ProfilePickerViewModel(
            store,
            openSession,
            existing => new ProfileEditorWindow(existing).ShowDialog<SessionProfile?>(this),
            quit);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm && vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
    }
}
```

- [ ] **Step 6: Wire the application lifetime**

Replace `src/LizTerm.App/App.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LizTerm.App.Startup;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App;

public partial class App : Application
{
    private readonly List<SessionWindow> _sessions = [];
    private ProfilePickerWindow? _picker;
    private ProfileStore? _store;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the last session window returns to the picker; only Quit ends the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _store = new ProfileStore(ProfileStore.DefaultDirectory());
            var profile = StartupArguments.Parse(desktop.Args ?? []).Resolve(_store.LoadAll());
            if (profile is not null) OpenSession(profile);
            else ShowPicker();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public void OpenSession(SessionProfile profile)
    {
        var viewModel = new SessionViewModel(SessionFactory.Create(profile), action => Dispatcher.UIThread.Post(action));
        var window = new SessionWindow { DataContext = viewModel };
        _sessions.Add(window);
        window.Closed += async (_, _) =>
        {
            _sessions.Remove(window);
            await viewModel.DisposeAsync();
            if (_sessions.Count == 0) ShowPicker();
        };
        _picker?.Close();
        window.Show();
        _ = viewModel.ConnectCommand.ExecuteAsync(null);
    }

    public void ShowPicker()
    {
        if (_picker is { IsVisible: true })
        {
            _picker.Activate();
            return;
        }
        _picker = new ProfilePickerWindow(_store ?? new ProfileStore(ProfileStore.DefaultDirectory()), OpenSession, Quit);
        _picker.Closed += (_, _) => { if (_sessions.Count == 0 && !_quitting) { /* picker closed with the X: treat as quit */ Quit(); } };
        _picker.Show();
    }

    private bool _quitting;

    public void Quit()
    {
        _quitting = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
```

Note: `OpenSession` closes the picker before the session window shows, so the picker's `Closed` handler must not quit when a session is open. The `_sessions.Count == 0` check handles that because the window was already added.

- [ ] **Step 7: Build and run the tests**

```bash
dotnet build LizTerm.sln && dotnet test LizTerm.sln
```

Expected: build succeeds with no XAML compile errors; all tests pass.

- [ ] **Step 8: Run the app against a replayed host**

Terminal 1 (a fake host that serves the IBMLink welcome screen; type `e` after the app connects, `q` to end):

```bash
native/build-tmp/playback/playback -w -p 4001 native/build-tmp/playback/src/suite3270-4.5/s3270/Test/ibmlink_help.trc
```

Terminal 2:

```bash
LIZTERM_WIRE_LOG=/tmp/lizterm-wire.log dotnet run --project src/LizTerm.App -- 127.0.0.1:4001
```

Check, by eye:
- A session window opens titled `127.0.0.1:4001 - 127.0.0.1`, status bar shows `Connecting to 127.0.0.1` then `Connected to 127.0.0.1 (TN3270)`.
- After typing `e` in terminal 1, the IBMLink screen appears in the 3270 font, highlighted lines brighter, cursor block visible on row 21.
- Resizing the window rescales the grid, keeping it centered.
- Clicking a cell moves the cursor; the cursor text in the status bar updates.
- Typing text or pressing F3 produces `run` lines in `/tmp/lizterm-wire.log` (`grep '> ' /tmp/lizterm-wire.log`).
- Closing the window brings back the picker. New... opens the editor; saving a profile lists it; double-click connects.

If the app reports that b3270 was not found, either run `native/build/build-macos.sh` (the csproj then copies it into `runtimes/osx-arm64/native/`) or set `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270`.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "Add session window, profile picker and editor, startup arguments

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 15: Integration lane and developer README

**Files:**
- Create: `tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj`, `tests/LizTerm.Integration.Tests/LiveHostTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `B3270Session`, `B3270ChildProcess`, `B3270Locator`, `WireLog`.
- Produces: an opt-in test that connects to `LIZTERM_TEST_HOST` (`host[:port]`) and waits for a non-blank screen.

- [ ] **Step 1: Create the project and test**

`tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj" />
  </ItemGroup>
</Project>
```

`tests/LizTerm.Integration.Tests/LiveHostTests.cs`:

```csharp
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_TEST_HOST=host[:port] is set. Needs a b3270 (bundled, or LIZTERM_B3270_PATH).</summary>
public class LiveHostTests
{
    [Fact]
    public async Task Connects_and_receives_a_screen()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");

        var colon = target!.LastIndexOf(':');
        var host = colon > 0 ? target[..colon] : target;
        var port = colon > 0 ? int.Parse(target[(colon + 1)..]) : 23;
        var profile = new SessionProfile { Name = "integration", Host = host, Port = port };

        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find()), WireLog.FromEnvironment());
        var gotText = new TaskCompletionSource<ScreenSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ScreenUpdated += (_, s) =>
        {
            if (s.ToText().Any(char.IsLetterOrDigit)) gotText.TrySetResult(s);
        };

        await session.ConnectAsync();
        var screen = await gotText.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(session.ConnectionState.IsConnected(), $"state was {session.ConnectionState}");
        Assert.True(screen.ToText().Any(char.IsLetter), "screen has no letters");
    }
}
```

```bash
dotnet sln add tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj
```

- [ ] **Step 2: Run it skipped, then against a host**

```bash
dotnet test tests/LizTerm.Integration.Tests
```

Expected: 1 skipped.

Against the local MVS/CE container (image `mainframed767/mvsce` exposes 3270/tcp; wait a couple of minutes after start for MVS to IPL), or any MVS instance:

```bash
LIZTERM_B3270_PATH=$PWD/native/out/osx-arm64/b3270 LIZTERM_TEST_HOST=127.0.0.1:3270 dotnet test tests/LizTerm.Integration.Tests
```

Expected: 1 passed. The playback fake host also works: start `playback -w -p 4001 <trace>` and type `e` once the test connects.

- [ ] **Step 3: Write the README**

Replace `README.md`:

```markdown
# LizTerm

A cross-platform TN3270 client for retro mainframe hobbyists: macOS, Linux, and Windows, one UI, no install
dependencies. Built with .NET 10 and Avalonia on top of the b3270 engine from the x3270 suite.

Design: `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`.

## Developer setup (macOS)

1. .NET 10 SDK, Xcode command line tools, Homebrew `openssl@3`.
2. Build the emulator engine once: `native/build/build-macos.sh` (produces `native/out/osx-<arch>/b3270`,
   statically linked against OpenSSL; the app project copies it into its output).
3. `dotnet test LizTerm.sln`
4. `dotnet run --project src/LizTerm.App` (or `-- profile-name`, or `-- host:port`).

Environment variables:

- `LIZTERM_B3270_PATH`: use this b3270 instead of the bundled one.
- `LIZTERM_WIRE_LOG`: append every protocol line in both directions to this file (attach to bug reports).
- `LIZTERM_TEST_HOST`: `host[:port]` for the opt-in integration tests.

## Recording protocol fixtures

`tools/record-fixture.sh <trace.trc> <out.jsonl>` replays an x3270 host trace through b3270 and saves its output;
see `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`.

## Licenses of bundled components

- x3270 / b3270: BSD-3-Clause, Copyright Paul Mattes and others.
- IBM 3270 font by Ricardo Banffy: SIL Open Font License 1.1 (`src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt`).
- LizTerm's own license: to be decided.
```

- [ ] **Step 4: Full verification**

```bash
dotnet test LizTerm.sln
```

Expected: every project passes; the integration test is skipped unless the host variable is set.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add opt-in live host integration test and developer README

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Follow-up plans (not part of this milestone)

Each gets its own plan from the same spec once Milestone 1 is running:

- **Milestone 2, product polish:** rectangular selection and copy (trailing spaces trimmed), paste via `PasteString`, blink timer, splash window with timer/click dismissal, wire log toggle in the Help menu, connect-anyway prompt on certificate failure with save-to-profile, IND$FILE transfer dialog and progress (add `StartTransferAsync` to `IEmulatorSession`; b3270 `Transfer(...)` keywords: `direction`, `hostfile`, `localfile`, `host`, `mode`, `cr`, `remap`, `exist`, `recfm`, `lrecl`, `blksize`, `allocation`, `primaryspace`, `secondaryspace`; progress arrives as `ft` indications with `state` of `awaiting`, `running` (with `bytes`), `aborting`, `complete` (with `success` and `text`)), keymap cross-check against wc3270 and Vista defaults.
- **Milestone 3, distribution:** Linux x64/arm64 b3270 builds (same static-OpenSSL staging trick, `ldd` gate, oldest supported glibc), Windows x64 via the upstream MinGW cross build (`./configure --host=x86_64-w64-mingw32`, Schannel, no OpenSSL), Windows arm64 (ship the x64 b3270 under emulation unless an llvm-mingw arm64 build proves easy), `dotnet publish` self-contained for all six runtime identifiers, macOS app bundle, GitHub Actions with the native verification gates, tagged releases, scheduled integration run against a Hercules TK4-/TK5 container.
