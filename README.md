# Cells of Interest

An Oxygen Not Included mod that tints a building's important cells while you place it, so you can plan around them before you commit the build.

## What it shows

When you place a building, the mod paints translucent overlays on the cells that matter for the building you're holding:

- **Output cells** show where the building sends what it makes, colored by what comes out: cyan for liquid, amber for gas, violet for a solid item dropped on the floor.
- **Work cells (green)** show where a duplicant stands to run the building.
- **Candidate work cells (faint green)** mark the likely stand cell when the exact one depends on terrain the game only picks at task time.

When one cell is more than one thing at once (a work cell that also sends something out, or two outputs together), the colors layer, so you see a blended tint there.

A small legend appears next to the game's own overlay legend while you're placing, one swatch per color. It shares the preview's lifecycle: no build tool, nothing drawn, nothing running.

Farm tiles, pure storage (Storage Bin and the like), tiles, ladders, and wires resolve to nothing and stay untinted.

## Compatibility

- Base Game and Spaced Out, one DLL.
- Tested with FastTrack enabled.
- No serialized state of any kind. Adding or removing the mod never touches your save.

## Install

Subscribe on the Steam Workshop (link added after first publish).

To run it locally instead, drop the mod folder (the `.dll`, `mod.yaml`, `mod_info.yaml`) into:

```
Documents/Klei/OxygenNotIncluded/mods/local/CellsOfInterest/
```

then enable it in the in-game mods menu.

## Build from source

Classic non-SDK C# project targeting .NET Framework 4.8. Build with MSBuild (not `dotnet build`):

```
MSBuild.exe "CellsOfInterest.csproj" -t:Rebuild -p:Configuration=Debug
```

The `.csproj` references the game's assemblies by absolute `HintPath` into your `OxygenNotIncluded_Data/Managed/` folder. On a new machine, repoint those paths to that machine's Managed folder before building.

## Credits

Made in response to [a request on r/Oxygennotincluded](https://www.reddit.com/r/Oxygennotincluded/comments/1uxlji1/tiles_of_interest_mod/). Someone asked if it could be made, so here it is. Thanks for the idea.

## License

MIT. See [LICENSE](LICENSE).
