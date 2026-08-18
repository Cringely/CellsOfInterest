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

            // Compared before the store, because after it both arguments are the same object.
            bool entrySetChanged = Compare(Active, loaded);
            // Unconditional. Every reader takes Active as a whole reference and nothing mutates one
            // in place, so replacing it on each activation costs a single store and is the only
            // form of this that cannot silently drop an edit - see Compare.
            Active = loaded;
            if (entrySetChanged)
                Version++;
        }

        // Does the resolved ENTRY SET differ? Only the six toggles below qualify: they decide which
        // entries a BuildingDef produces, and a difference costs a full CoiResolver cache rebuild.
        // Palette and the alphas are read at render time and leave the entry set intact, so they
        // must not bump Version. Comparing whole objects instead would flush every cached
        // BuildingDef on a single alpha-slider tick. Shared-cell striping needs no entry here at
        // all: it carries no setting.
        //
        // A second hand-maintained list used to sit beside this one, deciding whether Active was
        // replaced at all, and it is deleted rather than maintained. A setting left out of that one
        // was discarded on load and read its default for the rest of the session, invisibly and
        // permanently. Leaving a new toggle out of THIS list is still a bug - its change would not
        // flush the resolver cache, so the toggle would look like it did nothing - but that is
        // bounded by the next flush of that cache, where the Active omission was bounded by nothing.
        private static bool Compare(CoiSettings a, CoiSettings b)
        {
            return
                a.TintWork != b.TintWork ||
                a.TintGas != b.TintGas ||
                a.TintLiquid != b.TintLiquid ||
                a.TintSolid != b.TintSolid ||
                a.TintPipedOutputs != b.TintPipedOutputs ||
                a.TintHeat != b.TintHeat;
        }
    }
}
