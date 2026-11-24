# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Hearwood is a Windows tray app (WinForms, .NET 10, `net10.0-windows`) that reads text aloud with its own built-in TTS. It is the release version of a voice-cloning app by the same author: built-in voices only, no voice cloning and no audio-file output. The TTS is Kyutai's **Pocket TTS 100M** running in-process on the CPU through Gradium's Phonon runtime (`ptts_ffi.dll`). Its behaviour is modelled on another TTS app by the same author, and much of the Pocket code is ported from another of the author's projects. The name plays on heartwood; the voices are named after trees.

Hearwood is licensed GPL-3.0-or-later (`LICENSE`), with a §7 additional permission to combine it with the Windows SDK for .NET DLLs (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`). Third-party code must be GPLv3-compatible (GPLv2-only code is not) and listed in `THIRD-PARTY-NOTICES.txt`.

## Commands

```
dotnet build Hearwood.slnx
dotnet test Hearwood.slnx
dotnet test Hearwood.slnx --filter "FullyQualifiedName~SpeechPlanTests"
dotnet run --project src/Hearwood
pwsh tools/build-phonon.ps1 -Arch x64      # or arm64
```

- **Native DLL:** `build-phonon.ps1` builds `ptts_ffi.dll` into `src/Hearwood/native/<arch>/`, which is git-ignored. It needs Rust and MSVC. Every build copies the DLL next to the exe. Without the DLL the app still builds, and Pocket reports it as missing.
- **Store package (MSIX):** `pwsh tools/build-msix.ps1 [-Version 1.0.0.0] [-Arch x64]` builds `dist\msix\Hearwood_<version>.msixbundle` from `packaging\AppxManifest.xml`, `packaging\priconfig.xml` and `packaging\Assets` (drawn by `make_icon.py`). It needs the Windows SDK, both DLLs and the converted model in `%APPDATA%\Hearwood`, which it checks against pinned SHA-256s. The identity defaults are the Store's (`IdleMadStudio.Hearwood`, publisher IdleMad Studio, from Partner Center). Each Store upload needs a higher `-Version` than the last (last part always 0). `-CertificateThumbprint` signs a build for installing outside the Store; with Developer Mode on, `Add-AppxPackage -Register obj\msix\layout-x64\AppxManifest.xml` installs the unpacked layout instead.
- **Single instance:** only one copy can run (mutex `Local\Hearwood.SingleInstance`). A second launch signals `Local\Hearwood.Show` so the running copy shows its window, then exits. Quit the running copy from the tray before rebuilding, or the exe stays locked.
- **Launch flags:** `--tray` starts without the window. "Start with Windows" (a tray menu item) adds the app to the registry Run entry with that flag, and each launch updates the entry's path if the exe has moved. The Store package can't use the Run entry (its registry writes are private to it), so `StartupRegistration` uses the manifest's `windows.startupTask` there and detects that launch instead of `--tray`; this is why the app targets `net10.0-windows10.0.19041.0`.
- **Theme:** the window follows Windows' light/dark app mode. Set `HEARWOOD_THEME=light` or `dark` to force one when checking both palettes.
- **Icon:** `python tools/make_icon.py` draws `src/Hearwood/assets/Hearwood.ico` (and a PNG); edit the script to change the icon.
- **Checking the engine without the UI (Debug builds only; Release builds cannot write audio files):**
  - `Hearwood.exe --speak-to OUT.wav [--voice NAME] TEXT...` renders a 1× WAV.
  - `Hearwood.exe --stream-to OUT.wav RATE TEXT...` renders through the stretched stream the speakers get. It also writes `OUT.wav.words.txt` with the time each word would be highlighted.
  - Errors go to `OUT.wav.error.txt`. Run these from PowerShell: Git Bash rewrites the path arguments.
  - Check the result with a speech recognizer, listening for the expected words.
- **Real-engine tests:** `SpeechStreamTests` and the BOS check in `VoiceLibraryTests` are skipped unless the DLL is built and the model is already in `%APPDATA%\Hearwood` (run the app once).

## Architecture

- **Model setup.**
  - Files live in `%APPDATA%\Hearwood\pocket\kyutai-english_2026-10\`.
  - `PocketSetup` downloads each missing file from a pinned Hugging Face URL and checks its SHA-256.
  - Kyutai's raw weights are converted by `PocketCheckpoint.SplitOffBos` into the weights and the BOS frame.
  - `PocketService.ReadyAsync` does this once in the background, then loads the engine. A failed attempt is retried on the next call.
  - The Store package ships the converted folder next to the exe (`PocketService.BundledCheckpointDir`, with the voices in it). It is read-only, so when it is complete nothing is downloaded, converted or copied; ptts only reads it.
- **Voices.**
  - Hearwood can't clone. The fifteen voices in `VoiceLibrary.BuiltIn` (default `Laurel`) ship as ready-made ptts voice files, `src/Hearwood/assets/voices/<name>.safetensors`: one `emb` tensor, `[1, frames, 1024]` F32 (107 to 139 frames), the checkpoint's BOS frame and then the voice's frames.
  - At startup `VoiceLibrary.InstallBundled` copies them into the checkpoint's `voices\` (replacing a copy that differs) before the engine loads, because ptts reads voices only when it loads. `PocketEngine` just selects one.
  - Menus show `VoiceLibrary.Label` ("Willow – American, female"); the window's closed voice box shows only the name.
  - Each voice is a blend of two Kokoro-82M voices (Apache-2.0), made by the user. The Kokoro clips they were cloned from (8.5 to 11 s each) and their recipes are in `voices/` (`Voice mixes.txt`; `Lines.txt` has what each clip says); they are the record of where each voice came from and aren't shipped.
  - The voice files were made with the cloning code of another project by the same author, which isn't in this repo: the KevinAHM ONNX speaker encoder (english_2026-04 export, CC BY 4.0), re-projected onto english_2026-10, then BOS plus frames. The files depend on the pinned 2026-10 checkpoint's BOS and projection.
  - Before levelling, that encoder high-passes each clip (`DcBlocker`): a one-pole DC blocker (R = 0.995, about 19 Hz at 24 kHz) run forwards, then backwards. Kokoro's speech carries a DC offset of +0.03 to +0.04 that its silences don't, and levelling only removes the clip's mean, so a clone of an unfiltered clip starts every piece with about a second of drifting offset and a noise burst that the trims can't cut. The clips in `voices/` are Kokoro's originals, unfiltered.
  - Consent: Kokoro's card says it was trained partly on synthetic audio from unnamed commercial TTS providers, and it documents no speakers. Blending two voices (50/50 or 60/40) is the user's choice so that no voice is a copy of a single Kokoro voice. Don't add unblended voices, `af_sky`, or any clone of a real person.
- **Text into pieces.**
  - `SpeechPlan.Build` splits the text into paragraphs (one per line), then sentences (`SentenceRules`/`Abbreviations`: "Mr.", "3 p.m.", `"No!" he said` don't end a sentence). Sentences are cut into pieces of at most 300 characters (`TextChunker.SplitBalanced`).
  - Each piece carries the source offsets of its words, with weights for estimating their timing, plus the pause that follows it (sentence, clause or paragraph).
  - The model gives no timestamps, so word timings are estimates.
- **Playback.**
  - `SpeechStream` synthesizes up to 3 pieces ahead on a worker thread. `Read` feeds 20 ms blocks into `Audio.Sonic`, reading `Rate` for each block, so a speed change is heard at once without changing pitch.
  - `Audio.Sonic` is a C# port of Sonic (Apache-2.0; Bill Cox's `Sonic.java` with androidx/media's fixes), cut down to the speed change for mono floats. It drops or repeats whole pitch periods, which keeps speech clear past 2×. It replaced SoundTouch.Net (LGPL).
  - The stream also records how output samples map to source samples, which is how `WordAt` finds the word being heard.
  - Words are timed over the audible part of each piece (a 35 dB trim). The engine's own 60 dB trim leaves a soft lead-in that would light words too early.
  - Before that trim, `PocketEngine.Finish` drops a call's first 40 ms (`DecoderWarmUp`) when silence follows them. ptts starts the decoder from a zero state on every call, and its 13–20 ms tick, about 40 dB below the speech, otherwise keeps the 0.5–0.8 s of silence the model leads with. About a quarter of calls (short pieces, numbers) speak within 20 ms and are left whole: a flat cut clipped their first word. This matches the code it was ported from.
  - `SpeechPlayer` gives each utterance its own stream and `WaveOut`, and uses the device position for highlighting. Stop disposes the utterance.
- **UI.**
  - `TrayApp` owns the config, the engine, the player, the hotkeys and the window. The read hotkey (default Ctrl+Alt+S) puts the selected text in the window's box as one undo step and speaks it, without showing the window. Ctrl+Alt+X stops; Ctrl+Alt+Up/Down step through `Config.SpeedChoices`. Each `HotkeyAction` has a default and can be changed through `HotkeyDialog`, from Change Hotkeys in the tray menu or the window's footer; `Config.GetHotkey` falls back to the default for an entry that is missing or unreadable. All hotkeys are unregistered while `HotkeyDialog` is open, so a pressed combination shows there instead of acting.
  - The look lives in `Ui/`: `Theme` holds the cedar palette (light and dark, chosen once at startup), and `PillButton` and `Card` are owner-drawn controls in that palette. Button glyphs come from the Windows icon font (Segoe Fluent Icons, falling back to Segoe MDL2 Assets). The window and tray icon are loaded from the embedded `.ico` (`AppIcon`).
  - `MainWindow` highlights through `TextHighlighter`, which sets the RichEdit TOM range background with undo suspended, so highlights never become undo steps.
  - A highlight is shown only while the playing utterance's tag is the box's current version object. Any edit replaces that object.
- **Speed** only affects playback. `SpeechRender` (Debug `--speak-to` and the tests) renders at 1×.

## Gotchas

- **NAudio 3:** `ISampleProvider.Read` takes a `Span<float>`, and event-driven `WaveOut` replaces `WaveOutEvent`.
- **Engine threading:** `PocketEngine` serializes every native call on one lock. Never call `Speak` on the UI thread.
- **Native callbacks:** exceptions must not cross into the native frame callback. Cancellation returns `false` from it instead.
