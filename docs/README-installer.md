# Star Wars: Empire at War - Force Commander Camera Mod

Adds Force Commander-style camera keys to tactical battles, in both **Empire at War** and **Forces of Corruption**:

| Key | Action |
|-----|--------|
| Q / E | Rotate the camera left / right (90° a second) |
| Z / C | Tilt the camera up, more top-down / down, flatter (45° a second) |

Zoom, WASD scrolling and the mouse controls are unchanged. It works in land and space battles.

The stock land camera can't rotate at all, so the mod also unlocks it. Land battles then use the game's built-in "Unlocked" camera, set up to look and zoom like the normal land view (tilt range 10–85°). The space camera already rotates and is otherwise untouched.

## Requirements

- The **Steam** release of *Star Wars: Empire at War - Gold Pack*, 64-bit (the 2017 update). The installer checks each `StarWarsG.exe` and won't touch one it doesn't recognise.

## Install

1. Run `EaWCameraModInstaller.exe`. It looks for the game in your Steam libraries. If it doesn't find it, browse to the `Star Wars Empire at War` folder (the one that contains `GameData` and `corruption`).
2. Tick the games you want the mod in, then press **Install selected**.

If Windows refuses access, right-click the installer and choose **Run as administrator**.

## Remove

- Untick a game and press **Install selected** to remove the mod from that game only, or
- press **Restore original game** to remove it from both.
- Steam's **Verify integrity of game files** also puts the original exe back (the two XML files below then stay in place and keep the land camera unlocked, so delete them or use the installer).

Steam game updates replace the exe too. Run the installer again afterwards.

## What it changes

In each game folder (`GameData` = Empire at War, `corruption` = Forces of Corruption):

- **`StarWarsG.exe`**: a small code addition that reads Q/E/Z/C and turns the battle camera. The untouched exe is saved first as `StarWarsG.exe.orig`.
- **`Data\XML\GameConstants.xml`**: `Land_Tactical_Camera_Locked` is set to `False`.
- **`Data\XML\TacticalCameras.xml`**: the "Unlocked" camera is replaced with a copy of the normal land camera, with its tilt range opened up.

The two XML files are built on your machine from the game's own data archives. If another mod already put its own copy of either file there, the installer edits that copy instead. It saves the old version as `<name>.before-camera-mod` and puts it back when you remove the mod.

`PATCHES.txt` lists every byte the exe patch changes, with a disassembly.

## Source

https://github.com/Ma77i7yahu/empire-at-war-pc-fixes

## Disclaimer

Unofficial fan-made mod, not affiliated with or endorsed by Lucasfilm, LucasArts, Disney, Petroglyph or Steam. No game files are included. Use at your own risk.
