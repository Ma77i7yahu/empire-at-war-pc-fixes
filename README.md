# Star Wars: Empire at War (PC) – Force Commander camera keys

Force Commander-style camera keys for tactical battles in **Empire at War** and **Forces of Corruption**: **Q/E rotate** the camera, **Z/C tilt** it (more top-down / flatter). Zoom, WASD and the mouse are unchanged, and it works in land and space battles. The stock land camera can't rotate, so the mod also unlocks it, set up to look and zoom like the normal land view.

**This is a patch only.** No game files are included. You need the Steam release (Gold Pack, 64-bit 2017 update).

## Download

Get `EaWCameraMod-v1.0.zip` from the [**Releases**](https://github.com/Ma77i7yahu/empire-at-war-pc-fixes/releases/latest) page. It holds the installer, its README and `PATCHES.txt`. `SHA256SUMS.txt` on the release page lets you check your download.

Full instructions: [docs/README-installer.md](docs/README-installer.md).

## Status

| | Status |
|---|---|
| Empire at War: land and space | Tested |
| Forces of Corruption: land and space | Tested |

The installer's output is byte-identical to the tested files, and its test harness passes (clean install, untick, restore, another mod's XMLs, unknown exe).

## What's in this repo

- `docs/README-installer.md`: the README that comes with the installer.
- `PATCHES.txt`: every byte the exe patch changes, with a disassembly.
- `src/`: the code cave source (`cave.s`) and `build.py` (GNU as/objcopy), and under `src/installer/` the installer's C# source, `PatchData.cs` and the `gen.py` that generates it.
- `test/`: the test harness and screenshot tool (the test game folder isn't included).

## Licence

The code, patches and documentation here are under the [MIT licence](LICENSE): reuse or port them freely, with credit. The game itself isn't covered.

## Disclaimer

Unofficial fan-made mod, not affiliated with or endorsed by Lucasfilm, LucasArts, Disney, Petroglyph or Steam. Star Wars is a trademark of its owners. No game files are distributed here. Use at your own risk.
