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

        public Color DimColor = new Color(0f, 0f, 0f, 0.48f);
        public Color CardColor = new Color(0.06f, 0.07f, 0.078f, 0.98f);
        public Color BorderColor = new Color(0.48f, 0.44f, 0.34f, 0.95f);
        public Color FocusColor = new Color(1f, 0.82f, 0.22f, 0.78f);
        public Color FocusFillColor = new Color(1f, 0.82f, 0.22f, 0.07f);
        public Color TitleColor = new Color(1f, 0.86f, 0.34f, 1f);
        public Color AccentColor = new Color(0.85f, 0.67f, 0.28f, 0.9f);
    }
}
