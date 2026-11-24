# Hearwood

Hearwood is a small Windows tray app that reads text aloud. Select text in any app, press **Ctrl+Alt+S**, and it starts speaking. Speech is generated on your own PC by Kyutai's [Pocket TTS](https://huggingface.co/kyutai/pocket-tts) (100M parameters), running on the CPU. No cloud service is involved and no account is needed.

## Features

- **Read the selection from any app** with a global hotkey, without opening a window.
- **Change speed while it speaks**, from 0.75× to 3×. The pitch stays the same.
- **Word highlighting** in the Hearwood window follows the speech. The model doesn't report timings, so the highlight position is an estimate.
- **Read from any word** or just the selected text, from the window's right-click menu.
- **Adjustable text size** in the window.
- **Fifteen voices**, American and British, female and male (see [Voices](#voices)).
- **Light and dark themes**, following the Windows setting.
- **Start with Windows** (an option in the tray menu), starting in the tray.

## Requirements

- Windows 10 or 11, x64 or Arm64.
- About 230 MB of free space for the model. The Microsoft Store version includes it and needs no internet connection.
- Other builds download the model on first run, so they need an internet connection then. After setup, speech works offline.

## Using it

Hearwood lives in the notification area. Double-click the tray icon to open the window. Right-click it to speak the clipboard, pause or stop, choose a voice or speed, change the hotkeys, turn on Start with Windows, or quit.

| Hotkey | Action |
|---|---|
| Ctrl+Alt+S | Read the selected text (in any app) |
| Ctrl+Alt+P | Pause / resume |
| Ctrl+Alt+X | Stop |
| Ctrl+Alt+Up / Down | Faster / slower |

To use other keys, click **Change Hotkeys** in the window's footer or the tray menu, pick an action, and press the new combination. A hotkey needs Ctrl or Alt (Shift is optional) and one other key.

To read the selection, Hearwood presses Ctrl+C in the app you're using and then restores what was on your clipboard. If nothing is selected, it reads whatever text is on the clipboard.

In the window, type or paste text and press **Speak**. The same button pauses and resumes.

Right-click the text for **Read from here** (starting at the word you clicked), **Read selection**, **Paste and read** (replacing the text with the clipboard), and the usual Undo, Cut, Copy, Paste and Select all. Opened from the keyboard (Shift+F10 or the Menu key), Read from here starts at the cursor.

To change the text size, use the **A−** and **A+** buttons, press Ctrl+Plus or Ctrl+Minus (Ctrl+0 goes back to the default), or hold Ctrl and turn the mouse wheel. The size is remembered.

### Settings and files

Everything is kept in `%APPDATA%\Hearwood`: settings in `config.json`, and the model and voices under `pocket\` (the Store version keeps its model inside the app instead). To remove the Store version, uninstall it from Settings > Apps; Windows deletes its settings too, unless another build of Hearwood made that folder first, in which case the two share it and it stays. To remove another build completely, turn off Start with Windows, quit it, and delete that folder along with the app's own folder.

## Voices

| Voice | Accent | | Voice | Accent |
|---|---|---|---|---|
| Willow | American, female | | Alder | American, male |
| Hazel | American, female | | Rowan | American, male |
| Juniper | American, female | | Cedar | American, male |
| Laurel (default) | American, female | | Elm | American, male |
| Ivy | American, female | | Linden | British, male |
| Aspen | British, female | | Hawthorn | British, male |
| Heather | British, female | | Birch | British, male |
| Primrose | British, female | | | |

Each voice blends two voices of [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M), so none is a copy of a single Kokoro voice. Kokoro read a short passage in each blend, and that recording became the Pocket voice. The recipes are in [voices/Voice mixes.txt](voices/Voice%20mixes.txt). Hearwood can't clone other voices.

Kyutai's terms for Pocket TTS prohibit, among other things, presenting generated speech as a genuine recording of a real person, and fraud, misinformation or harassment.

## Building from source

You need:

- the [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) (`pwsh`)
- [Rust](https://rustup.rs/) and the MSVC C++ build tools (Visual Studio Build Tools, "Desktop development with C++")

First build the speech runtime. It's Gradium's [xn-ptts](https://github.com/gradium-ai/xn-ptts), fetched at a pinned commit, given a one-line patch, and compiled to `ptts_ffi.dll`. Then build or run the app:

```
pwsh tools/build-phonon.ps1 -Arch x64      # or -Arch arm64
dotnet run --project src/Hearwood
```

To make a self-contained build in `dist\`, run `build-x64.bat` or `build-arm64.bat` (build the matching DLL first).

To make the Microsoft Store package, build both DLLs, run the app once so it downloads the model, and run `pwsh tools/build-msix.ps1 -Version <version>`. It needs the Windows SDK, and writes `dist\msix\Hearwood_<version>.msixbundle`.

Run the tests:

```
dotnet test Hearwood.slnx
```

The tests that use the real engine are skipped until the DLL is built and the model has been downloaded (run the app once).

Without the DLL the app still builds, and it reports that the engine is missing. Only one copy of Hearwood runs at a time, so quit it from the tray before rebuilding.

## Credits

- **Pocket TTS** by [Kyutai](https://kyutai.org/), CC BY 4.0. The Store version includes the model, converted for the runtime; other builds download it from Hugging Face on first run and convert it locally.
- **The voices** are blends of [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) voices by hexgrad, Apache-2.0, turned into Pocket voices with the speaker encoder from [KevinAHM/pocket-tts-onnx](https://huggingface.co/KevinAHM/pocket-tts-onnx), CC BY 4.0. Neither is included in Hearwood.
- **xn-ptts** (the Phonon runtime) by [Gradium](https://github.com/gradium-ai/xn-ptts), MIT or Apache-2.0.
- [NAudio](https://github.com/naudio/NAudio), MIT.
- **Sonic** by Bill Cox ([waywardgeek/sonic](https://github.com/waywardgeek/sonic)), with fixes from [androidx/media](https://github.com/androidx/media), Apache-2.0. Hearwood's speed control is a C# port of it.
- [kokoro-onnx](https://github.com/thewh1teagle/kokoro-onnx) by thewh1teagle, MIT, and [librosa](https://github.com/librosa/librosa)'s `trim()`, ISC, by way of kokoro-onnx. Hearwood's text chunker and silence trim are C# ports of them.

Full licence texts and notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Licence

Hearwood is free software, copyright IdleMad Studio: you can redistribute and modify it under the GNU General Public License, version 3 or (at your option) any later version, with an additional permission to combine it with Microsoft's Windows SDK for .NET; see [NOTICE](NOTICE) and the licence text in [COPYING](COPYING). The model, the voices and the libraries above have their own licences, listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
