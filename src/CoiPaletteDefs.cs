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

        // There is no CoiClass.Heat yet, so nothing reads this: CoiPalette.For has no arm that
        // returns it and no other caller exists. The slot is filled now anyway: the three
        // colorblind sets are graded on whether all five classes stay mutually distinguishable
        // under simulation, and grading four of them is a different, easier test that would have
        // to be redone the moment heat existed. Filling it late would mean tuning twice.
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

    // The four named palettes. Default is v1's color set; the other three are tuned against the
    // matching color-vision deficiency.
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

        // Deuteranopia and Protanopia start from one set: both are red-green deficiencies, so
        // separation has to live on the blue-orange-purple axis with luminance steps either way.
        // They are written out twice rather than aliased because the in-game retune grades them
        // under two different simulations and may well split them; sharing one def would make a
        // fix for one silently change the other.
        //
        // Values are Okabe-Ito. Untuned starting points: the spec's acceptance test is that the
        // five classes stay mutually distinguishable under that deficiency's simulation and stay
        // off the desaturated build-mode background, and that check has not been run yet.
        //
        // Heat is the tightest pair here — #D55E00 and gas #E69F00 both read yellow-orange under
        // red-green simulation and separate on luminance alone. If they collapse in-game, the
        // spec's fallback is heat at #F0E442, rechecked against the background rather than
        // against the other classes.
        public static readonly CoiPaletteDef Deuteranopia = new CoiPaletteDef(
            work:   Hex(0x00, 0x72, 0xB2),
            gas:    Hex(0xE6, 0x9F, 0x00),
            liquid: Hex(0x56, 0xB4, 0xE9),
            solid:  Hex(0xCC, 0x79, 0xA7),
            heat:   Hex(0xD5, 0x5E, 0x00));

        public static readonly CoiPaletteDef Protanopia = new CoiPaletteDef(
            work:   Hex(0x00, 0x72, 0xB2),
            gas:    Hex(0xE6, 0x9F, 0x00),
            liquid: Hex(0x56, 0xB4, 0xE9),
            solid:  Hex(0xCC, 0x79, 0xA7),
            heat:   Hex(0xD5, 0x5E, 0x00));

        // Tritanopia is a blue-yellow deficiency, so it moves off the axis the two above use.
        // Okabe-Ito again except heat, which is Paul Tol's muted #882255 so it sits in the same
        // family as the rest of this column. Heat and solid #CC6677 separate on luminance, which
        // makes them the pair to check first during the retune.
        public static readonly CoiPaletteDef Tritanopia = new CoiPaletteDef(
            work:   Hex(0x00, 0x9E, 0x73),
            gas:    Hex(0xD5, 0x5E, 0x00),
            liquid: Hex(0x11, 0x77, 0x33),
            solid:  Hex(0xCC, 0x66, 0x77),
            heat:   Hex(0x88, 0x22, 0x55));

        public static CoiPaletteDef Get(PaletteChoice choice)
        {
            switch (choice)
            {
                case PaletteChoice.Deuteranopia: return Deuteranopia;
                case PaletteChoice.Protanopia: return Protanopia;
                case PaletteChoice.Tritanopia: return Tritanopia;
                // Not just C# exhaustiveness. PaletteChoice persists to config.json as an ordinal
                // (see CoiSettings), and Newtonsoft will deserialize any integer into the enum
                // without complaint, so a hand-edited or future-version config can hand us a value
                // no member matches. Landing that on the v1 colors is the conservative answer.
                default: return Default;
            }
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
