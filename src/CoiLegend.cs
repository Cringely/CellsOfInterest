using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CellsOfInterest
{
    // Screen-space legend explaining CoiTintController's swatch colors. Visible exactly while a
    // build-tool preview has live tints: CoiTintController.Start/OnDestroy call Show/Hide 1:1.
    // OnActivateTool double-fires (BuildToolPatch.cs) and re-selecting a building destroys the OLD
    // preview's controller AFTER the NEW one's Start already ran (BuildTool.cs), so Show/Hide pairs
    // can interleave — this is refcounted rather than a bool so no ordering assumption is needed.
    //
    // Content (SetRows) crosses that same interleave but is last-writer-wins rather than
    // refcounted; `currentMask` below is why a stale write cannot stick.
    public static class CoiLegend
    {
        private const float PanelWidth = 250f;
        private const float RowHeight = 20f;
        private const float SwatchSize = 16f;
        private const float Padding = 8f;
        private const float LabelGap = 6f;

        // Fallback anchor (bottom-right of ssOverlayCanvas) used whenever OverlayLegend isn't
        // present/active — e.g. the selected building's ViewMode is None, so the game never
        // shows its own overlay box.
        private const float FallbackOffsetX = -12f;
        private const float FallbackOffsetY = 220f;

        // Gap between our panel's right edge and OverlayLegend's left edge when docked beside it.
        // No standard inter-panel spacing constant was evident in the OverlayLegend/OverlayScreen
        // decompile, so this keeps the value this feature shipped with.
        private const float OverlayGapPx = 8f;

        // Positioner throttle: OverlayLegend's active rect can appear/move a frame after our
        // Show(), and again whenever the player switches buildings, so recheck a few times a
        // second rather than once at Show() only.
        private const float RepositionIntervalSeconds = 0.25f;

        // A row names the class it explains rather than carrying a literal color, so its swatch
        // resolves through the same CoiPalette.For call the tint quads use and cannot describe a
        // color nothing on screen is drawn in.
        private static readonly (CoiClass cls, CoiPhase phase, string label)[] Rows =
        {
            (CoiClass.Work,   CoiPhase.None,   "Dupe works here"),
            (CoiClass.Output, CoiPhase.Liquid, "Liquid output"),
            (CoiClass.Output, CoiPhase.Gas,    "Gas output"),
            (CoiClass.Output, CoiPhase.Solid,  "Solid / item drop"),
            (CoiClass.Heat,   CoiPhase.None,   "Heat exchange"),
        };

        // One persistent row container per Rows[] index (holds that row's swatch + label as
        // children). Built once alongside `panel`, never destroyed/recreated: SetRows toggles
        // SetActive and repositions the survivors into contiguous slots, which is allocation-free
        // on the per-cell-change hot path (spec §6 "rebuild when the present-set changes" describes
        // the visible result, not the mechanism). Held as RectTransform rather than GameObject so
        // the reflow writes anchoredPosition straight through instead of a GetComponent per row.
        private static readonly RectTransform[] rows = new RectTransform[Rows.Length];

        // Built once per colony and never recolored. The only in-game writer of config.json is
        // PLib's options dialog, which hangs off ModsScreen, and MainMenu.Mods() is that screen's
        // only instantiation site in Assembly-CSharp; reaching it tears down the game scene and
        // GameScreenManager's canvas, so Show finds panel == null and rebuilds the swatches from
        // the new palette. An edit made outside the game mid-colony is stale until the next load.
        private static GameObject panel;
        private static int refs;

        // The row mask currently on screen. Kept here rather than in the controller because it
        // describes this shared static panel, not one preview's belief about it: a superseded
        // controller that published over a live one would be corrected by the live one's next
        // Redraw (at most RefreshSeconds away), where a per-controller copy latches the wrong rows
        // for the rest of the preview. -1 is "unknown" — no real mask is negative — so the next
        // SetRows always lands. Show() resets it, because Show re-activates the panel without
        // touching rows: re-shown on a stale zero-row mask the panel would sit there as an empty
        // box, its owner's matching zero mask reading as "no change". Rejected: a generation token
        // that rejects the superseded writer outright — four members and a parameter to harden a
        // write BuildTool.OnActivateTool already rules out, since it Destroys the old visualizer
        // (BuildTool.cs:54-57) before instantiating the new one (:62), so the old controller is
        // Destroy-marked before the new controller it would clobber exists.
        private static int currentMask = -1;
        public static int CurrentMask => currentMask;

        public static void Show()
        {
            if (panel == null)
            {
                CreatePanel();
                refs = 0; // fresh panel: a stale count from a dead canvas would never reach zero
            }
            if (panel == null)
                return; // no screen-space canvas yet (e.g. called before GameScreenManager exists)
            refs++;
            currentMask = -1; // rows still belong to the previous preview; force the next publish
            panel.SetActive(true);
            Reposition(); // don't wait for the next throttled recheck to land in the right spot
        }

        // Rebuilds visible legend rows to match `mask` (one bit per Rows[] index, set exactly
        // where CoiTintController.Redraw drew a quad for that row's class/phase — see RowIndexFor).
        // Content only: never touches refs, so it cannot become the refcount bug the spec calls
        // out (Show/Hide already own visibility lifetime; this owns what the panel currently says).
        public static void SetRows(int mask)
        {
            if (panel == null)
                return; // no panel yet: leave currentMask alone so the caller retries next Redraw
            currentMask = mask;

            int slot = 0;
            for (int i = 0; i < Rows.Length; i++)
            {
                bool visible = (mask & (1 << i)) != 0;
                rows[i].gameObject.SetActive(visible);
                if (!visible)
                    continue;
                rows[i].anchoredPosition = new Vector2(Padding, -(Padding + slot * RowHeight));
                slot++;
            }

            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(PanelWidth, slot * RowHeight + Padding * 2f);
            // Reposition centers the panel on OverlayLegend from rt.rect.height, which the line
            // above just changed, and the panel's pivot is (1, 0) — so skipping it pins the bottom
            // edge and leaves the box off-center by half the delta until the Positioner's next tick
            // a quarter second later. Not a case for that throttle: the Positioner polls because
            // OverlayLegend can move without telling us, whereas this height change is ours, and
            // Redraw has already dropped every cell change that did not alter the mask.
            Reposition();

            // Decision: panel visible iff refs > 0 AND at least one row is visible (zero rows over
            // solid ground must stay hidden even while refs > 0). refs > 0 nearly always holds here
            // — Redraw only reaches this call from LateUpdate, which only runs after Start called
            // Show() for THIS controller — but Hide() may have already zeroed refs from a different
            // controller sharing this static panel, so check it explicitly rather than assume.
            panel.SetActive(refs > 0 && slot > 0);
        }

        // Maps an entry's (class, phase) to its Rows[] index, matching CoiPalette.For's fold
        // exactly: Output is the only class that varies by phase, so Work and Heat fold to None
        // (the phase their rows carry) whatever they were built with, and every output phase except
        // Liquid/Gas is drawn in the Solid color and shares the Solid row. Testing `!= Output`
        // rather than listing the phaseless classes means a class added later folds correctly
        // without an edit here; a class that DOES vary by phase would need one.
        // Scans Rows[] instead of hardcoding indices so Rows[] stays the only place row identity
        // and order are declared (reordering or renaming a row cannot silently desync this lookup
        // from the array a maintainer is actually looking at). Returns -1 for a class with no row
        // at all — callers must ignore that bit rather than shift by it, since C# masks the shift
        // count and 1 << -1 sets bit 31.
        public static int RowIndexFor(CoiClass cls, CoiPhase phase)
        {
            CoiPhase canonical = cls != CoiClass.Output ? CoiPhase.None
                : phase == CoiPhase.Liquid || phase == CoiPhase.Gas ? phase : CoiPhase.Solid;
            for (int i = 0; i < Rows.Length; i++)
                if (Rows[i].cls == cls && Rows[i].phase == canonical)
                    return i;
            return -1;
        }

        public static void Hide()
        {
            if (panel == null)
            {
                refs = 0;
                return;
            }
            refs = refs > 0 ? refs - 1 : 0;
            if (refs == 0)
                panel.SetActive(false);
        }

        private static void CreatePanel()
        {
            // ssOverlayCanvas is a GameObject (its Canvas is a component on it), per every call
            // site in the decompile (e.g. DropDown.cs, DebugHandler.cs: `...ssOverlayCanvas.GetComponent<...>()`).
            GameObject canvasGo = GameScreenManager.Instance != null ? GameScreenManager.Instance.ssOverlayCanvas : null;
            if (canvasGo == null)
                return;

            TMP_FontAsset font = FindFont();

            panel = new GameObject("CoiLegendPanel");
            panel.transform.SetParent(canvasGo.transform, worldPositionStays: false);

            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);
            bg.raycastTarget = false;

            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(FallbackOffsetX, FallbackOffsetY);
            // Zero-rows height: every row starts inactive below, and SetRows resizes this the
            // moment the owning controller publishes its first mask (same frame, before render —
            // see CoiTintController.Start), so this value never actually shows.
            rt.sizeDelta = new Vector2(PanelWidth, Padding * 2f);

            for (int i = 0; i < Rows.Length; i++)
            {
                // Row container: SetRows moves this (and only this) to reflow visible rows into
                // contiguous slots, so the swatch/label children below are positioned relative to
                // it at (0,0) rather than computing a slot position at creation time. Deliberately
                // unsized: the (0,1) pivot parks the row's rect origin at (0,0) whatever its
                // extents, so the children resolve against it either way, and nothing else reads
                // that rect — the row carries no Graphic and no layout component.
                var rowGo = new GameObject("Row" + i);
                rowGo.transform.SetParent(panel.transform, worldPositionStays: false);
                var rowRt = rowGo.AddComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(0f, 1f);
                rowRt.pivot = new Vector2(0f, 1f);
                rows[i] = rowRt;

                var swatchGo = new GameObject("Swatch");
                swatchGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
                var swatchImg = swatchGo.AddComponent<Image>();
                swatchImg.color = CoiPalette.For(Rows[i].cls, Rows[i].phase);
                swatchImg.raycastTarget = false;
                var swatchRt = swatchGo.GetComponent<RectTransform>();
                swatchRt.anchorMin = new Vector2(0f, 1f);
                swatchRt.anchorMax = new Vector2(0f, 1f);
                swatchRt.pivot = new Vector2(0f, 1f);
                swatchRt.anchoredPosition = new Vector2(0f, -(RowHeight - SwatchSize) * 0.5f);
                swatchRt.sizeDelta = new Vector2(SwatchSize, SwatchSize);

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
                var text = labelGo.AddComponent<TextMeshProUGUI>();
                text.text = Rows[i].label;
                text.fontSize = 14f;
                text.color = Color.white;
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                if (font != null)
                    text.font = font;
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = new Vector2(0f, 1f);
                labelRt.anchorMax = new Vector2(0f, 1f);
                labelRt.pivot = new Vector2(0f, 1f);
                labelRt.anchoredPosition = new Vector2(SwatchSize + LabelGap, 0f);
                labelRt.sizeDelta = new Vector2(PanelWidth - Padding * 2f - SwatchSize - LabelGap, RowHeight);

                rowGo.SetActive(false); // no mask published yet; SetRows decides visibility/slot
            }

            panel.AddComponent<Positioner>();

            panel.SetActive(false);
        }

        // Repositions our panel flush to the left of the game's OverlayLegend box (the "Plumbing
        // Overlay" / "Power Overlay" panel shown while a ViewMode overlay is active), vertically
        // centered on it. Falls back to the fixed bottom-right anchor when OverlayLegend isn't
        // present/active (e.g. the current building's ViewMode is None, so the game never shows it).
        private static void Reposition()
        {
            if (panel == null)
                return;

            var rt = panel.GetComponent<RectTransform>();
            var parentRt = panel.transform.parent as RectTransform;
            if (parentRt == null)
                return;

            RectTransform overlayRt = ResolveOverlayLegendRect();
            if (overlayRt == null)
            {
                rt.anchoredPosition = new Vector2(FallbackOffsetX, FallbackOffsetY);
                return;
            }

            // anchorMin == anchorMax == (1, 0) (bottom-right pivot), so anchoredPosition is measured
            // from the parent rect's (xMax, yMin) corner - reproduce that reference point here so we
            // can go straight from "desired pivot position" to anchoredPosition without touching anchors.
            Rect parentRect = parentRt.rect;
            Vector2 referencePoint = new Vector2(parentRect.xMax, parentRect.yMin);

            var corners = new Vector3[4]; // GetWorldCorners order: bottom-left, top-left, top-right, bottom-right
            overlayRt.GetWorldCorners(corners);

            Camera fromCam = CanvasCameraFor(overlayRt);
            Camera toCam = CanvasCameraFor(parentRt);

            Vector2 screenTopLeft = RectTransformUtility.WorldToScreenPoint(fromCam, corners[1]);
            Vector2 screenBottomLeft = RectTransformUtility.WorldToScreenPoint(fromCam, corners[0]);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screenTopLeft, toCam, out Vector2 localTopLeft);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screenBottomLeft, toCam, out Vector2 localBottomLeft);

            float overlayLeftX = localTopLeft.x;
            float overlayCenterY = (localTopLeft.y + localBottomLeft.y) * 0.5f;
            float panelHeight = rt.rect.height;

            Vector2 desiredPivot = new Vector2(overlayLeftX - OverlayGapPx, overlayCenterY - panelHeight * 0.5f);
            rt.anchoredPosition = desiredPivot - referencePoint;
        }

        // OverlayLegend.Instance is the game's own singleton (OverlayLegend.cs:53: `public static
        // OverlayLegend Instance;`, assigned in OnSpawn, cleared in OnLoadLevel) - reading it is a
        // static field access, not a scene scan, so there's nothing worth caching on top of it.
        // ClearLegend() -> Show(show:false) -> KScreen.Show(bool) -> `gameObject.SetActive(show)`
        // (confirmed in the real game assembly's decompile, not just the stripped modding stub),
        // so activeInHierarchy is exactly "no overlay box right now."
        private static RectTransform ResolveOverlayLegendRect()
        {
            OverlayLegend instance = OverlayLegend.Instance;
            if (instance == null || !instance.gameObject.activeInHierarchy)
                return null;
            return instance.transform as RectTransform;
        }

        private static Camera CanvasCameraFor(RectTransform rt)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        // A few-times-a-second recheck: OverlayLegend's active rect can appear a frame after our
        // Show(), and its position/visibility changes as the player switches between buildings while
        // the build tool stays active, so Show() alone isn't enough to stay docked correctly.
        private sealed class Positioner : MonoBehaviour
        {
            private float nextCheckTime;

            private void LateUpdate()
            {
                float now = Time.unscaledTime;
                if (now < nextCheckTime)
                    return;
                nextCheckTime = now + RepositionIntervalSeconds;
                Reposition();
            }
        }

        private static TMP_FontAsset FindFont()
        {
            TMP_FontAsset fallback = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (fallback == null)
                    fallback = f;
                if (f.name == "NotoSans-Regular")
                    return f;
            }
            return fallback;
        }
    }
}
