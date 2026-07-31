using UnityEngine;

namespace Spine.UI.Tutorial
{
    public sealed class TutorialOverlayStyle
    {
        public float CardWidth = 440f;
        public float CardPadding = 16f;
        public float CardGap = 18f;
        public float WindowMargin = 12f;
        public float LayoutAnimationSeconds = 0.28f;

        public Color DimColor = new Color(0f, 0f, 0f, 0.18f);
        public Color CardColor = new Color(0.105f, 0.11f, 0.12f, 0.98f);
        public Color BorderColor = new Color(0.26f, 0.27f, 0.28f, 1f);
        public Color FocusColor = new Color(1f, 1f, 1f, 0.82f);
        public Color FocusFillColor = new Color(1f, 1f, 1f, 0.05f);
        public Color TitleColor = Color.white;
    }
}
