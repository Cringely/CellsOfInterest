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
        // Surviving entries and the cell each resolved to, as two parallel lists rather than a
        // Dictionary<int, List<CoiEntry>>: Redraw runs on every cell change, so grouping has to be
        // allocation-free, and a dictionary plus a list per cell allocates on each call. Both are
        // cleared and refilled in place. Entry counts are single digits, so the O(n^2) grouping
        // scan in the draw pass costs less than the allocation it avoids.
        private readonly List<int> drawCells = new List<int>();
        private readonly List<CoiEntry> drawEntries = new List<CoiEntry>();
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
            drawCells.Clear();
            drawEntries.Clear();
            // One bit per CoiLegend row, set only where this pass actually keeps an entry for that
            // row's class/phase — "present" means drawn, not merely resolved (spec §6). Every entry
            // that survives to drawEntries gets a stripe, so setting the bit here and drawing in the
            // second pass cannot disagree. Declared 0 here (not inside the `if` below) so an invalid
            // baseCell correctly publishes "no rows" rather than skipping the publish and leaving
            // the previous cell's rows on screen. Plain int, not a HashSet/List: this runs every
            // cell change, so it has to be allocation-free and comparable with ==.
            int presentMask = 0;
            if (Grid.IsValidCell(baseCell))
            {
                Vector3 basePos = transform.position;
                // The two origins a world offset can be measured from. basePos is the building
                // transform, which BuildTool.cs:190 and BuildingDef.Build:398 both put at
                // Grid.CellToPosCBC - so the preview and the finished building agree, and an offset
                // the game adds to transform.position resolves the same either side of placing it.
                // baseCenter is the middle of the same cell, half a cell higher. CoiEntry.AtWorld
                // records which one each source uses and why; getting it wrong is a whole row.
                // Layer is irrelevant here because the z is thrown away below, and it is spelled
                // CellToPosCCC rather than an equivalent CellToPos call so it reads as the same
                // thing the emission sites do.
                Vector3 baseCenter = Grid.CellToPosCCC(baseCell, Grid.SceneLayer.Ore);
                foreach (var e in data.Entries)
                {
                    int cell;
                    if (e.IsWorldOffset)
                    {
                        // Output world offsets: raw adds, never rotated (matches emission sites).
                        Vector3 origin = e.FromCellCenter ? baseCenter : basePos;
                        cell = Grid.PosToCell(new Vector3(origin.x + e.World.x, origin.y + e.World.y, 0f));
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

                    // Collapse a repeat of the same class and phase on the same cell: it would be
                    // an identical-colored stripe, so it can only narrow the others for nothing.
                    // Same class, DIFFERENT phase is kept - two output phases landing on one cell
                    // is exactly the information §9 exists to show. Deterministic is not part of
                    // the key: it decides alpha only, and the first entry wins, which keeps the
                    // more-confident reading when a resolver ever emits both.
                    if (AlreadyDrawn(cell, e))
                        continue;

                    drawCells.Add(cell);
                    drawEntries.Add(e);

                    int row = CoiLegend.RowIndexFor(e.Cls, e.Phase);
                    if (row >= 0)
                        presentMask |= 1 << row;
                }
            }

            // Second pass: a cell carrying n entries is split into n equal vertical stripes rather
            // than n quads stacked at full width, which is what v1 did and what blended every
            // shared cell into mud (spec §9). Position and width are computed per quad because n
            // varies by cell within one preview.
            for (int i = 0; i < drawCells.Count; i++)
            {
                int cell = drawCells[i];
                CoiEntry e = drawEntries[i];
                StripeOf(i, cell, e, out int slot, out int n);

                var quad = GetQuad(i);
                // Canvas units are world units, so a cell is 1.0 wide and a stripe is 1/n. The
                // rect's pivot is centered, so the quad centers on the transform position and the
                // x offset is measured from the cell centre: slot 0 of 2 sits at -0.25, slot 1 at
                // +0.25, and the pair covers exactly the cell. n == 1 reduces to offset 0 and the
                // full-cell quad v1 drew, so an unshared cell renders bit-identically to before.
                quad.rectTransform.sizeDelta = new Vector2(1f / n, 1f);
                Vector3 pos = Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2);
                pos.x += (slot + 0.5f) / n - 0.5f;
                quad.transform.SetPosition(pos);

                Color c = CoiPalette.For(e.Cls, e.Phase);
                // Not hoisted above the loop: the ternary reads one property either way, so
                // there is nothing to hoist.
                c.a = e.Deterministic ? CoiConfig.Active.AlphaSolid : CoiConfig.Active.AlphaCandidate;
                quad.color = c;
                quad.gameObject.SetActive(true);
            }
            for (int i = drawCells.Count; i < pool.Count; i++)
                pool[i].gameObject.SetActive(false);

            // Only call SetRows when the set actually changed, so a per-cell-change hook doesn't
            // become a per-frame panel rebuild (spec §6). Compared against the legend's own
            // published mask rather than a copy kept here: the panel is shared static state, so a
            // controller recording what it asked for could sit forever on rows it never got.
            if (presentMask != CoiLegend.CurrentMask)
                CoiLegend.SetRows(presentMask);
        }

        // Has this exact class/phase already been kept for this cell? Linear scan over what is
        // usually two or three entries, so it beats any set that would have to be allocated.
        private bool AlreadyDrawn(int cell, CoiEntry e)
        {
            for (int i = 0; i < drawCells.Count; i++)
                if (drawCells[i] == cell && drawEntries[i].Cls == e.Cls && drawEntries[i].Phase == e.Phase)
                    return true;
            return false;
        }

        // Which stripe of how many, for the entry at drawEntries[i]. Ordering is by CoiClass
        // ordinal first (work, then output, then heat), emission order second, which makes it a
        // stable sort computed in place. Ordering on the ordinal rather than trusting the order
        // CoiResolver.Build happened to append in means a shared cell reads the same way on every
        // building, and a class added later slots in by its ordinal without a second edit here.
        // No sort call and no comparer: with n this small the scan is cheaper than the delegate.
        private void StripeOf(int i, int cell, CoiEntry e, out int slot, out int n)
        {
            slot = 0;
            n = 0;
            int rank = (int)e.Cls;
            for (int j = 0; j < drawCells.Count; j++)
            {
                if (drawCells[j] != cell)
                    continue;
                n++;
                if (j == i)
                    continue;
                int other = (int)drawEntries[j].Cls;
                if (other < rank || (other == rank && j < i))
                    slot++;
            }
        }

        private Image GetQuad(int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("CoiTint");
                go.transform.SetParent(GameScreenManager.Instance.worldSpaceCanvas.transform, worldPositionStays: false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false; // must never block build clicks
                // sizeDelta is deliberately NOT set here: Redraw sets it per draw because a pooled
                // quad's width depends on how many entries share its cell this time round, and a
                // quad reused from a 3-way split would otherwise stay a third of a cell wide.
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
