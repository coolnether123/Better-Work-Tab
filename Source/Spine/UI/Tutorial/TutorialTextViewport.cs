using System;
using UnityEngine;
using Verse;

namespace Spine.UI.Tutorial
{
    /// <summary>
    /// Draws tutorial body copy inside a hard viewport boundary and enables
    /// scrolling only when resolution, UI scale, or localization requires it.
    /// </summary>
    public sealed class TutorialTextViewport
    {
        private const float ScrollbarWidth = 16f;

        private Vector2 scrollPosition;
        private string contentKey = string.Empty;

        public void Reset()
        {
            scrollPosition = Vector2.zero;
            contentKey = string.Empty;
        }

        public bool TryHandleScroll(Rect viewport, string text, Event evt)
        {
            if (evt == null ||
                evt.type != EventType.ScrollWheel ||
                !viewport.Contains(evt.mousePosition))
            {
                return false;
            }

            EnsureContent(text);
            TextMetrics metrics = Measure(viewport, text);
            if (!metrics.RequiresScroll)
            {
                return false;
            }

            scrollPosition.y = TutorialOverflowScrollPolicy.ApplyWheel(
                scrollPosition.y,
                evt.delta.y,
                viewport.height,
                metrics.ContentHeight);
            evt.Use();
            return true;
        }

        public void Draw(Rect viewport, string text)
        {
            if (viewport.width <= 1f || viewport.height <= 1f)
            {
                return;
            }

            EnsureContent(text);
            TextMetrics metrics = Measure(viewport, text);
            scrollPosition.y = TutorialOverflowScrollPolicy.ClampOffset(
                scrollPosition.y,
                viewport.height,
                metrics.ContentHeight);

            GameFont previousFont = Text.Font;
            bool previousWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;

            if (metrics.RequiresScroll)
            {
                Rect view = new Rect(
                    0f,
                    0f,
                    metrics.ContentWidth,
                    Mathf.Max(viewport.height, metrics.ContentHeight));
                Widgets.BeginScrollView(viewport, ref scrollPosition, view);
                Widgets.Label(
                    new Rect(0f, 0f, metrics.ContentWidth, metrics.ContentHeight),
                    text ?? string.Empty);
                Widgets.EndScrollView();
            }
            else
            {
                GUI.BeginGroup(viewport);
                Widgets.Label(
                    new Rect(0f, 0f, metrics.ContentWidth, metrics.ContentHeight),
                    text ?? string.Empty);
                GUI.EndGroup();
            }

            Text.Font = previousFont;
            Text.WordWrap = previousWordWrap;
        }

        private void EnsureContent(string text)
        {
            string nextKey = text ?? string.Empty;
            if (string.Equals(contentKey, nextKey, StringComparison.Ordinal))
            {
                return;
            }

            contentKey = nextKey;
            scrollPosition = Vector2.zero;
        }

        private static TextMetrics Measure(Rect viewport, string text)
        {
            float width = Mathf.Max(1f, viewport.width);
            float height = MeasureHeight(text, width);
            bool requiresScroll = height > viewport.height + 0.5f;
            if (requiresScroll)
            {
                width = Mathf.Max(1f, viewport.width - ScrollbarWidth);
                height = MeasureHeight(text, width);
            }

            return new TextMetrics(width, Mathf.Max(1f, height), requiresScroll);
        }

        private static float MeasureHeight(string text, float width)
        {
            GameFont previousFont = Text.Font;
            bool previousWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float height = Mathf.Ceil(Text.CalcHeight(text ?? string.Empty, width));
            Text.Font = previousFont;
            Text.WordWrap = previousWordWrap;
            return height;
        }

        private readonly struct TextMetrics
        {
            internal TextMetrics(float contentWidth, float contentHeight, bool requiresScroll)
            {
                ContentWidth = contentWidth;
                ContentHeight = contentHeight;
                RequiresScroll = requiresScroll;
            }

            internal float ContentWidth { get; }
            internal float ContentHeight { get; }
            internal bool RequiresScroll { get; }
        }
    }
}
