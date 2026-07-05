using System.Collections.Generic;
using UnityEngine;

namespace Spine.UI.Tutorial
{
    public readonly struct TutorialOverlayLayout
    {
        public TutorialOverlayLayout(Rect cardRect, Rect focusBounds, List<Rect> focusRects)
        {
            CardRect = cardRect;
            FocusBounds = focusBounds;
            FocusRects = focusRects ?? new List<Rect>();
        }

        public Rect CardRect { get; }
        public Rect FocusBounds { get; }
        public List<Rect> FocusRects { get; }
    }
}
