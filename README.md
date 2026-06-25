<p align="center">
  <img src="src/EmuDOS/Assets/EmuDOS-Logo.png" alt="EmuDOS" width="420">
</p>

<p align="center">
  <a href="https://emudos.emutastic.com/#features"><b>🌐 Website &amp; feature tour</b></a>
</p>

A good-looking, Boxer-style DOS gaming frontend for Windows. Drop your games in and they appear as boxes on a shelf — art downloaded automatically, sensible settings applied for you, and Roland MT-32 music (with a working LCD) when you supply the ROMs.

Inspired by [Boxer](http://boxerapp.com/) for the Mac, built Windows-first.

> **📖 Full documentation is on the [Wiki](https://github.com/codingncaffeine/EmuDOS/wiki)** — features, usage, and the tech behind it. (The same guide ships with the app as `README.txt`.)

## Highlights

- **Bookshelf library** — your games as box art on a shelf; hover a box to preview its gameplay video in a retro monitor, and press <kbd>Ctrl</kbd>+<kbd>F</kbd> to search and filter. Drop a folder, `.zip`, or CD image to import.
- **2D & 3D box art** — downloaded automatically (ScreenScraper, with a SteamGridDB fallback); choose 2D or 3D per game or library-wide, or drop in your own cover. Logos, marquees, maps and screenshots download from the Manage window's Extras tab.
- **Just-works settings** — a curated catalog applies known-good DOSBox options on import; everything is overridable per game and survives updates.
- **Discs & Windows** — multi-disc games, disc swapping, and installing/booting a full Windows 9x.
- **Roland MT-32** — drop the ROMs in and MT-32 games use them, with an on-screen dot-matrix LCD.
- **Save states**, **screenshots/recording**, **mouse lock**, and a **smart launcher** that picks the right program.
- **Cloud save sync** — back up your save states and notes to your own private GitHub repo, with optional passphrase encryption, synced automatically at launch.
- **CRT shaders** — download the libretro slang shader collection (CRT, scanlines, monochrome monitors); GPU-accelerated, switched live in-game and remembered per game, and captured in screenshots and recordings.
- **Hardware 3dfx** — Voodoo/Glide games render through hardware OpenGL for a sharp, accelerated picture.

See the **[Wiki](https://github.com/codingncaffeine/EmuDOS/wiki)** for the details on all of these.

## Install

1. Download the latest build from the **[Releases page](https://github.com/codingncaffeine/EmuDOS/releases/latest)**, unzip it, and run `EmuDOS.exe`.
2. On first launch, open **Preferences → Downloads** and get the **DOSBox Pure core** (fetched on demand, not bundled).
3. Drag a game folder, `.zip`, or disc image onto the window to add it.

See **[Getting Started](https://github.com/codingncaffeine/EmuDOS/wiki/Getting-Started)** for the full walkthrough, and **[How It Works](https://github.com/codingncaffeine/EmuDOS/wiki/How-It-Works)** for the gamebox model and the tech behind it.

## Project layout

Building from source (the .NET 10 SDK) is covered in **[How It Works → Building](https://github.com/codingncaffeine/EmuDOS/wiki/How-It-Works#building-from-source)**.

| Project | Purpose |
|---|---|
| `src/EmuDOS` | WPF app — the shelf UI and emulator window |
| `src/EmuDOS.Core` | libretro host, dosbox_pure interop, render/input/audio, import, catalog |
| `src/EmuDOS.Metadata` | box art & manual sources (ScreenScraper, SteamGridDB, Internet Archive) |
| `src/native/mt32` | the MT-32 synth shim (C over munt) — see the [wiki](https://github.com/codingncaffeine/EmuDOS/wiki/How-It-Works) |
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
- **[FFmpeg](https://ffmpeg.org/)** — optional gameplay video recording.
- **[VLC / LibVLC](https://www.videolan.org/)** by VideoLAN — plays the game-card video snaps.
- **[librashader](https://github.com/SnowflakePowered/librashader)** by Snowflake — runs the libretro/RetroArch slang shaders on the GPU.
- **[libretro slang shaders](https://github.com/libretro/slang-shaders)** — the downloadable CRT / scanline / monitor shader collection.
- **[ScreenScraper](https://www.screenscraper.fr/)**, **[SteamGridDB](https://www.steamgriddb.com/)**, and the **[Internet Archive](https://archive.org/)** — box art and manuals.

## Third-party components

- **DOSBox Pure** (libretro core) — GPLv2; downloaded at runtime, never bundled.
- **FFmpeg** — GPL; downloaded on demand for the optional video-recording feature, never bundled.
- **munt / mt32emu** — LGPL 2.1; compiled into our `emudos_mt32.dll` (source under `src/native/mt32`, rebuildable via `build.cmd`).
- **LibVLC** — LGPL 2.1; bundled (via LibVLCSharp) to play the game-card video snaps.
- **librashader** (Snowflake) — MPL-2.0; downloaded at runtime to run the slang shaders, never bundled.
- **libretro slang shaders** — community shader collection (various open-source licenses); downloaded on demand from the Downloads tab, never bundled.
- Box art / manuals come from **ScreenScraper**, **SteamGridDB**, and the **Internet Archive** via their APIs.

> Rebuilding the bundled MT-32 synth DLL is covered in the [wiki](https://github.com/codingncaffeine/EmuDOS/wiki/How-It-Works#building-the-mt-32-shim-optional).

## License

[GNU General Public License v3.0](LICENSE)
