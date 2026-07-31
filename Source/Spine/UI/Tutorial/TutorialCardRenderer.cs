using UnityEngine;
using Verse;

namespace Spine.UI.Tutorial
{
    public static class TutorialCardRenderer
    {
        public static void Draw(Rect rect, TutorialOverlayStyle style)
        {
            Widgets.DrawShadowAround(rect);
            Widgets.DrawBoxSolid(rect, style.CardColor);

            Color previousColor = GUI.color;
            GUI.color = style.BorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;
        }
    }
}
