# Murmur manual verification

The Win32 surface — the keyboard hook, the overlay, text injection — cannot be meaningfully
asserted on in tests. Everything below has to be checked by hand. Run it before each release.

Launch with:

```bash
dotnet run --project src/Murmur.App
```

The app lives in the tray. Right-click the microphone icon for the menu; **Quit** exits.

## Text insertion

Default hotkey is **Right Ctrl**. Tap to start, tap again to insert.

- [ ] Notepad — tap, dictate a sentence, tap. Text appears at the caret.
- [ ] Chrome address bar.
- [ ] A web textarea, e.g. a comment box.
- [ ] VS Code editor pane.
- [ ] Slack message box.
- [ ] A borderless-windowed game.
- [ ] **Hold-to-talk into VS Code.** Hold Right Ctrl, speak, release. This is the case most
      likely to expose a stuck modifier — a leaked Ctrl turns the pasted text into a burst
      of shortcuts. If the editor starts executing commands, the modifier-clearing in
      `Win32TextInjector` has regressed.
- [ ] Dictate something containing `æ`, `ø` and `å` and confirm they survive insertion.

## Overlay

- [ ] The caret keeps blinking in the target textbox while the overlay is visible. If focus
      moves to the overlay, the whole design is broken.
- [ ] Clicking where the overlay sits hits the window underneath.
- [ ] The overlay does not appear in alt-tab.
- [ ] With two monitors, the overlay appears on the one holding the focused window — not
      the one holding the mouse.
- [ ] Partial text streams into the overlay while you are still speaking.

## Hotkey

- [ ] Tap starts, tap stops.
- [ ] Hold records, release inserts.
- [ ] **Right-Ctrl+C still copies.** Modifier bindings are deliberately not swallowed; if
      this breaks, the swallow rule has regressed.
- [ ] Rebinding via `Hotkey…` takes effect without restarting.
- [ ] Rebind to `F9`, confirm F9 no longer types or triggers shortcuts (normal keys *are*
      swallowed), then rebind back to Right Ctrl.
- [ ] Still responds after 30+ minutes idle.
- [ ] Still responds while the CPU is under heavy load.

## Model lifecycle

- [ ] `nvidia-smi` shows no Murmur process at startup — the model loads on first use.
- [ ] After a dictation, `nvidia-smi` shows roughly 1.2–1.5 GB held.
- [ ] **Release GPU now** frees it immediately — confirm in `nvidia-smi`.
- [ ] **Preload model** loads it, and it survives well past the 5-minute idle timeout.
- [ ] After a normal dictation with no pin, VRAM frees on its own within the idle window.
- [ ] Start a game, then dictate — either it works on GPU or falls back to CPU with a
      balloon. It must not hang or crash.

## Failure handling

- [ ] Unplug the microphone mid-recording — balloon appears, nothing is typed.
- [ ] Click into another window mid-dictation — text goes to the clipboard, balloon
      explains why. **Nothing is typed into the window you switched to.**
- [ ] Tap the hotkey, say nothing, tap again — nothing is typed and no notification
      appears. This is the silence-hallucination gate; if "Thank you." or a Danish subtitle
      credit appears, `ContainsSpeech` has regressed.
- [ ] Copy an image to the clipboard, then dictate — the image survives, and the text is
      typed rather than pasted.
- [ ] Copy some text, dictate, then paste — your original clipboard text is restored.
- [ ] Launch a second instance — it shows a message and exits.

## Settings

- [ ] Language switch persists across a restart.
- [ ] Dictate in Danish with the language set to Danish, and in English set to English.
- [ ] **Start with Windows** survives a reboot.
- [ ] Delete `%LOCALAPPDATA%\Murmur\settings.json` — defaults restore without a crash.
- [ ] Corrupt that file with junk — it is renamed to `.corrupt` and defaults load.
