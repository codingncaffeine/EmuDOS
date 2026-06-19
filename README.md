# EmuDOS

A good-looking, Boxer-style DOS gaming frontend for Windows. Drop your games in and they appear as boxes on a shelf — art downloaded automatically, sensible settings applied for you, and Roland MT-32 music (with a working LCD) when you supply the ROMs.

Inspired by [Boxer](http://boxerapp.com/) for the Mac, built Windows-first.

> **New here? See the [User Guide](docs/GUIDE.md)** for how to use everything.

## Features

- **Bookshelf library** — your games as box art on a shelf; drag to arrange in edit mode.
- **Drag-and-drop import** — drop a folder or `.zip`; EmuDOS finds the program to run (and ignores DOS extenders/installers), then applies curated settings if it recognizes the game.
- **Automatic box art** — from ScreenScraper, with SteamGridDB as a fallback for anything it misses (log in under Preferences → Snaps).
- **Manuals** — right-click → *Download manual* (ScreenScraper, with an Internet Archive fallback), saved per game.
- **Per-game settings** — CPU cycles, machine type, memory, Sound Blaster/MIDI device, aspect correction, and brightness/gamma, saved as an override that survives catalog updates.
- **Roland MT-32** — drop the ROMs in and MT-32 games just use them, with an on-screen dot-matrix LCD on a picture of the unit. (ROMs are user-supplied — see below.)
- **Save states**, **per-game window size memory**, and **remembered launcher** (the program you pick in the *Run* menu becomes the default).
- **FPS mouse lock** — middle-click to lock/unlock the mouse in-game; scroll wheel adjusts sensitivity live.
- **Open in DOS** — boot a game straight to the `C:\` prompt to run its `SETUP`, poke around, or pick a different executable.
- **Downloads tab** — fetches the emulator core and game catalog on demand, and tells you whether your MT-32 ROMs are in place.

## Quick start

1. Install the **.NET 10 SDK**.
2. `dotnet build -c Release`
3. Run `EmuDOS.exe`. On first launch it downloads the DOSBox Pure core (Preferences → Downloads).
4. Drag a game folder or `.zip` onto the window. It appears on the shelf with art.
5. Click a box to play. Right-click for options (Preferences, Open in DOS, Download manual, Run ▸).

Full walkthrough: **[docs/GUIDE.md](docs/GUIDE.md)**.

## How it works

Games run through the **dosbox_pure** libretro core — loaded directly from a folder or archive, no extraction or manual config. Each game lives in a self-contained **gamebox** folder (its profile, content, media, saves, and per-game state), so backing up or moving the folder moves the whole game; the library database is just a rebuildable index over those folders.

A curated **catalog** (seeded from the eXoDOS configuration set) recognizes games and applies known-good DOSBox settings on import.

## Project layout

| Project | Purpose |
|---|---|
| `src/EmuDOS` | WPF app — the shelf UI and emulator window |
| `src/EmuDOS.Core` | libretro host, dosbox_pure interop, render/input/audio, import, catalog |
| `src/EmuDOS.Metadata` | box art & manual sources (ScreenScraper, SteamGridDB, Internet Archive) |
| `src/native/mt32` | the MT-32 synth shim (C over munt) — see below |
| `tests/EmuDOS.Tests` | unit tests |

## MT-32 and the ROMs

EmuDOS plays MT-32 music with its own synth (a small DLL built from [munt](https://github.com/munt/munt), shipped with the app). It needs the **Roland MT-32 (or CM-32L) ROMs**, which are **Roland's copyrighted firmware** — we can't and don't distribute them. Supply your own by dragging the `.rom` files (or a folder containing them) onto EmuDOS; the Downloads tab shows whether they're detected.

## Credits

EmuDOS stands on the work of others, with thanks:

- **[Boxer](http://boxerapp.com/)** by Alun Bestor — the Mac DOS frontend that inspired EmuDOS, and the reference for recreating the Roland MT-32 LCD.
- **[DOSBox Pure](https://github.com/schellingb/dosbox-pure)** by Bernhard Schelling, and the **[DOSBox](https://www.dosbox.com/)** project it builds on — the emulator that runs the games.
- **[munt / mt32emu](https://github.com/munt/munt)** — the Roland MT-32 emulation behind our synth.
- **[eXoDOS](https://www.retro-exo.com/exodos.html)** — the DOS configuration set our catalog is seeded from.
- **[libretro](https://www.libretro.com/)** — the core API EmuDOS hosts.
- **[ScreenScraper](https://www.screenscraper.fr/)**, **[SteamGridDB](https://www.steamgriddb.com/)**, and the **[Internet Archive](https://archive.org/)** — box art and manuals.

## Third-party components

- **DOSBox Pure** (libretro core) — GPLv2; downloaded at runtime, never bundled.
- **munt / mt32emu** — LGPL 2.1; compiled into our `emudos_mt32.dll` (source under `src/native/mt32`, rebuildable via `build.cmd`).
- Box art / manuals come from **ScreenScraper**, **SteamGridDB**, and the **Internet Archive** via their APIs.

## Building the MT-32 shim (optional)

The prebuilt `emudos_mt32.dll` is committed and ships with the app. To rebuild it you need Visual Studio with the C++ workload, then run `src/native/mt32/build.cmd`.
