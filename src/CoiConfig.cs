using System;
using PeterHan.PLib.Options;
using UnityEngine;

namespace CellsOfInterest
{
    // Live settings, re-read from config.json on mod load and on every build-tool activation so a
    // change saved from the Mods menu applies the next time the player picks a building, with no
    // colony reload.
    public static class CoiConfig
    {
        public static CoiSettings Active { get; private set; } = new CoiSettings();

        // Bumped only when the resolved entry set can have changed. CoiResolver records the
        // Version its per-BuildingDef cache was built against and flushes when it sees a newer
        // one, so this counter is what costs a full cache rebuild.
        public static int Version { get; private set; }

        public static void Reload()
        {
            CoiSettings loaded;
            try
            {
                // Returns null when the file does not exist yet, which is the first-run and the
                // deleted-config case; both must land on the shipping defaults.
                loaded = POptions.ReadSettings<CoiSettings>() ?? new CoiSettings();
            }
            catch (Exception e)
            {
                // This runs from a Harmony Postfix on BuildTool.OnActivateTool, so a throw here
                // would propagate into the game's tool activation. A malformed config costs the
                // player their settings, never the build menu.
                Debug.LogWarning($"[CellsOfInterest] could not read settings, keeping current: {e}");
                return;
            }

            Compare(Active, loaded, out bool anyChanged, out bool entrySetChanged);
            if (!anyChanged)
                return;

            Active = loaded;
            if (entrySetChanged)
                Version++;
        }

        // Both field lists live here, side by side, so adding a setting without deciding which
        // list it belongs to is a visible omission rather than a silent default.
        //
        // anyChanged replaces Active. entrySetChanged additionally bumps Version, and only the
        // six toggles below qualify: they decide which entries a BuildingDef produces. Palette and
        // the alphas are read at render time and leave the entry set intact, so they must not bump
        // Version. Comparing whole objects instead would flush every cached BuildingDef on a single
        // alpha-slider tick. Shared-cell striping needs no entry here at all: it carries no setting.
        //
        // Floats compare exactly on purpose. These values round-trip through one JSON file, so
        // any difference at all is a real edit, and an epsilon would only hide small ones.
        private static void Compare(CoiSettings a, CoiSettings b, out bool anyChanged, out bool entrySetChanged)
        {
            entrySetChanged =
                a.TintWork != b.TintWork ||
                a.TintGas != b.TintGas ||
                a.TintLiquid != b.TintLiquid ||
                a.TintSolid != b.TintSolid ||
                a.TintPipedOutputs != b.TintPipedOutputs ||
                a.TintHeat != b.TintHeat;

            anyChanged = entrySetChanged ||
                a.Palette != b.Palette ||
                a.AlphaSolid != b.AlphaSolid ||
                a.AlphaCandidate != b.AlphaCandidate ||
                a.ConfigFileFormat != b.ConfigFileFormat;
        }
    }
}
