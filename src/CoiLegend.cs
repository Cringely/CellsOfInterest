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
            rt.anchoredPosition = new Vector2(-12f, 220f);
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

            panel.SetActive(false);
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
