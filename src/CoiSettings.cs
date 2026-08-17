using PeterHan.PLib.Options;

namespace CellsOfInterest
{
    // The enum below persists to config.json as an INTEGER, not a name: the game ships
    // Newtonsoft.Json 7.0.1, it carries no [JsonConverter], and Newtonsoft's default for an enum is
    // its ordinal. So member order is the on-disk wire format once v2 is released.
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

    // Mod options, surfaced by PLib POptions in the Mods menu and persisted to config.json.
    //
    // PLib reads and writes options through PropertyInfo (OptionsHandlers.FindOptionClass and
    // OptionsEntry.TryCreateEntry both take a PropertyInfo), so every setting has to be a
    // property, not a field. Public type and public accessors, because POptions builds the
    // dialog by reflecting this type from PLib's own assembly.
    //
    // Every default below reproduces v1 behavior, so a player who updates and restarts without
    // opening this screen sees what v1 showed. The one disclosed exception carries no setting at
    // all: v1 blended stacked quads into mud, and spec §9 replaces that with one vertical stripe
    // per class, unconditionally. Striping is static and deterministic, so it introduces no motion
    // on upgrade and touches only the cells that already rendered as mud. Changing any default
    // here changes what an existing player sees on upgrade; it is not a free edit.
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

        // No shared-cell setting exists on purpose. Striping is unconditional (spec §9): it is
        // static, deterministic, needs no Update, and shows every class present, so neither of the
        // modes an earlier draft specified had anything left to offer. Do not reintroduce one
        // without re-reading §9 - a mode enum here is also a wire-format member, per the note above.

        [Option("Confirmed cell opacity", "Opacity of cells the mod resolved exactly.", "Opacity", Format = "F2")]
        [Limit(0.10, 0.90)]
        public float AlphaSolid { get; set; } = 0.55f;

        [Option("Candidate cell opacity", "Opacity of cells the mod could only infer.", "Opacity", Format = "F2")]
        [Limit(0.10, 0.90)]
        public float AlphaCandidate { get; set; } = 0.25f;
    }
}
