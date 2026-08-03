using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers.Angled;
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
            int totalCount)
        {
            Mode = mode;
            Instruction = instruction ?? string.Empty;
            CompletedCount = completedCount;
            TotalCount = totalCount;
        }

        internal BWTTutorialStripMode Mode { get; }
        internal string Instruction { get; }
        internal int CompletedCount { get; }
        internal int TotalCount { get; }
        internal bool HasProgress => TotalCount > 0;
    }

    internal readonly struct BWTTutorialStripLayout
    {
        internal BWTTutorialStripLayout(
            Rect stripRect,
            Rect progressRect,
            Rect instructionRect,
            Rect skipRect,
            Rect exitRect)
        {
            StripRect = stripRect;
            ProgressRect = progressRect;
            InstructionRect = instructionRect;
            SkipRect = skipRect;
            ExitRect = exitRect;
        }

        internal Rect StripRect { get; }
        internal Rect ProgressRect { get; }
        internal Rect InstructionRect { get; }
        internal Rect SkipRect { get; }
        internal Rect ExitRect { get; }
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
        // Small-font text needs about 22px of line box, so the band has to clear
        // that plus its padding or the instruction is clipped at both ends.
        internal const float RowHeight = 34f;
        private const float Padding = 5f;
        private const float ButtonGap = 6f;
        private const float ButtonHeight = 22f;
        private const float ProgressWidth = 44f;
        private const float MinimumInstructionWidth = 120f;

        // The reserved height feeds Work-tab layout, so it must stay constant for
        // a whole frame. Visibility is therefore latched once per Work-tab pass
        // rather than re-derived from tutorial policy inside geometry code.
        private static bool reserved;

        internal static float ReservedHeight => reserved ? RowHeight : 0f;

        internal static bool IsReserved => reserved;

        /// <summary>
        /// Latches whether the band exists this frame. Call once per Work-tab
        /// pass before any layout geometry is derived.
        /// </summary>
        internal static void RefreshReservation(bool visible)
        {
            reserved = visible;
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
            float top = layout.TableOrigin.y +
                layout.HeaderHeight +
                TimePriorityScheduleEditor.HeaderPinnedRowsHeight +
                SubWorkDrilldownBarRenderer.ReservedRowHeight;
            float width = Mathf.Max(
                layout.Table != null ? layout.Table.Size.x - 16f : 0f,
                1f);
            Rect strip = new Rect(layout.TableOrigin.x, top, width, RowHeight);
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
                instruction = new Rect(
                    instructionLeft,
                    inner.y,
                    Mathf.Max(1f, inner.xMax - instructionLeft),
                    inner.height);
            }

            return new BWTTutorialStripLayout(strip, progress, instruction, skip, exit);
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
            Text.WordWrap = false;
            Text.Font = GameFont.Small;

            bool complete = content.Mode == BWTTutorialStripMode.Complete;
            // RimWorld's own tutorial chrome. Using the game's tutor palette
            // rather than a custom dark panel is what makes the band read as
            // part of RimWorld instead of an overlay bolted onto the Work tab.
            Widgets.DrawWindowBackgroundTutor(layout.StripRect);

            if (layout.ProgressRect.width > 1f)
            {
                DrawProgress(layout.ProgressRect, content);
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            string instruction = complete
                ? "✓  " + content.Instruction
                : content.Instruction;
            Widgets.Label(layout.InstructionRect, instruction.Truncate(layout.InstructionRect.width));
            if (Text.CalcSize(instruction).x > layout.InstructionRect.width)
            {
                TooltipHandler.TipRegion(layout.InstructionRect, instruction);
            }
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

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
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
                    return T("BWT_Tutorial_LeaveTutorial");
                default:
                    return T("BWT_Tutorial_SkipLesson");
            }
        }

        private static float MeasureButtonWidth(string label)
        {
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float width = Text.CalcSize(label ?? string.Empty).x;
            Text.Font = oldFont;
            return Mathf.Clamp(width + 18f, 60f, 170f);
        }

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }
    }
}
