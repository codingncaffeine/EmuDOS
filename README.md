# EmuDOS

A good-looking DOS gaming frontend for Windows — drop in your games and they appear as boxes on a shelf, with art downloaded automatically. No setup required.

Inspired by [Boxer](http://boxerapp.com/) for the Mac, built for Windows first.

## Status

Early scaffold. Not yet runnable as a product.

## Tech

- **.NET 10** (LTS), WPF
- **CommunityToolkit.Mvvm** for the UI layer
- DOS games run through the **dosbox_pure** libretro core — load directly from ZIP/folder/ISO, no extraction or manual configuration

## Project layout

| Project | Purpose |
|---|---|
| `src/EmuDOS` | WPF app — the shelf UI (the product surface) |
| `src/EmuDOS.Core` | libretro host, dosbox_pure interop, render/input |
| `src/EmuDOS.Metadata` | box art & metadata: matching and source providers |
| `tests/EmuDOS.Tests` | unit tests |

## Building

Requires the .NET 10 SDK.

```
dotnet build
```
