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
            List<string> sources;
            try
            {
                (data, sources) = Build(def);
            }
            catch (Exception e)
            {
                // A modded building must never crash-loop the build menu (spec: failure handling).
                Debug.LogWarning($"[CellsOfInterest] resolver failed for {def.PrefabID}: {e}");
                data = CoiData.Empty;
                sources = new List<string> { $"EXCEPTION:{e.GetType().Name}" };
            }
            cache[def] = data;
            // TEMPORARY diagnostic (2026-07-16): log the source of every resolved entry once
            // per def so we can see ground truth for buildings that should resolve to zero
            // entries (tiles, ladders, drywall) but are showing tints/legend anyway.
            Debug.Log($"[CellsOfInterest] resolve {def.PrefabID}: {data.Entries.Length} entries [{string.Join(", ", sources)}]");
            return data;
        }

        private static (CoiData, List<string>) Build(BuildingDef def)
        {
            var sources = new List<string>();
            var go = def.BuildingComplete;
            if (go == null)
                return (CoiData.Empty, sources);
            var entries = new List<CoiEntry>();

            AddWork(go, entries, sources);
            AddOutputs(go, entries, sources);

            var data = entries.Count == 0 ? CoiData.Empty : new CoiData { Entries = entries.ToArray() };
            return (data, sources);
        }

        private static void AddWork(GameObject go, List<CoiEntry> entries, List<string> sources)
        {
            foreach (var w in go.GetComponents<Workable>())
            {
                if (w is Storage)
                    continue; // pure-storage approach cells are out of scope (2026-07-16 ruling)

                // Skip ONLY the maintenance Workables verified as added to every building by the
                // game's own universal building setup, not by any per-config opt-in:
                //  - Deconstructable: BuildingConfigManager.OnPrefabInit ->
                //    baseTemplate.AddComponent<Deconstructable>() (BuildingConfigManager.cs:37).
                //  - BuildingHP: BuildingConfigManager.cs:45 adds BuildingHP to baseTemplate
                //    (every building's universal template).
                //  - Repairable: BuildingDef.Repairable defaults to true (BuildingDef.cs:74) and
                //    BuildingLoader.cs:216 calls UpdateComponentRequirement<Repairable> for any
                //    def with Repairable == true, i.e. almost every building unless it opts out.
                //  - Door: Door : Workable, toggle errand is incidental to placement planning
                //    (user-directed exclusion: door open/close/lock UI should not tint cells).
                // These three (Deconstructable, BuildingHP, Repairable) are the exhaustive set of
                // universal-template Workables added by the game's own infrastructure.
                // Without this skip, maintenance Workables fall into the unknown-subclass fallback
                // below and get a candidate pivot tint on every tile/ladder/drywall (spec bug: tints
                // on ALL buildings).
                //
                // Deliberately NOT excluded: Demolishable (added per-config via
                // BuildingTemplates.ExtendBuildingToGravitas, not universal). It stays on the
                // unknown-Workable candidate-pivot fallback path, same as any other operational
                // interaction Workable (e.g. Sleepable, the manual generator wheel) that players
                // rely on for automation-sensor placement.
                if (w is Deconstructable || w is Repairable || w is BuildingHP || w is Door)
                {
                    sources.Add($"skip:{w.GetType().Name}");
                    continue;
                }

                // Explicit single offset set by the config (deterministic, rotates with the building).
                if (TryExplicitOffset(w, out var cell))
                {
                    entries.Add(CoiEntry.AtCell(CoiClass.Work, cell, deterministic: true, rotates: true));
                    sources.Add($"Work:explicit:{w.GetType().Name}");
                    continue;
                }

                // ComplexFabricatorWorkable never sets offsets: dupe stands on the pivot (verified).
                if (w is ComplexFabricatorWorkable)
                {
                    entries.Add(CoiEntry.AtCell(CoiClass.Work, new CellOffset(0, 0), deterministic: true, rotates: false));
                    sources.Add($"Work:{w.GetType().Name}");
                    continue;
                }

                // Unknown Workable subclass: it may set offsets in OnPrefabInit where this
                // resolver cannot see. Pivot as CANDIDATE, never authoritative (spec honesty rule).
                entries.Add(CoiEntry.AtCell(CoiClass.Work, new CellOffset(0, 0), deterministic: false, rotates: false));
                sources.Add($"Work:{w.GetType().Name}");
            }
        }

        private static void AddOutputs(GameObject go, List<CoiEntry> entries, List<string> sources)
        {
            // All output offsets are UNROTATED by the game at their emission sites
            // (EnergyGenerator.cs:370, ElementConverter.cs:558, ComplexFabricator.cs:1224).
            var gen = go.GetComponent<EnergyGenerator>();
            if (gen != null && gen.formula.outputs != null)
                foreach (var o in gen.formula.outputs)
                    if (!o.store)
                    {
                        entries.Add(CoiEntry.AtCell(CoiClass.Output, o.emitOffset, deterministic: true, rotates: false));
                        sources.Add("Out:EnergyGenerator");
                    }

            var fab = go.GetComponent<ComplexFabricator>();
            if (fab != null && !fab.storeProduced)
            {
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, new Vector2(fab.outputOffset.x, fab.outputOffset.y)));
                sources.Add("Out:ComplexFabricator");
            }

            var conv = go.GetComponent<ElementConverter>();
            if (conv != null && conv.outputElements != null)
                foreach (var oe in conv.outputElements)
                {
                    entries.Add(CoiEntry.AtWorld(CoiClass.Output, oe.outputElementOffset));
                    sources.Add("Out:ElementConverter");
                }

            var emitter = go.GetComponent<BuildingElementEmitter>();
            if (emitter != null)
            {
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, emitter.modifierOffset));
                sources.Add("Out:BuildingElementEmitter");
            }

            var storage = go.GetComponent<Storage>();
            if (storage != null && storage.dropOffset != Vector2.zero)
            {
                entries.Add(CoiEntry.AtWorld(CoiClass.Output, storage.dropOffset));
                sources.Add("Out:Storage.dropOffset");
            }
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
