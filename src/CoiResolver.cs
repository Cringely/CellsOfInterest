using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CellsOfInterest
{
    public enum CoiClass { Work, Delivery, Output }

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
            AddDelivery(go, entries);
            AddOutputs(go, entries);

            return entries.Count == 0 ? CoiData.Empty : new CoiData { Entries = entries.ToArray() };
        }

        private static void AddWork(GameObject go, List<CoiEntry> entries)
        {
            foreach (var w in go.GetComponents<Workable>())
            {
                if (w is Storage)
                    continue; // delivery, handled below

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

        private static void AddDelivery(GameObject go, List<CoiEntry> entries)
        {
            // Delivery/approach is Storage-driven: the game puts an OffsetGroups reachability
            // table on every Storage (Storage.OnPrefabInit), and any Storage means dupe traffic
            // (deliver in / fetch out) at those cells. ManualDeliveryKG is NOT required —
            // Storage Bins and Graves are fetch-driven and have none.
            var storage = go.GetComponent<Storage>();
            if (storage == null)
                return;

            // Explicit delivery-offset overrides (spec cache-input list: CreatureDeliveryPoint
            // instance field deliveryOffsets (OnPrefabInit), Grave static DELIVERY_OFFSETS
            // (OnSpawn); both applied via Storage.SetOffsets). Fields readable on the prefab;
            // deterministic, rotates like other explicit offsets.
            foreach (var comp in go.GetComponents<KMonoBehaviour>())
            {
                foreach (var fieldName in new[] { "deliveryOffsets", "DELIVERY_OFFSETS" })
                {
                    var f = AccessTools.Field(comp.GetType(), fieldName);
                    if (f != null && f.FieldType == typeof(CellOffset[]) && f.GetValue(f.IsStatic ? null : comp) is CellOffset[] overrides && overrides.Length > 0)
                    {
                        foreach (var c in overrides)
                            entries.Add(CoiEntry.AtCell(CoiClass.Delivery, c, deterministic: true, rotates: true));
                        return;
                    }
                }
            }

            // Storage IS a Workable; its approach table is chosen by useWideOffsets
            // (Storage.OnPrefabInit — field readable on the prefab, tracker is not). Tables are
            // never rotated by the game. row[0] of each row = candidate stand cell.
            CellOffset[][] table = storage.useWideOffsets
                ? OffsetGroups.InvertedWideTable
                : OffsetGroups.InvertedStandardTable;
            var seen = new HashSet<(int, int)>();
            foreach (var row in table)
            {
                var c = row[0];
                if (seen.Add((c.x, c.y)))
                    entries.Add(CoiEntry.AtCell(CoiClass.Delivery, c, deterministic: false, rotates: false));
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
