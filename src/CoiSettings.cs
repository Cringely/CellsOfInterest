using PeterHan.PLib.Options;

namespace CellsOfInterest
{
    // Both enums below persist to config.json as INTEGERS, not names: the game ships
    // Newtonsoft.Json 7.0.1, neither enum carries a [JsonConverter], and Newtonsoft's default for
    // an enum is its ordinal. So member order is the on-disk wire format once v2 is released.
    // Append new members at the end, never insert or reorder — inserting silently shifts every
    // existing player's saved value by one, with no error and no migration.

    // Named palettes. Default is the v1 color set; the other three are tuned against the
    // matching color-vision deficiency.
    public enum PaletteChoice
    {
        Default,
        Deuteranopia,
        Protanopia,
        Tritanopia
    }

    // How a cell carrying more than one entry is drawn. Priority draws only the highest-priority
    // class and never moves; Rotate steps through the classes on a timer.
    //
    // Priority is first so the zero value matches the shipping default. A settings file that is
    // truncated, hand-edited to garbage, or written by a future version that drops a member
    // deserializes to 0, and that has to land on the conservative mode rather than on motion.
    public enum SharedCellMode
    {
        Priority,
        Rotate
    }

    // Mod options, surfaced by PLib POptions in the Mods menu and persisted to config.json.
    //
    // PLib reads and writes options through PropertyInfo (OptionsHandlers.FindOptionClass and
    // OptionsEntry.TryCreateEntry both take a PropertyInfo), so every setting has to be a
    // property, not a field. Public type and public accessors, because POptions builds the
    // dialog by reflecting this type from PLib's own assembly.
    //
    // Every default below reproduces v1 behavior, so a player who updates and restarts without
    // opening this screen sees what v1 showed. The one disclosed exception is SharedCells:
    // v1 blended stacked quads into mud, which is the defect the shared-cell modes exist to
    // remove, so Priority is the closest static equivalent. Changing any default here changes
    // what an existing player sees on upgrade; it is not a free edit.
    public sealed class CoiSettings
    {
        // Format version of the persisted file, carried so a later release can migrate a config
        // written by this one. Deliberately not an [Option]: it round-trips through the JSON but
        // never appears in the dialog.
        public int ConfigFileFormat { get; set; } = 1;

        [Option("Palette", "Color set used for every tint class.")]
        public PaletteChoice Palette { get; set; } = PaletteChoice.Default;

        [Option("Work cells", "Tint the cells a duplicant operates the building from.", "Tint classes")]
        public bool TintWork { get; set; } = true;

        [Option("Gas outputs", "Tint the cells a building emits gas into.", "Tint classes")]
        public bool TintGas { get; set; } = true;

        [Option("Liquid outputs", "Tint the cells a building emits liquid into.", "Tint classes")]
        public bool TintLiquid { get; set; } = true;

        [Option("Solid outputs", "Tint the cells a building drops solid output into.", "Tint classes")]
        public bool TintSolid { get; set; } = true;

        [Option("Piped outputs", "Tint the conduit output port of outputs that leave through a pipe instead of into the room.", "Tint classes")]
        public bool TintPipedOutputs { get; set; } = false;

        [Option("Heat exchange", "Tint the cells a building exchanges heat with the world over.", "Tint classes")]
        public bool TintHeat { get; set; } = false;

        [Option("Shared cells", "How a cell carrying more than one class is drawn: rotating through the colors, or showing only the highest-priority one.", "Shared cells")]
        public SharedCellMode SharedCells { get; set; } = SharedCellMode.Priority;

        // Floor is a flash-rate limit, not a taste call: three classes at 0.40s is 2.5
        // transitions per second, under the 3-per-second general flash threshold in WCAG 2.1.
        // Keep the lower bound if this is ever retuned.
        [Option("Rotation interval", "Seconds each color holds before the next one. Only used when shared cells rotate.", "Shared cells", Format = "F2")]
        [Limit(0.40, 2.00)]
        public float RotateInterval { get; set; } = 0.70f;

        [Option("Confirmed cell opacity", "Opacity of cells the mod resolved exactly.", "Opacity", Format = "F2")]
        [Limit(0.10, 0.90)]
        public float AlphaSolid { get; set; } = 0.55f;

        [Option("Candidate cell opacity", "Opacity of cells the mod could only infer.", "Opacity", Format = "F2")]
        [Limit(0.10, 0.90)]
        public float AlphaCandidate { get; set; } = 0.25f;
    }
}
