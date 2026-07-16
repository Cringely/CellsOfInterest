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
