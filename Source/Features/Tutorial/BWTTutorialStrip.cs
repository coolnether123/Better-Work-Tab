using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGiverReassignments;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Features.Tutorial
{
    internal enum BWTTutorialStripMode
    {
        Hidden,
        Browse,
        Lesson,
        Complete
    }

    internal readonly struct BWTTutorialStripContent
    {
        internal BWTTutorialStripContent(
            BWTTutorialStripMode mode,
            string instruction,
            int completedCount,
            int totalCount,
            int disabledFeatureCount = 0)
        {
            Mode = mode;
            Instruction = instruction ?? string.Empty;
            CompletedCount = completedCount;
            TotalCount = totalCount;
            DisabledFeatureCount = disabledFeatureCount;
        }

        internal BWTTutorialStripMode Mode { get; }
        internal string Instruction { get; }
        internal int CompletedCount { get; }
        internal int TotalCount { get; }

        /// <summary>
        /// Features the player has switched off that the tour could otherwise
        /// have taught. Non-zero puts the discovery button on the band.
        /// </summary>
        internal int DisabledFeatureCount { get; }
        internal bool HasProgress => TotalCount > 0;
        internal bool OffersDiscovery => DisabledFeatureCount > 0;
    }

    internal readonly struct BWTTutorialStripLayout
    {
        internal BWTTutorialStripLayout(
            Rect stripRect,
            Rect progressRect,
            Rect instructionRect,
            Rect skipRect,
            Rect exitRect,
            Rect discoverRect = default(Rect))
        {
            StripRect = stripRect;
            ProgressRect = progressRect;
            InstructionRect = instructionRect;
            SkipRect = skipRect;
            ExitRect = exitRect;
            DiscoverRect = discoverRect;
        }

        internal Rect StripRect { get; }
        internal Rect ProgressRect { get; }
        internal Rect InstructionRect { get; }
        internal Rect SkipRect { get; }
        internal Rect ExitRect { get; }
        internal Rect DiscoverRect { get; }
        internal bool IsValid => StripRect.width > 1f && StripRect.height > 1f;
    }

    /// <summary>
    /// The tutorial's only instruction surface. It occupies a reserved band
    /// between the Work headers and the pawn rows, so it sits next to every
    /// anchor it points at and keeps the same position and shape whether the
    /// colony has ten pawns or a hundred.
    /// </summary>
    internal static class BWTTutorialStrip
    {
        // Small-font text needs about 22px of line box, so even a one-line band
        // has to clear that plus its padding. Longer translated instructions
        // grow the reserved band instead of being shortened with an ellipsis.
        private const float MinimumRowHeight = 34f;

        // Angled header labels hang their stems slightly past the header lane.
        // Starting the band flush with the lane bottom clipped those stems right
        // where they end, so the leader lines stopped short of their column. This
        // gap keeps them terminating in clear space, as they do with no band.
        private const float TopGap = 6f;
        private const float Padding = 5f;
        private const float ButtonGap = 6f;
        private const float ButtonHeight = 22f;
        private const float ProgressWidth = 44f;
        private const float MinimumInstructionWidth = 120f;
        private const float TableRightInset = 16f;

        // Matched to SubWorkDrilldownBarRenderer's global row so the Work tab's
        // two pinned bands are visibly the same material.
        private static readonly Color SubWorkBandFill = new Color(0.08f, 0.1f, 0.11f, 0.72f);

        // Enough tutor colour to identify the band as the tutorial's, far too
        // little to make it a tan slab again.
        private static readonly Color TutorTint = new Color(
            BWTUiPalette.TutorFill.r,
            BWTUiPalette.TutorFill.g,
            BWTUiPalette.TutorFill.b,
            0.28f);

        // The reserved height feeds Work-tab layout, so it must stay constant for
        // a whole frame. Visibility is therefore latched once per Work-tab pass
        // rather than re-derived from tutorial policy inside geometry code.
        private static bool reserved;
        private static float rowHeight = MinimumRowHeight;

        /// <summary>
        /// The clearance above the band.
        ///
        /// <see cref="TopGap"/> exists to give angled header stems somewhere to
        /// terminate below the header lane. In a sub-work drilldown the global
        /// priority row is already sitting in that space and drawing its own
        /// separator, so adding the gap on top of it stacked two dividers and
        /// left a band of dead pixels between the row and the band.
        /// </summary>
        private static float CurrentTopGap =>
            WorkGridLayoutMetrics.SubWorkPinnedHeight > 0.5f ? 0f : TopGap;

        internal static float ReservedHeight => reserved ? rowHeight + CurrentTopGap : 0f;

        internal static bool IsReserved => reserved;

        /// <summary>
        /// Latches whether the band exists this frame. Call once per Work-tab
        /// pass before any layout geometry is derived.
        /// </summary>
        internal static void RefreshReservation(
            bool visible,
            float tableWidth,
            BWTTutorialStripContent content)
        {
            reserved = visible;
            rowHeight = MinimumRowHeight;
            if (!visible)
            {
                return;
            }

            float stripWidth = Mathf.Max(tableWidth - TableRightInset, 1f);
            BWTTutorialStripLayout measurementLayout = BuildLayout(
                new Rect(0f, 0f, stripWidth, MinimumRowHeight),
                content);
            if (measurementLayout.InstructionRect.width <= 1f)
            {
                return;
            }

            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float instructionHeight = Text.CalcHeight(
                DisplayInstruction(content),
                measurementLayout.InstructionRect.width);
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            rowHeight = Mathf.Max(
                MinimumRowHeight,
                Mathf.Ceil(instructionHeight) + Padding * 2f);
        }

        /// <summary>
        /// The vertical lane the band occupies, including the stem gap above it.
        /// Column-wide chrome drawn before the band — hover highlights, target
        /// tints — can use this span when it needs to part around the band. Row
        /// overlays drawn afterwards may still paint over it like any divider.
        /// </summary>
        internal static bool TryGetBandSpan(
            IWorkTabLayoutController layout,
            out float top,
            out float bottom)
        {
            top = 0f;
            bottom = 0f;
            if (!reserved || layout == null)
            {
                return false;
            }

            top = WorkGridLayoutMetrics.GetSubWorkBandTop(layout) +
                WorkGridLayoutMetrics.SubWorkPinnedHeight;
            bottom = top + CurrentTopGap + rowHeight;
            return true;
        }

        internal static BWTTutorialStripLayout BuildLayout(
            IWorkTabLayoutController layout,
            BWTTutorialStripContent content)
        {
            if (!reserved || layout == null || content.Mode == BWTTutorialStripMode.Hidden)
            {
                return default(BWTTutorialStripLayout);
            }

            // Sit in the pinned band directly under the header lane, below any
            // schedule or sub-work band that already claimed space there.
            float top = WorkGridLayoutMetrics.GetSubWorkBandTop(layout) +
                WorkGridLayoutMetrics.SubWorkPinnedHeight +
                CurrentTopGap;
            float width = Mathf.Max(
                layout.Table != null ? layout.Table.Size.x - TableRightInset : 0f,
                1f);
            return BuildLayout(new Rect(layout.TableOrigin.x, top, width, rowHeight), content);
        }

        private static BWTTutorialStripLayout BuildLayout(
            Rect strip,
            BWTTutorialStripContent content)
        {
            Rect inner = strip.ContractedBy(Padding);
            if (inner.width <= 1f)
            {
                return new BWTTutorialStripLayout(strip, Rect.zero, inner, Rect.zero, Rect.zero);
            }

            float buttonTop = inner.y + (inner.height - ButtonHeight) * 0.5f;
            Rect exit = Rect.zero;
            Rect skip = Rect.zero;
            float rightEdge = inner.xMax;

            if (content.Mode != BWTTutorialStripMode.Complete)
            {
                float exitWidth = MeasureButtonWidth(ExitLabel(content.Mode));
                exit = new Rect(rightEdge - exitWidth, buttonTop, exitWidth, ButtonHeight);
                rightEdge = exit.xMin - ButtonGap;
            }

            float skipWidth = MeasureButtonWidth(SkipLabel(content.Mode));
            skip = new Rect(rightEdge - skipWidth, buttonTop, skipWidth, ButtonHeight);
            rightEdge = skip.xMin - ButtonGap;

            // Only while browsing. During a lesson the player is being asked to
            // do one thing, and a second call to action next to it competes
            // with the instruction the band exists to carry.
            Rect discover = Rect.zero;
            if (content.OffersDiscovery && content.Mode == BWTTutorialStripMode.Browse)
            {
                float discoverWidth = MeasureButtonWidth(DiscoverLabel(content));
                discover = new Rect(rightEdge - discoverWidth, buttonTop, discoverWidth, ButtonHeight);
                rightEdge = discover.xMin - ButtonGap;
            }

            Rect progress = Rect.zero;
            float instructionLeft = inner.x;
            if (content.HasProgress && inner.width > ProgressWidth + MinimumInstructionWidth)
            {
                progress = new Rect(inner.x, inner.y, ProgressWidth, inner.height);
                instructionLeft = progress.xMax + Padding;
            }

            Rect instruction = new Rect(
                instructionLeft,
                inner.y,
                Mathf.Max(1f, rightEdge - instructionLeft),
                inner.height);

            // A cramped tab loses the buttons before it loses the instruction; the
            // strip is worthless without its text, and Escape still leaves.
            if (instruction.width < MinimumInstructionWidth)
            {
                skip = Rect.zero;
                exit = Rect.zero;
                discover = Rect.zero;
                instruction = new Rect(
                    instructionLeft,
                    inner.y,
                    Mathf.Max(1f, inner.xMax - instructionLeft),
                    inner.height);
            }

            return new BWTTutorialStripLayout(strip, progress, instruction, skip, exit, discover);
        }

        internal static void Draw(BWTTutorialStripLayout layout, BWTTutorialStripContent content)
        {
            if (!layout.IsValid)
            {
                return;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = true;
            Text.Font = GameFont.Small;

            DrawBandSurface(layout.StripRect);

            if (layout.ProgressRect.width > 1f)
            {
                DrawProgress(layout.ProgressRect, content);
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(layout.InstructionRect, DisplayInstruction(content));
            GUI.color = oldColor;

            // Flat buttons bordered in the tutor accent. RimWorld's ButtonText
            // atlas is a dark grey slab that fights the tan band behind it.
            if (layout.SkipRect.width > 1f)
            {
                DrawStripButton(layout.SkipRect, SkipLabel(content.Mode));
            }

            if (layout.ExitRect.width > 1f)
            {
                DrawStripButton(layout.ExitRect, ExitLabel(content.Mode));
            }

            if (layout.DiscoverRect.width > 1f)
            {
                DrawStripButton(layout.DiscoverRect, DiscoverLabel(content));

                // Naming every switched-off feature means walking the catalog
                // and translating each label, so it is built only for the frames
                // where somebody is actually pointing at the button.
                if (layout.DiscoverRect.Contains(Event.current?.mousePosition ?? new Vector2(-1f, -1f)))
                {
                    TooltipHandler.TipRegion(
                        layout.DiscoverRect,
                        BWTTutorialFeatureDiscovery.DescribeDisabled(
                            BetterWorkTabMod.Settings?.selectedTutorialCourse ?? BWTTutorialCourse.Full));
                }
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        /// <summary>
        /// Paints the band out of the same material as the sub-work global row,
        /// which is the Work tab's other pinned band.
        ///
        /// <see cref="Widgets.DrawWindowBackgroundTutor"/> was the obvious choice
        /// and the wrong one: it draws a RimWorld *window*, an opaque saturated
        /// slab with a hard border on all four sides. A window is a thing that
        /// floats above the tab, so painting one into a lane between the headers
        /// and the pawn rows read as pasted on rather than built in. The dark
        /// translucent wash below is what every other band in this tab is made
        /// of, so the band now sits in the table instead of on top of it, and the
        /// tutor palette survives where it carries meaning — as a tint and the
        /// separator — instead of as the whole surface.
        /// </summary>
        private static void DrawBandSurface(Rect rect)
        {
            Color oldColor = GUI.color;

            Widgets.DrawBoxSolid(rect, SubWorkBandFill);
            Widgets.DrawBoxSolid(rect, TutorTint);

            // Mirrors the sub-work band's separator placement so consecutive
            // bands rule off at the same inset and read as one system.
            GUI.color = new Color(
                BWTUiPalette.TutorAccent.r,
                BWTUiPalette.TutorAccent.g,
                BWTUiPalette.TutorAccent.b,
                0.55f);
            Widgets.DrawLineHorizontal(rect.xMin, rect.yMax - 3f, rect.width);

            // A single faint rule along the top seats the band under the header
            // lane. Anything heavier rebuilds the boxed-in look this replaced.
            GUI.color = new Color(1f, 1f, 1f, 0.10f);
            Widgets.DrawLineHorizontal(rect.xMin, rect.yMin, rect.width);

            GUI.color = oldColor;
        }

        private static void DrawProgress(Rect rect, BWTTutorialStripContent content)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(rect, content.CompletedCount + " / " + content.TotalCount);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
        }

        private static void DrawStripButton(Rect rect, string label)
        {
            bool hovered = rect.Contains(Event.current?.mousePosition ?? new Vector2(-1f, -1f));
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            Widgets.DrawBoxSolid(rect, hovered
                ? new Color(1f, 1f, 1f, 0.18f)
                : new Color(0f, 0f, 0f, 0.12f));
            GUI.color = hovered ? Color.white : BWTUiPalette.TutorAccent;
            Widgets.DrawBox(rect, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            GUI.color = hovered ? Color.white : new Color(1f, 1f, 1f, 0.9f);
            Widgets.Label(rect, label);
            MouseoverSounds.DoRegion(rect);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static string ExitLabel(BWTTutorialStripMode mode)
        {
            return mode == BWTTutorialStripMode.Lesson
                ? T("BWT_Tutorial_BackToMap")
                : T("BWT_Tutorial_SkipForNow");
        }

        /// <summary>
        /// The strip is the tutorial's only chrome now, so it carries whichever
        /// left-hand action the current mode needs.
        /// </summary>
        private static string SkipLabel(BWTTutorialStripMode mode)
        {
            switch (mode)
            {
                case BWTTutorialStripMode.Complete:
                    return T("BWT_Tutorial_Outcome_Continue");
                case BWTTutorialStripMode.Browse:
                    return T("BWT_Tutorial_GiveFeedback");
                default:
                    return T("BWT_Tutorial_SkipLesson");
            }
        }

        /// <summary>
        /// Names the count, because the number is the reason to press it. "More
        /// features" reads as marketing; "3 features are off" reads as a fact
        /// about this install.
        /// </summary>
        private static string DiscoverLabel(BWTTutorialStripContent content)
        {
            return "BWT_Tutorial_Discover_Button".Translate(content.DisabledFeatureCount);
        }

        private static float MeasureButtonWidth(string label)
        {
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float width = Text.CalcSize(label ?? string.Empty).x;
            Text.Font = oldFont;
            return Mathf.Clamp(width + 18f, 60f, 170f);
        }

        private static string DisplayInstruction(BWTTutorialStripContent content)
        {
            return content.Mode == BWTTutorialStripMode.Complete
                ? "\u2713  " + content.Instruction
                : content.Instruction;
        }

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }
    }
}
