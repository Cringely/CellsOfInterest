using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CellsOfInterest
{
    public enum CoiClass { Work, Output }

    public struct CoiEntry
    {
        public CoiClass Cls;
        public bool Deterministic;  // solid tint vs low-alpha candidate
        public bool Rotates;        // explicit work offsets only (spec: rotation rules)
        public bool IsWorldOffset;  // float world-space offset (outputs) vs integer CellOffset
        public CellOffset Cell;
        public Vector2 World;

        public static CoiEntry AtCell(CoiClass cls, CellOffset cell, bool deterministic, bool rotates)
            => new CoiEntry { Cls = cls, Cell = cell, Deterministic = deterministic, Rotates = rotates };

        public static CoiEntry AtWorld(CoiClass cls, Vector2 world)
            => new CoiEntry { Cls = cls, World = world, IsWorldOffset = true, Deterministic = true };
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

        // Explicit work-offset field names seen in configs (Bottler.workCellOffset etc.).
        private static readonly string[] ExplicitOffsetFields = { "workCellOffset", "workOffset" };

        public static CoiData Get(BuildingDef def)
        {
            if (def == null)
                return CoiData.Empty;
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
            AddOutputs(go, entries);

            return entries.Count == 0 ? CoiData.Empty : new CoiData { Entries = entries.ToArray() };
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
                    || w is Toggleable || w is Breakable || w.GetType().Name == "StorageTileSwitchItemWorkable")
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

        private static void AddOutputs(GameObject go, List<CoiEntry> entries)
        {
            // All output offsets are UNROTATED by the game at their emission sites
            // (EnergyGenerator.cs:370, ElementConverter.cs:558, ComplexFabricator.cs:1224).
            var gen = go.GetComponent<EnergyGenerator>();
            if (gen != null && gen.formula.outputs != null)
                foreach (var o in gen.formula.outputs)
                    if (!o.store)
                        entries.Add(CoiEntry.AtCell(CoiClass.Output, o.emitOffset, deterministic: true, rotates: false));

            var fab = go.GetComponent<ComplexFabricator>();
            if (fab != null && !fab.storeProduced)
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, new Vector2(fab.outputOffset.x, fab.outputOffset.y)));

            var conv = go.GetComponent<ElementConverter>();
            if (conv != null && conv.outputElements != null)
                foreach (var oe in conv.outputElements)
                    entries.Add(CoiEntry.AtWorld(CoiClass.Output, oe.outputElementOffset));

            var emitter = go.GetComponent<BuildingElementEmitter>();
            if (emitter != null)
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, emitter.modifierOffset));

            var storage = go.GetComponent<Storage>();
            if (storage != null && storage.dropOffset != Vector2.zero)
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, storage.dropOffset));
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
