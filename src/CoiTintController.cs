using System.Collections.Generic;
using UnityEngine;

namespace CellsOfInterest
{
    // Attached to BuildTool's preview visualizer by BuildToolPatch. Lifetime = the preview's:
    // the game Destroys the visualizer on tool deactivate and on building re-selection
    // (BuildTool.cs:54-57, 96-107), which destroys this controller and its child quads.
    public sealed class CoiTintController : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f; // cadence re-check of Grid.Solid (spec: staleness receipt)
        private const float AlphaSolid = 0.55f;
        private const float AlphaCandidate = 0.25f;
        private static readonly Color WorkColor = new Color(0.20f, 0.85f, 0.25f);
        private static readonly Color DeliveryColor = new Color(0.25f, 0.55f, 0.95f);
        private static readonly Color OutputColor = new Color(0.95f, 0.60f, 0.15f);

        private static Sprite whiteSprite;

        private CoiData data = CoiData.Empty;
        private Rotatable rotatable;
        private readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        private int lastCell = -1;
        private Orientation lastOrientation = (Orientation)(-1);
        private float nextRefresh;

        private void Start()
        {
            var building = GetComponent<Building>();
            if (building != null && building.Def != null)
                data = CoiResolver.Get(building.Def);
            rotatable = GetComponent<Rotatable>();
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
                    quad.transform.position = Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2);
                    Color c = e.Cls == CoiClass.Work ? WorkColor
                            : e.Cls == CoiClass.Delivery ? DeliveryColor
                            : OutputColor;
                    c.a = e.Deterministic ? AlphaSolid : AlphaCandidate;
                    quad.color = c;
                    quad.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < pool.Count; i++)
                pool[i].gameObject.SetActive(false);
        }

        private SpriteRenderer GetQuad(int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("CoiTint");
                // Parent for lifetime only; positions are set in world space every redraw.
                go.transform.SetParent(transform, worldPositionStays: false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = WhiteSprite();
                pool.Add(sr);
            }
            return pool[index];
        }

        private static Sprite WhiteSprite()
        {
            if (whiteSprite == null)
            {
                var tex = Texture2D.whiteTexture; // 4x4 white
                // pixelsPerUnit = texture width -> sprite is exactly 1x1 world units = one cell.
                whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), tex.width);
            }
            return whiteSprite;
        }
    }
}
