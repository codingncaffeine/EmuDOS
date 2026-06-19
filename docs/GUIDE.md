# EmuDOS User Guide

Everything you can do in EmuDOS, and how.

- [First run](#first-run)
- [Adding games](#adding-games)
- [The shelf](#the-shelf)
- [Playing a game](#playing-a-game)
- [Mouse: lock and sensitivity](#mouse-lock-and-sensitivity)
- [Picking the right program to run](#picking-the-right-program-to-run)
- [Per-game settings](#per-game-settings)
- [Box art](#box-art)
- [Manuals](#manuals)
- [Roland MT-32 sound](#roland-mt-32-sound)
- [Save states and window size](#save-states-and-window-size)
- [Downloads](#downloads)
- [Where your files live](#where-your-files-live)
- [Troubleshooting](#troubleshooting)

---

## First run

On first launch, EmuDOS needs the DOS emulator core. Open **Preferences** (right-click the shelf or the title area → **Preferences**) → **Downloads** and click **Download** next to *DOSBox Pure core*. While you're there, download the *Game catalog* too — it lets EmuDOS recognize games and apply good settings automatically.

---

## Adding games

Drag a **game folder** or a **`.zip`** onto the EmuDOS window. EmuDOS will:

1. Copy it into a self-contained *gamebox* and figure out which program to run (skipping DOS extenders like DOS/4GW and installers).
2. Match it against the catalog and apply curated DOSBox settings if recognized.
3. Download box art.

You can drop **multiple** items at once. You can also drop a **folder of MT-32 ROMs** (or the loose `.rom` files) — those get routed to the MT-32 system folder instead of being imported as a game.

### CD games (disc images)

Drop a **`.iso`**, **`.cue`**/**`.bin`**, or **`.chd`** and EmuDOS imports it as a CD game: the image is mounted as drive **D:** every launch. The first launch boots to a DOS prompt with a hint — switch to the disc (`D:`) and run its installer (`SETUP` or `INSTALL`). The game installs onto the writable **C:** drive; afterward, use **Run ▸** once to pick the installed program and plain clicks launch it (with the CD still mounted on D:).

> EmuDOS runs **DOS** games. A disc for a **Windows** game (e.g. a Win95/98 title) generally won't install or run, because its installer is a Windows program, not a DOS one.

---

## The shelf

- **Click a box** to play.
- **Right-click a box** for its menu: *Preferences*, *Open in DOS*, *Download manual*, and *Run ▸*.
- **Edit mode** — press **F2** to toggle. In edit mode you can drag boxes to arrange them; press **Ctrl+S** to save the layout.
- **Select and delete** — **Ctrl+click** boxes to select (or **Ctrl+A** for all), then press **Delete**. Deleting removes the game from your library **but keeps the downloaded art**, so re-adding it won't re-download.

---

## Playing a game

Click a box and the game opens in its own window. Keyboard and mouse go straight to the game.

- **Resize** the window freely; EmuDOS remembers the size **per game** for next time.
- Close the window to quit the game.

### Save states

Use the save-state controls to snapshot and restore your exact place in a game. States are stored in the gamebox's `saves` folder.

---

## Mouse: lock and sensitivity

For games that use the mouse to look/turn:

- **Middle-click** locks the mouse: the cursor hides and is held to the window, so you can turn continuously without the pointer escaping. **Middle-click again** (or **Alt-Tab**) to release it.
- **Scroll wheel** raises/lowers mouse sensitivity on the fly (a small readout appears at the top). DOS games don't use the wheel, so there's no conflict.

---

## Picking the right program to run

EmuDOS guesses the program to launch on import, but DOS games sometimes have several executables. To choose:

- **Right-click → Run ▸** lists the programs found in the game (and any you've used before). Pick one to run it.
- The program you pick becomes the **remembered default**, so plain clicks launch it from then on. (Picking a `SETUP`/`INSTALL`/`CONFIG` program is treated as a one-off and won't replace the game.)
- **Right-click → Open in DOS** boots straight to the `C:\` prompt, where you can run a game's `SETUP.EXE`, browse files, or start a program by hand.

If a game opens to a DOS prompt instead of starting, use **Run ▸** to pick the actual launcher (often a `.BAT`).

---

## Per-game settings

**Right-click → Preferences → Game Options.** Everything here is saved as an override for that game and survives catalog updates.

| Setting | What it does |
|---|---|
| **CPU cycles** | Emulated CPU speed. `Auto`/`Max` for most; `Fixed` to pin a cycle count for speed-sensitive games. |
| **Machine type** | Emulated graphics/era (VGA, etc.). |
| **Memory** | Conventional/extended memory size. |
| **Sound card** | Sound Blaster model for digital sound. |
| **MIDI device** | Music device — e.g. General MIDI or **Roland MT-32**. |
| **Aspect correction** | Corrects the picture to the original aspect ratio. |
| **Brightness / Gamma** | Frontend image adjustment. |

**Save** applies on next launch. **Reset** returns the game to the catalog default.

---

## Box art

EmuDOS downloads art automatically. To improve coverage, open **Preferences → Snaps**:

- **ScreenScraper** — log in with your screenscraper.fr account for higher download limits (optional; basic art works without it).
- **SteamGridDB** — paste an API key as a fallback source for games ScreenScraper doesn't have.

After logging in, EmuDOS backfills art for any games still missing a cover.

To re-fetch art manually:

- **Right-click a game → Download box art** — re-fetches that game's cover (handy if it grabbed the wrong one or none; it only overwrites on success).
- **Right-click the shelf background → Download missing art** — fetches covers for every game that doesn't have one.

### Drag your own cover

Found a better cover online? **Drag the image straight onto the game's box** — from a web browser (e.g. an image search) or a local image file. EmuDOS copies it into that game's folder, normalizes it to PNG, and shows it scaled to the box. Covers are stored per game at `gamebox/media/box-front.png`.

---

## Manuals

**Right-click → Download manual.** EmuDOS fetches the manual (from ScreenScraper, falling back to the Internet Archive) and saves it in a per-game folder under your data directory, then opens it. Files are saved as real PDFs.

---

## Roland MT-32 sound

EmuDOS recreates the Boxer experience: drop the ROMs in once and MT-32 games just work, with a dot-matrix LCD shown on a picture of the unit.

**Steps:**

1. **Supply the ROMs.** The Roland MT-32 (or CM-32L) ROMs are Roland's copyrighted firmware — EmuDOS can't distribute them. Drag the `.rom` files, or a folder containing them, onto EmuDOS. Check **Preferences → Downloads** — the *Roland MT-32 ROMs* line shows ✓ when they're detected.
2. **Set the game to MT-32.** In **Preferences → Game Options**, set **MIDI device** to *Roland MT-32*. (Many catalog games are already set up for it.)
3. **Make the game output MT-32 music.** The game itself has to be configured to use Roland/MT-32 for its music. EmuDOS does this automatically for Sierra games (it points their sound config at the MT-32 driver); for others you may need to run the game's own `SETUP` (via *Open in DOS*) and choose Roland MT-32.

When a game writes to the MT-32 display, the dotted amber text appears on the LCD. **Scroll the wheel** over the MT-32 window to resize it; **drag** it to reposition.

---

## Save states and window size

Both are remembered per game, stored in the gamebox:

- **Window size** — the size you last played a game at is restored next launch.
- **Save states** — snapshots of your place in the game.

---

## Downloads

**Preferences → Downloads** manages the on-demand pieces:

- **DOSBox Pure core** — required; the DOS emulator.
- **Game catalog** — recommended; recognizes games and applies settings on import.
- **Roland MT-32 ROMs** — *detected, not downloaded* (you supply these; see above).

The MT-32 synth itself ships with EmuDOS — there's nothing to download for it beyond the ROMs.

---

## Where your files live

Each game is a **gamebox** — a self-contained folder under your data directory containing:

```
<game>/
  profile.json   curated + your settings
  state.json     window size, remembered program
  content/       the game files (mounted as C:)
  media/         box art, manuals, screenshots
  saves/         save data and save states
```

Because a gamebox is self-contained, **backing up or moving the folder moves the whole game**. The library database is only a rebuildable index over these folders.

---

## Troubleshooting

**Game opens to a DOS prompt instead of starting.**
The guessed program wasn't the launcher. Right-click → **Run ▸** and pick the real one (often a `.BAT`). Your pick becomes the default.

**No sound.**
Open **Preferences → Game Options** and check the **Sound card** / **MIDI device**. Some games also need their own `SETUP` run (via **Open in DOS**) to select a sound device.

**MT-32 selected but no music.**
The game's own sound has to be set to Roland/MT-32, not just EmuDOS. For Sierra games EmuDOS handles this; for others, run the game's `SETUP` and choose Roland MT-32. Also confirm the ROMs show ✓ in the Downloads tab.

**No box art.**
Log in to ScreenScraper and/or add a SteamGridDB key under **Preferences → Snaps**; EmuDOS will backfill missing covers.

**Can't type during a copy-protection / manual-lookup screen.**
Type exactly what's asked — many of these want page/word references from the manual, not just letters.
