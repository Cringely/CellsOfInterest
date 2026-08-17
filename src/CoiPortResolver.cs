namespace CellsOfInterest
{
    // Resolves a BuildingDef's primary conduit OUTPUT port: the cell a stored output actually
    // leaves the building through, when TintPipedOutputs (spec §7) is on. A lookup over the def,
    // not a subsystem — no component reads, no reflection, no caching of its own (CoiResolver's
    // per-def cache already covers the caller).
    //
    // The def carries the whole answer as two public fields, and nothing else needs consulting:
    //   ConduitType OutputConduitType   - BuildingDef.cs:58
    //   CellOffset  UtilityOutputOffset - BuildingDef.cs:173 (default (1,0), NOT a sentinel - see below)
    // That pair is exactly what the game itself uses to reserve the port's cell
    // (BuildingDef.MarkArea, BuildingDef.cs:853-860), release it (UnmarkArea, :1001-1006), validate
    // placement against it (AreConduitPortsInValidPositions, :1437-1442), and pick the ghost's port
    // icon (BuildingCellVisualizer.cs:38-59 -> BuildingDef.CheckRequiresGasOutput/LiquidOutput/
    // SolidOutput, BuildingDef.cs:1879-1902).
    //
    // Rejected: reading a ConduitDispenser component instead (spec §7's second bullet). Its
    // GetOutputCell (ConduitDispenser.cs:196-217) reduces to Building.GetUtilityOutputCell(), i.e.
    // this same def pair, on every vanilla prefab - measured 14/14 agreement between
    // def.OutputConduitType and the dispenser's own conduitType across every stock store-flagged
    // building that carries one, re-derived from the configs rather than taken on trust. The
    // one path where a dispenser would disagree, useSecondaryOutput, is set by exactly three
    // runtime AddComponent call sites (RocketConduitReceiver, WarpConduitReceiver,
    // RailGunPayloadOpener), none of which has a store-flagged output, and secondary ports are out
    // of scope (spec constraint 5). A second lookup path with zero stock case behind it, and whose
    // fields (ConduitDispenser.utilityCell/GetOutputCell) are both private and both -1 on an
    // unspawned def prefab anyway (ConduitDispenser.cs:48,95 - OnSpawn hasn't run), is a primitive
    // without a justification. Reading the def field is simpler and it is what the game itself
    // reads at every site above.
    internal static class CoiPortResolver
    {
        // UtilityOutputOffset is NOT "unset means no port": several no-conduit buildings assign it
        // anyway (AlgaeHabitatConfig.cs:35 and CompostConfig.cs:28 both set (0,0) with no
        // OutputConduitType - the disease-port icon reads the same field), and the untouched
        // default (1,0) is a REAL port on AlgaeDistillery, which sets OutputConduitType but never
        // touches the offset (AlgaeDistilleryConfig.cs:31). OutputConduitType is the only field the
        // game ever tests for existence; the offset is meaningless without it.
        public static bool TryGetOutputPort(BuildingDef def, out CellOffset offset, out CoiPhase phase)
        {
            switch (def.OutputConduitType)
            {
                case ConduitType.Gas: phase = CoiPhase.Gas; break;
                case ConduitType.Liquid: phase = CoiPhase.Liquid; break;
                case ConduitType.Solid: phase = CoiPhase.Solid; break;
                default:
                    offset = default;
                    phase = CoiPhase.None;
                    return false;
            }
            offset = def.UtilityOutputOffset;
            return true;
        }
    }
}
