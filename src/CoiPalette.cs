using UnityEngine;

namespace CellsOfInterest
{
    // Single source of truth for tint colors, shared by CoiTintController (the quads) and
    // CoiLegend (the swatches) so a legend swatch can never drift from the tint it explains.
    // The colors themselves live in CoiPaletteDefs; this maps a (class, phase) onto one of them.
    internal static class CoiPalette
    {
        // Opaque color for an entry; the caller sets alpha by deterministic vs candidate, now from
        // CoiConfig.Active.AlphaSolid / AlphaCandidate (the sliders default to the 0.55 / 0.25 this
        // class used to hold as constants). CoiLegend's swatches take the returned Color unchanged,
        // so they stay fully opaque: UnityEngine.Color's (r, g, b) constructor sets a = 1.
        public static Color For(CoiClass cls, CoiPhase phase)
        {
            CoiPaletteDef palette = CoiPaletteDefs.Default;
            if (cls == CoiClass.Work) return palette.Work;
            // Heat carries CoiPhase.None, which the switch below folds into Solid, so it needs its
            // arm here for the same reason CoiResolver.Enabled does.
            if (cls == CoiClass.Heat) return palette.Heat;
            switch (phase)
            {
                case CoiPhase.Liquid: return palette.Liquid;
                case CoiPhase.Gas: return palette.Gas;
                default: return palette.Solid;
            }
        }
    }
}
