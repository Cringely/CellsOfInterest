using HarmonyLib;
using UnityEngine;

namespace CellsOfInterest
{
    // OnActivateTool instantiates, orients, and activates def.BuildingPreview as `visualizer`
    // inside the method body (BuildTool.cs:51-94), so this Postfix sees it ready. It re-fires
    // once per building selection and DOUBLE-fires on first tool open (BuildTool.cs:116-124),
    // hence the idempotent GetComponent check.
    [HarmonyPatch(typeof(BuildTool), "OnActivateTool")]
    public static class BuildTool_OnActivateTool_Patch
    {
        public static void Postfix(BuildTool __instance)
        {
            // Re-read config.json here so a change saved from the Mods menu applies on the next
            // building selection instead of waiting for a colony reload. Runs before the
            // controller is attached, so its first Redraw already sees the new values.
            CoiConfig.Reload();

            // `visualizer` is a public field declared on InterfaceTool (BuildTool -> DragTool ->
            // InterfaceTool; decompile confirms `public GameObject visualizer;`), so no
            // Traverse reflection is needed here.
            GameObject vis = __instance.visualizer;
            if (vis == null)
                return;
            if (vis.GetComponent<CoiTintController>() == null)
                vis.AddComponent<CoiTintController>();
        }
    }
}
