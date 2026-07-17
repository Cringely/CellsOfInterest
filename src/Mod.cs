using HarmonyLib;
using UnityEngine;

namespace CellsOfInterest
{
    // Base UserMod2.OnLoad calls harmony.PatchAll on this assembly.
    public sealed class Mod : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            Debug.Log("[CellsOfInterest] loaded build 2026-07-16-diag1");
        }
    }
}
