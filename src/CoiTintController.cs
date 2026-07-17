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
                    if (!e.Deterministic && Grid.Solid[cell])
                        continue; // candidate stand cell currently blocked: drop

                    var quad = GetQuad(used++);
                    quad.transform.SetPosition(Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2));
                    Color c = CoiPalette.For(e.Cls, e.Phase);
                    c.a = e.Deterministic ? CoiPalette.AlphaSolid : CoiPalette.AlphaCandidate;
                    quad.color = c;
                    quad.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < pool.Count; i++)
                pool[i].gameObject.SetActive(false);
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
