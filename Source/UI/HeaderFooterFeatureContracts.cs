using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// The immutable geometry inputs shared by the header host and one optional
    /// footer feature. The feature owns its own availability and presentation
    /// policy; the host owns the row anchor and the always-available ruleset
    /// selector.
    /// </summary>
    internal readonly struct HeaderFooterLayoutContext
    {
        internal HeaderFooterLayoutContext(
            float leftEdge,
            float rightEdge,
            float y,
            float height,
            bool allowRuleset,
            float rulesetWidth)
        {
            LeftEdge = leftEdge;
            RightEdge = rightEdge;
            Y = y;
            Height = height;
            AllowRuleset = allowRuleset;
            RulesetWidth = rulesetWidth;
        }

        internal float LeftEdge { get; }
        internal float RightEdge { get; }
        internal float Y { get; }
        internal float Height { get; }
        internal bool AllowRuleset { get; }
        internal float RulesetWidth { get; }
    }

    /// <summary>
    /// Optional header/footer feature boundary. It deliberately contains no
    /// feature-owned types so the header host remains usable when no optional
    /// feature is registered.
    /// </summary>
    internal interface IHeaderFooterFeature
    {
        bool IsPopoverOpen { get; }

        bool TryLayout(
            HeaderFooterLayoutContext context,
            ref HeaderButtons.BottomButtonRects rects);

        void Draw(HeaderButtons.BottomButtonRects rects);

        bool TryHandleInput(
            Rect inRect,
            Rect gearRect,
            Event evt,
            HeaderButtons.BottomButtonRects rects);

        void DrawPopoverOnTop(
            Rect inRect,
            Rect gearRect,
            HeaderButtons.BottomButtonRects rects);

        void Reset();
    }

    /// <summary>Composition point for the one optional footer feature.</summary>
    internal static class HeaderFooterFeatureRegistry
    {
        private static IHeaderFooterFeature _current;

        internal static IHeaderFooterFeature Current => _current;

        internal static void Register(IHeaderFooterFeature feature)
        {
            _current = feature;
        }
    }
}
