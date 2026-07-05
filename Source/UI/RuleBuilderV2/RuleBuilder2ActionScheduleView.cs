using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2ActionScheduleView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private readonly RuleBuilder2TutorialController tutorial;
        private bool schedulePaintActive;
        private int schedulePaintPriority;

        internal RuleBuilder2ActionScheduleView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout,
            RuleBuilder2TutorialController tutorial)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
            this.tutorial = tutorial;
        }

        internal void ClearSchedulePaintOnMouseUp()
        {
            if (schedulePaintActive && Event.current.rawType == EventType.MouseUp)
            {
                schedulePaintActive = false;
            }
        }

        internal void DrawActionSection(Rect rect, RuleBuilder2Card card)
        {
            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            RuleBuilder2ActionSectionRects action = layout.ActionSection(rect);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_ActionBlockTitle"));

            DrawActionModeButtons(action.Modes, card);

            Rect priority = new Rect(action.Priority.x + 58f, action.Priority.y, action.Priority.width - 58f, action.Priority.height);
            Rect chip = new Rect(action.Priority.x, action.Priority.y + 2f, 42f, 24f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(chip, WorkPrioritySystem.GetPriorityColor(card.Action.Priority));
            Widgets.DrawBox(chip, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            DrawFittedLabel(chip, card.Action.Priority <= 0 ? "X" : card.Action.Priority.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            DrawIntTextEntry(priority, card.Action, 0, RuleBuilder2PriorityRange.Max);
            TooltipHandler.TipRegion(action.Priority, T("BWT_PriorityInput_Tooltip").Formatted(RuleBuilder2PriorityRange.Max).ToString());
            GUI.color = Color.gray;
            DrawFittedLabel(action.Scope, card.Target.IsSubWorkTarget
                ? T("BWT_RuleBuilder2_ActionScopeSpecificJob")
                : T("BWT_RuleBuilder2_ActionScopeWholeWork"));
            GUI.color = Color.white;

            if (card.Action.Kind == RuleBuilder2ActionKind.SetTimeSchedule ||
                card.Action.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule)
            {
                card.Action.EnsureSchedule(card.Action.Priority);
                DrawSchedule(action.Body, card.Action, card.Target.IsSubWorkTarget);
            }
            else
            {
                schedulePaintActive = false;
            }
            GUI.EndGroup();
        }

        internal void DrawActionModeButtons(Rect rect, RuleBuilder2Card card)
        {
            float gap = 6f;
            float width = (rect.width - gap * 3f) / 4f;
            RuleBuilder2ActionKind scheduleKind = card.Target.IsSubWorkTarget
                ? RuleBuilder2ActionKind.SetSubWorkSchedule
                : RuleBuilder2ActionKind.SetTimeSchedule;

            DrawActionModeButton(new Rect(rect.x, rect.y, width, rect.height), card, RuleBuilder2ActionKind.SetPriority, T("BWT_RuleBuilder2_ActionModeAllDay"));
            DrawActionModeButton(new Rect(rect.x + (width + gap), rect.y, width, rect.height), card, scheduleKind, T("BWT_RuleBuilder2_ActionModeDailyPlan"));
            DrawActionModeButton(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), card, RuleBuilder2ActionKind.Disable, T("BWT_RuleBuilder2_ActionModeDisable"));
            DrawActionModeButton(new Rect(rect.x + (width + gap) * 3f, rect.y, width, rect.height), card, RuleBuilder2ActionKind.FollowGlobal, T("BWT_RuleBuilder2_ActionModeFollow"));
        }

        internal void DrawActionModeButton(Rect rect, RuleBuilder2Card card, RuleBuilder2ActionKind kind, string label)
        {
            bool selected = card.Action.Kind == kind ||
                            (kind == RuleBuilder2ActionKind.SetTimeSchedule && card.Action.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule);
            Color previous = GUI.color;
            if (selected)
            {
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, new Color(0.24f, 0.3f, 0.22f, 0.95f));
            }
            else
            {
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.92f));
            }

            Widgets.DrawBox(rect, selected ? 2 : 1);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            DrawFittedLabel(new Rect(rect.x + 6f, rect.y + 5f, rect.width - 12f, rect.height - 10f), label);
            TooltipHandler.TipRegion(rect, GetActionModeTooltip(kind));

            if (Better_Work_Tab.WidgetsCompat.ButtonInvisible(rect))
            {
                flow.SetActionKind(card, kind);
                if (card.Action.Kind != RuleBuilder2ActionKind.SetTimeSchedule &&
                    card.Action.Kind != RuleBuilder2ActionKind.SetSubWorkSchedule)
                {
                    schedulePaintActive = false;
                }
                UISoundCompat.TickLow.PlayOneShotOnCamera();
            }

            GUI.color = previous;
        }

        internal static string GetActionModeTooltip(RuleBuilder2ActionKind kind)
        {
            switch (kind)
            {
                case RuleBuilder2ActionKind.SetPriority:
                    return T("BWT_RuleBuilder2_ActionModeAllDay_Tooltip");
                case RuleBuilder2ActionKind.SetTimeSchedule:
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                    return T("BWT_RuleBuilder2_ActionModeDailyPlan_Tooltip");
                case RuleBuilder2ActionKind.Disable:
                    return T("BWT_RuleBuilder2_ActionModeDisable_Tooltip");
                case RuleBuilder2ActionKind.FollowGlobal:
                    return T("BWT_RuleBuilder2_ActionModeFollow_Tooltip");
                default:
                    return "";
            }
        }

        internal void DrawSchedule(Rect rect, RuleBuilder2Action action, bool specificJob)
        {
            Rect fillAll = new Rect(rect.xMax - 196f, rect.y, 88f, 26f);
            Rect workday = new Rect(fillAll.xMax + 8f, rect.y, 100f, 26f);
            Rect header = new Rect(rect.x, rect.y + 1f, Mathf.Max(1f, fillAll.x - rect.x - 8f), 24f);
            Rect grid = new Rect(rect.x, rect.y + 32f, rect.width, Mathf.Max(56f, rect.height - 32f));

            GUI.color = Color.gray;
            DrawFittedLabel(header, specificJob
                ? T("BWT_RuleBuilder2_ScheduleSubJobHint")
                : T("BWT_RuleBuilder2_ScheduleWorkHint"));
            GUI.color = Color.white;

            if (Better_Work_Tab.WidgetsCompat.ButtonText(fillAll, T("BWT_RuleBuilder2_ScheduleFillAll")))
            {
                FillSchedule(action, action.Priority);
            }

            if (Better_Work_Tab.WidgetsCompat.ButtonText(workday, T("BWT_RuleBuilder2_ScheduleWorkday")))
            {
                FillWorkdaySchedule(action);
            }

            DrawScheduleGrid(grid, action);
        }

        internal void DrawScheduleGrid(Rect rect, RuleBuilder2Action action)
        {
            float cellWidth = rect.width / 24f;
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            for (int hour = 0; hour < 24; hour++)
            {
                Rect label = new Rect(rect.x + hour * cellWidth, rect.y, cellWidth, 18f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(label, hour.ToString());

                Rect cell = new Rect(label.x + 1f, rect.y + 22f, cellWidth - 2f, 34f);
                Text.Font = previousFont;
                int priority = action.HourlyPriorities[hour];
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(cell, WorkPrioritySystem.GetPriorityColor(priority));
                Widgets.DrawBox(cell, 1);
                GUI.color = Color.white;
                Widgets.Label(cell, priority <= 0 ? "X" : priority.ToString());
                Text.Font = GameFont.Tiny;

                bool overCell = Mouse.IsOver(cell);
                if (overCell)
                {
                    Widgets.DrawBox(cell.ExpandedBy(1f), 2);
                    TooltipHandler.TipRegion(cell, T("BWT_RuleBuilder2_SchedulePaintTooltip").Formatted(hour).ToString());
                }

                if (overCell && Event.current.type == EventType.MouseDown)
                {
                    int dir = Event.current.button == 1 ? 1 : -1;
                    schedulePaintPriority = RuleBuilder2PriorityRange.Step(priority, dir);
                    schedulePaintActive = true;
                    action.HourlyPriorities[hour] = schedulePaintPriority;
                    Event.current.Use();
                    UISoundCompat.TickTiny.PlayOneShotOnCamera();
                }

                if (schedulePaintActive && overCell && Event.current.type == EventType.MouseDrag)
                {
                    action.HourlyPriorities[hour] = schedulePaintPriority;
                    Event.current.Use();
                }
            }

            Text.Font = previousFont;
            if (schedulePaintActive && Event.current.rawType == EventType.MouseUp)
            {
                schedulePaintActive = false;
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        internal static void FillSchedule(RuleBuilder2Action action, int priority)
        {
            action.EnsureSchedule(priority);
            for (int i = 0; i < action.HourlyPriorities.Count; i++)
            {
                action.HourlyPriorities[i] = RuleBuilder2PriorityRange.Clamp(priority);
            }
        }

        internal static void FillWorkdaySchedule(RuleBuilder2Action action)
        {
            action.EnsureSchedule(action.Priority);
            int activePriority = RuleBuilder2PriorityRange.Clamp(action.Priority);
            int defaultPriority = WorkPrioritySystem.GetDefaultEnabledPriority();
            for (int hour = 0; hour < action.HourlyPriorities.Count; hour++)
            {
                action.HourlyPriorities[hour] = hour >= 8 && hour <= 17
                    ? activePriority
                    : defaultPriority;
            }
        }
    }
}
