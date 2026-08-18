# Steam Workshop listing

Paste the block below into the "Description" field of the Oxygen Not Included Uploader.
Steam uses BBCode, not Markdown. Back it up here in case the uploader loses it.

Suggested tags to check in the uploader: Base Game, Spaced Out!, and a UI/Quality-of-Life tag if offered.

---

[b]See what a building will put out, and where a duplicant will stand, before you build it.[/b]

Ever placed a Natural Gas Generator and only later found out which tile the CO2 comes out of? This mod fixes that. While you place a building, it colors the tiles around it.

The colors tell you what each tile does:

[list]
[*][b]Green[/b]: a duplicant stands here to work the building.
[*][b]Faint green[/b]: a duplicant might stand here. The game picks the exact spot later.
[*][b]Cyan (light blue)[/b]: a liquid comes out here.
[*][b]Amber (orange-yellow)[/b]: a gas comes out here.
[*][b]Violet (purple)[/b]: a solid item drops on the floor here.
[/list]

If one tile does more than one of these things, the tile is split into vertical stripes, one stripe per color. Every color keeps its own shade, so you can still tell what you are looking at.

A small color guide appears next to the game's own legend while you place the building. It lists only the colors actually on screen for the building you are holding.

[h1]Settings[/h1]
There is an options screen on the mod's row in the Mods menu. You can ignore it entirely and the mod still works out of the box. What you can change:

[list]
[*][b]Palette[/b]: the standard colors, or one of three other color sets (Deuteranopia, Protanopia, Tritanopia). Fair warning, these three were checked against a simulation of the color blindness each is named for and did not keep the colors apart, so pick whichever one you find easiest to read rather than expecting it to fix anything.
[*][b]Turn colors off[/b]: work, gas, liquid and solid each have their own switch. Switching one off also removes its row from the color guide.
[*][b]Opacity[/b]: one slider for the tiles the mod is sure about, another for the faint "probably here" ones.
[*][b]Piped outputs[/b] (off by default): when a building sends its output down a pipe instead of into the room, this colors the tile the pipe connects to.
[*][b]Heat[/b] (off by default): colors the tiles a building trades heat with, and only for the buildings that reach past their own outline: the Tempshift Plate, Ice-E Fan, Steam Turbine and Conduction Panel. Every other building shows nothing here, because it only trades heat over the tiles you are already placing it on.
[/list]

Changes apply the next time you pick a building. You do not need to reload your colony.

[h1]Compatibility[/h1]
[list]
[*]Works with the Base Game and Spaced Out. Tested with all DLC enabled at time of publish.
[*]Tested with FastTrack turned on.
[*]Safe to add or remove at any time. It stores nothing in your save.
[/list]

[h1]About the code[/h1]
The code for this mod was written with significant AI help. I have tested it in the game myself. Fair warning: I made this for my own use, and I may not support it beyond that. You are welcome to use it and to report problems, but I am not promising fixes or updates.

[h1]Source code[/h1]
On GitHub: [url=https://github.com/Cringely/CellsOfInterest]github.com/Cringely/CellsOfInterest[/url]

[h1]Thanks[/h1]
Thanks to u/Jaaaameslol, who asked on r/Oxygennotincluded for a mod that shows a building's tiles of interest, ([url=https://www.reddit.com/r/Oxygennotincluded/comments/1uxlji1/tiles_of_interest_mod/]the original post[/url]). Here it is.