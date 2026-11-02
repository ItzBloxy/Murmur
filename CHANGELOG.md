# Changelog

All notable changes to Murmur are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-11-02

### Added

- `Murmur-CUDA.exe`, a CUDA edition for NVIDIA cards. Whisper runs on CUDA 12 and takes about a third less time than on Vulkan. It falls back to Vulkan, then the CPU. It bundles NVIDIA's CUDA runtime, taken from NVIDIA's official redistributable archives and hash-checked, so it's an 873 MB download.

### Changed

- `Murmur.exe` shrank from 87 MB to 68 MB by leaving out Whisper runtimes for Linux, 32-bit and ARM64 Windows, which a 64-bit Windows exe can never use.

## [1.0.0] - 2026-10-19

The first public release.

### Added

- Dictation into any focused window: hold the hotkey (Right Ctrl by default) to talk, or tap it to start and again to stop.
- Whisper large-v3-turbo for English and Danish, on the GPU through Vulkan with a CPU fallback.
- Whistle, by Cactus Compute, for German, French, Spanish, Italian, Dutch and Polish, running on the CPU. Picking a language picks its model, and switching releases the model being left.
- A live transcript overlay, and two insert modes: when you stop (best accuracy) or as you speak (phrase by phrase).
- Focus protection: if the window changed mid-dictation, the text goes to the clipboard instead of being typed into the wrong place.
- Silence handling: a voice-activity gate and an artifact filter stop Whisper's invented subtitle credits.
- Preload, Release GPU and an automatic idle unload after 5 minutes.
- Start with Windows, a rebindable hotkey and a diagnostic log.
- A single portable `Murmur.exe`: nothing to install or unzip. If the exe is moved, *Start with Windows* follows it.

[1.1.0]: https://github.com/ItzBloxy/Murmur/releases/tag/v1.1.0
[1.0.0]: https://github.com/ItzBloxy/Murmur/releases/tag/v1.0.0
