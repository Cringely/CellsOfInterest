using UnityEngine;

namespace CellsOfInterest
{
    // Single source of truth for tint colors, shared by CoiTintController (the quads) and
    // CoiLegend (the swatches) so a legend swatch can never drift from the tint it explains.
    // Colors are intentionally editable; retune after seeing them in-game.
    internal static class CoiPalette
    {
        public const float AlphaSolid = 0.55f;
        public const float AlphaCandidate = 0.25f;

        public static readonly Color Work   = new Color(0.20f, 0.85f, 0.25f);
        public static readonly Color Liquid = new Color(0.25f, 0.65f, 0.95f);
        public static readonly Color Gas    = new Color(0.95f, 0.60f, 0.15f);
        public static readonly Color Solid  = new Color(0.70f, 0.35f, 0.90f);

        // Opaque color for an entry; the caller sets alpha by deterministic vs candidate.
        public static Color For(CoiClass cls, CoiPhase phase)
        {
            if (cls == CoiClass.Work) return Work;
            switch (phase)
            {
                case CoiPhase.Liquid: return Liquid;
                case CoiPhase.Gas: return Gas;
                default: return Solid;
            }
        }
    }
}
