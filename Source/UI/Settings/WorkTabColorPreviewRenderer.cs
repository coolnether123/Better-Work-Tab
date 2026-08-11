using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Draws only the live-preview label. Preview geometry belongs to the real Work-tab
    /// renderers and is never recreated here.
    /// </summary>
    internal static class WorkTabColorPreviewRenderer
    {
        internal static void Draw(Rect windowRect)
        {
            if (Event.current.type != EventType.Repaint ||
                !WorkTabColorPreviewController.Instance.TryGetPreview(out WorkTabColorPreview preview))
            {
                return;
            }

            const float width = 300f;
            Rect legend = new Rect(windowRect.xMax - width - 8f, windowRect.y + 8f, width, 30f);
            Widgets.DrawBoxSolid(legend, new Color(0.035f, 0.04f, 0.045f, 0.96f));

            Rect swatch = new Rect(legend.x + 7f, legend.y + 6f, 18f, 18f);
            Widgets.DrawBoxSolid(swatch, preview.Color);
            Widgets.DrawBox(swatch);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(swatch.xMax + 7f, legend.y, legend.width - 40f, legend.height),
                "Live preview: " + preview.Label);
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWordWrap;
        }
    }
}
