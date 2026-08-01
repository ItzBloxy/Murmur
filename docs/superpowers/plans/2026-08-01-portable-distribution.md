# Murmur Portable Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a self-contained zip that another person can unzip and run on their own Windows machine — no installer, no .NET install, no CUDA toolkit, no administrator rights.

**Architecture:** The application is already close to portable. Three small code changes clarify what it reports about itself, and the packaging is a single `dotnet publish` driven by one MSBuild property. A post-publish target removes both carriers of CUDA — the `native/cuda12` redistributables and the `Whisper.net.Runtime.Cuda12` package's own folder — leaving Vulkan as the GPU backend and the CPU runtime as a fallback. The development machine's build is untouched and keeps CUDA.

**Tech Stack:** .NET 10 self-contained publish (`win-x64`), MSBuild targets, Whisper.net runtime packages.

**Spec:** `docs/superpowers/specs/2026-08-01-portable-distribution-design.md`

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Murmur.Core/Settings.cs` | Modify | `Device` default becomes `"auto"` |
| `src/Murmur.Platform/WhisperEngine.cs` | Modify | Extract `PrefersGpu` as a testable static; use it |
| `src/Murmur.Core/FileDiagnosticLog.cs` | — | Already writes the session header; no change needed |
| `src/Murmur.App/Program.cs` | Modify | Log the app version alongside the other session facts |
| `src/Murmur.App/Murmur.App.csproj` | Modify | `Version`, conditional CUDA copy, README copy, strip target |
| `packaging/README.txt` | Create | Ships inside the zip |
| `.gitignore` | Modify | Exclude `dist/` |
| `tests/Murmur.Core.Tests/SettingsStoreTests.cs` | Modify | Default is now `"auto"` |
| `tests/Murmur.Core.Tests/FileDiagnosticLogTests.cs` | Create | Session header content |
| `tests/Murmur.Integration.Tests/WhisperEngineDeviceTests.cs` | Create | `PrefersGpu` mapping, including legacy `"cuda"` |

`PrefersGpu` is extracted rather than left inline because it is the one piece of the device
setting with behaviour worth pinning down — specifically that an existing `"cuda"` settings
file keeps preferring the GPU after the rename.

---

## Task 1: Rename the device setting to auto/cpu

The setting defaults to `"cuda"`, but since the backend fix it only ever meant "prefer a
GPU" — the real backend is whatever Whisper.net loads. A portable build would report
`device: cuda` while running Vulkan, which is the exact confusion that hid the CPU fallback
for months.

**Files:**
- Modify: `src/Murmur.Core/Settings.cs`
- Modify: `src/Murmur.Platform/WhisperEngine.cs`
- Test: `tests/Murmur.Core.Tests/SettingsStoreTests.cs`
- Test: `tests/Murmur.Integration.Tests/WhisperEngineDeviceTests.cs`

- [ ] **Step 1: Write the failing test for the device mapping**

Create `tests/Murmur.Integration.Tests/WhisperEngineDeviceTests.cs`:

```csharp
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class WhisperEngineDeviceTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    public void Auto_prefers_the_gpu(string device)
    {
        Assert.True(WhisperEngine.PrefersGpu(device));
    }

    /// <summary>
    /// Settings files written before the rename say "cuda". They must keep preferring the
    /// GPU rather than silently dropping to the CPU path, which would be a 40x regression
    /// with no visible cause.
    /// </summary>
    [Theory]
    [InlineData("cuda")]
    [InlineData("Cuda")]
    public void Legacy_cuda_still_prefers_the_gpu(string device)
    {
        Assert.True(WhisperEngine.PrefersGpu(device));
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("CPU")]
    [InlineData("Cpu")]
    public void Cpu_is_the_only_value_that_declines_the_gpu(string device)
    {
        Assert.False(WhisperEngine.PrefersGpu(device));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj --filter-class "*WhisperEngineDeviceTests"`
Expected: FAIL — `'WhisperEngine' does not contain a definition for 'PrefersGpu'`.

- [ ] **Step 3: Extract PrefersGpu in WhisperEngine**

In `src/Murmur.Platform/WhisperEngine.cs`, add this next to the other public members
(directly above `public void Pin()`):

```csharp
    /// <summary>
    /// Whether a configured device string asks for a GPU. Only "cpu" declines; everything
    /// else, including settings files still saying "cuda" from before the rename, prefers
    /// a GPU and lets Whisper.net pick whichever backend actually loads.
    /// </summary>
    public static bool PrefersGpu(string device) =>
        !device.Equals("cpu", StringComparison.OrdinalIgnoreCase);
```

Then replace the inline check inside `EnsureLoadedAsync`:

```csharp
            var preferGpu = PrefersGpu(device) && !UsingCpuFallback;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj --filter-class "*WhisperEngineDeviceTests"`
Expected: PASS — 8 tests passed.

- [ ] **Step 5: Change the Settings default**

In `src/Murmur.Core/Settings.cs`, replace the `Device` property:

```csharp
    /// <summary>
    /// "auto" or "cpu". "auto" lets Whisper.net load the best backend available — CUDA if
    /// its runtime is present, otherwise Vulkan, otherwise CPU. It deliberately does not
    /// name a backend: the setting records a preference, and only the "backend=" line in
    /// the log says what actually loaded. A legacy value of "cuda" is read as "auto".
    /// </summary>
    public string Device { get; init; } = "auto";
```

- [ ] **Step 6: Update the settings default test**

In `tests/Murmur.Core.Tests/SettingsStoreTests.cs`, inside
`Load_returns_defaults_when_file_missing`, replace the device assertion:

```csharp
        Assert.Equal("auto", settings.Device);
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test --project tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj`
Expected: PASS — 78 tests passed.

Run: `dotnet test --project tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj`
Expected: PASS — 87 total, 83 succeeded, 4 skipped.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "refactor: rename the device setting from cuda/cpu to auto/cpu"
```

---

## Task 2: Log the application version

When someone sends a log, the build that produced it should not be a guess.

**Files:**
- Modify: `src/Murmur.App/Murmur.App.csproj`
- Modify: `src/Murmur.App/Program.cs`
- Test: `tests/Murmur.Core.Tests/FileDiagnosticLogTests.cs`

- [ ] **Step 1: Write the failing test**

`FileDiagnosticLog` has no tests at all. Create
`tests/Murmur.Core.Tests/FileDiagnosticLogTests.cs`:

```csharp
using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class FileDiagnosticLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-log-tests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_dir, "murmur.log");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Creates_its_directory_on_construction()
    {
        _ = new FileDiagnosticLog(LogPath);

        Assert.True(Directory.Exists(_dir));
    }

    [Fact]
    public void Session_header_records_every_fact_it_is_given()
    {
        var log = new FileDiagnosticLog(LogPath);

        log.WriteSessionHeader([("version", "1.2.3"), ("hotkey", "RControlKey")]);

        var contents = File.ReadAllText(LogPath);
        Assert.Contains("SESSION START", contents);
        Assert.Contains("version", contents);
        Assert.Contains("1.2.3", contents);
        Assert.Contains("RControlKey", contents);
    }

    [Fact]
    public void Entries_carry_their_category()
    {
        var log = new FileDiagnosticLog(LogPath);

        log.Write("whisper", "backend=Vulkan");

        Assert.Contains("[whisper", File.ReadAllText(LogPath));
    }

    [Fact]
    public void Writing_appends_rather_than_replacing()
    {
        var log = new FileDiagnosticLog(LogPath);

        log.Write("one", "first line");
        log.Write("two", "second line");

        var contents = File.ReadAllText(LogPath);
        Assert.Contains("first line", contents);
        Assert.Contains("second line", contents);
    }

    /// <summary>Diagnostics must never break the thing they are diagnosing.</summary>
    [Fact]
    public void An_unwritable_path_does_not_throw()
    {
        var log = new FileDiagnosticLog(Path.Combine(_dir, "nested", "murmur.log"));
        File.WriteAllText(Path.Combine(_dir, "nested", "murmur.log"), "");

        using var holder = File.Open(Path.Combine(_dir, "nested", "murmur.log"), FileMode.Open, FileAccess.Write, FileShare.None);

        log.Write("test", "this cannot be written while the file is locked");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj --filter-class "*FileDiagnosticLogTests"`
Expected: PASS — these describe existing behaviour, so they should pass immediately. If any
fail, the logger is not behaving as documented and that is worth knowing before shipping it
to someone else.

- [ ] **Step 3: Give the app a version number**

In `src/Murmur.App/Murmur.App.csproj`, add to the existing `<PropertyGroup>`:

```xml
    <Version>1.0.0</Version>
```

- [ ] **Step 4: Log the version**

In `src/Murmur.App/Program.cs`, add the `System.Reflection` using at the top:

```csharp
using System.Reflection;
```

Then add a version line as the first fact in the session header, replacing the
`("executable", ...)` line with these two:

```csharp
            ("version", typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"),
            ("executable", Application.ExecutablePath),
```

- [ ] **Step 5: Verify the version reaches the log**

Run: `dotnet build`
Expected: `Build succeeded`, 0 warnings.

Run the app, then read the header:

```bash
dotnet run --project src/Murmur.App
```

Quit from the tray, then check `%LOCALAPPDATA%\Murmur\murmur.log` contains a `version` line
reading `1.0.0`. Expected: `  version               : 1.0.0`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test --project tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj`
Expected: PASS — 83 tests passed.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: record the app version in the session log"
```

---

## Task 3: The Portable property and the CUDA strip

The only hard requirement of the whole plan. Two independent pieces carry CUDA and removing
one is not enough.

**Files:**
- Modify: `.gitignore`
- Modify: `src/Murmur.App/Murmur.App.csproj`

- [ ] **Step 1: Keep publish output out of git — do this before the first publish**

This task publishes at Step 5 and commits at Step 7 with `git add -A`. Without the ignore
in place first, that commit would drag roughly 270 MB of build output into history.

In `.gitignore`, add below the existing `artifacts/` line:

```gitignore
dist/
```

- [ ] **Step 2: Condition the native CUDA copy on a normal build**

In `src/Murmur.App/Murmur.App.csproj`, replace the existing `native/cuda12` ItemGroup:

```xml
  <!--
    NVIDIA's CUDA 12 runtime, bundled so the GPU backend works without a toolkit install.
    Absent, Whisper.net falls back to Vulkan. See native/README.md.
    Excluded from portable builds: 752 MB that only helps NVIDIA cards which already have
    a working Vulkan driver. See docs/superpowers/specs/2026-08-01-portable-distribution-design.md.
  -->
  <ItemGroup Condition="'$(Portable)' != 'true'">
    <None Include="..\..\native\cuda12\*.dll" CopyToOutputDirectory="PreserveNewest" Visible="false" />
  </ItemGroup>
```

- [ ] **Step 3: Add the post-publish strip target**

In the same file, before the closing `</Project>`:

```xml
  <!--
    The Whisper.net.Runtime.Cuda12 package contributes runtimes/cuda12 regardless of the
    ItemGroup above, so a portable publish has to remove both carriers.

    This runs after publish rather than conditioning the PackageReference itself. A
    conditional PackageReference reads better but alters the NuGet restore graph, making the
    output depend on whether restore last ran with the same property — a publish that skips
    restore would silently ship 1.8 GB of CUDA. Deleting from the publish directory cannot
    be defeated by a stale assets file.

    (Note: XML comments cannot contain a double hyphen, so the restore-skipping flag is
    described in words rather than written out.)
  -->
  <Target Name="StripCudaFromPortablePublish" AfterTargets="Publish" Condition="'$(Portable)' == 'true'">
    <ItemGroup>
      <PortableCudaLeftovers Include="$(PublishDir)cudart64_12.dll" />
      <PortableCudaLeftovers Include="$(PublishDir)cublas64_12.dll" />
      <PortableCudaLeftovers Include="$(PublishDir)cublasLt64_12.dll" />
      <PortableCudaLeftovers Include="$(PublishDir)runtimes\cuda12\**\*" />
    </ItemGroup>
    <Delete Files="@(PortableCudaLeftovers)" />
    <RemoveDir Directories="$(PublishDir)runtimes\cuda12" />
    <Message Importance="high" Text="Portable build: removed the CUDA 12 backend, leaving Vulkan and CPU." />
  </Target>
```

- [ ] **Step 4: Verify a normal build still bundles CUDA**

Run: `dotnet build`
Expected: `Build succeeded`. Then confirm the development build is unchanged:

```bash
ls src/Murmur.App/bin/Debug/net10.0-windows/cublasLt64_12.dll
```

Expected: the file exists. The development machine keeps its CUDA backend.

- [ ] **Step 5: Verify a portable publish removes it**

```bash
dotnet publish src/Murmur.App -c Release -r win-x64 --self-contained true -p:Portable=true -o dist/Murmur
```

Expected: build succeeds and prints
`Portable build: removed the CUDA 12 backend, leaving Vulkan and CPU.`

- [ ] **Step 6: Assert every CUDA artifact is gone**

```bash
ls dist/Murmur/cublasLt64_12.dll dist/Murmur/cublas64_12.dll dist/Murmur/cudart64_12.dll dist/Murmur/runtimes/cuda12 2>&1
```

Expected: every one reports "No such file or directory". If any survives, the strip target
did not match and the zip would carry 1.8 GB of unusable CUDA.

- [ ] **Step 7: Commit**

Confirm nothing under `dist/` is staged before committing — if anything appears, Step 1 was
skipped. Scope the check to the path rather than grepping for the word: this plan's own
filename contains "distribution", so a bare `grep dist` reports a false positive.

```bash
git status --short -- dist/
```

Expected: no output at all.

```bash
git add -A && git commit -m "build: add a Portable property that strips CUDA from published output"
```

---

## Task 4: The README that ships with it

**Files:**
- Create: `packaging/README.txt`
- Modify: `src/Murmur.App/Murmur.App.csproj`

- [ ] **Step 1: Write the README**

Create `packaging/README.txt`:

```text
Murmur - local speech to text
=============================

Hold or tap a key, talk, and what you said is typed into whatever window you
were already using. Everything runs on your own machine.


RUNNING IT
----------

1. Unzip this folder anywhere you like. There is nothing to install.

2. Run Murmur.App.exe.

3. Windows will almost certainly say "Windows protected your PC".
   Click "More info", then "Run anyway".

   This happens because the app is not code-signed, and because it installs a
   global keyboard hook so it can notice your hotkey from any application.
   That is the same Windows API a keylogger uses, so antivirus software may
   flag it too. Being up front about this seems better than letting you
   discover a security warning and wonder.

   What it actually does with that hook: watches for one key, and nothing
   else. See PRIVACY below.

4. The first launch downloads the speech model, about 870 MB. Progress shows
   in the tray icon's tooltip. The hotkey will say "still downloading" until
   it finishes. This happens once.

5. A microphone icon appears in your system tray. That is the whole app.


USING IT
--------

  Tap Right Ctrl        start recording; tap again to stop and insert
  Hold Right Ctrl       record while held; release to insert

A small panel appears near the bottom of the screen while it is listening,
showing what it has heard so far. Your text is inserted when you stop.

Right-click the tray icon for:

  Language              Danish or English. DEFAULTS TO DANISH - change it
                        here if you speak English.
  Insert text           "When I stop" gives the best accuracy. "As I speak"
                        types each phrase as you pause, but the model sees
                        less context so punctuation is rougher.
  Hotkey...             press any key to rebind
  Start with Windows
  Preload model         load it now so the first dictation is instant
  Release GPU now       free the graphics memory, e.g. before a game
  Open diagnostic log
  Quit


REQUIREMENTS
------------

  Windows 10 or later, 64-bit
  A microphone
  About 1 GB free disk space for the model
  A graphics card from roughly 2012 onward, or it falls back to the CPU

Any NVIDIA, AMD or Intel GPU with current drivers will do. Nothing to
install - it uses Vulkan, which ships with your display driver.

Without a usable GPU it still works, just slowly: several seconds per
dictation instead of a fraction of one.


PRIVACY
-------

Everything is local. Your voice never leaves the machine.

The speech model runs on your own hardware. The only time Murmur uses the
network at all is the one-time model download on first launch. There is no
account, no telemetry, and no audio is stored - it is held in memory during
a dictation and discarded afterwards.


WHERE IT KEEPS THINGS
---------------------

  %LOCALAPPDATA%\Murmur\settings.json   your settings
  %LOCALAPPDATA%\Murmur\murmur.log      diagnostic log
  %LOCALAPPDATA%\Murmur\models\         the downloaded speech model

To remove Murmur completely: untick "Start with Windows" if you enabled it,
quit, then delete this folder and %LOCALAPPDATA%\Murmur.


IF SOMETHING IS WRONG
---------------------

Open the diagnostic log from the tray menu and look for the line reading
"backend=".

  backend=Vulkan   good, running on your graphics card
  backend=Cuda12   good, running on your graphics card
  backend=Cpu      the GPU could not be used; expect several seconds per
                   dictation

The log also records which microphone was used and the peak audio level of
each recording. A peak near zero means the wrong microphone is selected in
Windows, or it is muted.

If nothing is typed at all, check the log for a line saying focus changed -
Murmur refuses to type into a window other than the one you started in, and
puts the text on your clipboard instead.
```

- [ ] **Step 2: Copy it into portable publishes**

In `src/Murmur.App/Murmur.App.csproj`, add before the closing `</Project>`:

```xml
  <ItemGroup Condition="'$(Portable)' == 'true'">
    <None Include="..\..\packaging\README.txt" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Verify it lands in the publish output**

```bash
dotnet publish src/Murmur.App -c Release -r win-x64 --self-contained true -p:Portable=true -o dist/Murmur && ls dist/Murmur/README.txt
```

Expected: `dist/Murmur/README.txt` exists.

- [ ] **Step 4: Verify the condition actually gates it**

Checking a plain `dotnet build` would prove nothing here: `CopyToPublishDirectory` only acts
during publish, so the README would be absent whether the condition works or not. The
meaningful test is a **non-portable publish**, which also confirms the CUDA condition from
Task 3 behaves in the opposite direction:

```bash
dotnet publish src/Murmur.App -c Release -r win-x64 --self-contained true -o dist/_nonportable
```

```bash
ls dist/_nonportable/README.txt 2>&1; ls dist/_nonportable/cublasLt64_12.dll
```

Expected: `README.txt` reports "No such file or directory", and `cublasLt64_12.dll` exists.
Together those show `Portable` genuinely drives both conditions rather than one of them
happening to be inert.

```bash
rm -rf dist/_nonportable
```

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "docs: add the README that ships inside the portable zip"
```

---

## Task 5: Produce the zip and record real sizes

**Files:** none — `dist/` was already gitignored in Task 3 Step 1.

- [ ] **Step 1: Build the portable output from clean**

```bash
rm -rf dist && dotnet publish src/Murmur.App -c Release -r win-x64 --self-contained true -p:Portable=true -o dist/Murmur
```

Expected: `Portable build: removed the CUDA 12 backend, leaving Vulkan and CPU.`

- [ ] **Step 2: Measure the folder**

```bash
du -sm dist/Murmur && du -sm dist/Murmur/runtimes/*
```

Expected: the folder is in the region of 270 MB, with `runtimes/vulkan` around 116 MB and
no `runtimes/cuda12`. Record the real numbers — the spec's figures are estimates and
section 5 of the spec says they are to be replaced by measurement.

- [ ] **Step 3: Create the zip**

Run in PowerShell:

```powershell
Compress-Archive -Path "D:\whisper\dist\Murmur\*" -DestinationPath "D:\whisper\dist\Murmur-portable.zip" -Force
```

- [ ] **Step 4: Measure the zip**

```bash
du -sm dist/Murmur-portable.zip
```

Expected: roughly 150 MB. Record the real figure.

- [ ] **Step 5: Update the spec with measured sizes**

In `docs/superpowers/specs/2026-08-01-portable-distribution-design.md`, replace the
"Expected contents" table's estimate column with the measured values, and change the
sentence below it from "These are projections from measured package sizes" to a statement
of what was measured, on which date.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "build: document the portable zip command and measured sizes"
```

---

## Task 6: Run the verification checklist

The spec commits to executing this rather than merely listing it. Five checks; none needs a
second machine.

**Files:** none — this task verifies what the previous tasks built.

- [ ] **Step 1: Stop any running instance and clear the log**

Run in PowerShell (a stale instance holds the single-instance mutex, so a new one would
exit immediately and you would be testing nothing):

```powershell
Get-Process -Name 'Murmur.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\Murmur\murmur.log" -ErrorAction SilentlyContinue
Start-Process "D:\whisper\dist\Murmur\Murmur.App.exe"
```

- [ ] **Step 2: Check the backend it actually loaded**

Press the hotkey once and speak a sentence, so the model loads. Then in PowerShell:

```powershell
Select-String -Path "$env:LOCALAPPDATA\Murmur\murmur.log" -Pattern 'backend='
```

Expected: `backend=Vulkan`. **Not** `Cuda12` — that would mean the strip failed and the
build is still carrying CUDA. **Not** `Cpu` — that would mean Vulkan failed to initialise.

This is the check that matters, because the app reports what it loaded rather than what it
requested.

- [ ] **Step 3: Confirm there is no dependency on the source tree**

Copy the folder somewhere unrelated and run it from there. In PowerShell:

```powershell
Get-Process -Name 'Murmur.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item -Recurse -Force "D:\whisper\dist\Murmur" "$env:USERPROFILE\murmur-portable-check"
Start-Process "C:\murmur-portable-check\Murmur.App.exe"
```

Dictate once and confirm text is inserted. This proves the build does not reach back into
`native/cuda12` or anything else in the repository.

- [ ] **Step 4: Confirm it is genuinely self-contained**

```bash
ls dist/Murmur/coreclr.dll dist/Murmur/hostfxr.dll dist/Murmur/hostpolicy.dll
```

Expected: all three exist. Their presence is what means the recipient needs no .NET install.

- [ ] **Step 5: Confirm CUDA is absent**

```bash
ls dist/Murmur/cublasLt64_12.dll dist/Murmur/cublas64_12.dll dist/Murmur/cudart64_12.dll dist/Murmur/runtimes/cuda12 2>&1
```

Expected: every one reports "No such file or directory".

- [ ] **Step 6: Clean up the temporary copy**

```powershell
Get-Process -Name 'Murmur.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Recurse -Force "$env:USERPROFILE\murmur-portable-check"
```

- [ ] **Step 7: Record the results**

Append a short "Verification results" section to
`docs/superpowers/specs/2026-08-01-portable-distribution-design.md` recording the backend
observed, the measured sizes, and the date. State plainly that the friend's GTX hardware
and SmartScreen behaviour on his machine remain unverified, as section 8 of the spec
already says.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "docs: record portable build verification results"
```

---

## Done

`dist/Murmur-portable.zip` can be sent to another person. They unzip it, click through
SmartScreen, wait once for the model, and dictate. The development machine's build is
unchanged and keeps CUDA.
