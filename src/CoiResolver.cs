using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CellsOfInterest
{
    public enum CoiClass { Work, Output }

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
            if (e.Cls == CoiClass.Work)
                return s.TintWork;
            switch (e.Phase)
            {
                case CoiPhase.Gas: return s.TintGas;
                case CoiPhase.Liquid: return s.TintLiquid;
                // Solid, None, and anything outside the enum. Same fold as CoiPalette.For, which
                // has no None arm either: the toggle that hides a cell is named after the color the
                // cell is drawn in, so "Solid outputs: off" cannot leave a purple cell on screen.
                // Step 7 caution: spec section 8 gives heat entries CoiPhase.None, so CoiClass.Heat
                // needs its own arm on e.Cls above or it silently gates on TintSolid (default true)
                // instead of TintHeat (default false), with no compile error.
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
