# Murmur — Tray Dictation App

**Date:** 2026-08-01
**Status:** Approved design

> "Murmur" is a working name. It appears in exactly four places: the assembly name, the
> data directory (`%LOCALAPPDATA%\Murmur\`), the single-instance mutex, and the registry
> Run-key value. Renaming is a mechanical find-and-replace.

## 1. Purpose

A minimal Windows tray application that transcribes speech to text and inserts it into
whatever application currently has focus. Press a key, talk, press it again, and the words
appear in the textbox you were already using.

Everything runs locally. No audio leaves the machine.

## 2. Target environment

This app is built for one machine and does not need to be portable:

- Windows 10 Pro (19045)
- AMD Ryzen 7 9800X3D, 32 GB RAM
- NVIDIA RTX 3070, 8 GB VRAM
- .NET 10 SDK, MSVC 2022

The user speaks Danish and English.

## 3. Decisions and their rationale

Each of these was chosen deliberately over a plausible alternative.

### Local GPU inference

`large-v3-turbo` quantized to Q8_0, running on the RTX 3070 via CUDA. Private, free per
use, works offline, and transcribes a typical utterance in well under a second. Footprint
while loaded is roughly 1.2–1.5 GB of VRAM (~870 MB of weights plus KV cache and compute
buffers), against the ~2.2 GB an unquantized float16 load would take.

Model size is not a free choice: Whisper's Danish quality drops sharply below the large
models, and there is no English-only large variant — the `.en` models stop at `medium.en`
(769M params) which is *larger and slower* than `large-v3-turbo` (809M, 4 decoder layers
vs 24) while being less accurate. So the multilingual turbo model is both the fastest and
the most accurate option available, and switching to English-only would save nothing.

The integrated Radeon on the 9800X3D was considered and rejected: it is a 2-compute-unit
display adapter with no dedicated memory, it cannot run CUDA, and Whisper inference is
memory-bandwidth-bound — it would share ~80 GB/s of DDR5 with the CPU against the 3070's
448 GB/s dedicated. It would be slower than the CPU path.

### Overlay preview, single insertion

While recording, partial transcripts stream into a small floating overlay. The target
application is not touched until recording stops, at which point the complete transcript —
transcribed in one pass with full cross-phrase context — is inserted once.

The rejected alternative was typing partial results directly into the target and then
backspacing to replace them with a better final transcript. That requires knowing exactly
how many characters are in someone else's textbox, which is unknowable: autocorrect,
autocomplete, markdown auto-formatting and emoji substitution all mutate text after
insertion. A miscount deletes text the user wrote. The overlay approach gets the same live
feedback, gets the same full-context final transcript, produces a single undo step, and
cannot destroy anything.

### Smart single hotkey

One binding serves both gestures. A quick tap toggles recording on and off; holding the
key past the threshold makes it push-to-talk, ending on release.

Recording starts on key-*down* in both cases, so no audio is lost while the app is still
determining which gesture is happening.

### Manual language selection

Danish and English are selected from the tray menu. Auto-detection was rejected: it is
unreliable on short utterances and on Danish containing English technical vocabulary,
which is the common case here.

### Load on demand, unload when idle, with manual overrides

The model loads on first use and unloads after an idle period, so it holds no VRAM while
gaming. Two menu items override the automatic policy: **Preload model** loads it early and
*pins* it (suspending the idle timer until explicitly released), and **Release GPU now**
unloads immediately rather than waiting out the timer.

Cold start is largely hidden: loading begins the instant the hotkey is pressed, running
concurrently with recording. Since the user is speaking for at least a second or two, the
2–3 second load usually completes before the audio is needed.

### C# / .NET 10

`Whisper.net` wraps whisper.cpp, and `Whisper.net.Runtime.Cuda` ships prebuilt CUDA
binaries via NuGet, so no CUDA Toolkit or CMake installation is required. WinForms' `NotifyIcon` is the most reliable tray
implementation on Windows, and P/Invoke to `SetWindowsHookEx`, `SendInput` and layered
windows is straightforward. Result: ~2 MB framework-dependent executable, sub-200ms
startup, ~50 MB idle.

Rust would produce a leaner binary (~15 MB, ~25 MB idle) but requires installing CUDA
Toolkit and CMake and hand-writing considerably more Win32. Python would prototype fastest
but lands at a ~2 GB environment with 1–3s startup, and carries a specific hazard: Windows
silently removes a low-level keyboard hook whose callback exceeds ~300ms, and a GC pause or
GIL contention can trigger that. The failure mode is the hotkey dying mid-session.

On raw throughput the difference is immaterial — CTranslate2 int8 versus whisper.cpp Q8 on
a 3070 is roughly 0.4s versus 0.6s for a 10-second utterance.

## 4. Scope

### In

- Tray icon with three visual states (idle, recording, transcribing)
- Configurable hotkey with tap-to-toggle and hold-to-talk
- Danish / English selection from the tray menu
- Live overlay preview during recording
- Text insertion into the focused application
- Model load/unload with Preload and Release GPU overrides
- Persisted settings
- Start with Windows

### Out

Deliberately excluded from v1. Each is a small, isolated addition later.

- Microphone picker (uses the Windows default input device)
- Audio cues on start/stop
- Custom vocabulary / initial prompt
- Automatic language detection
- Danish→English translation mode
- Transcript history
- Word-by-word streaming into the target application
- Cloud transcription or cloud fallback

## 5. Architecture

The organizing principle: all platform-specific behaviour sits behind interfaces so that
the sequencing logic — where the real bugs live — is testable without hardware.

```
Murmur.sln
├── src/Murmur.Core/            no platform dependencies, fully unit-testable
│   ├── DictationController     the state machine; owns all sequencing
│   ├── GestureRecognizer       tap/hold discrimination (pure function)
│   ├── HallucinationFilter     silence-artifact rejection
│   ├── Settings                model + defaults
│   ├── SettingsStore           JSON persistence, atomic write
│   └── Abstractions/           IHotkeySource, IAudioCapture, IVoiceSegmenter,
│                               ITranscriptionEngine, ITextInjector, IOverlay,
│                               INotifier, IStartupRegistrar
├── src/Murmur.Platform/        the impure implementations
│   ├── LowLevelHotkeySource    WH_KEYBOARD_LL hook + watchdog
│   ├── WasapiAudioCapture      NAudio → 16 kHz mono float32
│   ├── SileroVoiceSegmenter    Whisper.net's built-in Silero VAD
│   ├── WhisperEngine           Whisper.net + model lifecycle
│   ├── Win32TextInjector       clipboard paste / synthetic typing
│   └── RunKeyStartupRegistrar  registry Run key
├── src/Murmur.App/             WinForms host
│   ├── Program                 single-instance mutex, composition root
│   ├── TrayShell               NotifyIcon, context menu, icon states
│   ├── OverlayWindow           layered click-through preview pill
│   └── HotkeyCaptureDialog     rebinding UI
├── tests/Murmur.Core.Tests/
└── tests/Murmur.Integration.Tests/
```

`Murmur.Core` referencing nothing platform-specific is the constraint that makes the test
strategy work. If Core ever needs a `using System.Windows.Forms`, something has been put in
the wrong project.

### Component responsibilities

| Component | Does | Does not |
|---|---|---|
| `LowLevelHotkeySource` | Installs the hook; emits **raw timestamped key-down / key-up** for the bound key | Interpret gestures, or know anything about recording |
| `GestureRecognizer` | Turns raw key events into `StartRecording` / `StopRecording` commands | Touch Win32 |
| `WasapiAudioCapture` | Produces 16 kHz mono float32 frames | Decide when to start or stop |
| `SileroVoiceSegmenter` | Emits phrase segments at silences; retains the full buffer | Transcribe |

Voice activity detection uses Whisper.net's own Silero integration
(`WhisperVadFactory` / `WhisperVadProcessor`, model fetched via
`GetGgmlSileroVadModelAsync`) rather than a separate ONNX Runtime dependency. It is the
same Silero model either way, so this costs nothing and removes a package.
| `WhisperEngine` | Transcribes; owns model load/unload/pin | Know about hotkeys or focus |
| `OverlayWindow` | Displays preview text without taking focus | Contain logic |
| `Win32TextInjector` | Inserts a string into the foreground window | Decide whether it is safe to do so |
| `DictationController` | Sequences everything; owns all state | Touch Win32 directly |

Gesture interpretation belongs in `GestureRecognizer`, in Core, precisely because it is the
fiddliest logic in the app. `IHotkeySource` therefore exposes only raw key transitions with
timestamps — if it emitted gestures, the tap/hold rules would live in the one project that
cannot be unit-tested.

## 6. The dictation flow

1. **Key-down** — capture `GetForegroundWindow()`, start `AudioCapture`, show the overlay,
   set the tray icon to recording, and begin `WhisperEngine.EnsureLoadedAsync()`
   concurrently.
2. **While recording** — frames flow to the segmenter. At each silence boundary the phrase
   is transcribed greedily (beam = 1, speed-first) and appended to the overlay.
3. **Stop** — audio capture ends and the overlay switches to a transcribing indicator. Any
   in-flight preview pass is cancelled through the `CancellationToken` on
   `WhisperProcessor.ProcessAsync`, so the final pass is not held up behind it.
4. **Final pass** — the entire buffer is transcribed with beam search (beam = 5) and the
   pinned language, using the same loaded model.
5. **Insert** — the foreground window is compared against the handle from step 1. If
   unchanged, the text is injected. The overlay hides, the icon returns to idle, and the
   idle-unload timer starts.

### Gesture recognition

`GestureRecognizer` is a pure function over timestamped key events, with no Win32
dependency, so it is directly unit-testable.

Three states — `Idle`, `Undetermined`, `Toggled` — and the table is total, so every event is
accounted for in every state.

| State | Event | Result | Next state |
|---|---|---|---|
| `Idle` | key-down | Start recording; record `t0` | `Undetermined` |
| `Idle` | key-up | Ignored — this is the trailing release after a toggle-stop | `Idle` |
| `Undetermined` | key-up, `t − t0 < 400ms` | **Tap** — keep recording | `Toggled` |
| `Undetermined` | key-up, `t − t0 ≥ 400ms` | **Hold** — stop and finalize | `Idle` |
| `Toggled` | key-down | Stop immediately and finalize | `Idle` |
| `Toggled` | key-up | Ignored | `Toggled` |
| any | key-repeat | Ignored | unchanged |

Stopping on key-*down* in `Toggled` rather than waiting for the release is what makes the
second tap feel instant. The consequence is a trailing key-up arriving in `Idle`, which is
why that row exists.

### Hotkey binding and swallowing

Default binding: **Right Ctrl**. It is a single large key that is comfortable to hold for
push-to-talk, and a lone Right-Ctrl press does nothing in virtually every application.

Right Alt is explicitly unsuitable and must not be offered as a default: on the Danish
keyboard layout it is AltGr, required for `@`, `[`, `]`, `{`, `}` and `$`.

Swallowing follows a rule rather than a setting:

- **Modifier-only bindings** (Right Ctrl, Right Shift) pass through to the application. A
  lone modifier press is inert, and swallowing would break combinations like Right-Ctrl+C.
- **Normal keys and combinations** (F9, Ctrl+Alt+D) are swallowed by the hook so they do
  not type a character or trigger an application shortcut.

### Keeping the hook alive

Windows silently removes low-level hooks whose callback exceeds `LowLevelHooksTimeout`
(~300ms default). The callback therefore does exactly three things — read the event
structure, decide swallow-or-pass, post to a `Channel<T>` — and returns. All real work
happens on a consumer task.

The hook runs on a dedicated thread with its own message pump rather than sharing the UI
thread, which can block on painting. A watchdog polls periodically and reinstalls the hook
if Windows has dropped it regardless.

### The overlay

Window styles: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`,
topmost, and `WM_MOUSEACTIVATE` answered with `MA_NOACTIVATE`.

That combination means it never takes the caret from the target textbox, mouse clicks pass
through it, and it does not appear in alt-tab. It renders bottom-center on whichever
monitor holds the foreground window — the same placement Windows' own Voice Typing uses —
showing a recording indicator and the running preview text, truncated to the last two
lines.

### Text insertion

**The safety rule:** the foreground window handle captured at record start is compared
against the current foreground window before inserting. If focus moved, the app does *not*
type. The text goes to the clipboard and a balloon explains why. Typing into whatever
happened to grab focus is the one unrecoverable mistake this app could make.

Primary path is clipboard paste: save existing clipboard text, set the transcript, send
`Ctrl+V` via `SendInput`, restore after ~300ms. Paste is instant regardless of length,
whereas synthetic typing of a long transcript is slow and some applications drop characters
under it.

**Modifier state must be cleared before the paste.** The default binding is Right Ctrl, and
hold-to-talk keeps it physically down until the moment insertion begins — so a `Ctrl+V`
sent naively can land with a stale or doubled Ctrl. The injector checks `GetAsyncKeyState`
for Ctrl, Alt, Shift and Win, synthesizes key-ups for any that are down, sends the paste,
and does not restore them. The same applies to the typing fallback, where a held Ctrl would
turn plain characters into shortcuts.

Fallback is per-character `SendInput` with `KEYEVENTF_UNICODE`. It is used when the
clipboard cannot be opened after retries, and also when the clipboard currently holds an
image or file list — those cannot be faithfully restored, and silently destroying them
would be worse than typing slowly.

Text is inserted verbatim. No whitespace is added.

### Model lifecycle and concurrency

whisper.cpp contexts are not thread-safe, so `WhisperEngine` serializes every call through
a single lock. Preview passes and the final pass never overlap.

Unloading takes that same lock. **Release GPU now** pressed during an active transcription
does not tear the context out from under it — the unload waits for the current pass to
complete, then frees. The idle timer only ever fires from the idle state, so it cannot race
a transcription at all.

**Preload model** sets a pin flag that suspends the idle timer; the model then stays
resident until **Release GPU now**, which clears the pin and unloads. Ordinary dictation
neither sets nor clears the pin.

## 7. Tray menu

```
Murmur — Idle                    (disabled status label)
─────────────────────────────
Language          ▸  ● Danish
                     ○ English
─────────────────────────────
Hotkey…              (Right Ctrl)
Start with Windows   [✓]
─────────────────────────────
Preload model        (disabled while loaded)
Release GPU now      (disabled while unloaded)
─────────────────────────────
Quit
```

The status label carries two independent facts, so it shows both: the dictation state
(`Idle` / `Recording` / `Transcribing`) and, when the model is resident, a model suffix —
`Murmur — Idle`, `Murmur — Idle · model loaded`, `Murmur — Idle · model pinned`,
`Murmur — Recording`. Treating "model loaded" as a fourth dictation state would be wrong;
the model can be loaded or unloaded in any of them.

`Hotkey…` opens a small modal dialog that captures the next key or combination pressed,
with Escape to cancel.

Tray icon states: idle (outline microphone), recording (filled microphone), transcribing
(microphone with activity indicator).

## 8. Configuration

`%LOCALAPPDATA%\Murmur\settings.json`, written atomically (temp file plus move). The model
lives alongside it in `%LOCALAPPDATA%\Murmur\models\`. Local rather than roaming, so an
~870 MB model never enters a roaming profile.

```json
{
  "language": "da",
  "hotkey": "RControlKey",
  "holdThresholdMs": 400,
  "idleUnloadMinutes": 5,
  "maxRecordingMinutes": 5,
  "vadSilenceMs": 500,
  "startWithWindows": false,
  "device": "cuda"
}
```

`device` accepts `cuda` or `cpu` and exists so the CPU fallback can be forced manually if
the automatic path ever misbehaves. Only `language`, `hotkey` and `startWithWindows` are
exposed in the UI; the rest are editable in the file.

## 9. Error handling

**Hallucination on silence is the failure mode that matters most.** Whisper is trained on
subtitle data and reliably emits stock phrases on near-silent audio — "Thank you for
watching" in English, "Tak fordi du så med" and subtitle-credit lines in Danish. In a
dictation tool this means junk text appearing when nothing was said. Three layers guard
against it:

1. Segments containing no VAD-detected speech are never sent for transcription.
2. Results above a `no_speech_prob` threshold are dropped.
3. Output is matched against a small blocklist of known Danish and English artifacts.

Layer 2 is confirmed available: `Whisper.net` 1.9.1 exposes
`SegmentData.NoSpeechProbability`, and the processor builder additionally offers
`WithNoSpeechThreshold(float)` to reject inside whisper.cpp before a segment is even
emitted. Both are used — the builder threshold as a coarse gate, the property for the
explicit check.

| Failure | Response |
|---|---|
| Microphone missing or unplugged mid-record | Abort, balloon, return to idle. Never insert a partial transcript |
| Model fails to load (VRAM exhausted by a game, driver fault) | Fall back to CPU for the session, balloon once. The 9800X3D makes this a genuine fallback |
| Focus changed during dictation | Clipboard plus balloon; never type |
| Windows drops the keyboard hook | Watchdog reinstalls it |
| Clipboard locked by another application | Retry with backoff, then the typing path |
| Empty transcript | Do nothing. Silence is the correct response, not a notification |
| Corrupt settings file | Back it up, load defaults, continue. Configuration never blocks startup |
| Second instance launched | Named mutex; the existing instance balloons, the new one exits |
| Recording left running | Auto-stop at `maxRecordingMinutes` and finalize, with a balloon |
| Model download fails or is interrupted | Resume from the partial file on next attempt; balloon with the reason. A truncated file is checksum-rejected and re-fetched rather than loaded |

**First run** downloads `ggml-large-v3-turbo-q8_0.bin` (~870 MB), with progress shown in
the tray tooltip. The app is usable only after this completes; the hotkey reports "still
downloading" if pressed meanwhile.

## 10. Testing

### Unit tests — `Murmur.Core.Tests`

Fast, no hardware, no model. `DictationController` depends only on interfaces, so every
sequencing case is exercised with fakes:

- Tap toggles on, second tap toggles off
- Hold records for the duration and finalizes on release
- Key-repeat events do not alter state
- Focus changing mid-record routes to clipboard instead of typing
- Empty transcript produces no insertion and no notification
- Model load failure falls back to CPU
- `maxRecordingMinutes` auto-stop finalizes correctly
- Preload pins the model; the idle timer does not unload while pinned
- Release GPU unloads immediately and clears the pin

Plus, as pure logic with no host required:

- `GestureRecognizer` against synthetic timestamped events, covering **every cell** of the
  state table above — including the trailing key-up in `Idle` and key-up in `Toggled`
- The swallow rule: modifier-only bindings pass through, normal keys and combinations are
  swallowed
- `SettingsStore` round-trip, corrupt-file and missing-file cases
- `HallucinationFilter` against known artifact strings and `no_speech_prob` values

### Integration tests — `Murmur.Integration.Tests`

Slower, requires the real model, run as a separate category.

- Short Danish and English WAV fixtures, asserted on **word error rate below a threshold**
  rather than exact string equality — exact matches break on every model update
- **A pure-silence fixture asserting empty output.** This is the hallucination regression
  test and the most important assertion in the suite
- Load → unload → reload; idle timer fires; pinning suspends it
- `SileroVoiceSegmenter` against a fixture with known pause positions, asserting segment
  count and boundaries

Fixtures: two short recordings per language, made on this machine with the actual
microphone, committed alongside the tests.

### Manual checklist

The Win32 surface cannot be meaningfully asserted on. Run before each release:

- Insertion into Notepad, Chrome's address bar, a web textarea, VS Code, and Slack
- Insertion into a borderless-windowed game
- **Hold-to-talk into VS Code**, which is the case most likely to expose a stuck modifier —
  a leaked Ctrl turns the pasted text into a burst of shortcuts
- Caret remains in the target textbox while the overlay is displayed
- Hotkey still responds after 30+ minutes and under heavy CPU load
- `nvidia-smi` confirms **Release GPU now** actually frees VRAM
- **Start with Windows** survives a reboot
- Rebinding the hotkey takes effect without restarting

## 11. Known limitations

- The overlay will not draw over true exclusive-fullscreen games. Borderless windowed —
  the modern default — works.
- First run requires an ~870 MB download.
- Transcription is not available during the 2–3 second cold start following an idle
  unload, though the concurrent-load design usually hides this.
- Applications that block synthetic input entirely (some anti-cheat layers, elevated
  windows when Murmur is not elevated) cannot receive text. The focus check prevents
  misdirected insertion, but insertion will simply fail.
