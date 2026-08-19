# Cells of Interest

An Oxygen Not Included mod that tints a building's important cells while you place it, so you can plan around them before you commit the build.

## What it shows

When you place a building, the mod paints translucent overlays on the cells that matter for the building you're holding:

- **Output cells** show where the building sends what it makes, colored by what comes out: cyan for liquid, amber for gas, violet for a solid item dropped on the floor.
- **Work cells (green)** show where a duplicant stands to run the building.
- **Candidate work cells (faint green)** mark the likely stand cell when the exact one depends on terrain the game only picks at task time.

When one cell is more than one thing at once (a work cell that also sends something out, or two outputs together), the cell splits into vertical stripes rather than blending the colors together. Each stripe keeps its own color rather than mixing into a third one you can't look up.

A small legend appears at the top right of the screen while you're placing, clear of the overlay info panel, showing only the rows present in the building you're holding. It shares the preview's lifecycle: no build tool, nothing drawn, nothing running.

Farm tiles, pure storage (Storage Bin and the like), tiles, ladders, and wires resolve to nothing and stay untinted.

## Options

The mod's row in the Mods menu has an Options button. Settings are saved to `config.json` next to the DLL, and a change applies the next time you pick a building, with no colony reload. Every default carries its v1 value, so a v1 player who updates and never opens the dialog keeps the same tints. Three things still change on upgrade, because all three were defects rather than preferences: cells with more than one class now stripe instead of blending, the legend lists only the classes actually present, and it sits at a fixed top-right anchor instead of docking to the overlay panel.

- **Per-class toggles** for work, gas, liquid, and solid. Switching a class off drops both its tint and its legend row.
- **Opacity sliders**, one for the cells the mod resolved exactly and one for the rest. The second also governs the heat and pipe-port cells, which are fixed geometry rather than guesses but are not claimed as the exact cell the game will use. Both run from 0.10 to 0.90, defaulting to 0.55 and 0.25.
- **Piped outputs**, off by default. For an output that leaves down a conduit instead of into the room, this tints the building's pipe port cell, colored by what comes out of it.
- **Heat exchange (red)**, off by default. This tints the cells a building trades heat over, and only for the buildings whose thermal reach is not their own footprint: Tempshift Plate, Ice-E Fan, Steam Turbine, and Conduction Panel. Every other building draws nothing here, because its reach is exactly the footprint you are already placing. Heat cells are drawn only where the cell is not already solid, so a plate placed against rock or tile shows fewer than nine.

## Compatibility

- Base Game and Spaced Out, one DLL.
- Tested with FastTrack enabled.
- Nothing is written into your save. Adding or removing the mod never touches it, and your settings live in `config.json` beside the DLL rather than in the colony.

## Install

Subscribe on the Steam Workshop (link added after first publish).

To run it locally instead, drop the mod folder (the `.dll`, `mod.yaml`, `mod_info.yaml`) into:

```
Documents/Klei/OxygenNotIncluded/mods/local/CellsOfInterest/
```

then enable it in the in-game mods menu.

## Build from source

SDK-style C# project targeting .NET Framework 4.8:

```
dotnet build -c Release
```

The build pulls PLib from NuGet and ILRepack merges it into `CellsOfInterest.dll`, so the mod ships as one file with no loose `PLib.dll` beside it. Use `dotnet build` rather than a bare `MSBuild -t:Rebuild`, which skips restore and fails on a clean clone with `NETSDK1004: Assets file 'obj\project.assets.json' not found`.

The `.csproj` references the game's assemblies through the `GameLibsFolder` property, set to an absolute path into `OxygenNotIncluded_Data/Managed/`. On a new machine, repoint that one property before building.

## Credits

Idea by [u/Jaaaameslol](https://www.reddit.com/user/Jaaaameslol/), who [asked on r/Oxygennotincluded](https://www.reddit.com/r/Oxygennotincluded/comments/1uxlji1/tiles_of_interest_mod/) for a mod that shows a building's tiles of interest. Here it is.

The options screen is built with [PLib](https://github.com/peterhaneve/ONIMods) by Peter Han, which is merged into the shipped DLL and used under the MIT license.

## License

MIT. See [LICENSE](LICENSE), which also carries PLib's copyright notice.
