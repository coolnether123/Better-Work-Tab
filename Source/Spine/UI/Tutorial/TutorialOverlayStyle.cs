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
        public Color CardColor = new Color(0.52f, 0.33f, 0.17f, 1f);
        public Color BorderColor = new Color(0.69f, 0.55f, 0.24f, 1f);
        public Color FocusColor = new Color(1f, 1f, 1f, 0.82f);
        public Color FocusFillColor = new Color(1f, 1f, 1f, 0.05f);
        public Color TitleColor = Color.white;
        public Color AccentColor = new Color(0.69f, 0.55f, 0.24f, 1f);
    }
}
