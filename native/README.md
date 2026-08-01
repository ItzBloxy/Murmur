# Native CUDA 12 runtime

`cuda12/` holds the three NVIDIA runtime libraries whisper.cpp's CUDA backend links
against:

| file | size |
|---|---|
| `cudart64_12.dll` | ~1 MB |
| `cublas64_12.dll` | ~108 MB |
| `cublasLt64_12.dll` | ~643 MB |

**These are deliberately not committed.** They are ~752 MB of redistributable binaries and
have no business in git history. `.gitignore` excludes them.

## Why they are here at all

`Whisper.net.Runtime.Cuda12` ships whisper.cpp's CUDA backend but not NVIDIA's runtime,
which normally arrives with the CUDA Toolkit. Bundling these three files gets CUDA working
without a toolkit install.

Measured on an RTX 3070, 12 s of audio:

| backend | model load | inference |
|---|---|---|
| CUDA 12 | 0.79 s | 0.17 s |
| Vulkan | 0.84 s | 0.25 s |
| CPU | 0.57 s | ~10 s |

## If the folder is empty

Nothing breaks. Whisper.net probes runtimes in order and falls back to Vulkan, which needs
only the NVIDIA display driver. You lose about 80 ms per dictation. The startup line in
`%LOCALAPPDATA%\Murmur\murmur.log` reports which backend actually loaded — trust that over
the `device` setting, which only states a preference.

## Restoring them

Any CUDA 12.x install has them. Either install the CUDA Toolkit (>= 12.4.1) and copy from
`bin/`, or take them from another application that bundles a CUDA 12 runtime. They must
match the major version the package expects — `_12`, not `_13`.
