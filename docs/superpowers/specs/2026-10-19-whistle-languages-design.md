# Whistle for German, French, Spanish, Italian, Dutch and Polish

## Goal

The Language menu gains a second speech model. English and Danish stay on Whisper; six new
languages run on [Whistle](https://cactuscompute.com/blog/whistle), Cactus Compute's
open speech model. Choosing a language chooses its model — there is no separate model
setting to keep in sync.

```
Language ▸  Whisper          (header, not clickable)
            English
            Danish
            ───────────
            Whistle          (header, not clickable)
            German
            French
            Spanish
            Italian
            Dutch
            Polish
```

Whistle also speaks English, but English deliberately stays on Whisper: large-v3-turbo is
the model this app was tuned and measured against, and moving an existing language would
change results for people who did not ask for anything new.

## What Whistle is, and what that rules out

Whistle is not a Whisper variant. It is a 16.9 MB `.cact` file run by Cactus's own C++
engine, *needle*. Whisper.net cannot load it.

For `windows-x86_64`, Cactus ships `needle.exe`, a static `libneedle.a` and `needle.h` —
no DLL. Turning the `.a` into a DLL needs a MinGW toolchain at build time and makes the
build depend on matching that toolchain's C++ runtime. Instead Murmur runs the published
`needle.exe` once per transcription:

```
needle.exe --model whistle.cact --audio clip.wav --audio-language de
{"text":"...","language":"de","ttft_ms":24.4,"decode_tps":720.2}
```

Measured on the development machine (9800X3D), a 5 s clip takes **~120 ms wall time
including process start and model load**. Whistle is CPU-only, so there is no resident
model and nothing on the GPU — which is exactly why a process per call costs so little.

## Pinned, verified downloads

The engine and model download on first use of a Whistle language, into the existing
`%LOCALAPPDATA%\Murmur\models` directory. Both come from Hugging Face at a pinned commit and
are checked against a SHA-256 before they are moved into place — one of them is an
executable, and "whatever `main` points at today" is not something to run.

| file | source | sha256 |
|---|---|---|
| `needle-d6c7d6a2.exe` (1.6 MB) | `Cactus-Compute/needle3@d6c7d6a2…/windows-x86_64/needle.exe` | `0d4bc1da…` |
| `whistle-74111868.cact` (16.9 MB) | `Cactus-Compute/whistle@74111868…/whistle.cact` | `b6e02f04…` |

The short commit is in the cached file name, so bumping the pin downloads the new version
instead of reusing a stale one. Both are Apache-2.0.

## Routing

`LanguageRoutedEngine` (Core) implements `ITranscriptionEngine` over both engines, so
`DictationController` is unchanged:

- `TranscribeAsync` routes by the language it is given.
- `EnsureLoadedAsync`, `IsLoaded` route by the current language setting.
- Moving to the other model **unloads the one being left**. Whisper holds ~1.5 GB of VRAM;
  keeping it resident while dictating German would be the opposite of what the user chose.
- Pin intent ("Preload model") follows the switch: a pinned router pins whichever engine is
  active, and switching while pinned preloads the new one.
- `UnloadAsync` (idle unload, "Release GPU now") unloads both.

`WhistleEngine.IsLoaded` is always false — there is nothing resident to report — and its
`UnloadAsync` is a no-op. The tray therefore shows no "model loaded" suffix and keeps
"Release GPU now" disabled while a Whistle language is active, which is the truth.

## The hallucination filter

Whistle returns an empty string for silence and steady noise (documented in `needle.h`)
rather than inventing subtitle credits, so its segments carry a no-speech probability of 0
and rely on the existing VAD gate and empty-text rejection.

## Out of scope

- Streaming (`needle_stream_transcribe_process`). The process-per-call design cannot hold
  stream state; as-you-speak mode already transcribes each phrase on its own.
- Keyword biasing and word timestamps.
- Moving English to Whistle, or offering a per-language model choice.
