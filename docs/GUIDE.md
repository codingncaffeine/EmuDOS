# EmuDOS User Guide

Everything you can do in EmuDOS, and how.

- [First run](#first-run)
- [Adding games](#adding-games)
- [The shelf](#the-shelf)
- [Playing a game](#playing-a-game)
- [Mouse: lock and sensitivity](#mouse-lock-and-sensitivity)
- [Screenshots and recording](#screenshots-and-recording)
- [Hotkeys](#hotkeys)
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

Drop a **`.iso`**, **`.cue`**/**`.bin`**, or **`.chd`** and EmuDOS imports it as a CD game, mounting the disc as a CD-ROM on launch. Run the disc's installer (`SETUP` or `INSTALL`); the game installs onto a writable **C:** drive, and from then on it launches into the installed program.

**Multi-disc games** — select a game's discs (e.g. `Game (Disc 1).iso` and `Game (Disc 2).iso`) and **drop them together**. EmuDOS imports them as a single game with all discs attached. To attach more discs to an existing game later, right-click → **Add disc…**.

**Swapping discs while playing** — all of a game's discs are mounted up front, so a game (or installer) that asks for "Disc 2" never needs a restart. Press **F10** to open the emulator's menu, choose the disc you want, and it swaps in automatically — there's no need to eject or unmount the current disc first. F10 is rebindable under **Preferences → Hotkeys**.

> Non-bootable preservation rips and **UDF** images can't be read by the DOS emulator — EmuDOS warns you on import if a disc isn't a standard ISO9660 CD.

### Windows games (advanced)

EmuDOS runs **DOS**, but the DOSBox Pure core can also install and boot a real **Windows 9x**. With a *bootable* Windows install CD image, launch it and choose **[ Boot and Install New Operating System ]** from the start menu, pick a hard-disk size, and install Windows; the install persists. You can then add Windows game CDs to that box (right-click → **Add disc…**) and run them inside Windows; mount every disc a game needs before launching, then press **F10** in Windows to swap between them. This is involved and needs a genuinely bootable install image — see the project notes if you go down this road.

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

Snapshot your exact place in a game and jump back to it later:

- **F5** writes a quick save; **F8** loads it back. A small on-screen note confirms each.
- One quick-save slot per game, stored in the gamebox's `saves` folder.
- Both keys are rebindable in **Preferences → Hotkeys**.

Save states capture the whole machine, so they're best for plain DOS games; a booted OS is less reliable. A saved state expects the same game and settings it was made with.

---

## Mouse: lock and sensitivity

For games that use the mouse to look/turn:

- **Middle-click** locks the mouse: the cursor hides and is held to the window, so you can turn continuously without the pointer escaping. **Middle-click again** (or **Alt-Tab**) to release it. (You can also bind a key for this under **Preferences → Hotkeys**.) Locked motion uses raw mouse input, so it stays accurate and even in every direction.
- **Scroll wheel** raises/lowers mouse sensitivity on the fly (a small readout appears at the top). DOS games don't use the wheel, so there's no conflict.

---

## Screenshots and recording

- **F12** saves a screenshot.
- **F9** starts and stops recording gameplay video. Recording needs **FFmpeg** — install it once from **Preferences → Downloads** (it's optional and not bundled).

Under **Preferences → Media** you can set the save folders, the screenshot size (the game's native pixels, or the displayed window size), and the video quality (Low / Medium / High). A **● REC** badge shows while recording; the video encodes when you stop or close the game.

---

## Hotkeys

**Preferences → Hotkeys** rebinds the screenshot, record, mouse-lock, disc-swap menu (**F10**), and quick save/load state (**F5**/**F8**) keys — click a box and press the key you want (Esc resets it to the default). Middle-click always toggles mouse lock regardless.

---

## Picking the right program to run

EmuDOS auto-detects the game program on launch — it prefers an executable whose name matches the game's title, then the **largest** one (the game engine dwarfs little config/registration helpers), and it skips installers and DOS extenders (DOS/4GW). For most games you just click and play.

When it guesses wrong, there are two ways to set it straight, and both **stick**:

- **Right-click → Choose program…** opens a clickable list of every program in the game (the game, its `SETUP`, etc.). Pick one. A game program becomes the **default**; a `SETUP`/`INSTALL`/`CONFIG` tool just runs once, so "go fix the sound" never replaces the game.
- **Right-click → Open in DOS** boots to the `C:\` prompt. Whatever you run there — the game, a launcher `.BAT`, the installer — **EmuDOS remembers as the default** for next time.

So even an awkward game only needs sorting out once.

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
- **FFmpeg** — optional; only needed to record gameplay video (F9).
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
  media/         box art and manuals
  saves/         save data and save states
```

Because a gamebox is self-contained, **backing up or moving the folder moves the whole game**. The library database is only a rebuildable index over these folders.

Screenshots and recorded videos save to the folders set in **Preferences → Media** (by default, `Screenshots/` and `Videos/` in your data directory).

---

## Troubleshooting

**Game opens to a DOS prompt instead of starting.**
The auto-detected program wasn't the launcher. Right-click → **Choose program…** and pick the real one (often a `.BAT`), or just run it once via **Open in DOS** — either way EmuDOS remembers it as the default.

**No sound.**
Open **Preferences → Game Options** and check the **Sound card** / **MIDI device**. Some games also need their own `SETUP` run (via **Open in DOS**) to select a sound device.

**MT-32 selected but no music.**
The game's own sound has to be set to Roland/MT-32, not just EmuDOS. For Sierra games EmuDOS handles this; for others, run the game's `SETUP` and choose Roland MT-32. Also confirm the ROMs show ✓ in the Downloads tab.

**No box art.**
Log in to ScreenScraper and/or add a SteamGridDB key under **Preferences → Snaps**; EmuDOS will backfill missing covers.

**Can't type during a copy-protection / manual-lookup screen.**
Type exactly what's asked — many of these want page/word references from the manual, not just letters.
