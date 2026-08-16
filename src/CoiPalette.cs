using UnityEngine;

namespace CellsOfInterest
{
    // Single source of truth for tint colors, shared by CoiTintController (the quads) and
    // CoiLegend (the swatches) so a legend swatch can never drift from the tint it explains.
    // The colors themselves live in CoiPaletteDefs; this resolves the player's choice.
    internal static class CoiPalette
    {
        // Opaque color for an entry; the caller sets alpha by deterministic vs candidate, now from
        // CoiConfig.Active.AlphaSolid / AlphaCandidate (the sliders default to the 0.55 / 0.25 this
        // class used to hold as constants). CoiLegend's swatches take the returned Color unchanged,
        // so they stay fully opaque: UnityEngine.Color's (r, g, b) constructor sets a = 1.
        //
        // The def is looked up per call rather than held in a field or behind a named property:
        // CoiConfig.Reload replaces CoiConfig.Active wholesale on each build-tool activation, so a
        // def captured in a static initializer would serve whichever palette was selected when
        // this class was first touched and never change again. Get is a switch over four readonly
        // fields, and For is its only caller.
        public static Color For(CoiClass cls, CoiPhase phase)
        {
            CoiPaletteDef palette = CoiPaletteDefs.Get(CoiConfig.Active.Palette);
            if (cls == CoiClass.Work) return palette.Work;
            switch (phase)
            {
                case CoiPhase.Liquid: return palette.Liquid;
                case CoiPhase.Gas: return palette.Gas;
                default: return palette.Solid;
            }
        }
    }
}
