using UnityEngine;

namespace CellsOfInterest
{
    // One palette: the five class colors, opaque. Alpha is deliberately absent — it belongs to
    // the entry, not the palette (CoiTintController picks solid vs candidate alpha per entry),
    // so a def carrying one would give two owners for the same channel.
    internal sealed class CoiPaletteDef
    {
        public Color Work { get; }
        public Color Gas { get; }
        public Color Liquid { get; }
        public Color Solid { get; }

        public Color Heat { get; }

        internal CoiPaletteDef(Color work, Color gas, Color liquid, Color solid, Color heat)
        {
            Work = work;
            Gas = gas;
            Liquid = liquid;
            Solid = solid;
            Heat = heat;
        }
    }

    // The named palettes. Default is v1's color set and the only one left: the three palettes
    // named for colour-vision deficiencies were measured under a simulation of the deficiency each
    // was named for and did not separate the five classes, so they were removed rather than
    // shipped as an affordance that does not work.
    internal static class CoiPaletteDefs
    {
        // Default carries the v1 float literals verbatim, straight out of the pre-v2 CoiPalette.
        // The design spec writes the same colors as hex, and those are a rounded readout, not an
        // equivalent: 0.85f reads out as 0xD9, and 217/255f is 0.850980f, a different number.
        // Round-tripping through hex would move three of four channels on every class. The
        // shipping invariant is that a v1 player who updates and never opens the options screen
        // sees no change, and keeping the literals makes that true by construction instead of by
        // an argument about how small the shift is.
        public static readonly CoiPaletteDef Default = new CoiPaletteDef(
            work:   new Color(0.20f, 0.85f, 0.25f),
            gas:    new Color(0.95f, 0.60f, 0.15f),
            liquid: new Color(0.25f, 0.65f, 0.95f),
            solid:  new Color(0.70f, 0.35f, 0.90f),
            // Heat is new in v2 and has no v1 value to preserve, so it comes from the spec's hex.
            heat:   Hex(0xE6, 0x3C, 0x3C));

        // Every value lands on Default, which is a range check rather than C# exhaustiveness.
        // PaletteChoice persists to config.json as an ordinal (see CoiSettings) and Newtonsoft
        // deserializes any integer into the enum without complaint, so a config written while this
        // enum still had four members - or a hand-edited one - can hand us a 1, 2 or 3 that no
        // member matches. Landing that on the v1 colors is the conservative answer, and it is what
        // makes dropping the other three palettes safe for an existing config file.
        public static CoiPaletteDef Get(PaletteChoice choice)
        {
            return Default;
        }

        // Byte channels to Unity's 0..1 floats, the same division Color32's implicit conversion
        // to Color performs, so a hex from the spec lands on the value the engine would have
        // produced from the equivalent Color32.
        private static Color Hex(byte r, byte g, byte b)
        {
            return new Color(r / 255f, g / 255f, b / 255f);
        }
    }
}
