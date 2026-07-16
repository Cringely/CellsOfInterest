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
    public static class CoiLegend
    {
        private const float PanelWidth = 210f;
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

        private static readonly (Color color, string label)[] Rows =
        {
            (new Color(0.20f, 0.85f, 0.25f, 1f), "Dupe works here"),
            (new Color(0.25f, 0.55f, 0.95f, 1f), "Delivery / fetch approach"),
            (new Color(0.95f, 0.60f, 0.15f, 1f), "Output drops here"),
            (new Color(0.5f, 0.5f, 0.5f, 0.5f), "Faint = one of several possible cells"),
        };

        private static GameObject panel;
        private static int refs;

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
            panel.SetActive(true);
            Reposition(); // don't wait for the next throttled recheck to land in the right spot
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
            rt.sizeDelta = new Vector2(PanelWidth, Rows.Length * RowHeight + Padding * 2f);

            for (int i = 0; i < Rows.Length; i++)
            {
                float rowTop = -(Padding + i * RowHeight);

                var swatchGo = new GameObject("Swatch");
                swatchGo.transform.SetParent(panel.transform, worldPositionStays: false);
                var swatchImg = swatchGo.AddComponent<Image>();
                swatchImg.color = Rows[i].color;
                swatchImg.raycastTarget = false;
                var swatchRt = swatchGo.GetComponent<RectTransform>();
                swatchRt.anchorMin = new Vector2(0f, 1f);
                swatchRt.anchorMax = new Vector2(0f, 1f);
                swatchRt.pivot = new Vector2(0f, 1f);
                swatchRt.anchoredPosition = new Vector2(Padding, rowTop - (RowHeight - SwatchSize) * 0.5f);
                swatchRt.sizeDelta = new Vector2(SwatchSize, SwatchSize);

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(panel.transform, worldPositionStays: false);
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
                labelRt.anchoredPosition = new Vector2(Padding + SwatchSize + LabelGap, rowTop);
                labelRt.sizeDelta = new Vector2(PanelWidth - Padding * 2f - SwatchSize - LabelGap, RowHeight);
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
