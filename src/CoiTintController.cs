using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CellsOfInterest
{
    // Attached to BuildTool's preview visualizer by BuildToolPatch. Lifetime = the preview's:
    // the game Destroys the visualizer on tool deactivate and on building re-selection
    // (BuildTool.cs:54-57, 96-107), which destroys this controller. The tint quads themselves
    // are parented to GameScreenManager.worldSpaceCanvas (not this transform) so they render as
    // UI Images and stay colored under the build-mode desaturation post effect the way vanilla's
    // port icons do (EntityCellVisualizer.DrawUtilityIcon); OnDestroy cleans them up explicitly.
    public sealed class CoiTintController : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f; // cadence re-check of Grid.Solid (spec: staleness receipt)

        private CoiData data = CoiData.Empty;
        private Rotatable rotatable;
        private readonly List<Image> pool = new List<Image>();
        private int lastCell = -1;
        private Orientation? lastOrientation;
        private float nextRefresh;
        private bool legendShown;

        private void Start()
        {
            var building = GetComponent<Building>();
            if (building != null && building.Def != null)
                data = CoiResolver.Get(building.Def);
            rotatable = GetComponent<Rotatable>();
            // Show() re-activates the shared panel carrying whatever rows the PREVIOUS preview
            // left, but no frame renders in between: this controller's first LateUpdate lands in
            // the frame its Start did, always reaches Redraw (lastOrientation starts null and the
            // computed orientation never is), and always publishes (Show reset the legend's
            // currentMask to -1, which no real mask equals). Reasoned from Unity's documented
            // Start -> Update -> LateUpdate -> Render phase order, not from a live capture.
            if (data.Entries.Length > 0)
            {
                CoiLegend.Show();
                legendShown = true;
            }
        }

        private void LateUpdate()
        {
            if (data.Entries.Length == 0)
                return;
            int cell = Grid.PosToCell(transform.position);
            Orientation orientation = rotatable != null ? rotatable.GetOrientation() : Orientation.Neutral;
            float now = Time.unscaledTime;
            if (cell == lastCell && orientation == lastOrientation && now < nextRefresh)
                return;
            lastCell = cell;
            lastOrientation = orientation;
            nextRefresh = now + RefreshSeconds;
            Redraw(cell);
        }

        private void Redraw(int baseCell)
        {
            int used = 0;
            // One bit per CoiLegend row, set only where this loop actually draws a quad for that
            // row's class/phase — "present" means drawn, not merely resolved (spec §6). Declared
            // 0 here (not inside the `if` below) so an invalid baseCell correctly publishes "no
            // rows" rather than skipping the publish and leaving the previous cell's rows on
            // screen. Plain int, not a HashSet/List: this runs every cell change, so it has to be
            // allocation-free and comparable with ==.
            int presentMask = 0;
            if (Grid.IsValidCell(baseCell))
            {
                Vector3 basePos = transform.position;
                foreach (var e in data.Entries)
                {
                    int cell;
                    if (e.IsWorldOffset)
                    {
                        // Output world offsets: raw adds, never rotated (matches emission sites).
                        cell = Grid.PosToCell(new Vector3(basePos.x + e.World.x, basePos.y + e.World.y, 0f));
                    }
                    else
                    {
                        CellOffset off = e.Cell;
                        if (e.Rotates && rotatable != null)
                            off = rotatable.GetRotatedCellOffset(off);
                        if (!Grid.IsCellOffsetValid(baseCell, off))
                            continue; // offset crosses the map edge: OffsetCell would wrap into the adjacent row
                        cell = Grid.OffsetCell(baseCell, off);
                    }

                    if (!Grid.IsValidCell(cell))
                        continue; // map edge / rocket interior boundary: skip, don't guess
                    // Candidate cell currently blocked: drop. The premise is occupancy - a duplicant
                    // standing here, or §8's future heat wash exchanging here - so it never applied
                    // to an Output, which says where material GOES and not that anything has to
                    // stand there. Every Output entry already skipped this cull before step 5 (all
                    // of them were Deterministic), so naming the class changes no existing entry: it
                    // makes that exemption explicit and extends it to the piped port entry, which is
                    // candidate alpha (spec §7) but fixed building geometry. The extension matters
                    // because a port cell IS routinely solid during a perfectly valid placement.
                    // Nothing in the placement path rejects a footprint sitting in un-dug rock:
                    // BuildingDef.IsAreaClear:547 reads object layers, world index and Unobtanium;
                    // AreConduitPortsInValidPositions:1423 delegates to IsValidConduitConnection:1621,
                    // which tests port-layer overlap and nothing else; the one Grid.Solid test,
                    // CheckBaseFoundation:1690, demands solid ground in the row BELOW the footprint
                    // rather than empty space inside it; and Constructable.PlaceDiggables:657 just
                    // queues the digs. So culling here would hide the port exactly while the player
                    // is planning where the pipe run has to reach. Rejected: a per-entry
                    // IgnoreSolidCull flag on CoiEntry - a struct field plus an AtCell parameter
                    // that exactly one call site would ever set, and CoiClass.Heat is not an Output,
                    // so step 7 inherits the cull spec §8 asks for under either form.
                    if (!e.Deterministic && e.Cls != CoiClass.Output && Grid.Solid[cell])
                        continue;

                    var quad = GetQuad(used++);
                    quad.transform.SetPosition(Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2));
                    Color c = CoiPalette.For(e.Cls, e.Phase);
                    // Not hoisted above the loop: the ternary reads one property either way, so
                    // there is nothing to hoist.
                    c.a = e.Deterministic ? CoiConfig.Active.AlphaSolid : CoiConfig.Active.AlphaCandidate;
                    quad.color = c;
                    quad.gameObject.SetActive(true);

                    int row = CoiLegend.RowIndexFor(e.Cls, e.Phase);
                    if (row >= 0)
                        presentMask |= 1 << row;
                }
            }
            for (int i = used; i < pool.Count; i++)
                pool[i].gameObject.SetActive(false);

            // Only call SetRows when the set actually changed, so a per-cell-change hook doesn't
            // become a per-frame panel rebuild (spec §6). Compared against the legend's own
            // published mask rather than a copy kept here: the panel is shared static state, so a
            // controller recording what it asked for could sit forever on rows it never got.
            if (presentMask != CoiLegend.CurrentMask)
                CoiLegend.SetRows(presentMask);
        }

        private Image GetQuad(int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("CoiTint");
                go.transform.SetParent(GameScreenManager.Instance.worldSpaceCanvas.transform, worldPositionStays: false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;              // must never block build clicks
                img.rectTransform.sizeDelta = Vector2.one; // canvas units are world units: one cell
                pool.Add(img);
            }
            return pool[index];
        }

        private void OnDestroy()
        {
            foreach (var img in pool)
                if (img != null)
                    Object.Destroy(img.gameObject);
            pool.Clear();
            if (legendShown)
                CoiLegend.Hide();
        }
    }
}
