using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CellsOfInterest
{
    // Ordinal order is also stripe order on a shared cell (CoiTintController.StripeOf), so a class
    // appended here lands to the right of the existing ones. Not persisted anywhere, so unlike
    // PaletteChoice this carries no wire-format constraint.
    public enum CoiClass { Work, Output, Heat }

    public enum CoiPhase { None, Gas, Liquid, Solid }

    public struct CoiEntry
    {
        public CoiClass Cls;
        public CoiPhase Phase;      // outputs: Gas/Liquid/Solid; work: None
        public bool Deterministic;  // solid tint vs low-alpha candidate
        public bool Rotates;        // explicit work offsets only (spec: rotation rules)
        public bool IsWorldOffset;  // float world-space offset (outputs) vs integer CellOffset
        public CellOffset Cell;
        public Vector2 World;

        public static CoiEntry AtCell(CoiClass cls, CellOffset cell, bool deterministic, bool rotates, CoiPhase phase = CoiPhase.None)
            => new CoiEntry { Cls = cls, Cell = cell, Deterministic = deterministic, Rotates = rotates, Phase = phase };

        // No default for phase: every caller is an Output, and CoiPalette.For folds CoiPhase.None
        // into its Solid arm, so an omitted phase would silently paint an output the wrong color.
        public static CoiEntry AtWorld(CoiClass cls, Vector2 world, CoiPhase phase)
            => new CoiEntry { Cls = cls, World = world, IsWorldOffset = true, Deterministic = true, Phase = phase };
    }

    public sealed class CoiData
    {
        public CoiEntry[] Entries;
        public static readonly CoiData Empty = new CoiData { Entries = Array.Empty<CoiEntry>() };
    }

    public static class CoiResolver
    {
        // Keyed by def reference: defs are load-time singletons, reference keying
        // cannot collide across mods the way a PrefabID hash could (spec review finding 6).
        private static readonly Dictionary<BuildingDef, CoiData> cache
            = new Dictionary<BuildingDef, CoiData>();

        // The CoiConfig.Version `cache` was built against. A per-class toggle changes which entries
        // a def produces, and it changes them for every def visited this session, not just the one
        // on screen, so the whole dictionary goes. Two simpler things were rejected: dropping the
        // cache outright, which puts TryExplicitOffset's reflection on every building selection;
        // and keying the cache by (def, version), which is a smaller blast radius but never
        // reclaims the entries built under a toggle setting the player has already left.
        private static int cacheVersion;

        // Explicit work-offset field names seen in configs (Bottler.workCellOffset etc.).
        private static readonly string[] ExplicitOffsetFields = { "workCellOffset", "workOffset" };

        public static CoiData Get(BuildingDef def)
        {
            if (def == null)
                return CoiData.Empty;
            // Flush before the lookup, never after: an entry built under the old toggles must not
            // be served on the way to noticing they changed. `!=` rather than `<` so this needs no
            // assumption that Version only ever increments.
            if (cacheVersion != CoiConfig.Version)
            {
                cache.Clear();
                cacheVersion = CoiConfig.Version;
            }
            if (cache.TryGetValue(def, out var data))
                return data;
            try
            {
                data = Build(def);
            }
            catch (Exception e)
            {
                // A modded building must never crash-loop the build menu (spec: failure handling).
                Debug.LogWarning($"[CellsOfInterest] resolver failed for {def.PrefabID}: {e}");
                data = CoiData.Empty;
            }
            cache[def] = data;
            return data;
        }

        private static CoiData Build(BuildingDef def)
        {
            var go = def.BuildingComplete;
            if (go == null)
                return CoiData.Empty;
            var entries = new List<CoiEntry>();

            AddWork(go, entries);
            AddOutputs(def, go, entries);
            AddHeat(def, go, entries);
            entries.RemoveAll(e => !Enabled(e));

            return entries.Count == 0 ? CoiData.Empty : new CoiData { Entries = entries.ToArray() };
        }

        // Per-class gating. One pass over the finished list rather than a check at each of the
        // eight emit sites: the sites that need the phase compute it inline inside an `if`, so
        // gating there would mean hoisting PhaseOf into a local at three of them, and a ninth emit
        // site added later could silently skip the gate. Cached with the entries, so this is read
        // at build time only and the cache-version flush above is what makes a toggle change land.
        private static bool Enabled(CoiEntry e)
        {
            CoiSettings s = CoiConfig.Active;
            // Both non-output classes gate on e.Cls, before the phase switch, because both carry
            // CoiPhase.None and the switch's default arm answers None with TintSolid. Heat reaching
            // that arm would read the wrong setting - TintSolid, default true - with no compile
            // error, so this test is what makes the Heat toggle mean anything. AddHeat itself is
            // deliberately ungated: one gate, in the one place every class is gated.
            if (e.Cls == CoiClass.Work)
                return s.TintWork;
            if (e.Cls == CoiClass.Heat)
                return s.TintHeat;
            switch (e.Phase)
            {
                case CoiPhase.Gas: return s.TintGas;
                case CoiPhase.Liquid: return s.TintLiquid;
                // Solid, None, and anything outside the enum. Same fold as CoiPalette.For, which
                // has no None arm either: the toggle that hides a cell is named after the color the
                // cell is drawn in, so "Solid outputs: off" cannot leave a purple cell on screen.
                default: return s.TintSolid;
            }
        }

        private static void AddWork(GameObject go, List<CoiEntry> entries)
        {
            foreach (var w in go.GetComponents<Workable>())
            {
                if (w is Storage)
                    continue; // pure-storage approach cells are out of scope (2026-07-16 ruling)

                // Farm/planter tiles: PlantablePlot : SingleEntityReceptacle : Workable is the
                // seed/fertilizer deposit receptacle. Its pivot is the tile itself, not a machine
                // work cell, so the candidate tint there is noise. Excluded by user ruling (2026-07-17).
                // Covers FarmTile, PlanterBox, WideFarmTile, and DLC hydroponic/aquatic farm tiles.
                if (w is PlantablePlot)
                    continue;

                // Skip incidental maintenance/errand Workables that are not the building's primary
                // operation. Some are universal (added to every building), some are per-config opt-ins;
                // none is a work cell a player plans placement around:
                //  - Deconstructable: BuildingConfigManager.OnPrefabInit ->
                //    baseTemplate.AddComponent<Deconstructable>() (BuildingConfigManager.cs:37).
                //  - BuildingHP: BuildingConfigManager.cs:45 adds BuildingHP to baseTemplate
                //    (every building's universal template).
                //  - Repairable: BuildingDef.Repairable defaults to true (BuildingDef.cs:74) and
                //    BuildingLoader.cs:216 calls UpdateComponentRequirement<Repairable> for any
                //    def with Repairable == true, i.e. almost every building unless it opts out.
                //  - Disinfectable / AutoDisinfectable: disinfect errand is a maintenance task
                //    present on nearly every building (game universal setup).
                //  - Door: Door : Workable, toggle errand is incidental to placement planning
                //    (user-directed exclusion: door open/close/lock UI should not tint cells).
                //  - Toggleable: enable/disable errand on doors, reservoirs, dispensers.
                //  - Valve: the adjust-flow errand on Gas/Liquid Valve (Valve : Workable). The valve's
                //    dupe-set-flow cell is not a placement cell of interest; without this it falls to the
                //    unknown-subclass fallback and gets a candidate pivot tint (live-confirmed 2026-07-21).
                //  - Breakable: damage interaction errand.
                //  - StorageTileSwitchItemWorkable: storage tile item switch errand.
                //  - DropAllWorkable: the "empty the building's storage" errand, per-config on any
                //    storage-bearing building (fabricators, refineries, cookers, piped farm tiles). It
                //    renders at the pivot: redundant with the real work cell where one exists, and the
                //    wrong signal on storage/piped buildings (surfaced by hydroponic farm tiles 2026-07-17).
                // Without this skip, errand-adjacent Workables fall into the unknown-subclass fallback
                // below and get a candidate pivot tint on every tile/ladder/drywall (spec bug: tints
                // on ALL buildings).
                //
                // Deliberately NOT excluded: Demolishable (added per-config via
                // BuildingTemplates.ExtendBuildingToGravitas, not universal). It stays on the
                // unknown-Workable candidate-pivot fallback path, same as any other operational
                // interaction Workable (e.g. Sleepable, the manual generator wheel) that players
                // rely on for automation-sensor placement.
                if (w is Deconstructable || w is Repairable || w is BuildingHP || w is Door
                    || w is Disinfectable || w is AutoDisinfectable || w is DropAllWorkable
                    || w is Toggleable || w is Valve || w is Breakable
                    || w.GetType().Name == "StorageTileSwitchItemWorkable")
                {
                    continue;
                }

                // Explicit single offset set by the config (deterministic, rotates with the building).
                if (TryExplicitOffset(w, out var cell))
                {
                    entries.Add(CoiEntry.AtCell(CoiClass.Work, cell, deterministic: true, rotates: true));
                    continue;
                }

                // ComplexFabricatorWorkable never sets offsets: dupe stands on the pivot (verified).
                if (w is ComplexFabricatorWorkable)
                {
                    entries.Add(CoiEntry.AtCell(CoiClass.Work, new CellOffset(0, 0), deterministic: true, rotates: false));
                    continue;
                }

                // Unknown Workable subclass: it may set offsets in OnPrefabInit where this
                // resolver cannot see. Pivot as CANDIDATE, never authoritative (spec honesty rule).
                entries.Add(CoiEntry.AtCell(CoiClass.Work, new CellOffset(0, 0), deterministic: false, rotates: false));
            }
        }

        private static void AddOutputs(BuildingDef def, GameObject go, List<CoiEntry> entries)
        {
            // All ROOM-EMISSION output offsets below are UNROTATED by the game at their emission
            // sites (EnergyGenerator.cs:370, ElementConverter.cs:558, ComplexFabricator.cs:1224).
            // The piped-port branch at the end of this method is the one exception: a conduit port
            // is not an emission site, it IS rotated (BuildingDef.cs:855, Building.cs:297-301), and
            // its entry is built with rotates: true. See that branch's own comment.
            var gen = go.GetComponent<EnergyGenerator>();
            if (gen != null && gen.formula.outputs != null)
                foreach (var o in gen.formula.outputs)
                    if (!o.store)
                        entries.Add(CoiEntry.AtCell(CoiClass.Output, o.emitOffset, deterministic: true, rotates: false, PhaseOf(o.element)));

            var fab = go.GetComponent<ComplexFabricator>();
            if (fab != null && !fab.storeProduced)
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, new Vector2(fab.outputOffset.x, fab.outputOffset.y), CoiPhase.Solid));

            var conv = go.GetComponent<ElementConverter>();
            if (conv != null && conv.outputElements != null)
                foreach (var oe in conv.outputElements)
                    if (!oe.storeOutput) // stored output is piped out a utility port, not emitted at this cell
                        entries.Add(CoiEntry.AtWorld(CoiClass.Output, oe.outputElementOffset, PhaseOf(oe.elementHash)));

            var emitter = go.GetComponent<BuildingElementEmitter>();
            if (emitter != null)
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, emitter.modifierOffset, PhaseOf(emitter.element)));

            var storage = go.GetComponent<Storage>();
            if (storage != null && storage.dropOffset != Vector2.zero)
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, storage.dropOffset, CoiPhase.Solid));

            // Piped outputs (step 5, spec §7). `store`/`storeProduced`/`storeOutput` above all mean
            // "goes into this building's own Storage" (EnergyGenerator.cs:354-368,
            // ElementConverter.cs:532-555, ComplexFabricator.cs:1253/1279/1288), NOT "leaves through
            // a pipe" - most stored outputs are dupe-fetched or ElementDropper-dropped and never
            // touch a conduit (Fertilizer Synthesizer, Compost, Rust Deoxidizer all store with no
            // OutputConduitType at all). So a real port has to exist first; only then does a stored
            // output mean anything. Color and cell both come from the port, never from the element.
            //
            // The gen and conv arms filter on phase because that is the test the game applies at
            // runtime (ConduitDispenser.FindSuitableElement, ConduitDispenser.cs:167-186: a Liquid
            // port only ever ships an IsLiquid element, a Gas port only IsGas). The filter decides
            // only WHETHER an entry exists, never what color it is. No stock building fails it -
            // every vanilla port-plus-stored-output building has at least one phase-matching stored
            // output - so it earns its place as a guard against a modded building whose only stored
            // output cannot use its own port and would otherwise advertise a route that never
            // carries anything. Rejected: dropping it, free on stock content and one silent wrong
            // tint on the first mod that trips it.
            //
            // The fab arm deliberately has NO store test, because `storeProduced` is not the game's
            // stored predicate: ComplexFabricator.cs:1253/1279/1288 all read
            // `storeProduced || recipeElement.storeElement`, and four stock buildings pipe a liquid
            // out with `storeProduced == false` sitting on the prefab. Chemical Refinery and Milk
            // Press set it true then clear it (ChemicalRefineryConfig.cs:62/66,
            // MilkPressConfig.cs:47/51) and mark every liquid result `storeElement: true`; Sludge
            // Press never sets it and passes IsLiquid straight into the storeElement parameter
            // (SludgePressConfig.cs:72); Metal Refinery pipes its heated coolant back out with no
            // store flag anywhere (MetalRefineryConfig.cs:34-35, dispenser at :73-74). Gating on
            // storeProduced drops all four. All seven vanilla ComplexFabricators carrying an output
            // port - those four plus Glass Forge, Smoker, Uranium Centrifuge - genuinely ship
            // material through it, so "is a fabricator, has a port" is the honest test. It also
            // sidesteps the arm's real limit: products are per-recipe Tag-keyed materials with no
            // SimHashes to PhaseOf() at def time, so this arm could not phase-filter even if it
            // wanted to. Rejected: scanning ComplexRecipeManager for `storeElement` per result at
            // def-build time - a whole recipe-table lookup to re-derive what the port already says.
            //
            // One primary port per building (OutputConduitType is a single enum), so every
            // qualifying source resolves to the identical cell and phase - emit at most one entry,
            // never one per qualifying output, or Polymerizer (Steam+CO2, both Gas, one port) and
            // Smoker (a fabricator plus a storeOutput ElementConverter on the same gas port) stack
            // two identical quads and double the alpha. That dedupe is within-branch only: a port
            // cell already carrying an entry from another class still draws two quads. Two stock
            // cases do - Desalinator, whose port is (0,0) and whose DesalinatorWorkableEmpty pivots
            // there, and Metal Refinery, whose fabricator drop tint lands on the same cell as its
            // (1,0) port. Cross-class collapse is step 6's job (spec §9) and step 5 ships first
            // (spec §13), so those two cells read as a blend until then.
            if (CoiConfig.Active.TintPipedOutputs
                && CoiPortResolver.TryGetOutputPort(def, out CellOffset portOffset, out CoiPhase portPhase))
            {
                bool piped =
                    fab != null
                    || (gen != null && gen.formula.outputs != null
                        && Array.Exists(gen.formula.outputs, o => o.store && PhaseOf(o.element) == portPhase))
                    || (conv != null && conv.outputElements != null
                        && Array.Exists(conv.outputElements, oe => oe.storeOutput && PhaseOf(oe.elementHash) == portPhase));

                if (piped)
                    entries.Add(CoiEntry.AtCell(CoiClass.Output, portOffset, deterministic: false, rotates: true, portPhase));
            }
        }

        // Buildings whose thermal rectangle is not their footprint's bounding box, as deltas on
        // that box: (xMin, xMax, yMin, yMax), negative widening left/down. Deltas rather than
        // absolute rects so the Steam Turbine row can say what the game says - the same box, one
        // row lower - instead of a 5x4 literal that silently goes wrong if Klei resizes the def.
        //
        // Produced by sweeping the decompiled Assembly-CSharp (game build shipping with the Aquatic
        // Planet Pack) for StructureTemperaturePayload.OverrideExtents. That found exactly four call
        // sites in three shapes, all four listed here. Keyed on PrefabID rather than reflecting each
        // config's private `overrideOffsets` array, which no compile error would protect; the cost
        // is that a game update adding a fifth override passes unnoticed, so re-run that sweep when
        // the game updates. A building missing from this table draws NOTHING - see AddHeat for why
        // that is the whole point rather than a gap.
        private static readonly Dictionary<string, (int xMin, int xMax, int yMin, int yMax)> ExtentsOverrides
            = new Dictionary<string, (int, int, int, int)>
        {
            // Tempshift Plate: def is 1x1, reach is 3x3 centred on the cell. The single biggest
            // reason this class exists - nothing in the game's UI says the plate is 3x3.
            // ThermalBlockConfig.overrideOffsets = the four diagonals.
            { "ThermalBlock",   (-1, 1, -1, 1) },
            // Ice-Cooled Fan: def is 2x2 (box x 0..1, y 0..1), reach is x -2..2, y 0..1.
            // IceCooledFanConfig.overrideOffsets = (-2,1),(2,1),(-1,0),(1,0).
            { "IceCooledFan",   (-2, 1,  0, 0) },
            // Steam Turbine, both the base-game and the DLC config: new Extents(x, y - 1, width,
            // height + 1). One row BELOW the footprint, which is how it reaches into the steam room
            // and is invisible in the UI.
            { "SteamTurbine",   ( 0, 0, -1, 0) },
            { "SteamTurbine2",  ( 0, 0, -1, 0) },
        };

        // Thermal contact cells (spec §8), for the five buildings whose heat reach is not their
        // footprint. Every other building emits nothing, which is the point of the class rather than
        // a gap in it - the reasoning is at the early return below.
        //
        // This does not consult AddWork's Workable exclusion list, and the divergence is deliberate.
        // That list asks whether a Workable is the building's primary operation; this asks whether
        // the game registers the building for structure heat at all. They agree on doors and tiles
        // only because those are SimCellOccupier and fail the thermal gate for an unrelated reason.
        private static void AddHeat(BuildingDef def, GameObject go, List<CoiEntry> entries)
        {
            // Tile- and door-likes become sim solids on spawn and exchange heat as world cells
            // rather than as a structure, so there is no structure rectangle to draw.
            if (go.GetComponent<SimCellOccupier>() != null)
                return;
            if (def.PlacementOffsets == null || def.PlacementOffsets.Length == 0)
                return;

            // Conduction Panel shape, and the one case that is a cell SET rather than a rectangle.
            // StructureToStructureTemperature exchanges with a BUILDING, not with the world, over
            // DefineConductiveCells = placement cells minus the utility input cell minus the utility
            // output cell. For the stock 3x1 ContactConductivePipeBridge that leaves only the middle
            // cell, which is why the panel does nothing at all unless something is built on that
            // cell - the most common way it is misplaced, and not visible anywhere in the UI.
            //
            // Deliberate under-report, corrected against the assembly after the spec was written:
            // ContactConductivePipeBridgeConfig ALSO sets UseStructureTemperature = true, so the
            // panel additionally exchanges with the world over its full 3x1 box, and this branch
            // returns without drawing that. Painting all three would say nothing - the box is
            // already the placement ghost on a 3-cell building - while erasing the one cell that
            // decides whether the panel functions. Ceiling: on a modded StructureToStructureTemperature
            // building whose footprint is large enough for the box to be news, the world-contact
            // cells go undrawn. Upgrade path is a second class or phase so both can be shown at
            // once, which §9 striping would then place side by side; not worth it for one stock
            // building whose whole point is the middle cell.
            if (go.GetComponent<StructureToStructureTemperature>() != null)
            {
                foreach (CellOffset off in def.PlacementOffsets)
                    if (!off.Equals(def.UtilityInputOffset) && !off.Equals(def.UtilityOutputOffset))
                        entries.Add(CoiEntry.AtCell(CoiClass.Heat, off, deterministic: false, rotates: true));
                return;
            }

            // Everything else registers an Extents RECTANGLE, never a cell set: Building.RefreshCells
            // builds it as the bounding box of the rotated PlacementOffsets and hands it to
            // SimMessages.AddBuildingHeatExchange.
            //
            // And a building not in the override table draws NOTHING, which is the single decision
            // that makes this class worth shipping. BuildingDef.GenerateOffsets is the only
            // assignment to PlacementOffsets anywhere in the game assembly and it always writes a
            // full width x height rectangle, so for every other building that bounding box IS the
            // footprint, cell for cell, with no exception. Drawing it repaints the placement ghost
            // in red and tells the player something already on screen. Across 450 IBuildingConfig
            // classes exactly four override their extents; an earlier draft drew every building, so
            // it was noise on 445 of them to be useful on five.
            //
            // It also drops a class of confidently wrong answers. A modded building with its own
            // OverrideExtents is not in this table, and painting its footprint would have asserted a
            // reach the game does not use. Emitting nothing asserts nothing, which is correct.
            //
            // The 29 stock configs that set UseStructureTemperature = false are tiles and rocket
            // ports, already excluded above as SimCellOccupier, so nothing was learned from their
            // absence either. The check stays anyway: it costs one comparison and it holds if a mod
            // ever re-registers one of the four IDs below with structure temperature turned off.
            if (!ExtentsOverrides.TryGetValue(def.PrefabID, out var d) || !def.UseStructureTemperature)
                return;

            int xMin = int.MaxValue, xMax = int.MinValue, yMin = int.MaxValue, yMax = int.MinValue;
            foreach (CellOffset off in def.PlacementOffsets)
            {
                if (off.x < xMin) xMin = off.x;
                if (off.x > xMax) xMax = off.x;
                if (off.y < yMin) yMin = off.y;
                if (off.y > yMax) yMax = off.y;
            }
            xMin += d.xMin;
            xMax += d.xMax;
            yMin += d.yMin;
            yMax += d.yMax;

            // Deterministic: false, so contact cells draw at candidate alpha. They cover more screen
            // area than every other class put together and must not shout over the work cell. That
            // also routes them through CoiTintController's Grid.Solid cull, which drops the ones
            // sitting in solid terrain - wanted here, since a plate's reach into un-dug rock is not
            // a placement decision the player is making.
            for (int y = yMin; y <= yMax; y++)
                for (int x = xMin; x <= xMax; x++)
                    entries.Add(CoiEntry.AtCell(CoiClass.Heat, new CellOffset(x, y), deterministic: false, rotates: true));
        }

        // Element phase for an output. Null-guarded: FindElementByHash returns null for an
        // unregistered/modded hash, and an NRE here would be swallowed by Build's try/catch and
        // blank EVERY tint for the building (work cells included), not just this one output.
        private static CoiPhase PhaseOf(SimHashes hash)
        {
            Element e = ElementLoader.FindElementByHash(hash);
            if (e == null) return CoiPhase.Solid;
            if (e.IsLiquid) return CoiPhase.Liquid;
            if (e.IsGas) return CoiPhase.Gas;
            return CoiPhase.Solid;
        }

        private static bool TryExplicitOffset(Workable w, out CellOffset cell)
        {
            foreach (var name in ExplicitOffsetFields)
            {
                var f = AccessTools.Field(w.GetType(), name);
                if (f != null && f.FieldType == typeof(CellOffset))
                {
                    cell = (CellOffset)f.GetValue(w);
                    return true;
                }
            }
            cell = default;
            return false;
        }
    }
}
