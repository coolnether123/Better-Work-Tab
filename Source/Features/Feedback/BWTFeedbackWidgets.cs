using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Feedback
{
    /// <summary>
    /// The flat, panelled look the beta portal is drawn in.
    ///
    /// The portal used to be a stack of individually bordered cards, which made
    /// every answer look like its own dialog and gave a page of eleven questions
    /// eleven competing frames. This is the opposite arrangement: one framed
    /// section holds the content, rows inside it are separated by hairlines
    /// rather than boxes, and emphasis is spent on the few things that are
    /// actually interactive.
    /// </summary>
    internal static class BWTFeedbackWidgets
    {
        internal const float RowHeight = 34f;
        internal const float TallRowHeight = 46f;
        internal const float SectionHeadingHeight = 26f;
        internal const float BadgeSize = 22f;

        internal static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        internal static readonly Color Fainter = new Color(1f, 1f, 1f, 0.38f);
        internal static readonly Color Hairline = new Color(1f, 1f, 1f, 0.09f);
        internal static readonly Color Accent = new Color(0.85f, 0.72f, 0.42f);

        private static readonly Color PillOff = new Color(0.14f, 0.145f, 0.155f, 1f);
        private static readonly Color PillOffBorder = new Color(1f, 1f, 1f, 0.13f);
        private static readonly Color PillOn = new Color(0.42f, 0.34f, 0.19f, 1f);
        private static readonly Color PillOnBorder = new Color(0.85f, 0.72f, 0.42f, 0.75f);
        private static readonly Color BadgeFill = new Color(0.18f, 0.19f, 0.20f, 1f);

        /// <summary>The title band: a mark, a name, a line of context, one primary action.</summary>
        internal static bool DrawHeaderBand(Rect rect, string title, string subtitle, string action, Texture2D mark)
        {
            Rect markRect = new Rect(rect.x, rect.y + 2f, 34f, 34f);
            if (mark != null)
            {
                GUI.DrawTexture(markRect, mark);
            }

            float textX = markRect.xMax + 10f;
            float actionWidth = action.NullOrEmpty() ? 0f : 190f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(textX, rect.y - 2f, rect.width - textX - actionWidth - 12f, 32f), title);
            Text.Font = GameFont.Tiny;
            Color previous = GUI.color;
            GUI.color = Dim;
            Widgets.Label(new Rect(textX, rect.y + 26f, rect.width - textX - actionWidth - 12f, 22f), subtitle);
            GUI.color = previous;
            Text.Font = GameFont.Small;

            bool clicked = false;
            if (!action.NullOrEmpty())
            {
                clicked = Widgets.ButtonText(
                    new Rect(rect.xMax - actionWidth, rect.y + 2f, actionWidth, 32f),
                    action);
            }

            HairlineUnder(rect);
            return clicked;
        }

        /// <summary>A dim caption with a rule under it, used to break a list into groups.</summary>
        internal static void SectionHeading(Rect rect, string text, string trailing = null)
        {
            Color previous = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = Dim;
            Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width * 0.7f, 20f), text);
            if (!trailing.NullOrEmpty())
            {
                TextAnchor anchor = Text.Anchor;
                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = Fainter;
                Widgets.Label(new Rect(rect.x + (rect.width * 0.7f), rect.y + 4f, rect.width * 0.3f, 20f), trailing);
                Text.Anchor = anchor;
            }

            Text.Font = GameFont.Small;
            GUI.color = previous;
            HairlineUnder(rect);
        }

        internal static void HairlineUnder(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = Hairline;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            GUI.color = previous;
        }

        /// <summary>
        /// A label/value line. The value sits right-aligned and dim so a column of
        /// them reads as one block of facts rather than a column of sentences.
        /// </summary>
        internal static void FactRow(Rect rect, string label, string value, string tooltip = null)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            Color previous = GUI.color;
            Rect text = rect.ContractedBy(0f);
            text.xMin += 6f;
            text.xMax -= 6f;

            Widgets.Label(new Rect(text.x, text.y + 7f, text.width * 0.42f, 24f), label);

            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = Dim;
            Rect valueRect = new Rect(text.x + (text.width * 0.42f), text.y + 7f, text.width * 0.58f, 24f);
            Widgets.Label(valueRect, value.Truncate(valueRect.width));
            Text.Anchor = anchor;
            GUI.color = previous;

            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            HairlineUnder(rect);
        }

        /// <summary>A list line: small badge, name, dim second line, dim right-hand note.</summary>
        internal static void ListRow(Rect rect, string badge, string label, string sub, string trailing)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            Color previous = GUI.color;

            Rect badgeRect = new Rect(rect.x + 6f, rect.y + ((rect.height - BadgeSize) * 0.5f), BadgeSize, BadgeSize);
            if (!badge.NullOrEmpty())
            {
                Widgets.DrawBoxSolid(badgeRect, BadgeFill);
                TextAnchor anchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                GUI.color = Dim;
                Widgets.Label(badgeRect, badge);
                Text.Font = GameFont.Small;
                Text.Anchor = anchor;
                GUI.color = previous;
            }

            float textX = badge.NullOrEmpty() ? rect.x + 6f : badgeRect.xMax + 8f;
            float trailingWidth = trailing.NullOrEmpty() ? 0f : rect.width * 0.35f;
            float labelWidth = rect.xMax - textX - trailingWidth - 8f;
            bool twoLine = !sub.NullOrEmpty();
            Widgets.Label(new Rect(textX, rect.y + (twoLine ? 3f : 7f), labelWidth, 24f), label.Truncate(labelWidth));
            if (twoLine)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Dim;
                Widgets.Label(new Rect(textX, rect.y + 22f, labelWidth, 20f), sub.Truncate(labelWidth));
                Text.Font = GameFont.Small;
                GUI.color = previous;
            }

            if (!trailing.NullOrEmpty())
            {
                TextAnchor anchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Dim;
                Widgets.Label(new Rect(rect.xMax - trailingWidth - 6f, rect.y, trailingWidth, rect.height), trailing);
                Text.Anchor = anchor;
                GUI.color = previous;
            }

            HairlineUnder(rect);
        }

        /// <summary>
        /// A row of mutually exclusive choices drawn flat. Clicking the choice
        /// already selected clears it, so a mis-click is not a permanent claim;
        /// <paramref name="selected"/> of -1 means nothing is chosen yet.
        /// </summary>
        internal static int DrawSegmented(Rect rect, string[] labels, int selected, float gap = 4f)
        {
            int result = selected;
            float width = (rect.width - (gap * (labels.Length - 1))) / labels.Length;
            TextAnchor anchor = Text.Anchor;
            Color previous = GUI.color;
            bool previousWrap = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;

            // A pill is one line tall. Left to wrap, a long label takes two lines
            // and the box clips the second one halfway through, which is how
            // "It doesn't need a lesson" came out unreadable. Wrapping off means
            // a long label is shortened on purpose, with the whole of it in the
            // tooltip.
            Text.WordWrap = false;

            for (int i = 0; i < labels.Length; i++)
            {
                Rect pill = new Rect(rect.x + (i * (width + gap)), rect.y, width, rect.height);
                bool on = selected == i;
                bool hovered = Mouse.IsOver(pill);
                Widgets.DrawBoxSolid(pill, on ? PillOn : PillOff);
                GUI.color = on ? PillOnBorder : PillOffBorder;
                Widgets.DrawBox(pill, 1);
                GUI.color = on || hovered ? Color.white : Dim;

                string shown = labels[i].Truncate(width - 8f);
                Widgets.Label(pill, shown);
                GUI.color = previous;

                if (!string.Equals(shown, labels[i], StringComparison.Ordinal))
                {
                    TooltipHandler.TipRegion(pill, labels[i]);
                }

                if (Widgets.ButtonInvisible(pill))
                {
                    result = on ? -1 : i;
                }
            }

            Text.WordWrap = previousWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = anchor;
            GUI.color = previous;
            return result;
        }

        /// <summary>A text field with placeholder text shown while it is empty.</summary>
        internal static string TextFieldWithPlaceholder(Rect rect, string value, string placeholder, bool area = false)
        {
            string result = area ? Widgets.TextArea(rect, value ?? string.Empty) : Widgets.TextField(rect, value ?? string.Empty);
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            Color previous = GUI.color;
            GUI.color = Fainter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 6f), placeholder);
            Text.Font = GameFont.Small;
            GUI.color = previous;
            return result;
        }

        /// <summary>A small right-aligned action, e.g. "Go to tutorial".</summary>
        internal static bool MiniButton(Rect rect, string label)
        {
            bool hovered = Mouse.IsOver(rect);
            Color previous = GUI.color;
            Widgets.DrawBoxSolid(rect, PillOff);
            GUI.color = hovered ? PillOnBorder : PillOffBorder;
            Widgets.DrawBox(rect, 1);
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = hovered ? Color.white : Dim;
            Widgets.Label(rect, label);
            Text.Font = GameFont.Small;
            Text.Anchor = anchor;
            GUI.color = previous;
            return Widgets.ButtonInvisible(rect);
        }

        internal static void Placeholder(Rect rect, string text)
        {
            Color previous = GUI.color;
            TextAnchor anchor = Text.Anchor;
            GUI.color = Fainter;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(rect, text);
            Text.Anchor = anchor;
            GUI.color = previous;
        }
    }
}
