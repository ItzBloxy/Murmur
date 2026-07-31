# Murmur Tray Dictation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows tray application that transcribes speech locally with Whisper and inserts the text into whatever application has focus, driven by a single configurable hotkey that supports both tap-to-toggle and hold-to-talk.

**Architecture:** Three production projects. `Murbur.Core` targets `net10.0` with zero platform dependencies and holds the state machine, gesture recognition, hallucination filtering and settings — this is where nearly all the logic and nearly all the tests live. `Murmur.Platform` targets `net10.0-windows` and holds the impure implementations: the keyboard hook, WASAPI capture, the Whisper engine and the text injector. `Murmur.App` is the WinForms host: tray icon, overlay window and composition root. Core cannot reference Windows-only APIs because its target framework forbids it — the boundary is compiler-enforced, not a convention.

**Tech Stack:** .NET 10, WinForms (host only), Whisper.net 1.9.1 with `Whisper.net.Runtime.Cuda`, NAudio 2.3.0, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-08-01-tray-dictation-design.md`

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Murmur.Core/Settings.cs` | Settings record and defaults |
| `src/Murmur.Core/SettingsStore.cs` | JSON load/save, atomic write, corrupt-file recovery |
| `src/Murmur.Core/GestureRecognizer.cs` | Raw key transitions → start/stop commands |
| `src/Murmur.Core/HallucinationFilter.cs` | Rejects Whisper's silence artifacts |
| `src/Murmur.Core/TranscribedSegment.cs` | Text + no-speech probability |
| `src/Murmur.Core/DictationState.cs` | Idle / Recording / Transcribing |
| `src/Murmur.Core/Abstractions/*.cs` | One interface per file |
| `src/Murmur.Core/DictationController.cs` | The state machine; owns all sequencing |
| `src/Murmur.Platform/ModelStore.cs` | Model download, resume, size verification |
| `src/Murmur.Platform/WhisperEngine.cs` | Whisper.net wrapper, model lifecycle, serialization |
| `src/Murmur.Platform/SileroVoiceSegmenter.cs` | VAD phrase-boundary detection |
| `src/Murmur.Platform/WasapiAudioCapture.cs` | Microphone → 16 kHz mono float32 |
| `src/Murmur.Platform/LowLevelHotkeySource.cs` | `WH_KEYBOARD_LL` hook, swallow rule, watchdog |
| `src/Murmur.Platform/HotkeySwallowRule.cs` | Pure decision: swallow or pass through |
| `src/Murmur.Platform/Win32TextInjector.cs` | Clipboard paste / typing, modifier clearing |
| `src/Murmur.Platform/RunKeyStartupRegistrar.cs` | Registry Run key |
| `src/Murmur.App/OverlayWindow.cs` | Click-through, non-focusing preview pill |
| `src/Murmur.App/TrayShell.cs` | `NotifyIcon`, context menu, icon states |
| `src/Murmur.App/HotkeyCaptureDialog.cs` | Rebinding UI |
| `src/Murmur.App/TrayIcons.cs` | Generates the three tray icons |
| `src/Murmur.App/Program.cs` | Single-instance mutex, composition root |

`HotkeySwallowRule` lives in Platform rather than Core because it is expressed in terms of `System.Windows.Forms.Keys`, but it is a pure static function and is unit-tested from the integration test project.

---

## Task 1: Solution scaffold

**Files:**
- Create: `Murmur.sln`, `.gitignore`, `Directory.Build.props`
- Create: `src/Murmur.Core/Murmur.Core.csproj`
- Create: `src/Murmur.Platform/Murmur.Platform.csproj`
- Create: `src/Murmur.App/Murmur.App.csproj`
- Create: `tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj`
- Create: `tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj`

- [ ] **Step 1: Create the .gitignore**

Create `.gitignore`:

```gitignore
bin/
obj/
.vs/
*.user
TestResults/
artifacts/
```

- [ ] **Step 2: Create Directory.Build.props**

Create `Directory.Build.props` at the repository root:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create the projects**

Run each command from the repository root:

```bash
dotnet new classlib -n Murmur.Core -o src/Murmur.Core -f net10.0
```

```bash
dotnet new classlib -n Murmur.Platform -o src/Murmur.Platform -f net10.0-windows
```

```bash
dotnet new winforms -n Murmur.App -o src/Murmur.App -f net10.0-windows
```

The xUnit v3 templates do not ship with the SDK, so install them first:

```bash
dotnet new install xunit.v3.templates
```

```bash
dotnet new xunit3 -n Murmur.Core.Tests -o tests/Murmur.Core.Tests -f net10.0
```

```bash
dotnet new xunit3 -n Murmur.Integration.Tests -o tests/Murmur.Integration.Tests -f net10.0-windows
```

If the template install fails or `xunit3` is not found, create plain class libraries instead
and add the packages by hand — the result is identical:

```bash
dotnet new classlib -n Murmur.Core.Tests -o tests/Murmur.Core.Tests -f net10.0 && dotnet add tests/Murmur.Core.Tests package xunit.v3 --version 3.2.2 && dotnet add tests/Murmur.Core.Tests package Microsoft.NET.Test.Sdk --version 18.8.1 && dotnet add tests/Murmur.Core.Tests package xunit.runner.visualstudio --version 3.1.5
```

Delete the placeholder classes the templates generate:

```bash
rm -f src/Murmur.Core/Class1.cs src/Murmur.Platform/Class1.cs
```

- [ ] **Step 4: Create the solution and add projects**

```bash
dotnet new sln -n Murmur && dotnet sln add src/Murmur.Core src/Murmur.Platform src/Murmur.App tests/Murmur.Core.Tests tests/Murmur.Integration.Tests
```

- [ ] **Step 5: Wire project references**

```bash
dotnet add src/Murmur.Platform reference src/Murmur.Core && dotnet add src/Murmur.App reference src/Murmur.Core src/Murmur.Platform && dotnet add tests/Murmur.Core.Tests reference src/Murmur.Core && dotnet add tests/Murmur.Integration.Tests reference src/Murmur.Core src/Murmur.Platform
```

- [ ] **Step 6: Add NuGet packages**

```bash
dotnet add src/Murmur.Platform package Whisper.net --version 1.9.1 && dotnet add src/Murmur.Platform package Whisper.net.Runtime.Cuda --version 1.9.1 && dotnet add src/Murmur.Platform package Whisper.net.Runtime --version 1.9.1 && dotnet add src/Murmur.Platform package NAudio --version 2.3.0
```

`Whisper.net.Runtime` is included alongside the CUDA runtime so the CPU fallback path has native binaries available.

```bash
dotnet add tests/Murmur.Core.Tests package Microsoft.Extensions.TimeProvider.Testing --version 10.8.0
```

- [ ] **Step 7: Enable WinForms in Murmur.App only**

Confirm `src/Murmur.App/Murmur.App.csproj` contains `<UseWindowsForms>true</UseWindowsForms>` and `<OutputType>WinExe</OutputType>`. The `winforms` template sets both. Add `<ApplicationManifest>` is not needed.

- [ ] **Step 8: Verify the solution builds**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors. Warnings are errors, so any warning fails the build — fix before continuing.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "chore: scaffold Murmur solution with Core/Platform/App projects"
```

---

## Task 2: Settings and SettingsStore

**Files:**
- Create: `src/Murmur.Core/Settings.cs`
- Create: `src/Murmur.Core/SettingsStore.cs`
- Test: `tests/Murmur.Core.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Murmur.Core.Tests/SettingsStoreTests.cs`:

```csharp
using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-tests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_dir, name);

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new SettingsStore(PathFor("settings.json"));

        var settings = store.Load();

        Assert.Equal("da", settings.Language);
        Assert.Equal("RControlKey", settings.Hotkey);
        Assert.Equal(400, settings.HoldThresholdMs);
        Assert.Equal(5, settings.IdleUnloadMinutes);
        Assert.Equal(5, settings.MaxRecordingMinutes);
        Assert.Equal(500, settings.VadSilenceMs);
        Assert.False(settings.StartWithWindows);
        Assert.Equal("cuda", settings.Device);
    }

    [Fact]
    public void Save_then_Load_round_trips_every_field()
    {
        var path = PathFor("settings.json");
        var store = new SettingsStore(path);
        var original = new Settings
        {
            Language = "en",
            Hotkey = "F9",
            HoldThresholdMs = 250,
            IdleUnloadMinutes = 2,
            MaxRecordingMinutes = 10,
            VadSilenceMs = 700,
            StartWithWindows = true,
            Device = "cpu",
        };

        store.Save(original);
        var loaded = new SettingsStore(path).Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Save_creates_the_directory_when_it_does_not_exist()
    {
        var path = Path.Combine(_dir, "nested", "deeper", "settings.json");
        var store = new SettingsStore(path);

        store.Save(new Settings { Language = "en" });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Load_backs_up_corrupt_file_and_returns_defaults()
    {
        var path = PathFor("settings.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("da", settings.Language);
        Assert.True(File.Exists(path + ".corrupt"), "corrupt file should be preserved for inspection");
    }

    [Fact]
    public void Load_treats_a_json_null_document_as_corrupt()
    {
        var path = PathFor("settings.json");
        File.WriteAllText(path, "null");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("da", settings.Language);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Save_overwrites_an_existing_file()
    {
        var path = PathFor("settings.json");
        var store = new SettingsStore(path);

        store.Save(new Settings { Language = "da" });
        store.Save(new Settings { Language = "en" });

        Assert.Equal("en", store.Load().Language);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Murmur.Core.Tests`
Expected: FAIL — compilation errors, `The type or namespace name 'Settings' could not be found`.

- [ ] **Step 3: Write Settings**

Create `src/Murmur.Core/Settings.cs`:

```csharp
namespace Murmur.Core;

/// <summary>Persisted user configuration. Defaults match the spec.</summary>
public sealed record Settings
{
    /// <summary>Whisper language code: "da" or "en".</summary>
    public string Language { get; init; } = "da";

    /// <summary>Name of a <c>System.Windows.Forms.Keys</c> value, e.g. "RControlKey".</summary>
    public string Hotkey { get; init; } = "RControlKey";

    public int HoldThresholdMs { get; init; } = 400;
    public int IdleUnloadMinutes { get; init; } = 5;
    public int MaxRecordingMinutes { get; init; } = 5;
    public int VadSilenceMs { get; init; } = 500;
    public bool StartWithWindows { get; init; }

    /// <summary>"cuda" or "cpu".</summary>
    public string Device { get; init; } = "cuda";
}
```

- [ ] **Step 4: Write SettingsStore**

Create `src/Murmur.Core/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace Murmur.Core;

/// <summary>
/// Loads and saves <see cref="Settings"/> as JSON. A corrupt file is moved aside rather
/// than thrown at the user — configuration must never block startup.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Path => path;

    public Settings Load()
    {
        if (!File.Exists(path)) return new Settings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Settings>(json, Options) ?? throw new JsonException("null document");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryBackup();
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }

    private void TryBackup()
    {
        try { File.Move(path, path + ".corrupt", overwrite: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Murmur.Core.Tests`
Expected: PASS — 6 tests passed.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add Settings record and SettingsStore with corrupt-file recovery"
```

---

## Task 3: GestureRecognizer

This is the tap-vs-hold state machine from the spec. It is a pure function over timestamped
key transitions, so it needs no host and no hardware.

**Files:**
- Create: `src/Murmur.Core/GestureRecognizer.cs`
- Test: `tests/Murmur.Core.Tests/GestureRecognizerTests.cs`

- [ ] **Step 1: Write the failing tests**

These cover **every cell** of the spec's state table, including the two easily-missed
rows: the trailing key-up that arrives in `Idle` after a toggle-stop, and key-up in
`Toggled`.

Create `tests/Murmur.Core.Tests/GestureRecognizerTests.cs`:

```csharp
using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class GestureRecognizerTests
{
    private static GestureRecognizer Recognizer() => new(holdThresholdMs: 400);

    [Fact]
    public void Key_down_from_idle_starts_recording()
    {
        var r = Recognizer();

        var action = r.Handle(KeyTransition.Down, 0);

        Assert.Equal(GestureAction.StartRecording, action);
        Assert.Equal(GestureState.Undetermined, r.State);
    }

    [Fact]
    public void Key_up_in_idle_is_ignored()
    {
        var r = Recognizer();

        var action = r.Handle(KeyTransition.Up, 0);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Quick_release_is_a_tap_and_keeps_recording()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);

        var action = r.Handle(KeyTransition.Up, 1399);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Toggled, r.State);
    }

    [Fact]
    public void Release_at_the_threshold_is_a_hold_and_stops()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);

        var action = r.Handle(KeyTransition.Up, 1400);

        Assert.Equal(GestureAction.StopRecording, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Long_release_is_a_hold_and_stops()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);

        var action = r.Handle(KeyTransition.Up, 5000);

        Assert.Equal(GestureAction.StopRecording, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Second_tap_stops_on_key_down_not_key_up()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 100);   // tap -> Toggled

        var action = r.Handle(KeyTransition.Down, 3000);

        Assert.Equal(GestureAction.StopRecording, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Trailing_key_up_after_a_toggle_stop_is_ignored()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 100);     // tap -> Toggled
        r.Handle(KeyTransition.Down, 3000);  // stop -> Idle

        var action = r.Handle(KeyTransition.Up, 3050);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Key_up_in_toggled_is_ignored()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 100);   // tap -> Toggled

        var action = r.Handle(KeyTransition.Up, 200);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Toggled, r.State);
    }

    [Theory]
    [InlineData(GestureState.Idle)]
    [InlineData(GestureState.Undetermined)]
    [InlineData(GestureState.Toggled)]
    public void Key_repeat_never_changes_state(GestureState target)
    {
        var r = Recognizer();
        if (target is GestureState.Undetermined or GestureState.Toggled) r.Handle(KeyTransition.Down, 0);
        if (target is GestureState.Toggled) r.Handle(KeyTransition.Up, 50);
        Assert.Equal(target, r.State);

        var action = r.Handle(KeyTransition.Repeat, 900);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(target, r.State);
    }

    [Fact]
    public void Repeats_do_not_extend_the_hold_measurement()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);
        r.Handle(KeyTransition.Repeat, 1200);
        r.Handle(KeyTransition.Repeat, 1300);

        var action = r.Handle(KeyTransition.Up, 1399);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Toggled, r.State);
    }

    [Fact]
    public void A_full_tap_cycle_can_repeat()
    {
        var r = Recognizer();

        Assert.Equal(GestureAction.StartRecording, r.Handle(KeyTransition.Down, 0));
        Assert.Equal(GestureAction.None, r.Handle(KeyTransition.Up, 50));
        Assert.Equal(GestureAction.StopRecording, r.Handle(KeyTransition.Down, 2000));
        Assert.Equal(GestureAction.None, r.Handle(KeyTransition.Up, 2050));
        Assert.Equal(GestureAction.StartRecording, r.Handle(KeyTransition.Down, 4000));
    }

    [Fact]
    public void Reset_returns_to_idle_from_any_state()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 50);
        Assert.Equal(GestureState.Toggled, r.State);

        r.Reset();

        Assert.Equal(GestureState.Idle, r.State);
        Assert.Equal(GestureAction.StartRecording, r.Handle(KeyTransition.Down, 1000));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter GestureRecognizerTests`
Expected: FAIL — `The type or namespace name 'GestureRecognizer' could not be found`.

- [ ] **Step 3: Write GestureRecognizer**

Create `src/Murmur.Core/GestureRecognizer.cs`:

```csharp
namespace Murmur.Core;

/// <summary>A raw key transition for the bound hotkey, as reported by the platform hook.</summary>
public enum KeyTransition
{
    Down,
    Up,
    /// <summary>An auto-repeat key-down while the key is already held. Always ignored.</summary>
    Repeat,
}

public enum GestureState
{
    Idle,
    /// <summary>Recording, but we do not yet know whether this is a tap or a hold.</summary>
    Undetermined,
    /// <summary>Recording, latched on by a tap. Ends on the next key-down.</summary>
    Toggled,
}

public enum GestureAction
{
    None,
    StartRecording,
    StopRecording,
}

/// <summary>
/// Turns raw key transitions into recording commands.
///
/// Recording starts on key-<em>down</em> in both gestures, so no audio is lost while we are
/// still determining which gesture is happening. A release before the threshold is a tap
/// and latches recording on; a release at or after the threshold is a hold and ends it.
/// </summary>
public sealed class GestureRecognizer(int holdThresholdMs)
{
    private long _downAtMs;

    public GestureState State { get; private set; } = GestureState.Idle;

    public void Reset() => State = GestureState.Idle;

    public GestureAction Handle(KeyTransition transition, long timestampMs)
    {
        if (transition == KeyTransition.Repeat) return GestureAction.None;

        switch (State)
        {
            case GestureState.Idle:
                if (transition != KeyTransition.Down) return GestureAction.None;
                _downAtMs = timestampMs;
                State = GestureState.Undetermined;
                return GestureAction.StartRecording;

            case GestureState.Undetermined:
                if (transition != KeyTransition.Up) return GestureAction.None;
                if (timestampMs - _downAtMs < holdThresholdMs)
                {
                    State = GestureState.Toggled;
                    return GestureAction.None;
                }
                State = GestureState.Idle;
                return GestureAction.StopRecording;

            case GestureState.Toggled:
                if (transition != KeyTransition.Down) return GestureAction.None;
                State = GestureState.Idle;
                return GestureAction.StopRecording;

            default:
                return GestureAction.None;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Murmur.Core.Tests --filter GestureRecognizerTests`
Expected: PASS — 14 tests passed (the `Theory` contributes 3).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add GestureRecognizer with full tap/hold state coverage"
```

---

## Task 4: HallucinationFilter

Whisper emits stock subtitle phrases on near-silent audio. This is the filter that keeps
them out of the user's textbox.

**Files:**
- Create: `src/Murmur.Core/TranscribedSegment.cs`
- Create: `src/Murmur.Core/HallucinationFilter.cs`
- Test: `tests/Murmur.Core.Tests/HallucinationFilterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Murmur.Core.Tests/HallucinationFilterTests.cs`:

```csharp
using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class HallucinationFilterTests
{
    private readonly HallucinationFilter _filter = new();

    [Theory]
    [InlineData("Thank you for watching!")]
    [InlineData("thanks for watching")]
    [InlineData("  Thank you for watching. ")]
    [InlineData("Tak fordi du så med!")]
    [InlineData("Tak fordi du så med.")]
    [InlineData("Undertekster af Nicolai Winther")]
    [InlineData("Undertekster af Seriefan")]
    [InlineData("Tekstet af Sofie Boysen")]
    [InlineData("Please subscribe")]
    public void Rejects_known_silence_artifacts(string text)
    {
        Assert.True(_filter.ShouldReject(text, noSpeechProbability: 0.1f));
    }

    [Theory]
    [InlineData("Send the report to Anna before Friday.")]
    [InlineData("Jeg kommer hjem klokken syv.")]
    [InlineData("Thanks for watching the deployment logs closely.")]
    public void Keeps_real_speech(string text)
    {
        Assert.False(_filter.ShouldReject(text, noSpeechProbability: 0.1f));
    }

    [Fact]
    public void Rejects_anything_above_the_no_speech_threshold()
    {
        Assert.True(_filter.ShouldReject("Send the report to Anna.", noSpeechProbability: 0.9f));
    }

    [Fact]
    public void Keeps_text_at_exactly_the_threshold()
    {
        Assert.False(_filter.ShouldReject("Send the report.", HallucinationFilter.NoSpeechThreshold));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("...")]
    public void Rejects_empty_and_punctuation_only_text(string text)
    {
        Assert.True(_filter.ShouldReject(text, noSpeechProbability: 0.0f));
    }

    [Fact]
    public void Join_concatenates_surviving_segments_with_single_spaces()
    {
        var segments = new[]
        {
            new TranscribedSegment(" Hello there. ", 0.05f),
            new TranscribedSegment("Thank you for watching", 0.05f),
            new TranscribedSegment("How are you?", 0.05f),
        };

        Assert.Equal("Hello there. How are you?", _filter.Join(segments));
    }

    [Fact]
    public void Join_returns_empty_when_every_segment_is_rejected()
    {
        var segments = new[]
        {
            new TranscribedSegment("Tak fordi du så med", 0.05f),
            new TranscribedSegment("Anything at all", 0.99f),
        };

        Assert.Equal("", _filter.Join(segments));
    }

    [Fact]
    public void Join_handles_an_empty_sequence()
    {
        Assert.Equal("", _filter.Join([]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter HallucinationFilterTests`
Expected: FAIL — `The type or namespace name 'HallucinationFilter' could not be found`.

- [ ] **Step 3: Write TranscribedSegment**

Create `src/Murmur.Core/TranscribedSegment.cs`:

```csharp
namespace Murmur.Core;

/// <summary>One segment returned by the transcription engine.</summary>
/// <param name="Text">The transcribed text, as produced by Whisper.</param>
/// <param name="NoSpeechProbability">
/// Whisper's own confidence that this segment contains no speech. High values are the
/// primary signal that a segment is a silence artifact rather than something the user said.
/// </param>
public sealed record TranscribedSegment(string Text, float NoSpeechProbability);
```

- [ ] **Step 4: Write HallucinationFilter**

Create `src/Murmur.Core/HallucinationFilter.cs`:

```csharp
using System.Text;

namespace Murmur.Core;

/// <summary>
/// Rejects Whisper's silence artifacts. Whisper is trained on subtitle data and reliably
/// emits stock phrases when handed near-silent audio — in a dictation tool that means junk
/// text appearing when the user said nothing.
/// </summary>
public sealed class HallucinationFilter
{
    public const float NoSpeechThreshold = 0.6f;

    /// <summary>Normalized text that is always an artifact, never real dictation.</summary>
    private static readonly HashSet<string> ExactArtifacts = new(StringComparer.Ordinal)
    {
        "thank you for watching",
        "thanks for watching",
        "thank you for watching this video",
        "please subscribe",
        "subscribe",
        "you",
        "bye",
        "tak fordi du sa med",
        "tak for at se med",
        "tak fordi i sa med",
    };

    /// <summary>Prefixes of subtitle credit lines, which carry an arbitrary name after them.</summary>
    private static readonly string[] ArtifactPrefixes =
    [
        "undertekster af",
        "undertekst af",
        "tekstet af",
        "subtitles by",
        "subtitled by",
    ];

    public bool ShouldReject(string text, float noSpeechProbability)
    {
        if (noSpeechProbability > NoSpeechThreshold) return true;

        var normalized = Normalize(text);
        if (normalized.Length == 0) return true;
        if (ExactArtifacts.Contains(normalized)) return true;

        foreach (var prefix in ArtifactPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>Joins the segments that survive filtering into the text to insert.</summary>
    public string Join(IEnumerable<TranscribedSegment> segments)
    {
        var kept = segments
            .Where(s => !ShouldReject(s.Text, s.NoSpeechProbability))
            .Select(s => s.Text.Trim())
            .Where(t => t.Length > 0);

        return string.Join(" ", kept);
    }

    /// <summary>
    /// Lowercases, strips punctuation and collapses whitespace, and folds the Danish
    /// letters å/æ/ø to a/ae/o so blocklist entries stay readable in plain ASCII.
    /// </summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var raw in text)
        {
            var c = char.ToLowerInvariant(raw);
            var mapped = c switch
            {
                'å' => "a",
                'æ' => "ae",
                'ø' => "o",
                _ when char.IsLetterOrDigit(c) => c.ToString(),
                _ when char.IsWhiteSpace(c) => " ",
                _ => "",
            };

            if (mapped.Length == 0) continue;
            if (mapped == " ")
            {
                if (lastWasSpace) continue;
                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            builder.Append(mapped);
        }

        return builder.ToString().Trim();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Murmur.Core.Tests --filter HallucinationFilterTests`
Expected: PASS — 21 tests passed.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add HallucinationFilter rejecting Whisper silence artifacts"
```

---

## Task 5: Core abstractions

Interfaces only, no logic, no tests. These are the seam that lets `DictationController` be
tested without a microphone, a GPU or a window.

**Files:**
- Create: `src/Murmur.Core/DictationState.cs`
- Create: `src/Murmur.Core/Abstractions/IHotkeySource.cs`
- Create: `src/Murmur.Core/Abstractions/IAudioCapture.cs`
- Create: `src/Murmur.Core/Abstractions/IVoiceSegmenter.cs`
- Create: `src/Murmur.Core/Abstractions/ITranscriptionEngine.cs`
- Create: `src/Murmur.Core/Abstractions/ITextInjector.cs`
- Create: `src/Murmur.Core/Abstractions/IOverlay.cs`
- Create: `src/Murmur.Core/Abstractions/INotifier.cs`
- Create: `src/Murmur.Core/Abstractions/IStartupRegistrar.cs`

- [ ] **Step 1: Write DictationState**

Create `src/Murmur.Core/DictationState.cs`:

```csharp
namespace Murmur.Core;

public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
}
```

- [ ] **Step 2: Write IHotkeySource**

Create `src/Murmur.Core/Abstractions/IHotkeySource.cs`:

```csharp
namespace Murmur.Core.Abstractions;

/// <summary>
/// Reports raw key transitions for the bound hotkey. Deliberately does not interpret
/// gestures — that logic lives in <see cref="GestureRecognizer"/> so it can be unit-tested.
/// </summary>
public interface IHotkeySource : IDisposable
{
    /// <summary>Fires with the transition and a monotonic timestamp in milliseconds.</summary>
    event Action<KeyTransition, long>? KeyEvent;

    void Start();

    /// <param name="hotkey">Name of a <c>System.Windows.Forms.Keys</c> value.</param>
    void Rebind(string hotkey);
}
```

- [ ] **Step 3: Write IAudioCapture**

Create `src/Murmur.Core/Abstractions/IAudioCapture.cs`:

```csharp
namespace Murmur.Core.Abstractions;

/// <summary>Captures the default input device as 16 kHz mono float32.</summary>
public interface IAudioCapture : IDisposable
{
    event Action<float[]>? FramesAvailable;

    /// <summary>Fires when capture breaks mid-recording, e.g. the microphone was unplugged.</summary>
    event Action<string>? Failed;

    void Start();
    void Stop();
}
```

- [ ] **Step 4: Write IVoiceSegmenter**

Create `src/Murmur.Core/Abstractions/IVoiceSegmenter.cs`:

```csharp
namespace Murmur.Core.Abstractions;

/// <summary>
/// Accumulates audio and detects phrase boundaries at silences, so the overlay can show a
/// running preview. Also retains the complete buffer for the final full-context pass.
/// </summary>
public interface IVoiceSegmenter
{
    /// <summary>Emits a completed phrase, as 16 kHz mono float32 samples.</summary>
    event Action<float[]>? SegmentCompleted;

    void Reset();
    void Push(float[] samples);

    /// <summary>Everything captured since the last <see cref="Reset"/>.</summary>
    float[] FullBuffer { get; }
}
```

- [ ] **Step 5: Write ITranscriptionEngine**

Create `src/Murmur.Core/Abstractions/ITranscriptionEngine.cs`:

```csharp
namespace Murmur.Core.Abstractions;

public enum TranscriptionQuality
{
    /// <summary>Greedy decoding for the live overlay. Speed over accuracy.</summary>
    Preview,
    /// <summary>Beam search over the whole buffer. The text the user actually receives.</summary>
    Final,
}

public interface ITranscriptionEngine : IAsyncDisposable
{
    bool IsLoaded { get; }

    /// <summary>True while the idle-unload timer is suspended by an explicit Preload.</summary>
    bool IsPinned { get; }

    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
    Task UnloadAsync();

    void Pin();
    void Unpin();

    Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples,
        string language,
        TranscriptionQuality quality,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Write ITextInjector**

Create `src/Murmur.Core/Abstractions/ITextInjector.cs`:

```csharp
namespace Murmur.Core.Abstractions;

public enum InjectionResult
{
    Injected,
    /// <summary>Focus moved during dictation, so the text went to the clipboard instead.</summary>
    CopiedToClipboardFocusChanged,
    Failed,
}

public interface ITextInjector
{
    /// <summary>The window handle to remember at record start.</summary>
    nint GetForegroundWindow();

    /// <param name="expectedWindow">
    /// The handle captured at record start. If the foreground window no longer matches,
    /// the implementation must not type — it copies to the clipboard instead.
    /// </param>
    InjectionResult Inject(string text, nint expectedWindow);
}
```

- [ ] **Step 7: Write IOverlay, INotifier and IStartupRegistrar**

Create `src/Murmur.Core/Abstractions/IOverlay.cs`:

```csharp
namespace Murmur.Core.Abstractions;

public interface IOverlay
{
    void Show();
    void SetPreview(string text);
    void SetTranscribing();
    void Hide();
}
```

Create `src/Murmur.Core/Abstractions/INotifier.cs`:

```csharp
namespace Murmur.Core.Abstractions;

public interface INotifier
{
    void Notify(string title, string message);
    void SetState(DictationState state, bool modelLoaded, bool modelPinned);
}
```

Create `src/Murmur.Core/Abstractions/IStartupRegistrar.cs`:

```csharp
namespace Murmur.Core.Abstractions;

public interface IStartupRegistrar
{
    bool IsEnabled { get; }
    void Enable(string executablePath);
    void Disable();
}
```

- [ ] **Step 8: Verify it builds**

Run: `dotnet build src/Murmur.Core`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: add Core abstractions for platform seams"
```

---

## Task 6: DictationController

The heart of the application. Everything it depends on is an interface, so every sequencing
rule in the spec is testable here.

**Files:**
- Create: `src/Murmur.Core/DictationController.cs`
- Test: `tests/Murmur.Core.Tests/Fakes.cs`
- Test: `tests/Murmur.Core.Tests/DictationControllerTests.cs`

- [ ] **Step 1: Write the fakes**

Hand-written fakes rather than a mocking library — they are easier to read and let tests
drive events directly.

Create `tests/Murmur.Core.Tests/Fakes.cs`:

```csharp
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Tests;

public sealed class FakeAudioCapture : IAudioCapture
{
    public event Action<float[]>? FramesAvailable;
    public event Action<string>? Failed;

    public bool IsCapturing { get; private set; }
    public int StartCount { get; private set; }
    public Exception? ThrowOnStart { get; set; }

    public void Start()
    {
        if (ThrowOnStart is not null) throw ThrowOnStart;
        IsCapturing = true;
        StartCount++;
    }

    public void Stop() => IsCapturing = false;

    public void EmitFrames(float[] samples) => FramesAvailable?.Invoke(samples);
    public void EmitFailure(string message) => Failed?.Invoke(message);

    public void Dispose() { }
}

public sealed class FakeVoiceSegmenter : IVoiceSegmenter
{
    private readonly List<float> _buffer = [];

    public event Action<float[]>? SegmentCompleted;

    public int ResetCount { get; private set; }
    public float[] FullBuffer => [.. _buffer];

    public void Reset()
    {
        _buffer.Clear();
        ResetCount++;
    }

    public void Push(float[] samples) => _buffer.AddRange(samples);

    public void EmitSegment(float[] samples) => SegmentCompleted?.Invoke(samples);
}

public sealed class FakeTranscriptionEngine : ITranscriptionEngine
{
    private readonly List<(TranscriptionQuality Quality, int SampleCount)> _calls = [];

    public bool IsLoaded { get; private set; }
    public bool IsPinned { get; private set; }
    public int LoadCount { get; private set; }
    public int UnloadCount { get; private set; }
    public Exception? ThrowOnLoad { get; set; }
    public string? LastLanguage { get; private set; }

    public IReadOnlyList<(TranscriptionQuality Quality, int SampleCount)> Calls => _calls;

    /// <summary>Result returned per quality level. Defaults to a single plain segment.</summary>
    public Func<TranscriptionQuality, IReadOnlyList<TranscribedSegment>> Results { get; set; } =
        quality => [new TranscribedSegment(quality == TranscriptionQuality.Final ? "final text" : "preview", 0.05f)];

    /// <summary>Set to block a transcription until the test releases it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnLoad is not null) return Task.FromException(ThrowOnLoad);
        if (!IsLoaded) LoadCount++;
        IsLoaded = true;
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        if (IsLoaded) UnloadCount++;
        IsLoaded = false;
        return Task.CompletedTask;
    }

    public void Pin() => IsPinned = true;
    public void Unpin() => IsPinned = false;

    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples, string language, TranscriptionQuality quality, CancellationToken cancellationToken = default)
    {
        _calls.Add((quality, samples.Length));
        LastLanguage = language;

        if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return Results(quality);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeTextInjector : ITextInjector
{
    public nint ForegroundWindow { get; set; } = 1;
    public InjectionResult NextResult { get; set; } = InjectionResult.Injected;
    public List<(string Text, nint ExpectedWindow)> Injections { get; } = [];

    public nint GetForegroundWindow() => ForegroundWindow;

    public InjectionResult Inject(string text, nint expectedWindow)
    {
        Injections.Add((text, expectedWindow));
        return NextResult;
    }
}

public sealed class FakeOverlay : IOverlay
{
    public bool IsVisible { get; private set; }
    public string LastPreview { get; private set; } = "";
    public bool TranscribingShown { get; private set; }

    public void Show() { IsVisible = true; TranscribingShown = false; }
    public void SetPreview(string text) => LastPreview = text;
    public void SetTranscribing() => TranscribingShown = true;
    public void Hide() => IsVisible = false;
}

public sealed class FakeNotifier : INotifier
{
    public List<string> Messages { get; } = [];
    public DictationState LastState { get; private set; } = DictationState.Idle;
    public bool LastModelLoaded { get; private set; }
    public bool LastModelPinned { get; private set; }

    public void Notify(string title, string message) => Messages.Add(message);

    public void SetState(DictationState state, bool modelLoaded, bool modelPinned)
    {
        LastState = state;
        LastModelLoaded = modelLoaded;
        LastModelPinned = modelPinned;
    }
}
```

- [ ] **Step 2: Write the failing controller tests**

Create `tests/Murmur.Core.Tests/DictationControllerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Tests;

public sealed class DictationControllerTests
{
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeVoiceSegmenter _segmenter = new();
    private readonly FakeTranscriptionEngine _engine = new();
    private readonly FakeTextInjector _injector = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeTimeProvider _time = new();
    private Settings _settings = new();

    private DictationController Create() => new(
        _audio, _segmenter, _engine, _injector, _overlay, _notifier, () => _settings, _time);

    /// <summary>Tap on, tap off — the common case.</summary>
    private async Task<DictationController> RecordAndStopAsync(DictationController? existing = null)
    {
        var controller = existing ?? Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);
        return controller;
    }

    [Fact]
    public async Task Tap_starts_recording_and_shows_the_overlay()
    {
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        Assert.Equal(DictationState.Recording, controller.State);
        Assert.True(_audio.IsCapturing);
        Assert.True(_overlay.IsVisible);
        Assert.Equal(1, _segmenter.ResetCount);
    }

    [Fact]
    public async Task Recording_captures_the_target_window_at_start()
    {
        _injector.ForegroundWindow = 42;
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _injector.ForegroundWindow = 99;   // focus moves after we started
        _segmenter.Push(new float[16000]);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        Assert.Equal(42, _injector.Injections.Single().ExpectedWindow);
    }

    [Fact]
    public async Task Second_tap_transcribes_the_full_buffer_and_injects()
    {
        var controller = await RecordAndStopAsync();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.False(_audio.IsCapturing);
        Assert.False(_overlay.IsVisible);
        Assert.Equal("final text", _injector.Injections.Single().Text);
        Assert.Contains(_engine.Calls, c => c.Quality == TranscriptionQuality.Final && c.SampleCount == 16000);
    }

    [Fact]
    public async Task Hold_records_until_release_then_injects()
    {
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        Assert.Equal(DictationState.Recording, controller.State);
        _segmenter.Push(new float[8000]);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 1000);   // past the 400ms threshold

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal("final text", _injector.Injections.Single().Text);
    }

    [Fact]
    public async Task Model_loading_begins_while_recording_not_after()
    {
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        Assert.True(_engine.IsLoaded);
        Assert.Equal(DictationState.Recording, controller.State);
    }

    [Fact]
    public async Task Completed_segments_stream_into_the_overlay_preview()
    {
        _engine.Results = _ => [new TranscribedSegment("hello there", 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("hello there", _overlay.LastPreview);
        Assert.Contains(_engine.Calls, c => c.Quality == TranscriptionQuality.Preview);
    }

    [Fact]
    public async Task Preview_segments_accumulate_in_order()
    {
        var texts = new Queue<string>(["first", "second"]);
        _engine.Results = _ => [new TranscribedSegment(texts.Dequeue(), 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);
        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("first second", _overlay.LastPreview);
    }

    [Fact]
    public async Task Hallucinated_previews_are_not_shown()
    {
        _engine.Results = _ => [new TranscribedSegment("Tak fordi du så med", 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("", _overlay.LastPreview);
    }

    [Fact]
    public async Task Focus_change_routes_to_the_clipboard_with_a_notification()
    {
        _injector.NextResult = InjectionResult.CopiedToClipboardFocusChanged;

        await RecordAndStopAsync();

        Assert.Contains(_notifier.Messages, m => m.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Empty_transcript_injects_nothing_and_notifies_nothing()
    {
        _engine.Results = _ => [];

        var controller = await RecordAndStopAsync();

        Assert.Empty(_injector.Injections);
        Assert.Empty(_notifier.Messages);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Fully_hallucinated_transcript_injects_nothing()
    {
        _engine.Results = _ => [new TranscribedSegment("Thank you for watching", 0.05f)];

        await RecordAndStopAsync();

        Assert.Empty(_injector.Injections);
        Assert.Empty(_notifier.Messages);
    }

    [Fact]
    public async Task Empty_audio_buffer_skips_transcription_entirely()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);   // no audio pushed

        Assert.Empty(_injector.Injections);
        Assert.DoesNotContain(_engine.Calls, c => c.Quality == TranscriptionQuality.Final);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Microphone_failure_aborts_without_injecting()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);

        _audio.EmitFailure("Device disconnected");

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Empty(_injector.Injections);
        Assert.Contains(_notifier.Messages, m => m.Contains("Device disconnected"));
    }

    [Fact]
    public async Task Recording_can_restart_after_a_microphone_failure()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _audio.EmitFailure("Device disconnected");

        await controller.HandleKeyEventAsync(KeyTransition.Down, 5000);

        Assert.Equal(DictationState.Recording, controller.State);
    }

    [Fact]
    public async Task Audio_start_failure_notifies_and_stays_idle()
    {
        _audio.ThrowOnStart = new InvalidOperationException("No input device");
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Contains(_notifier.Messages, m => m.Contains("No input device"));
    }

    [Fact]
    public async Task Max_recording_duration_stops_and_finalizes()
    {
        _settings = _settings with { MaxRecordingMinutes = 1 };
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);

        _time.Advance(TimeSpan.FromMinutes(1));
        await controller.WaitForIdleAsync();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal("final text", _injector.Injections.Single().Text);
        Assert.Contains(_notifier.Messages, m => m.Contains("maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Idle_timer_unloads_the_model()
    {
        _settings = _settings with { IdleUnloadMinutes = 5 };
        await RecordAndStopAsync();
        Assert.True(_engine.IsLoaded);

        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Yield();

        Assert.False(_engine.IsLoaded);
        Assert.Equal(1, _engine.UnloadCount);
    }

    [Fact]
    public async Task Pinning_suspends_the_idle_unload()
    {
        var controller = Create();
        await controller.PreloadAsync();
        Assert.True(_engine.IsLoaded);
        Assert.True(_engine.IsPinned);

        _time.Advance(TimeSpan.FromHours(1));
        await Task.Yield();

        Assert.True(_engine.IsLoaded);
        Assert.Equal(0, _engine.UnloadCount);
    }

    [Fact]
    public async Task Dictating_does_not_clear_the_pin()
    {
        var controller = Create();
        await controller.PreloadAsync();

        await RecordAndStopAsync(controller);
        _time.Advance(TimeSpan.FromHours(1));
        await Task.Yield();

        Assert.True(_engine.IsPinned);
        Assert.True(_engine.IsLoaded);
    }

    [Fact]
    public async Task Release_gpu_unloads_immediately_and_clears_the_pin()
    {
        var controller = Create();
        await controller.PreloadAsync();

        await controller.ReleaseGpuAsync();

        Assert.False(_engine.IsLoaded);
        Assert.False(_engine.IsPinned);
    }

    [Fact]
    public async Task Model_load_failure_notifies_and_does_not_crash_the_recording()
    {
        _engine.ThrowOnLoad = new InvalidOperationException("CUDA out of memory");
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        await controller.WaitForIdleAsync();

        Assert.Equal(DictationState.Recording, controller.State);
        Assert.Contains(_notifier.Messages, m => m.Contains("CUDA out of memory"));
    }

    [Fact]
    public async Task Stopping_cancels_an_in_flight_preview()
    {
        var gate = new TaskCompletionSource();
        _engine.Gate = gate;
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);

        var preview = controller.HandleSegmentAsync(new float[4000]);
        var stop = controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        await preview;                 // must complete without throwing
        _engine.Gate = null;
        gate.SetResult();
        await stop;

        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Language_setting_is_passed_to_the_engine()
    {
        _settings = _settings with { Language = "en" };

        await RecordAndStopAsync();

        Assert.Equal("en", _engine.LastLanguage);
    }

    [Fact]
    public async Task Status_reports_recording_then_idle()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        Assert.Equal(DictationState.Recording, _notifier.LastState);

        _segmenter.Push(new float[16000]);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        Assert.Equal(DictationState.Idle, _notifier.LastState);
        Assert.True(_notifier.LastModelLoaded);
    }

    [Fact]
    public async Task Audio_frames_flow_into_the_segmenter()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        _audio.EmitFrames(new float[1600]);

        Assert.Equal(1600, _segmenter.FullBuffer.Length);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter DictationControllerTests`
Expected: FAIL — `The type or namespace name 'DictationController' could not be found`.

- [ ] **Step 4: Write DictationController**

Create `src/Murmur.Core/DictationController.cs`:

```csharp
using System.Text;
using Murmur.Core.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Owns the dictation lifecycle: Idle → Recording → Transcribing → Idle. Every dependency
/// is an interface, so this class — which holds all the ordering rules — is fully testable
/// without a microphone, a GPU or a window.
/// </summary>
public sealed class DictationController : IAsyncDisposable
{
    private readonly IAudioCapture _audio;
    private readonly IVoiceSegmenter _segmenter;
    private readonly ITranscriptionEngine _engine;
    private readonly ITextInjector _injector;
    private readonly IOverlay _overlay;
    private readonly INotifier _notifier;
    private readonly Func<Settings> _settings;
    private readonly TimeProvider _time;

    private readonly GestureRecognizer _gestures;
    private readonly HallucinationFilter _filter = new();
    private readonly StringBuilder _preview = new();
    private readonly SemaphoreSlim _transition = new(1, 1);

    private ITimer? _maxDurationTimer;
    private ITimer? _idleTimer;
    private CancellationTokenSource? _previewCancellation;
    private Task _background = Task.CompletedTask;
    private nint _targetWindow;

    public DictationController(
        IAudioCapture audio,
        IVoiceSegmenter segmenter,
        ITranscriptionEngine engine,
        ITextInjector injector,
        IOverlay overlay,
        INotifier notifier,
        Func<Settings> settings,
        TimeProvider time)
    {
        _audio = audio;
        _segmenter = segmenter;
        _engine = engine;
        _injector = injector;
        _overlay = overlay;
        _notifier = notifier;
        _settings = settings;
        _time = time;
        _gestures = new GestureRecognizer(settings().HoldThresholdMs);

        _audio.FramesAvailable += OnFrames;
        _audio.Failed += OnAudioFailed;
        _segmenter.SegmentCompleted += OnSegmentCompleted;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Awaits work started on background paths. Tests use this; production does not need it.</summary>
    public Task WaitForIdleAsync() => _background;

    public async Task HandleKeyEventAsync(KeyTransition transition, long timestampMs)
    {
        switch (_gestures.Handle(transition, timestampMs))
        {
            case GestureAction.StartRecording:
                await StartRecordingAsync();
                break;
            case GestureAction.StopRecording:
                await StopRecordingAsync("");
                break;
        }
    }

    public async Task PreloadAsync()
    {
        try
        {
            await _engine.EnsureLoadedAsync();
            _engine.Pin();
            CancelIdleTimer();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _notifier.Notify("Murmur", $"Could not preload the model: {ex.Message}");
        }
    }

    public async Task ReleaseGpuAsync()
    {
        _engine.Unpin();
        CancelIdleTimer();
        await _engine.UnloadAsync();
        UpdateStatus();
    }

    /// <summary>Transcribes one completed phrase into the live overlay preview.</summary>
    public async Task HandleSegmentAsync(float[] samples)
    {
        if (State != DictationState.Recording) return;

        var cancellation = _previewCancellation;
        if (cancellation is null) return;

        try
        {
            var segments = await _engine.TranscribeAsync(
                samples, _settings().Language, TranscriptionQuality.Preview, cancellation.Token);

            var text = _filter.Join(segments);
            if (text.Length == 0 || State != DictationState.Recording) return;

            if (_preview.Length > 0) _preview.Append(' ');
            _preview.Append(text);
            _overlay.SetPreview(_preview.ToString());
        }
        catch (OperationCanceledException)
        {
            // Expected when the user stops mid-preview.
        }
        catch (Exception)
        {
            // Preview is best-effort. The final pass is what the user actually receives.
        }
    }

    private async Task StartRecordingAsync()
    {
        await _transition.WaitAsync();
        try
        {
            if (State != DictationState.Idle) return;

            _targetWindow = _injector.GetForegroundWindow();
            _preview.Clear();
            _overlay.SetPreview("");
            _segmenter.Reset();

            try
            {
                _audio.Start();
            }
            catch (Exception ex)
            {
                _gestures.Reset();
                _notifier.Notify("Murmur", $"Could not start recording: {ex.Message}");
                return;
            }

            State = DictationState.Recording;
            _previewCancellation = new CancellationTokenSource();
            _overlay.Show();
            CancelIdleTimer();
            UpdateStatus();

            _maxDurationTimer = _time.CreateTimer(
                _ => _background = StopRecordingAsync("Reached the maximum recording length."),
                null,
                TimeSpan.FromMinutes(_settings().MaxRecordingMinutes),
                Timeout.InfiniteTimeSpan);

            _background = LoadModelInBackgroundAsync();
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task LoadModelInBackgroundAsync()
    {
        try
        {
            await _engine.EnsureLoadedAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _notifier.Notify("Murmur", $"Model failed to load: {ex.Message}");
        }
    }

    private async Task StopRecordingAsync(string reason)
    {
        await _transition.WaitAsync();
        try
        {
            if (State != DictationState.Recording) return;

            CancelMaxDurationTimer();
            _audio.Stop();
            _previewCancellation?.Cancel();

            State = DictationState.Transcribing;
            _overlay.SetTranscribing();
            UpdateStatus();

            if (reason.Length > 0) _notifier.Notify("Murmur", reason);

            var buffer = _segmenter.FullBuffer;
            if (buffer.Length == 0) return;

            try
            {
                await _engine.EnsureLoadedAsync();
                var segments = await _engine.TranscribeAsync(
                    buffer, _settings().Language, TranscriptionQuality.Final, CancellationToken.None);

                var text = _filter.Join(segments);
                if (text.Length == 0) return;

                switch (_injector.Inject(text, _targetWindow))
                {
                    case InjectionResult.CopiedToClipboardFocusChanged:
                        _notifier.Notify("Murmur", "Focus changed — the text was copied to your clipboard instead.");
                        break;
                    case InjectionResult.Failed:
                        _notifier.Notify("Murmur", "Could not insert text into the focused window.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _notifier.Notify("Murmur", $"Transcription failed: {ex.Message}");
            }
        }
        finally
        {
            ReturnToIdle();
            _transition.Release();
        }
    }

    private void ReturnToIdle()
    {
        State = DictationState.Idle;
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        _overlay.Hide();
        UpdateStatus();
        StartIdleTimer();
    }

    private void OnFrames(float[] samples)
    {
        if (State == DictationState.Recording) _segmenter.Push(samples);
    }

    private void OnSegmentCompleted(float[] samples) => _background = HandleSegmentAsync(samples);

    private void OnAudioFailed(string message)
    {
        if (State != DictationState.Recording) return;

        CancelMaxDurationTimer();
        _audio.Stop();
        _previewCancellation?.Cancel();
        _gestures.Reset();
        _notifier.Notify("Murmur", $"Recording aborted: {message}");
        ReturnToIdle();
    }

    private void StartIdleTimer()
    {
        CancelIdleTimer();
        if (_engine.IsPinned || !_engine.IsLoaded) return;

        _idleTimer = _time.CreateTimer(
            _ => _background = UnloadIfStillIdleAsync(),
            null,
            TimeSpan.FromMinutes(_settings().IdleUnloadMinutes),
            Timeout.InfiniteTimeSpan);
    }

    private async Task UnloadIfStillIdleAsync()
    {
        if (State != DictationState.Idle || _engine.IsPinned) return;
        await _engine.UnloadAsync();
        UpdateStatus();
    }

    private void CancelIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private void CancelMaxDurationTimer()
    {
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;
    }

    private void UpdateStatus() => _notifier.SetState(State, _engine.IsLoaded, _engine.IsPinned);

    public async ValueTask DisposeAsync()
    {
        _audio.FramesAvailable -= OnFrames;
        _audio.Failed -= OnAudioFailed;
        _segmenter.SegmentCompleted -= OnSegmentCompleted;

        CancelIdleTimer();
        CancelMaxDurationTimer();
        _previewCancellation?.Dispose();
        _transition.Dispose();

        await _engine.DisposeAsync();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Murmur.Core.Tests`
Expected: PASS — all tests green (Settings 6, Gesture 14, Hallucination 21, Controller 25).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add DictationController state machine with full sequencing tests"
```

---

## Task 7: ModelStore

Downloads and caches the Whisper and VAD models under `%LOCALAPPDATA%\Murmur\models\`.

**Files:**
- Create: `src/Murmur.Platform/ModelStore.cs`
- Test: `tests/Murmur.Integration.Tests/ModelStoreTests.cs`

- [ ] **Step 1: Write the failing test**

This test exercises the caching and partial-file logic without hitting the network.

Create `tests/Murmur.Integration.Tests/ModelStoreTests.cs`:

```csharp
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class ModelStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-models", Guid.NewGuid().ToString("N"));

    public ModelStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Returns_the_cached_file_without_downloading()
    {
        var path = Path.Combine(_dir, "ggml-large-v3-turbo-q8_0.bin");
        await File.WriteAllBytesAsync(path, new byte[2048]);
        var store = new ModelStore(_dir);
        var downloadCalled = false;

        var result = await store.EnsureAsync(
            "ggml-large-v3-turbo-q8_0.bin",
            _ => { downloadCalled = true; return Task.FromResult<Stream>(new MemoryStream()); },
            progress: null,
            CancellationToken.None);

        Assert.Equal(path, result);
        Assert.False(downloadCalled);
    }

    [Fact]
    public async Task Downloads_when_missing_and_writes_atomically()
    {
        var store = new ModelStore(_dir);
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        var result = await store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(payload)),
            progress: null,
            CancellationToken.None);

        Assert.True(File.Exists(result));
        Assert.Equal(payload, await File.ReadAllBytesAsync(result));
        Assert.False(File.Exists(result + ".partial"), "the partial file must be cleaned up");
    }

    [Fact]
    public async Task Rejects_a_suspiciously_small_download()
    {
        var store = new ModelStore(_dir);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[10])),
            progress: null,
            CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_dir, "model.bin")));
    }

    [Fact]
    public async Task A_leftover_partial_file_is_discarded_and_redownloaded()
    {
        var partial = Path.Combine(_dir, "model.bin.partial");
        await File.WriteAllBytesAsync(partial, new byte[123]);
        var store = new ModelStore(_dir);
        var payload = new byte[4096];

        var result = await store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(payload)),
            progress: null,
            CancellationToken.None);

        Assert.Equal(4096, new FileInfo(result).Length);
        Assert.False(File.Exists(partial));
    }

    [Fact]
    public async Task Progress_is_reported_during_download()
    {
        var store = new ModelStore(_dir);
        var reports = new List<long>();

        await store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[200_000])),
            progress: new Progress<long>(reports.Add),
            CancellationToken.None);

        // Progress is reported asynchronously; give the synchronization context a chance.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Murmur.Integration.Tests --filter ModelStoreTests`
Expected: FAIL — `The type or namespace name 'ModelStore' could not be found`.

- [ ] **Step 3: Write ModelStore**

Create `src/Murmur.Platform/ModelStore.cs`:

```csharp
namespace Murmur.Platform;

/// <summary>
/// Caches GGML model files on disk. Downloads land in a <c>.partial</c> file that is only
/// moved into place once complete, so an interrupted download can never be mistaken for a
/// usable model.
/// </summary>
public sealed class ModelStore(string directory)
{
    /// <summary>Anything smaller than this is an error page or a truncated transfer, not a model.</summary>
    private const long MinimumPlausibleModelBytes = 1024;

    public string Directory => directory;

    public string PathFor(string fileName) => System.IO.Path.Combine(directory, fileName);

    /// <param name="openStream">Fetches the model. Called only when the file is not cached.</param>
    /// <param name="progress">Receives cumulative bytes written, if supplied.</param>
    public async Task<string> EnsureAsync(
        string fileName,
        Func<CancellationToken, Task<Stream>> openStream,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(directory);

        var finalPath = PathFor(fileName);
        if (File.Exists(finalPath) && new FileInfo(finalPath).Length >= MinimumPlausibleModelBytes)
        {
            return finalPath;
        }

        var partialPath = finalPath + ".partial";
        if (File.Exists(partialPath)) File.Delete(partialPath);

        try
        {
            await using (var source = await openStream(cancellationToken))
            await using (var destination = File.Create(partialPath))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    progress?.Report(written);
                }
            }

            if (new FileInfo(partialPath).Length < MinimumPlausibleModelBytes)
            {
                throw new InvalidDataException(
                    $"Downloaded model '{fileName}' is only {new FileInfo(partialPath).Length} bytes — the transfer failed.");
            }

            File.Move(partialPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
            throw;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Murmur.Integration.Tests --filter ModelStoreTests`
Expected: PASS — 5 tests passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add ModelStore with atomic download and truncation rejection"
```

---

## Task 8: WhisperEngine

**Files:**
- Create: `src/Murmur.Platform/WhisperEngine.cs`
- Test: `tests/Murmur.Integration.Tests/WhisperEngineTests.cs`
- Test: `tests/Murmur.Integration.Tests/AudioFixtures.cs`

- [ ] **Step 1: Write the fixture helper**

Create `tests/Murmur.Integration.Tests/AudioFixtures.cs`:

```csharp
namespace Murmur.Integration.Tests;

public static class AudioFixtures
{
    public const int SampleRate = 16000;

    /// <summary>Digital silence — the input that makes Whisper hallucinate subtitle credits.</summary>
    public static float[] Silence(double seconds) => new float[(int)(SampleRate * seconds)];

    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string Path(string name) => System.IO.Path.Combine(Directory, name);

    public static bool Exists(string name) => File.Exists(Path(name));

    /// <summary>Reads a 16-bit PCM mono WAV at 16 kHz into float samples.</summary>
    public static float[] ReadWav(string name)
    {
        using var reader = new BinaryReader(File.OpenRead(Path(name)));
        var bytes = reader.ReadBytes((int)reader.BaseStream.Length);

        // Locate the "data" chunk rather than assuming a fixed 44-byte header.
        var dataIndex = -1;
        for (var i = 12; i < bytes.Length - 8; i += 2)
        {
            if (bytes[i] == 'd' && bytes[i + 1] == 'a' && bytes[i + 2] == 't' && bytes[i + 3] == 'a')
            {
                dataIndex = i + 8;
                break;
            }
        }

        if (dataIndex < 0) throw new InvalidDataException($"No data chunk in {name}");

        var sampleCount = (bytes.Length - dataIndex) / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, dataIndex + i * 2) / 32768f;
        }

        return samples;
    }
}
```

- [ ] **Step 2: Write the failing tests**

The transcription accuracy tests skip themselves when fixtures are absent, so the suite
stays green on a fresh clone until the recordings are made in Task 18.

Create `tests/Murmur.Integration.Tests/WhisperEngineTests.cs`:

```csharp
using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class WhisperEngineTests : IAsyncLifetime
{
    private WhisperEngine _engine = null!;

    public async ValueTask InitializeAsync()
    {
        var store = new ModelStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models"));

        _engine = new WhisperEngine(store, device: "cuda");
        await _engine.EnsureModelsDownloadedAsync(progress: null, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _engine.DisposeAsync();

    [Fact]
    public async Task Loads_and_reports_loaded()
    {
        await _engine.EnsureLoadedAsync();

        Assert.True(_engine.IsLoaded);
    }

    [Fact]
    public async Task Unload_frees_the_model()
    {
        await _engine.EnsureLoadedAsync();

        await _engine.UnloadAsync();

        Assert.False(_engine.IsLoaded);
    }

    [Fact]
    public async Task Load_unload_reload_works()
    {
        await _engine.EnsureLoadedAsync();
        await _engine.UnloadAsync();
        await _engine.EnsureLoadedAsync();

        Assert.True(_engine.IsLoaded);
    }

    [Fact]
    public void Pin_and_unpin_toggle_the_flag()
    {
        _engine.Pin();
        Assert.True(_engine.IsPinned);

        _engine.Unpin();
        Assert.False(_engine.IsPinned);
    }

    /// <summary>
    /// The hallucination regression test. Digital silence must produce nothing the filter
    /// would let through — this is the single most important assertion in the suite.
    /// </summary>
    [Fact]
    public async Task Silence_produces_no_usable_text()
    {
        await _engine.EnsureLoadedAsync();
        var filter = new HallucinationFilter();

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.Silence(seconds: 4), "da", TranscriptionQuality.Final);

        Assert.Equal("", filter.Join(segments));
    }

    [Fact]
    public async Task Silence_produces_no_usable_text_in_english()
    {
        await _engine.EnsureLoadedAsync();
        var filter = new HallucinationFilter();

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.Silence(seconds: 4), "en", TranscriptionQuality.Final);

        Assert.Equal("", filter.Join(segments));
    }

    [Fact]
    public async Task Transcribes_danish_within_the_word_error_tolerance()
    {
        Assert.SkipUnless(AudioFixtures.Exists("danish.wav"), "Record fixtures first (Task 18).");
        await _engine.EnsureLoadedAsync();

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.ReadWav("danish.wav"), "da", TranscriptionQuality.Final);

        var actual = new HallucinationFilter().Join(segments);
        var expected = await File.ReadAllTextAsync(AudioFixtures.Path("danish.txt"));

        Assert.True(WordErrorRate.Compute(expected, actual) <= 0.15,
            $"WER too high. Expected \"{expected}\", got \"{actual}\".");
    }

    [Fact]
    public async Task Transcribes_english_within_the_word_error_tolerance()
    {
        Assert.SkipUnless(AudioFixtures.Exists("english.wav"), "Record fixtures first (Task 18).");
        await _engine.EnsureLoadedAsync();

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.ReadWav("english.wav"), "en", TranscriptionQuality.Final);

        var actual = new HallucinationFilter().Join(segments);
        var expected = await File.ReadAllTextAsync(AudioFixtures.Path("english.txt"));

        Assert.True(WordErrorRate.Compute(expected, actual) <= 0.10,
            $"WER too high. Expected \"{expected}\", got \"{actual}\".");
    }

    [Fact]
    public async Task Concurrent_transcriptions_are_serialized_not_corrupted()
    {
        await _engine.EnsureLoadedAsync();

        var a = _engine.TranscribeAsync(AudioFixtures.Silence(1), "da", TranscriptionQuality.Preview);
        var b = _engine.TranscribeAsync(AudioFixtures.Silence(1), "da", TranscriptionQuality.Preview);

        await Task.WhenAll(a, b);   // must not throw or deadlock
    }

    [Fact]
    public async Task Unload_during_transcription_waits_rather_than_freeing_the_context()
    {
        await _engine.EnsureLoadedAsync();

        var transcribe = _engine.TranscribeAsync(AudioFixtures.Silence(3), "da", TranscriptionQuality.Final);
        var unload = _engine.UnloadAsync();
        await Task.WhenAll(transcribe, unload);

        Assert.False(_engine.IsLoaded);
    }
}
```

- [ ] **Step 3: Write the word error rate helper**

Create `tests/Murmur.Integration.Tests/WordErrorRate.cs`:

```csharp
namespace Murmur.Integration.Tests;

/// <summary>
/// Levenshtein distance over words, divided by reference length. Used instead of exact
/// string equality because exact matches break on every model update for no real reason.
/// </summary>
public static class WordErrorRate
{
    public static double Compute(string reference, string hypothesis)
    {
        var refWords = Tokenize(reference);
        var hypWords = Tokenize(hypothesis);

        if (refWords.Length == 0) return hypWords.Length == 0 ? 0 : 1;

        var previous = new int[hypWords.Length + 1];
        var current = new int[hypWords.Length + 1];

        for (var j = 0; j <= hypWords.Length; j++) previous[j] = j;

        for (var i = 1; i <= refWords.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= hypWords.Length; j++)
            {
                var cost = refWords[i - 1] == hypWords[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return (double)previous[hypWords.Length] / refWords.Length;
    }

    private static string[] Tokenize(string text) => text
        .ToLowerInvariant()
        .Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Murmur.Integration.Tests --filter WhisperEngineTests`
Expected: FAIL — `The type or namespace name 'WhisperEngine' could not be found`.

- [ ] **Step 5: Write WhisperEngine**

Create `src/Murmur.Platform/WhisperEngine.cs`:

```csharp
using Murmur.Core;
using Murmur.Core.Abstractions;
using Whisper.net;
using Whisper.net.Ggml;

namespace Murmur.Platform;

/// <summary>
/// Wraps Whisper.net and owns the model lifecycle.
///
/// whisper.cpp contexts are not thread-safe, so every call — including unload — is
/// serialized through <see cref="_gate"/>. A Release GPU pressed mid-transcription
/// therefore waits for the pass to finish rather than freeing the context under it.
/// </summary>
public sealed class WhisperEngine(ModelStore store, string device) : ITranscriptionEngine
{
    public const string WhisperModelFileName = "ggml-large-v3-turbo-q8_0.bin";
    public const string VadModelFileName = "ggml-silero-v6.2.0.bin";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private string? _whisperModelPath;
    private string? _vadModelPath;

    public bool IsLoaded => _factory is not null;
    public bool IsPinned { get; private set; }

    /// <summary>Set once the CUDA path has failed and we have fallen back for this session.</summary>
    public bool UsingCpuFallback { get; private set; }

    public string VadModelPath =>
        _vadModelPath ?? throw new InvalidOperationException("Call EnsureModelsDownloadedAsync first.");

    public void Pin() => IsPinned = true;
    public void Unpin() => IsPinned = false;

    /// <summary>Fetches both models if they are not already cached. Safe to call repeatedly.</summary>
    public async Task EnsureModelsDownloadedAsync(IProgress<long>? progress, CancellationToken cancellationToken)
    {
        _whisperModelPath = await store.EnsureAsync(
            WhisperModelFileName,
            ct => WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                GgmlType.LargeV3Turbo, QuantizationType.Q8_0, ct),
            progress,
            cancellationToken);

        _vadModelPath = await store.EnsureAsync(
            VadModelFileName,
            ct => WhisperGgmlDownloader.Default.GetGgmlSileroVadModelAsync(SileroVadType.V6_2_0, ct),
            progress: null,
            cancellationToken);
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsLoaded) return;

            if (_whisperModelPath is null)
            {
                await EnsureModelsDownloadedAsync(progress: null, cancellationToken);
            }

            var preferCuda = device.Equals("cuda", StringComparison.OrdinalIgnoreCase) && !UsingCpuFallback;

            try
            {
                _factory = CreateFactory(_whisperModelPath!, preferCuda);
            }
            catch (Exception) when (preferCuda)
            {
                // VRAM exhausted by a game, or a driver fault. The 9800X3D makes CPU a real fallback.
                UsingCpuFallback = true;
                _factory = CreateFactory(_whisperModelPath!, useCuda: false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static WhisperFactory CreateFactory(string modelPath, bool useCuda)
    {
        var options = new WhisperFactoryOptions
        {
            UseGpu = useCuda,
        };

        return WhisperFactory.FromPath(modelPath, options);
    }

    public async Task UnloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples,
        string language,
        TranscriptionQuality quality,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var factory = _factory ?? throw new InvalidOperationException("Model is not loaded.");

            var builder = factory.CreateBuilder()
                .WithLanguage(language)
                .WithNoSpeechThreshold(HallucinationFilter.NoSpeechThreshold)
                .WithProbabilities();

            builder = quality == TranscriptionQuality.Preview
                ? builder.WithGreedySamplingStrategy()
                : builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5));

            await using var processor = builder.Build();

            var results = new List<TranscribedSegment>();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
            {
                results.Add(new TranscribedSegment(segment.Text, segment.NoSpeechProbability));
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await UnloadAsync();
        _gate.Dispose();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Murmur.Integration.Tests --filter WhisperEngineTests`
Expected: PASS. The first run downloads ~870 MB, so allow several minutes. Two accuracy
tests report as skipped until fixtures exist.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add WhisperEngine with serialized access and CPU fallback"
```

---

## Task 9: SileroVoiceSegmenter

Detects phrase boundaries during recording using Whisper.net's built-in Silero VAD.

**Files:**
- Create: `src/Murmur.Platform/SileroVoiceSegmenter.cs`
- Test: `tests/Murmur.Integration.Tests/SileroVoiceSegmenterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Murmur.Integration.Tests/SileroVoiceSegmenterTests.cs`:

```csharp
using Murmur.Platform;

namespace Murmur.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class SileroVoiceSegmenterTests : IAsyncLifetime
{
    private WhisperEngine _engine = null!;
    private SileroVoiceSegmenter _segmenter = null!;

    public async ValueTask InitializeAsync()
    {
        var store = new ModelStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models"));
        _engine = new WhisperEngine(store, "cuda");
        await _engine.EnsureModelsDownloadedAsync(progress: null, CancellationToken.None);
        _segmenter = new SileroVoiceSegmenter(_engine.VadModelPath, silenceMs: 500);
    }

    public async ValueTask DisposeAsync()
    {
        _segmenter.Dispose();
        await _engine.DisposeAsync();
    }

    [Fact]
    public void FullBuffer_accumulates_everything_pushed()
    {
        _segmenter.Reset();

        _segmenter.Push(new float[1600]);
        _segmenter.Push(new float[3200]);

        Assert.Equal(4800, _segmenter.FullBuffer.Length);
    }

    [Fact]
    public void Reset_clears_the_buffer()
    {
        _segmenter.Push(new float[1600]);

        _segmenter.Reset();

        Assert.Empty(_segmenter.FullBuffer);
    }

    [Fact]
    public void Pure_silence_emits_no_segments()
    {
        var emitted = 0;
        _segmenter.Reset();
        _segmenter.SegmentCompleted += _ => emitted++;

        for (var i = 0; i < 20; i++) _segmenter.Push(AudioFixtures.Silence(0.5));

        Assert.Equal(0, emitted);
    }

    [Fact]
    public void Speech_followed_by_silence_emits_one_segment()
    {
        Assert.SkipUnless(AudioFixtures.Exists("danish.wav"), "Record fixtures first (Task 18).");
        var emitted = new List<float[]>();
        _segmenter.Reset();
        _segmenter.SegmentCompleted += emitted.Add;

        _segmenter.Push(AudioFixtures.ReadWav("danish.wav"));
        _segmenter.Push(AudioFixtures.Silence(1.5));   // trailing silence closes the phrase

        Assert.NotEmpty(emitted);
        Assert.All(emitted, s => Assert.True(s.Length > 0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Murmur.Integration.Tests --filter SileroVoiceSegmenterTests`
Expected: FAIL — `The type or namespace name 'SileroVoiceSegmenter' could not be found`.

- [ ] **Step 3: Write SileroVoiceSegmenter**

Create `src/Murmur.Platform/SileroVoiceSegmenter.cs`:

```csharp
using Murmur.Core.Abstractions;
using Whisper.net;

namespace Murmur.Platform;

/// <summary>
/// Detects phrase boundaries so the overlay can show a running preview, while retaining the
/// complete buffer for the final full-context pass.
///
/// VAD runs over the <em>unconsumed tail</em> only — the audio since the last committed
/// boundary — rather than the whole recording, so cost stays flat regardless of how long
/// the user talks.
/// </summary>
public sealed class SileroVoiceSegmenter : IVoiceSegmenter, IDisposable
{
    private const int SampleRate = 16000;

    /// <summary>Run detection at most this often, to avoid burning CPU on every 100ms frame.</summary>
    private const int AnalyzeEverySamples = SampleRate / 2;

    private readonly WhisperVadFactory _factory;
    private readonly WhisperVadProcessor _processor;
    private readonly int _silenceMs;
    private readonly List<float> _all = [];
    private readonly Lock _sync = new();

    private int _consumed;
    private int _samplesSinceAnalysis;

    public SileroVoiceSegmenter(string vadModelPath, int silenceMs)
    {
        _silenceMs = silenceMs;
        _factory = WhisperVadFactory.FromPath(vadModelPath);
        _processor = _factory.CreateBuilder().Build();
    }

    public event Action<float[]>? SegmentCompleted;

    public float[] FullBuffer
    {
        get { lock (_sync) return [.. _all]; }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _all.Clear();
            _consumed = 0;
            _samplesSinceAnalysis = 0;
            _processor.ResetState();
        }
    }

    public void Push(float[] samples)
    {
        float[]? completed = null;

        lock (_sync)
        {
            _all.AddRange(samples);
            _samplesSinceAnalysis += samples.Length;

            if (_samplesSinceAnalysis < AnalyzeEverySamples) return;
            _samplesSinceAnalysis = 0;

            completed = TryTakeCompletedPhrase();
        }

        if (completed is not null) SegmentCompleted?.Invoke(completed);
    }

    /// <summary>
    /// Returns a phrase if the tail contains speech that has been followed by enough
    /// silence to consider it finished. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private float[]? TryTakeCompletedPhrase()
    {
        var tailLength = _all.Count - _consumed;
        if (tailLength <= 0) return null;

        var tail = new float[tailLength];
        _all.CopyTo(_consumed, tail, 0, tailLength);

        _processor.ResetState();
        var speech = _processor.DetectSpeech(tail);
        if (speech.Count == 0) return null;

        var last = speech[^1];
        var tailDuration = TimeSpan.FromSeconds((double)tailLength / SampleRate);
        var silenceAfter = tailDuration - last.End;

        if (silenceAfter < TimeSpan.FromMilliseconds(_silenceMs)) return null;

        var phraseEnd = (int)(last.End.TotalSeconds * SampleRate);
        if (phraseEnd <= 0 || phraseEnd > tailLength) return null;

        var phrase = new float[phraseEnd];
        Array.Copy(tail, phrase, phraseEnd);
        _consumed += phraseEnd;

        return phrase;
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Murmur.Integration.Tests --filter SileroVoiceSegmenterTests`
Expected: PASS — 3 passed, 1 skipped until fixtures exist.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add SileroVoiceSegmenter with flat-cost tail analysis"
```

---

## Task 10: WasapiAudioCapture

**Files:**
- Create: `src/Murmur.Platform/WasapiAudioCapture.cs`

No automated test — this needs a real microphone and is covered by the manual checklist.

- [ ] **Step 1: Write WasapiAudioCapture**

Create `src/Murmur.Platform/WasapiAudioCapture.cs`:

```csharp
using Murmur.Core.Abstractions;
using NAudio.Wave;

namespace Murmur.Platform;

/// <summary>
/// Captures the default input device and resamples to the 16 kHz mono float32 Whisper
/// expects. Reports device loss through <see cref="Failed"/> rather than throwing on a
/// background thread.
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 16000;

    private WaveInEvent? _capture;
    private BufferedWaveProvider? _buffer;
    private MediaFoundationResampler? _resampler;
    private bool _stopRequested;

    public event Action<float[]>? FramesAvailable;
    public event Action<string>? Failed;

    public void Start()
    {
        Stop();
        _stopRequested = false;

        var capture = new WaveInEvent
        {
            WaveFormat = new WaveFormat(44100, 16, 1),
            BufferMilliseconds = 100,
        };

        _buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
        };

        _resampler = new MediaFoundationResampler(
            _buffer, new WaveFormat(TargetSampleRate, 16, 1))
        {
            ResamplerQuality = 60,
        };

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        _capture = capture;
        capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_buffer is null || _resampler is null) return;

        try
        {
            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // Drain whatever the resampler can produce right now.
            var bytes = new byte[8192];
            int read;
            while ((read = _resampler.Read(bytes, 0, bytes.Length)) > 0)
            {
                var samples = new float[read / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                }

                FramesAvailable?.Invoke(samples);
                if (read < bytes.Length) break;
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopRequested) return;
        Failed?.Invoke(e.Exception?.Message ?? "The input device stopped unexpectedly.");
    }

    public void Stop()
    {
        _stopRequested = true;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch (Exception) { /* already gone */ }
            _capture.Dispose();
            _capture = null;
        }

        _resampler?.Dispose();
        _resampler = null;
        _buffer = null;
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Murmur.Platform`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: add WasapiAudioCapture with 16 kHz mono resampling"
```

---

## Task 11: LowLevelHotkeySource

**Files:**
- Create: `src/Murmur.Platform/HotkeySwallowRule.cs`
- Create: `src/Murmur.Platform/LowLevelHotkeySource.cs`
- Test: `tests/Murmur.Integration.Tests/HotkeySwallowRuleTests.cs`

- [ ] **Step 1: Write the failing test for the swallow rule**

Create `tests/Murmur.Integration.Tests/HotkeySwallowRuleTests.cs`:

```csharp
using System.Windows.Forms;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class HotkeySwallowRuleTests
{
    [Theory]
    [InlineData(Keys.RControlKey)]
    [InlineData(Keys.LControlKey)]
    [InlineData(Keys.RShiftKey)]
    [InlineData(Keys.LShiftKey)]
    [InlineData(Keys.RMenu)]
    [InlineData(Keys.LMenu)]
    [InlineData(Keys.LWin)]
    [InlineData(Keys.RWin)]
    public void Modifier_only_bindings_pass_through(Keys key)
    {
        Assert.False(HotkeySwallowRule.ShouldSwallow(key));
    }

    [Theory]
    [InlineData(Keys.F9)]
    [InlineData(Keys.D)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Pause)]
    [InlineData(Keys.Scroll)]
    [InlineData(Keys.Insert)]
    public void Normal_keys_are_swallowed(Keys key)
    {
        Assert.True(HotkeySwallowRule.ShouldSwallow(key));
    }

    [Fact]
    public void Parse_accepts_the_default_binding()
    {
        Assert.Equal(Keys.RControlKey, HotkeySwallowRule.Parse("RControlKey"));
    }

    [Fact]
    public void Parse_falls_back_to_the_default_on_nonsense()
    {
        Assert.Equal(Keys.RControlKey, HotkeySwallowRule.Parse("NotAKeyName"));
    }

    [Fact]
    public void Parse_accepts_a_function_key()
    {
        Assert.Equal(Keys.F9, HotkeySwallowRule.Parse("F9"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Murmur.Integration.Tests --filter HotkeySwallowRuleTests`
Expected: FAIL — `The type or namespace name 'HotkeySwallowRule' could not be found`.

- [ ] **Step 3: Write HotkeySwallowRule**

Create `src/Murmur.Platform/HotkeySwallowRule.cs`:

```csharp
using System.Windows.Forms;

namespace Murmur.Platform;

/// <summary>
/// Decides whether the hook consumes the hotkey or lets it reach the focused application.
///
/// Modifier-only bindings pass through: a lone Right-Ctrl press is inert in virtually every
/// application, and swallowing it would break combinations like Right-Ctrl+C. Normal keys
/// are swallowed so they do not type a character or fire an application shortcut.
/// </summary>
public static class HotkeySwallowRule
{
    public const Keys Default = Keys.RControlKey;

    private static readonly HashSet<Keys> ModifierKeys =
    [
        Keys.LControlKey, Keys.RControlKey,
        Keys.LShiftKey, Keys.RShiftKey,
        Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin,
    ];

    public static bool ShouldSwallow(Keys key) => !ModifierKeys.Contains(key);

    public static Keys Parse(string name) =>
        Enum.TryParse<Keys>(name, ignoreCase: true, out var key) && Enum.IsDefined(key)
            ? key
            : Default;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Murmur.Integration.Tests --filter HotkeySwallowRuleTests`
Expected: PASS — 17 tests passed.

- [ ] **Step 5: Write LowLevelHotkeySource**

Create `src/Murmur.Platform/LowLevelHotkeySource.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>
/// A <c>WH_KEYBOARD_LL</c> hook on a dedicated thread with its own message pump.
///
/// Windows silently removes hooks whose callback exceeds LowLevelHooksTimeout (~300ms), so
/// the callback only reads the event, decides swallow-or-pass, and raises the event. It
/// never transcribes, allocates large buffers or blocks. A watchdog reinstalls the hook if
/// Windows drops it anyway.
/// </summary>
public sealed class LowLevelHotkeySource : IHotkeySource
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int HC_ACTION = 0;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly LowLevelKeyboardProc _callback;

    private Thread? _thread;
    private nint _hookHandle;
    private uint _threadId;
    private volatile bool _running;
    private volatile bool _keyIsDown;
    private Keys _boundKey = HotkeySwallowRule.Default;

    public LowLevelHotkeySource() => _callback = HookCallback;

    public event Action<KeyTransition, long>? KeyEvent;

    public void Rebind(string hotkey)
    {
        _boundKey = HotkeySwallowRule.Parse(hotkey);
        _keyIsDown = false;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _thread = new Thread(PumpMessages)
        {
            IsBackground = true,
            Name = "Murmur hotkey hook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void PumpMessages()
    {
        _threadId = GetCurrentThreadId();
        Install();

        // Reinstall the hook if Windows drops it for exceeding the callback timeout.
        using var watchdog = new System.Threading.Timer(
            _ => { if (_running && _hookHandle == 0) Install(); },
            null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        while (_running && GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        Uninstall();
    }

    private void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(module.ModuleName), 0);
    }

    private void Uninstall()
    {
        if (_hookHandle == 0) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code != HC_ACTION) return CallNextHookEx(_hookHandle, code, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var key = (Keys)data.vkCode;

        if (key != _boundKey) return CallNextHookEx(_hookHandle, code, wParam, lParam);

        var message = (int)wParam;
        var timestamp = _clock.ElapsedMilliseconds;

        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            var transition = _keyIsDown ? KeyTransition.Repeat : KeyTransition.Down;
            _keyIsDown = true;
            KeyEvent?.Invoke(transition, timestamp);
        }
        else if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            _keyIsDown = false;
            KeyEvent?.Invoke(KeyTransition.Up, timestamp);
        }

        return HotkeySwallowRule.ShouldSwallow(_boundKey)
            ? 1
            : CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    public void Dispose()
    {
        _running = false;
        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string lpModuleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);
}
```

- [ ] **Step 6: Mark the class partial**

`LibraryImport` requires the containing class to be `partial`. Change the declaration to:

```csharp
public sealed partial class LowLevelHotkeySource : IHotkeySource
```

- [ ] **Step 7: Verify it builds**

Run: `dotnet build src/Murmur.Platform`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: add low-level keyboard hook with swallow rule and watchdog"
```

---

## Task 12: Win32TextInjector

**Files:**
- Create: `src/Murmur.Platform/Win32TextInjector.cs`

- [ ] **Step 1: Write Win32TextInjector**

Create `src/Murmur.Platform/Win32TextInjector.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>
/// Inserts transcribed text into the focused application.
///
/// Two safety properties matter here. First, the foreground window is verified against the
/// handle captured at record start — typing into whatever happened to grab focus is the one
/// unrecoverable mistake this app could make. Second, held modifiers are released before
/// pasting: hold-to-talk on the default Right Ctrl binding keeps that key down until
/// insertion begins, and a Ctrl+V sent on top of it lands corrupted.
/// </summary>
public sealed partial class Win32TextInjector : ITextInjector
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int ClipboardRestoreDelayMs = 300;

    private static readonly ushort[] ModifierVirtualKeys =
    [
        0xA0, 0xA1,   // LSHIFT, RSHIFT
        0xA2, 0xA3,   // LCONTROL, RCONTROL
        0xA4, 0xA5,   // LMENU, RMENU
        0x5B, 0x5C,   // LWIN, RWIN
    ];

    public nint GetForegroundWindow() => GetForegroundWindowNative();

    /// <summary>
    /// Runs the injection on a short-lived STA thread. <see cref="Clipboard"/> throws
    /// <see cref="ThreadStateException"/> off an STA thread, and this is reached from the
    /// keyboard hook's thread, not the UI thread. A thread per insertion is free at human
    /// dictation rates and avoids threading a marshaller through the interface.
    /// </summary>
    public InjectionResult Inject(string text, nint expectedWindow)
    {
        var result = InjectionResult.Failed;

        var thread = new Thread(() => result = InjectCore(text, expectedWindow))
        {
            IsBackground = true,
            Name = "Murmur text injection",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static InjectionResult InjectCore(string text, nint expectedWindow)
    {
        if (GetForegroundWindowNative() != expectedWindow)
        {
            TrySetClipboardText(text);
            return InjectionResult.CopiedToClipboardFocusChanged;
        }

        ReleaseHeldModifiers();

        // A clipboard holding an image or files cannot be faithfully restored, so type
        // instead rather than silently destroying it.
        var clipboardIsRestorable = ClipboardHoldsOnlyText(out var savedText);

        if (clipboardIsRestorable && TryPaste(text, savedText)) return InjectionResult.Injected;

        return TypeUnicode(text) ? InjectionResult.Injected : InjectionResult.Failed;
    }

    private static bool TryPaste(string text, string? savedText)
    {
        if (!TrySetClipboardText(text)) return false;

        SendKeyChord(0x11 /* VK_CONTROL */, 0x56 /* V */);

        Thread.Sleep(ClipboardRestoreDelayMs);
        if (savedText is not null) TrySetClipboardText(savedText);
        else TryClearClipboard();

        return true;
    }

    private static bool ClipboardHoldsOnlyText(out string? savedText)
    {
        savedText = null;

        try
        {
            if (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList() || Clipboard.ContainsAudio())
            {
                return false;
            }

            if (Clipboard.ContainsText()) savedText = Clipboard.GetText();
            return true;
        }
        catch (ExternalException)
        {
            return false;   // another application holds the clipboard open
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }

        return false;
    }

    private static void TryClearClipboard()
    {
        try { Clipboard.Clear(); } catch (ExternalException) { }
    }

    /// <summary>
    /// Synthesizes key-ups for any modifier the user is physically holding. Not restored
    /// afterwards — the user is about to release them anyway, and a stuck synthetic modifier
    /// is far worse than a missed one.
    /// </summary>
    private static void ReleaseHeldModifiers()
    {
        var inputs = new List<INPUT>();

        foreach (var vk in ModifierVirtualKeys)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            inputs.Add(KeyInput(vk, KEYEVENTF_KEYUP));
        }

        if (inputs.Count == 0) return;

        var array = inputs.ToArray();
        SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        Thread.Sleep(10);
    }

    private static void SendKeyChord(ushort modifier, ushort key)
    {
        INPUT[] inputs =
        [
            KeyInput(modifier, 0),
            KeyInput(key, 0),
            KeyInput(key, KEYEVENTF_KEYUP),
            KeyInput(modifier, KEYEVENTF_KEYUP),
        ];

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static bool TypeUnicode(string text)
    {
        foreach (var c in text)
        {
            INPUT[] inputs =
            [
                UnicodeInput(c, KEYEVENTF_UNICODE),
                UnicodeInput(c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
            ];

            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 0) return false;
            Thread.Sleep(1);
        }

        return true;
    }

    private static INPUT KeyInput(ushort virtualKey, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        union = new INPUTUNION { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = flags } },
    };

    private static INPUT UnicodeInput(char character, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        union = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = character, dwFlags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindowNative();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Murmur.Platform`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: add Win32TextInjector with focus check and modifier clearing"
```

---

## Task 13: RunKeyStartupRegistrar

**Files:**
- Create: `src/Murmur.Platform/RunKeyStartupRegistrar.cs`
- Test: `tests/Murmur.Integration.Tests/RunKeyStartupRegistrarTests.cs`

- [ ] **Step 1: Write the failing test**

The test uses a dedicated registry value name so it never disturbs a real installation.

Create `tests/Murmur.Integration.Tests/RunKeyStartupRegistrarTests.cs`:

```csharp
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class RunKeyStartupRegistrarTests : IDisposable
{
    private readonly RunKeyStartupRegistrar _registrar = new("MurmurTest");

    public void Dispose() => _registrar.Disable();

    [Fact]
    public void Starts_disabled()
    {
        _registrar.Disable();

        Assert.False(_registrar.IsEnabled);
    }

    [Fact]
    public void Enable_then_IsEnabled_is_true()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        Assert.True(_registrar.IsEnabled);
    }

    [Fact]
    public void Disable_removes_the_entry()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        _registrar.Disable();

        Assert.False(_registrar.IsEnabled);
    }

    [Fact]
    public void Disable_is_safe_when_not_enabled()
    {
        _registrar.Disable();
        _registrar.Disable();

        Assert.False(_registrar.IsEnabled);
    }

    [Fact]
    public void Enable_is_idempotent()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        Assert.True(_registrar.IsEnabled);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Murmur.Integration.Tests --filter RunKeyStartupRegistrarTests`
Expected: FAIL — `The type or namespace name 'RunKeyStartupRegistrar' could not be found`.

- [ ] **Step 3: Write RunKeyStartupRegistrar**

Create `src/Murmur.Platform/RunKeyStartupRegistrar.cs`:

```csharp
using Microsoft.Win32;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>Per-user Run key. No elevation required.</summary>
public sealed class RunKeyStartupRegistrar(string valueName = "Murmur") : IStartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(valueName) is not null;
        }
    }

    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(valueName, $"\"{executablePath}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Murmur.Integration.Tests --filter RunKeyStartupRegistrarTests`
Expected: PASS — 5 tests passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add RunKeyStartupRegistrar for start-with-Windows"
```

---

## Task 14: OverlayWindow

**Files:**
- Create: `src/Murmur.App/OverlayWindow.cs`

- [ ] **Step 1: Write OverlayWindow**

Create `src/Murmur.App/OverlayWindow.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;
using Murmur.Core.Abstractions;

namespace Murmur.App;

/// <summary>
/// The live preview pill. Must never take focus — if it did, the caret would leave the
/// user's textbox and the whole insertion would land in the wrong place.
///
/// <c>WS_EX_NOACTIVATE</c> keeps focus, <c>WS_EX_TRANSPARENT</c> lets clicks through and
/// <c>WS_EX_TOOLWINDOW</c> keeps it out of alt-tab.
/// </summary>
public sealed partial class OverlayWindow : Form, IOverlay
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    private readonly Label _label;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 27);
        Opacity = 0.92;
        Size = new Size(560, 84);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(16, 8, 16, 8),
        };

        Controls.Add(_label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WM_MOUSEACTIVATE)
        {
            message.Result = MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref message);
    }

    void IOverlay.Show() => RunOnUiThread(() =>
    {
        PositionOnActiveScreen();
        _label.Text = "🎙  Listening…";
        base.Show();
    });

    void IOverlay.SetPreview(string text) => RunOnUiThread(() =>
        _label.Text = string.IsNullOrWhiteSpace(text) ? "🎙  Listening…" : LastTwoLines(text));

    void IOverlay.SetTranscribing() => RunOnUiThread(() => _label.Text = "⏳  Transcribing…");

    void IOverlay.Hide() => RunOnUiThread(base.Hide);

    /// <summary>
    /// Bottom-center of the monitor holding the focused window — not the monitor holding
    /// the cursor, which is frequently a different one. Safe to read here because the
    /// overlay never activates, so the target window is still foreground.
    /// </summary>
    private void PositionOnActiveScreen()
    {
        var foreground = GetForegroundWindow();
        var screen = foreground != 0 ? Screen.FromHandle(foreground) : Screen.PrimaryScreen!;
        var area = screen.WorkingArea;

        Location = new Point(
            area.Left + (area.Width - Width) / 2,
            area.Bottom - Height - 80);
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    /// <summary>Keeps the tail of a long transcript visible rather than the beginning.</summary>
    private static string LastTwoLines(string text)
    {
        const int charactersPerLine = 62;
        if (text.Length <= charactersPerLine * 2) return text;
        return "… " + text[^(charactersPerLine * 2)..];
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Murmur.App`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: add non-focusing click-through overlay window"
```

---

## Task 15: TrayIcons and TrayShell

**Files:**
- Create: `src/Murmur.App/TrayIcons.cs`
- Create: `src/Murmur.App/TrayShell.cs`

- [ ] **Step 1: Write TrayIcons**

Icons are drawn in code so the project has no binary assets to manage.

Create `src/Murmur.App/TrayIcons.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Murmur.App;

/// <summary>Generates the three tray icons at runtime: idle, recording and transcribing.</summary>
public static class TrayIcons
{
    public static Icon Idle { get; } = Build(Color.FromArgb(160, 160, 170), filled: false);
    public static Icon Recording { get; } = Build(Color.FromArgb(230, 70, 70), filled: true);
    public static Icon Transcribing { get; } = Build(Color.FromArgb(90, 160, 240), filled: true);

    private static Icon Build(Color color, bool filled)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen = new Pen(color, 2.5f);
            using var brush = new SolidBrush(color);

            var capsule = new Rectangle(11, 5, 10, 15);
            if (filled) g.FillEllipse(brush, capsule);
            else g.DrawEllipse(pen, capsule);

            g.DrawArc(pen, new Rectangle(7, 13, 18, 13), 0, 180);
            g.DrawLine(pen, 16, 24, 16, 28);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
```

- [ ] **Step 2: Write TrayShell**

Create `src/Murmur.App/TrayShell.cs`:

```csharp
using System.Windows.Forms;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.App;

/// <summary>
/// The tray icon, its context menu and the balloon notifier. The status label carries two
/// independent facts — the dictation state and whether the model is resident — because
/// model residency is orthogonal to what the app is doing.
/// </summary>
public sealed class TrayShell : INotifier, IDisposable
{
    /// <summary>
    /// Hidden control used purely to marshal onto the UI thread. The keyboard hook runs on
    /// its own thread, so every call that reaches the tray from the controller arrives
    /// off-thread — and NotifyIcon, like every WinForms component, is not thread-safe.
    /// </summary>
    private readonly Control _marshaller = new();

    private readonly NotifyIcon _icon = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _danish = new("Danish");
    private readonly ToolStripMenuItem _english = new("English");
    private readonly ToolStripMenuItem _hotkey = new();
    private readonly ToolStripMenuItem _startWithWindows = new("Start with Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem _preload = new("Preload model");
    private readonly ToolStripMenuItem _release = new("Release GPU now");

    public TrayShell()
    {
        // Forces handle creation on the UI thread so InvokeRequired reports correctly.
        _marshaller.CreateControl();

        var language = new ToolStripMenuItem("Language");
        language.DropDownItems.AddRange([_danish, _english]);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _status,
            new ToolStripSeparator(),
            language,
            new ToolStripSeparator(),
            _hotkey,
            _startWithWindows,
            new ToolStripSeparator(),
            _preload,
            _release,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke()),
        ]);

        _danish.Click += (_, _) => LanguageChanged?.Invoke("da");
        _english.Click += (_, _) => LanguageChanged?.Invoke("en");
        _hotkey.Click += (_, _) => HotkeyChangeRequested?.Invoke();
        _startWithWindows.Click += (_, _) => StartWithWindowsToggled?.Invoke(_startWithWindows.Checked);
        _preload.Click += (_, _) => PreloadRequested?.Invoke();
        _release.Click += (_, _) => ReleaseGpuRequested?.Invoke();

        _icon.Icon = TrayIcons.Idle;
        _icon.Text = "Murmur";
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;
    }

    public event Action<string>? LanguageChanged;
    public event Action? HotkeyChangeRequested;
    public event Action<bool>? StartWithWindowsToggled;
    public event Action? PreloadRequested;
    public event Action? ReleaseGpuRequested;
    public event Action? QuitRequested;

    public void ApplySettings(Settings settings)
    {
        _danish.Checked = settings.Language == "da";
        _english.Checked = settings.Language == "en";
        _hotkey.Text = $"Hotkey…  ({settings.Hotkey})";
        _startWithWindows.Checked = settings.StartWithWindows;
    }

    public void Notify(string title, string message) => RunOnUiThread(() =>
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(4000);
    });

    /// <summary>
    /// Updates the hover tooltip without raising a balloon. Used for download progress —
    /// a balloon per chunk would fire thousands of times.
    /// </summary>
    public void SetTooltip(string text) => RunOnUiThread(() => _icon.Text = Truncate(text));

    public void SetState(DictationState state, bool modelLoaded, bool modelPinned) => RunOnUiThread(() =>
    {
        _icon.Icon = state switch
        {
            DictationState.Recording => TrayIcons.Recording,
            DictationState.Transcribing => TrayIcons.Transcribing,
            _ => TrayIcons.Idle,
        };

        var suffix = (modelLoaded, modelPinned) switch
        {
            (true, true) => " · model pinned",
            (true, false) => " · model loaded",
            _ => "",
        };

        var label = $"Murmur — {state}{suffix}";
        _status.Text = label;
        _icon.Text = Truncate(label);

        _preload.Enabled = !modelLoaded;
        _release.Enabled = modelLoaded;
    });

    /// <summary>NotifyIcon.Text throws above 63 characters.</summary>
    private static string Truncate(string text) => text.Length > 63 ? text[..63] : text;

    private void RunOnUiThread(Action action)
    {
        if (_marshaller.IsDisposed) return;

        if (_marshaller.InvokeRequired) _marshaller.BeginInvoke(action);
        else action();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _marshaller.Dispose();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Murmur.App`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: add tray shell with menu, icon states and balloon notifier"
```

---

## Task 16: HotkeyCaptureDialog

**Files:**
- Create: `src/Murmur.App/HotkeyCaptureDialog.cs`

- [ ] **Step 1: Write HotkeyCaptureDialog**

Create `src/Murmur.App/HotkeyCaptureDialog.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;

namespace Murmur.App;

/// <summary>
/// Captures the next key pressed and returns its <see cref="Keys"/> name. Modifier keys are
/// accepted as bindings in their own right, which is why the default is Right Ctrl.
/// </summary>
public sealed class HotkeyCaptureDialog : Form
{
    private readonly Label _prompt;

    public HotkeyCaptureDialog(string currentHotkey)
    {
        Text = "Set hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 130);
        KeyPreview = true;

        _prompt = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10f),
            Text = $"Press the key you want to use.\n\nCurrent: {currentHotkey}\nEscape cancels.",
        };

        Controls.Add(_prompt);
    }

    /// <summary>The chosen key name, or null if cancelled.</summary>
    public string? SelectedHotkey { get; private set; }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        if (message.Msg is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return base.ProcessCmdKey(ref message, keyData);

        var key = (Keys)message.WParam;

        if (key == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        SelectedHotkey = key.ToString();
        DialogResult = DialogResult.OK;
        Close();
        return true;
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Murmur.App`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: add hotkey capture dialog"
```

---

## Task 17: Program composition root

**Files:**
- Modify: `src/Murmur.App/Program.cs` (replace the template contents entirely)
- Delete: `src/Murmur.App/Form1.cs`, `src/Murmur.App/Form1.Designer.cs`, `src/Murmur.App/Form1.resx`

- [ ] **Step 1: Delete the template form**

```bash
rm -f src/Murmur.App/Form1.cs src/Murmur.App/Form1.Designer.cs src/Murmur.App/Form1.resx
```

- [ ] **Step 2: Write Program.cs**

Replace the entire contents of `src/Murmur.App/Program.cs`:

```csharp
using System.Windows.Forms;
using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.App;

internal static class Program
{
    private const string MutexName = "Murmur.SingleInstance.9F2C1A7B";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Murmur is already running — look for the microphone icon in your tray.",
                "Murmur", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MurmurContext());
    }
}

/// <summary>
/// Composition root. Owns every component's lifetime and wires the tray menu to the
/// controller. Deliberately thin — all the sequencing lives in DictationController.
/// </summary>
internal sealed class MurmurContext : ApplicationContext
{
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");

    private readonly SettingsStore _settingsStore;
    private readonly TrayShell _tray = new();
    private readonly OverlayWindow _overlay = new();
    private readonly WhisperEngine _engine;
    private readonly WasapiAudioCapture _audio = new();
    private readonly LowLevelHotkeySource _hotkeys = new();
    private readonly Win32TextInjector _injector = new();
    private readonly RunKeyStartupRegistrar _startup = new();
    private readonly System.Windows.Forms.Timer _startupTimer = new() { Interval = 1 };

    // Both need the VAD model on disk, so they are built after the download completes.
    private SileroVoiceSegmenter? _segmenter;
    private DictationController? _controller;

    private Settings _settings;
    private bool _modelsReady;

    public MurmurContext()
    {
        _settingsStore = new SettingsStore(Path.Combine(_dataDirectory, "settings.json"));
        _settings = _settingsStore.Load();

        var store = new ModelStore(Path.Combine(_dataDirectory, "models"));
        _engine = new WhisperEngine(store, _settings.Device);

        WireTray();
        WireHotkeys();

        _tray.ApplySettings(_settings);
        _tray.SetState(DictationState.Idle, modelLoaded: false, modelPinned: false);

        // Startup work is deferred to a one-shot timer rather than run here, for two
        // reasons: blocking the constructor on an 870 MB download would freeze the app
        // before the message loop ever starts (so no balloon could render), and WinForms
        // does not install its SynchronizationContext until Application.Run — awaiting in
        // the constructor would resume continuations on the thread pool.
        _startupTimer.Tick += OnStartupTick;
        _startupTimer.Start();
    }

    private async void OnStartupTick(object? sender, EventArgs e)
    {
        _startupTimer.Stop();
        _startupTimer.Tick -= OnStartupTick;

        try
        {
            _tray.Notify("Murmur", "Downloading the speech model (about 870 MB). This happens once.");

            // Progress fires per 80 KB chunk. Throttle to the tooltip every ~10 MB —
            // a balloon per chunk would fire thousands of times.
            var lastReportedStep = -1L;
            var progress = new Progress<long>(bytes =>
            {
                var step = bytes / 10_000_000;
                if (step == lastReportedStep) return;
                lastReportedStep = step;
                _tray.SetTooltip($"Murmur — downloading model, {bytes / 1_000_000} MB");
            });

            await _engine.EnsureModelsDownloadedAsync(progress, CancellationToken.None);

            _segmenter = new SileroVoiceSegmenter(_engine.VadModelPath, _settings.VadSilenceMs);
            _controller = new DictationController(
                _audio, _segmenter, _engine, _injector, _overlay, _tray, () => _settings, TimeProvider.System);

            _modelsReady = true;
            _tray.SetState(DictationState.Idle, _engine.IsLoaded, _engine.IsPinned);
        }
        catch (Exception ex)
        {
            _tray.Notify("Murmur", $"Model download failed: {ex.Message}. Restart Murmur to retry.");
        }
    }

    private void WireTray()
    {
        _tray.LanguageChanged += code => UpdateSettings(_settings with { Language = code });

        _tray.HotkeyChangeRequested += () =>
        {
            using var dialog = new HotkeyCaptureDialog(_settings.Hotkey);
            if (dialog.ShowDialog() != DialogResult.OK || dialog.SelectedHotkey is null) return;

            UpdateSettings(_settings with { Hotkey = dialog.SelectedHotkey });
            _hotkeys.Rebind(_settings.Hotkey);
        };

        _tray.StartWithWindowsToggled += enabled =>
        {
            if (enabled) _startup.Enable(Application.ExecutablePath);
            else _startup.Disable();

            UpdateSettings(_settings with { StartWithWindows = enabled });
        };

        // The controller does not exist until the model download finishes, so these are
        // wired once here and null-guarded rather than subscribed later.
        _tray.PreloadRequested += () => { if (_controller is { } c) _ = c.PreloadAsync(); };
        _tray.ReleaseGpuRequested += () => { if (_controller is { } c) _ = c.ReleaseGpuAsync(); };
        _tray.QuitRequested += ExitThread;
    }

    private void WireHotkeys()
    {
        _hotkeys.KeyEvent += (transition, timestamp) =>
        {
            // Runs on the hook thread. TrayShell marshals to the UI thread internally.
            if (!_modelsReady || _controller is not { } controller)
            {
                if (transition == KeyTransition.Down)
                {
                    _tray.Notify("Murmur", "Still downloading the speech model — one moment.");
                }

                return;
            }

            _ = controller.HandleKeyEventAsync(transition, timestamp);
        };

        _hotkeys.Rebind(_settings.Hotkey);
        _hotkeys.Start();
    }

    private void UpdateSettings(Settings updated)
    {
        _settings = updated;
        _settingsStore.Save(updated);
        _tray.ApplySettings(updated);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _startupTimer.Dispose();
            _hotkeys.Dispose();
            _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _segmenter?.Dispose();
            _audio.Dispose();
            _overlay.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
```

- [ ] **Step 3: Verify the whole solution builds**

Run: `dotnet build`
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: All tests pass. Fixture-dependent tests report as skipped.

- [ ] **Step 5: Launch the app**

Run: `dotnet run --project src/Murmur.App`
Expected: A microphone icon appears in the tray. On first launch it downloads the model —
watch for balloon progress. Right-click shows the menu described in the spec.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: wire composition root with single-instance mutex and tray"
```

---

## Task 18: Fixtures and manual verification

**Files:**
- Create: `tests/Murmur.Integration.Tests/fixtures/danish.wav`, `danish.txt`
- Create: `tests/Murmur.Integration.Tests/fixtures/english.wav`, `english.txt`
- Modify: `tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj`
- Create: `docs/manual-checklist.md`

- [ ] **Step 1: Record the fixtures**

Record two short clips with the actual microphone, 16 kHz mono 16-bit WAV, roughly 5–8
seconds each. Use Audacity or `ffmpeg`. Write the exact words spoken into the matching
`.txt` file — that text is the word-error-rate reference.

Suggested content, chosen to include the technical vocabulary that trips Whisper up:

- `danish.txt` — `Jeg sender rapporten til Anna inden fredag klokken tolv.`
- `english.txt` — `Deploy the staging branch and check the Kubernetes logs afterwards.`

Convert an existing recording to the required format with:

```bash
ffmpeg -i input.wav -ar 16000 -ac 1 -c:a pcm_s16le tests/Murmur.Integration.Tests/fixtures/danish.wav
```

- [ ] **Step 2: Copy fixtures to the test output directory**

Add to `tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj` inside the
top-level `<Project>` element:

```xml
  <ItemGroup>
    <None Include="fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Run the previously-skipped tests**

Run: `dotnet test tests/Murmur.Integration.Tests`
Expected: PASS with no skips. If a word-error-rate assertion fails, first confirm the `.txt`
matches what was actually said — a transcription mismatch is usually a wrong reference, not
a broken engine.

- [ ] **Step 4: Write the manual checklist**

Create `docs/manual-checklist.md`:

```markdown
# Murmur manual verification

The Win32 surface cannot be meaningfully asserted on in tests. Run this before each release.

## Text insertion

- [ ] Notepad — tap hotkey, dictate a sentence, tap again. Text appears at the caret.
- [ ] Chrome address bar.
- [ ] A web textarea (e.g. a comment box).
- [ ] VS Code editor pane.
- [ ] Slack message box.
- [ ] A borderless-windowed game.
- [ ] **Hold-to-talk into VS Code.** This is the case most likely to expose a stuck
      modifier — a leaked Ctrl turns the pasted text into a burst of shortcuts.

## Overlay

- [ ] The caret keeps blinking in the target textbox while the overlay is visible.
- [ ] Clicking where the overlay sits hits the window underneath.
- [ ] The overlay does not appear in alt-tab.
- [ ] The overlay appears on the monitor holding the focused window.

## Hotkey

- [ ] Tap starts, tap stops.
- [ ] Hold records, release inserts.
- [ ] Right-Ctrl+C still copies (the modifier binding is not swallowed).
- [ ] Rebinding takes effect without restarting.
- [ ] Still responds after 30+ minutes idle.
- [ ] Still responds while the CPU is under heavy load.

## Model lifecycle

- [ ] `nvidia-smi` shows VRAM allocated after a dictation.
- [ ] **Release GPU now** frees it immediately — confirm in `nvidia-smi`.
- [ ] **Preload model** loads it, and it survives past the idle timeout.
- [ ] After a normal dictation, VRAM frees on its own within the idle window.

## Failure handling

- [ ] Unplug the microphone mid-recording — balloon appears, nothing is typed.
- [ ] Click into another window mid-dictation — text goes to the clipboard, balloon explains.
- [ ] Dictate into silence — nothing is typed and no notification appears.
- [ ] Launch a second instance — it exits with a message.

## Settings

- [ ] Language switch persists across a restart.
- [ ] **Start with Windows** survives a reboot.
- [ ] Deleting `%LOCALAPPDATA%\Murmur\settings.json` restores defaults without a crash.
```

- [ ] **Step 5: Run through the checklist**

Work through `docs/manual-checklist.md` against the running application. Fix anything that
fails before considering the feature complete.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "test: add audio fixtures and manual verification checklist"
```

---

## Done

At this point the application satisfies the spec: a tray dictation tool with a smart single
hotkey, live overlay preview, one verbatim insertion into the focused app, Danish/English
selection, on-demand model loading with Preload and Release overrides, and start-with-Windows.
