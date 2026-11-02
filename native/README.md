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

## Fetching them

```powershell
./native/fetch-cuda.ps1
```

The script downloads NVIDIA's official CUDA 12.8.1 redistributable archives from
`developer.download.nvidia.com/compute/cuda/redist`, checks each against the SHA-256 in
NVIDIA's `redistrib_12.8.1.json`, and extracts the three DLLs plus NVIDIA's license
(`NVIDIA-LICENSE.txt`) into `cuda12/`. Archives already downloaded with the right hash are
reused.

| file | version | sha256 (DLL) |
|---|---|---|
| `cudart64_12.dll` | CUDA Runtime 12.8.90 | `c2c9a9c22a9bcba9…` |
| `cublas64_12.dll` | CUDA BLAS 12.8.4.1 | `9513540e4ec4c51e…` |
| `cublasLt64_12.dll` | CUDA BLAS 12.8.4.1 | `b199d1ff892a81b7…` |

NVIDIA does not Authenticode-sign these files in its redistributable archives, so the
archive hashes are the provenance check. The copies this folder held before the script
existed turned out byte-identical to NVIDIA's.

## The CUDA edition

`dotnet publish ... -p:Portable=true -p:Edition=Cuda` bundles these files into
`Murmur-CUDA.exe`, and the build fails if any of them is missing rather than quietly
producing a Vulkan build under a CUDA name. Redistribution is permitted by the CUDA Toolkit
EULA, Attachment A ("CUDA Runtime" and "CUDA BLAS Library"); NVIDIA's license travels inside
the exe and `THIRD-PARTY-NOTICES.txt` says the files are NVIDIA's, not MIT.

In the single-file exe these DLLs are unpacked to `%TEMP%\.net\Murmur-CUDA\…`, but Windows
looks for `ggml-cuda-whisper.dll`'s dependencies beside the *exe*. `Program.Main` therefore
calls `SetDllDirectory(AppContext.BaseDirectory)`; without it the CUDA backend fails to load
and Whisper.net silently uses Vulkan.
