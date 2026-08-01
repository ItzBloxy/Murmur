# Murmur — Portable Distribution

**Date:** 2026-08-01
**Status:** Approved design
**Follows:** `2026-08-01-tray-dictation-design.md`

## 1. Purpose

Produce a zip that someone else can unzip and run on their own Windows machine: no
installer, no .NET install, no CUDA toolkit, no administrator rights.

The immediate case is handing it to a friend on a GTX card. Nothing here changes how Murmur
behaves on the development machine, which keeps its CUDA backend.

## 2. Will a GTX card work?

Yes, through Vulkan — and Vulkan turns out to be the whole portability story.

| backend | ships as | runs on |
|---|---|---|
| **Vulkan** | 116 MB | any NVIDIA from Kepler (2012) onward, plus AMD and Intel. Needs only a display driver |
| CUDA 12 | 1,084 MB package + 752 MB NVIDIA redistributables | NVIDIA only, Pascal or newer |
| CPU | 2 MB | anything, at roughly 4s per 30-second window |

Two facts decide it.

**CUDA 13 dropped Maxwell, Pascal and Volta.** A GTX 10-series card is Pascal, so the
`Whisper.net.Runtime.Cuda` package this project originally referenced could never have
worked on it — with or without a toolkit installed. The switch to `Cuda12` was made for
this machine's driver and happens to be the only CUDA line that supports those cards at
all.

**CUDA needs NVIDIA's redistributables, which arrive with the toolkit.** `cudart64_12.dll`,
`cublas64_12.dll` and `cublasLt64_12.dll` total 752 MB and are not part of any display
driver. Shipping CUDA means shipping those.

So CUDA costs roughly 1.8 GB to support a narrower range of hardware, in exchange for the
80 ms measured earlier (0.17s versus 0.25s on 12 seconds of audio). For anything handed to
another person, Vulkan is the correct default by a wide margin.

## 3. Scope

### In

- A self-contained portable build excluding CUDA
- A `Portable` MSBuild property and the publish command that uses it
- Clearer semantics for the `device` setting
- The application version in the diagnostic log
- A `packaging/README.txt` copied into the publish output
- The zip command, and a gitignore entry for `dist/`
- A verification checklist, executed rather than merely written

### Out

- Installer, MSIX, Start Menu integration, auto-update
- Code signing. Deliberate: it costs money annually and does not help a one-off share.
  Section 7 documents the consequence rather than hiding it
- Single-file publishing. Native libraries cannot be meaningfully embedded — they extract
  to a temp directory at startup, which is slower, and an unsigned self-extracting
  executable that installs a global keyboard hook is close to a textbook antivirus
  heuristic. A plain folder causes less trouble
- Trimming. WinForms, reflection and native interop make it unsafe for the size it saves
- Bundling the speech model. It stays a first-run download, keeping the zip at roughly
  150 MB rather than 1 GB
- Changing the default language. It remains Danish; the README says where to change it

## 4. What changes in the application

The app is already close to portable: the model downloads on first run, settings and logs
live under `%LOCALAPPDATA%`, the start-with-Windows entry is written to `HKCU` so no
elevation is needed, and backend selection already falls back on its own. Three changes.

### 4.1 CUDA must not reach the portable build

The only hard requirement. Two independent pieces carry CUDA and excluding one is not
enough:

- `native/cuda12/*.dll` — the 752 MB of NVIDIA redistributables copied by
  `src/Murmur.App/Murmur.App.csproj`
- `runtimes/cuda12/` — contributed by the `Whisper.net.Runtime.Cuda12` package itself

Both are removed by a single MSBuild target that runs after publish when `Portable=true`.
See section 5.

### 4.2 The `device` setting says something untrue

It defaults to `"cuda"`, but since the backend fix it only means "prefer a GPU" — the
actual backend is whatever Whisper.net successfully loads. On a machine without CUDA the
file would read `cuda` while Vulkan does the work. That is precisely the confusion that
concealed the CPU fallback for the entire early life of the project.

Accepted values become `auto` and `cpu`. A stored `"cuda"` is read as `auto`, so existing
settings files keep working without migration. `WhisperEngine` already treats anything
other than `"cpu"` as "prefer GPU", so this is a rename plus documentation, not a behaviour
change.

### 4.3 The log should identify the build

`FileDiagnosticLog`'s session header gains the assembly informational version. When a user
sends a log, the build that produced it should not be a guess.

### Explicitly unchanged

- The default hotkey stays **Right Ctrl**. The reasoning that chose it — a large key,
  inert when pressed alone, and never AltGr on any layout — holds generally, not just on a
  Danish keyboard.
- First-run model download logic is untouched.
- The default language stays Danish.

## 5. The portable build

```bash
dotnet publish src/Murmur.App -c Release -r win-x64 --self-contained true -p:Portable=true -o dist/Murmur
```

Publishing against an explicit runtime identifier does much of the work unprompted. Because
the RID is `win-x64`, the Linux `.so` payloads inside the Whisper runtime packages and the
`win-x86` and `win-arm64` folders are never copied — over 500 MB avoided without
configuration.

### Why a post-publish delete rather than a conditional PackageReference

A conditional `<PackageReference Condition="'$(Portable)' != 'true'">` reads better, but it
alters the NuGet restore graph, which makes the build depend on whether restore last ran
with the same property — a `--no-restore` publish would silently produce the wrong output.
It also only addresses the package folder, leaving `native/cuda12` to a second mechanism.

One target handles both carriers, cannot be defeated by a stale assets file, and keeps the
exclusion legible in a single place. It copies files it then deletes, which costs a few
seconds on a command run occasionally.

In `src/Murmur.App/Murmur.App.csproj`:

```xml
<Target Name="StripCudaFromPortablePublish" AfterTargets="Publish" Condition="'$(Portable)' == 'true'">
  <ItemGroup>
    <PortableCudaLeftovers Include="$(PublishDir)cudart64_12.dll" />
    <PortableCudaLeftovers Include="$(PublishDir)cublas64_12.dll" />
    <PortableCudaLeftovers Include="$(PublishDir)cublasLt64_12.dll" />
    <PortableCudaLeftovers Include="$(PublishDir)runtimes\cuda12\**\*" />
  </ItemGroup>
  <Delete Files="@(PortableCudaLeftovers)" />
  <RemoveDir Directories="$(PublishDir)runtimes\cuda12" />
</Target>
```

### Getting the README into the output

`packaging/README.txt` is committed to the repository and copied into the publish folder by
the same conditional mechanism, so it cannot drift from the build or be forgotten when
zipping:

```xml
<ItemGroup Condition="'$(Portable)' == 'true'">
  <None Include="..\..\packaging\README.txt" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

### Producing the zip

A second command, kept separate so the publish step stays a plain `dotnet publish`:

```bash
Compress-Archive -Path dist/Murmur/* -DestinationPath dist/Murmur-portable.zip -Force
```

`dist/` is gitignored. Build outputs do not belong in history, and the zip is roughly
150 MB.

### Expected contents

| | estimate |
|---|---|
| .NET runtime, self-contained WinForms | ~150 MB |
| Whisper.net and the Vulkan backend | ~116 MB |
| CPU backend | 2 MB |
| Murmur assemblies | <1 MB |
| **folder** | **~270 MB** |
| **zipped** | **~150 MB** |

These are projections from measured package sizes. Section 8 records the real figures once
built; they are not to be quoted as fact until then.

The CPU backend is retained deliberately. At 2 MB it is the difference between "slow but
working" and "does nothing" should Vulkan fail to initialise, and the log names whichever
one loaded.

## 6. What the recipient experiences

1. Unzip anywhere. No installation.
2. Run `Murmur.App.exe`. **Windows SmartScreen will block it** — see section 7.
3. A tray balloon reports the one-time model download, roughly 870 MB, with progress in the
   tray tooltip. The hotkey answers "still downloading" until it completes.
4. Once ready: tap **Right Ctrl** to start, tap again to insert. Hold it to talk and release
   to insert.
5. Language defaults to Danish. Right-click the tray icon to switch to English.

State written to `%LOCALAPPDATA%\Murmur\`: `settings.json`, `murmur.log`, and `models\`.
Uninstalling is deleting the folder — plus unticking **Start with Windows** first if it was
enabled, since that writes an `HKCU` Run entry.

## 7. SmartScreen, and being straight about it

An unsigned executable that installs a global keyboard hook looks, to Windows, exactly like
a keylogger. The API is the same one a keylogger uses. Expect:

- **"Windows protected your PC"** on first launch, requiring *More info → Run anyway*
- Possible antivirus flagging, for the same reason

The `README.txt` states this up front. Burying it would leave the recipient to discover a
security warning on an unsigned app from a friend and draw the obvious conclusion.

The README also states plainly that **all processing is local and no audio ever leaves the
machine**. Anyone handed a microphone-and-keyboard-hooking application is owed that in
writing, and here it is true: the model runs locally and the only network access in the
entire application is the one-time model download from Hugging Face.

`README.txt` contents:

- What Murmur is, in two sentences
- Requirements: Windows 10 or later, a microphone, ~1 GB free disk
- How to run, including the SmartScreen step
- The first-run download and its size
- Hotkey behaviour, tap versus hold
- Changing language and insertion mode from the tray
- Where settings, logs and models are stored, and how to remove them
- The privacy statement above
- Troubleshooting: read the `backend=` line in `murmur.log`; `Vulkan` or `Cuda12` is
  healthy, `Cpu` means the GPU path failed and transcription will be slow

## 8. Verification

Executed, not merely listed. No second machine is required for the first five.

1. **Backend.** Run from `dist/Murmur` and confirm the session log reports `backend=Vulkan`,
   not `Cuda12`. The app reports what it loaded rather than what it requested, which is what
   makes this proof rather than inference.
2. **No source-tree dependency.** Copy `dist/Murmur` to a location outside the repository
   and run it there.
3. **CUDA absent.** Assert `cublasLt64_12.dll`, `cublas64_12.dll`, `cudart64_12.dll` and
   `runtimes/cuda12/` are all gone from the published folder.
4. **Self-contained.** Confirm `coreclr.dll` and `hostfxr.dll` sit beside the executable, so
   no .NET installation is required.
5. **End to end.** Perform a real dictation from the portable build and confirm text is
   inserted.
6. **Size.** Record the actual folder and zip sizes, replacing the estimates in section 5.

### What cannot be verified here

- **The friend's specific GTX card.** The claim rests on NVIDIA supporting Vulkan since
  Kepler and on Vulkan working on this machine, not on testing his hardware.
- **SmartScreen behaviour on his machine**, which varies with Windows configuration and
  reputation data.

Both are stated in the spec rather than left implicit. If Vulkan does fail on his hardware,
the `backend=` line identifies it immediately — which is the reason that line exists.

## 9. Known limitations

- Unsigned, so SmartScreen intervenes on every fresh download.
- No update mechanism. A new version means sending a new zip, though `%LOCALAPPDATA%` state
  survives replacing the folder.
- x64 only. ARM64 Windows would need a separate publish; no ARM device is available to
  verify against.
- The portable build is measurably slower than the development build — 0.25s versus 0.17s
  on 12 seconds of audio. Imperceptible in use, and the correct trade for 1.8 GB and
  broader hardware support.
