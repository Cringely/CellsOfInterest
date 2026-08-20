using HarmonyLib;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace CellsOfInterest
{
    public sealed class Mod : KMod.UserMod2
    {
        // base UserMod2.OnLoad is what runs harmony.PatchAll on this assembly. Every Harmony
        // patch in this mod exists only because of that call, so base.OnLoad stays the first
        // statement here and runs before anything touches PLib. Dropping it disables the whole
        // mod silently: no exception, nothing in Player.log, only tints that never appear.
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);

            // Everything below is the options screen, which is optional. An escaping throw here
            // would be caught by DLLLoader, leave Content.DLL unset, and get the mod marked
            // crashed and disabled for the next launch. Tinting does not need PLib, so a PLib
            // failure costs the settings UI and nothing else.
            try
            {
                PUtil.InitLibrary();
                // RegisterOptions is an instance method on POptions (it calls RegisterForForwarding
                // on itself), not a static, so the instance is required.
                new POptions().RegisterOptions(this, typeof(CoiSettings));
                CoiConfig.Reload();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CellsOfInterest] options setup failed; tints still work, "
                    + "the Mods-menu settings screen will be missing: " + e);
            }
        }
    }
}
