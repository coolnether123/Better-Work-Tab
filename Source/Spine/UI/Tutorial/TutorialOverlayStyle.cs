using UnityEngine;

namespace Spine.UI.Tutorial
{
    public sealed class TutorialOverlayStyle
    {
        public float CardWidth = 440f;
        public float CardPadding = 14f;
        public float CardGap = 18f;
        public float WindowMargin = 12f;
        public float LayoutAnimationSeconds = 0.22f;

        public Color DimColor = new Color(0f, 0f, 0f, 0.56f);
        public Color CardColor = new Color(0.075f, 0.085f, 0.095f, 0.98f);
        public Color BorderColor = new Color(0.82f, 0.78f, 0.66f, 1f);
        public Color FocusColor = new Color(1f, 0.82f, 0.22f, 0.92f);
        public Color FocusFillColor = new Color(1f, 0.82f, 0.22f, 0.10f);
        public Color TitleColor = new Color(1f, 0.88f, 0.42f, 1f);
    }
}
