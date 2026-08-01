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
