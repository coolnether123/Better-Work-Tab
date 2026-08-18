using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Features.TimePriority
{
    /// <summary>
    /// Recreates Fluffy Work Tab's hour-selection scheduler while BWT owns the Work tab.
    /// The selector chooses which hours normal priority clicks assign; BWT continues to
    /// own persistence and runtime evaluation through <see cref="TimePriorityService"/>.
    /// </summary>
    internal static class FluffyTimeScheduleAssigner
    {
        internal const float TimeBarHeight = 40f;
        private const float MinTimeLabelSpacing = 50f;
        private const float ControlGap = 4f;

        private static readonly HashSet<int> SelectedHourSet =
            new HashSet<int>(Enumerable.Range(0, TimePriorityService.HoursPerDay));

        private static Rect _lastInteractiveRect;
        private static Rect _lastBarRect;
        private static Rect _lastWholeDayButtonRect;
        private static Rect _lastNowButtonRect;
        private static bool _isOpen;

        private static bool HasBetterWorkTabScheduleAuthority =>
            !PriorityAuthorityResolver.ShouldBlockBetterWorkTabPriorityDataAccess;

        private static bool EnsureBetterWorkTabScheduleAuthority()
        {
            if (HasBetterWorkTabScheduleAuthority)
            {
                return true;
            }

            if (_isOpen)
            {
                _isOpen = false;
                ClearInteractiveGeometry();
                NotifyLayoutChanged();
            }

            return false;
        }

        internal static bool IsAvailable =>
            FluffyWorkTabGateway.IsPresent &&
            FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.PrioritiesWholeDay, out _) &&
            FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.Now, out _) &&
            FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.PinEye, out _) &&
            FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.PinClock, out _);

        internal static bool IsEnabled =>
            IsAvailable &&
            (BetterWorkTabMod.Settings?.enableFluffyScheduleAssigner ??
             DefaultSettings.enableFluffyScheduleAssigner);

        internal static bool IsOpen => _isOpen && EnsureBetterWorkTabScheduleAuthority();

        internal static int VisibleHour { get; private set; } = -1;

        internal static float ReservedBottomSpace => IsOpen ? TimeBarHeight : 0f;

        internal static bool OwnsCurrentMousePosition =>
            IsOpen && Event.current != null && _lastInteractiveRect.Contains(Event.current.mousePosition);

        internal static bool TryHandleInput(Event evt)
        {
            if (!EnsureBetterWorkTabScheduleAuthority() || !_isOpen || evt == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag))
            {
                return false;
            }

            if (evt.button == 0 && _lastWholeDayButtonRect.Contains(evt.mousePosition))
            {
                SelectWholeDay();
                evt.Use();
                return true;
            }

            if (evt.button == 0 && _lastNowButtonRect.Contains(evt.mousePosition))
            {
                SelectHour(Find.CurrentMap != null ? GenLocalDate.HourOfDay(Find.CurrentMap) : 0, replace: true);
                evt.Use();
                return true;
            }

            if (!_lastBarRect.Contains(evt.mousePosition))
            {
                return false;
            }

            int hour = Mathf.Clamp(
                Mathf.FloorToInt((evt.mousePosition.x - _lastBarRect.xMin) /
                                 Mathf.Max(1f, _lastBarRect.width) * TimePriorityService.HoursPerDay),
                0,
                TimePriorityService.HoursPerDay - 1);
            if (evt.button == 0)
            {
                SelectHour(hour, replace: !evt.shift);
            }
            else if (evt.button == 1)
            {
                RemoveHour(hour);
            }
            else
            {
                return false;
            }

            evt.Use();
            return true;
        }

        internal static bool Toggle()
        {
            if (!IsEnabled || !EnsureBetterWorkTabScheduleAuthority())
            {
                return false;
            }

            _isOpen = !_isOpen;
            if (_isOpen)
            {
                TimePriorityScheduleEditor.CloseForWorkModeTransition();
            }

            NotifyLayoutChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            return true;
        }

        internal static void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            _isOpen = false;
            ClearInteractiveGeometry();
            NotifyLayoutChanged();
        }

        internal static void ResetForWindowClose()
        {
            bool wasOpen = _isOpen;
            _isOpen = false;
            SelectWholeDay();
            VisibleHour = -1;
            ClearInteractiveGeometry();

            if (wasOpen)
            {
                NotifyLayoutChanged();
            }
        }

        internal static int GetDisplayHour(Pawn pawn)
        {
            if (VisibleHour >= 0)
            {
                return VisibleHour;
            }

            return pawn != null
                ? GenLocalDate.HourOfDay(pawn)
                : Find.CurrentMap != null ? GenLocalDate.HourOfDay(Find.CurrentMap) : 0;
        }

        internal static int GetDisplayPriority(TimePriorityTarget target, int fallbackPriority, Pawn pawn)
        {
            return TimePriorityService.GetPriorityAtHour(target, fallbackPriority, GetDisplayHour(pawn));
        }

        internal static bool TryDrawWorkTypeCell(Rect cellRect, Pawn pawn, WorkTypeDef workType)
        {
            if (!IsOpen || pawn?.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return false;
            }

            Rect boxRect = Better_Work_Tab.UI.WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            int fallbackPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            int displayPriority = GetDisplayPriority(
                TimePriorityTarget.ForRuntimeWorkType(pawn, workType),
                fallbackPriority,
                pawn);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.WordWrap = false;
                WidgetsWork.DrawWorkBoxBackground(boxRect, pawn, workType);
                if (Find.PlaySettings.useWorkPriorities)
                {
                    if (displayPriority > WorkPrioritySystem.DisabledPriority)
                    {
                        Text.Font = GameFont.Medium;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        GUI.color = WorkPrioritySystem.GetPriorityColor(displayPriority);
                        Widgets.Label(boxRect.ContractedBy(-3f), displayPriority.ToString());
                    }
                }
                else if (displayPriority > WorkPrioritySystem.DisabledPriority)
                {
                    GUI.color = oldColor;
                    GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
                }

                if (Mouse.IsOver(boxRect))
                {
                    Widgets.DrawHighlight(boxRect);
                }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }

            HandleWorkTypeInput(boxRect, pawn, workType, displayPriority);
            return true;
        }

        private static void HandleWorkTypeInput(Rect boxRect, Pawn pawn, WorkTypeDef workType, int currentPriority)
        {
            Event evt = Event.current;
            if (evt == null || !boxRect.Contains(evt.mousePosition))
            {
                return;
            }

            int nextPriority;
            if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1))
            {
                nextPriority = Find.PlaySettings.useWorkPriorities
                    ? WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, evt.button)
                    : currentPriority > WorkPrioritySystem.DisabledPriority
                        ? WorkPrioritySystem.DisabledPriority
                        : WorkPrioritySystem.GetDefaultEnabledPriority();
            }
            else if (evt.type == EventType.ScrollWheel &&
                     (BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false))
            {
                int direction = evt.delta.y > 0f ? -1 : 1;
                nextPriority = Find.PlaySettings.useWorkPriorities
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(currentPriority, direction)
                    : currentPriority > WorkPrioritySystem.DisabledPriority
                        ? WorkPrioritySystem.DisabledPriority
                        : WorkPrioritySystem.GetDefaultEnabledPriority();
            }
            else
            {
                return;
            }

            if (nextPriority != currentPriority)
            {
                ApplyWorkTypePriority(pawn, workType, nextPriority);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            evt.Use();
        }

        internal static bool ApplyWorkTypePriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (!IsOpen || pawn?.workSettings == null || workType == null)
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            TimePriorityTarget target = TimePriorityTarget.ForRuntimeWorkType(pawn, workType);
            if (SelectedHourSet.Count == TimePriorityService.HoursPerDay)
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, priority);
                ClearScheduleSynced(target, priority);
                return true;
            }

            int fallback = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            ApplySelectedHours(target, fallback, priority);
            return true;
        }

        internal static bool ApplyWorkGiverPriority(int pawnId, WorkGiverDef workGiver, int priority)
        {
            if (!IsOpen || workGiver?.workType == null)
            {
                return false;
            }

            Pawn pawn = pawnId == TimePriorityTarget.GlobalPawnId
                ? null
                : PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(candidate => candidate.thingIDNumber == pawnId);
            int parentPriority = pawn == null
                ? WorkPrioritySystem.GetDefaultEnabledPriority()
                : WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workGiver.workType);
            int fallback = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            TimePriorityTarget target = TimePriorityTarget.ForRuntimeWorkGiver(pawn, workGiver.workType, workGiver);
            priority = WorkPrioritySystem.ClampPriority(priority);

            if (SelectedHourSet.Count == TimePriorityService.HoursPerDay)
            {
                WorkGiverReassignmentManager.SetPawnOverrideSynced(pawnId, workGiver.defName, priority);
                ClearScheduleSynced(target, priority);
                return true;
            }

            ApplySelectedHours(target, fallback, priority);
            return true;
        }

        private static void ApplySelectedHours(TimePriorityTarget target, int fallbackPriority, int priority)
        {
            int[] priorities = TimePriorityService.GetPrioritiesForDisplay(target, fallbackPriority);
            bool[] pinnedHours = TimePriorityService.GetLinkStateForDisplay(target);
            foreach (int hour in SelectedHourSet)
            {
                priorities[hour] = priority;
                pinnedHours[hour] = true;
            }

            TimePriorityService.SetScheduleSynced(target, priorities, pinnedHours, fallbackPriority);
        }

        private static void ClearScheduleSynced(TimePriorityTarget target, int fallbackPriority)
        {
            int[] wholeDay = Enumerable.Repeat(fallbackPriority, TimePriorityService.HoursPerDay).ToArray();
            TimePriorityService.SetScheduleSynced(
                target,
                wholeDay,
                new bool[TimePriorityService.HoursPerDay],
                fallbackPriority);
        }

        internal static void Draw(Rect inRect, IWorkTabLayoutController layout, float baseBottomSpace)
        {
            if (!IsOpen || !IsEnabled || layout?.Columns == null)
            {
                return;
            }

            if (!TryGetWorkColumnSpan(layout, inRect, out float start, out float width))
            {
                return;
            }

            Rect bar = new Rect(
                start,
                inRect.yMax - baseBottomSpace - TimeBarHeight,
                width,
                TimeBarHeight);
            Rect controls = new Rect(inRect.xMin, bar.yMin + bar.height / 3f, Mathf.Max(0f, bar.xMin - inRect.xMin), bar.height * 2f / 3f);
            float buttonSize = controls.height;
            Rect wholeDayButton = new Rect(controls.xMax - buttonSize, controls.y, buttonSize, buttonSize);
            Rect nowButton = new Rect(wholeDayButton.x - buttonSize - ControlGap, controls.y, buttonSize, buttonSize);
            _lastInteractiveRect = Rect.MinMaxRect(inRect.xMin, bar.yMin, bar.xMax, bar.yMax);
            _lastBarRect = bar;
            _lastWholeDayButtonRect = wholeDayButton;
            _lastNowButtonRect = nowButton;

            DrawControlButtons(wholeDayButton, nowButton);
            DrawHourBar(bar);
        }

        private static void DrawControlButtons(Rect wholeDayButton, Rect nowButton)
        {
            if (FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.PrioritiesWholeDay, out Texture2D wholeDayIcon) &&
                Widgets.ButtonImage(wholeDayButton, wholeDayIcon, Color.white, GenUI.MouseoverColor))
            {
                SelectWholeDay();
            }
            TooltipHandler.TipRegion(wholeDayButton, "Select the whole day");

            if (FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.Now, out Texture2D nowIcon) &&
                Widgets.ButtonImage(nowButton, nowIcon, Color.white, GenUI.MouseoverColor))
            {
                SelectHour(Find.CurrentMap != null ? GenLocalDate.HourOfDay(Find.CurrentMap) : 0, replace: true);
            }
            TooltipHandler.TipRegion(nowButton, "Select the current hour");
        }

        private static void DrawHourBar(Rect bar)
        {
            float hourWidth = bar.width / TimePriorityService.HoursPerDay;
            float lowerHeight = bar.height * 2f / 3f;
            float indicatorSize = lowerHeight;
            float lastLabelRight = float.MinValue;
            Rect hourRect = new Rect(bar.xMin, bar.yMax - lowerHeight, hourWidth, lowerHeight);
            int currentHour = Find.CurrentMap != null ? GenLocalDate.HourOfDay(Find.CurrentMap) : 0;

            Color oldColor = GUI.color;
            GUI.color = Color.grey;
            Widgets.DrawLineHorizontal(bar.xMin, bar.yMax - 1f, bar.width);
            Widgets.DrawLineVertical(hourRect.xMin, hourRect.yMin + hourRect.height / 2f, hourRect.height / 2f);
            GUI.color = oldColor;

            for (int hour = 0; hour < TimePriorityService.HoursPerDay; hour++)
            {
                GUI.color = Color.grey;
                Widgets.DrawLineVertical(hourRect.xMax, hourRect.yMin + hourRect.height / 2f, hourRect.height / 2f);
                Widgets.DrawLineVertical(hourRect.xMin + hourRect.width / 4f, hourRect.yMin + hourRect.height * 0.75f, hourRect.height * 0.25f);
                Widgets.DrawLineVertical(hourRect.xMin + hourRect.width / 2f, hourRect.yMin + hourRect.height * 0.75f, hourRect.height * 0.25f);
                Widgets.DrawLineVertical(hourRect.xMin + hourRect.width * 0.75f, hourRect.yMin + hourRect.height * 0.75f, hourRect.height * 0.25f);
                GUI.color = oldColor;

                string label = FormatHour(hour);
                Vector2 labelSize = Text.CalcSize(label);
                if (hourRect.xMin - lastLabelRight > MinTimeLabelSpacing)
                {
                    Rect labelRect = new Rect(hourRect.xMin - labelSize.x / 2f, bar.yMin + bar.height / 3f, labelSize.x, lowerHeight);
                    DrawTimeLabel(labelRect, label);
                    lastLabelRight = labelRect.xMax;
                }

                HandleHourInput(hourRect, hour);
                Widgets.DrawHighlightIfMouseover(hourRect);
                if (SelectedHourSet.Contains(hour))
                {
                    Widgets.DrawHighlightSelected(hourRect);
                }

                if (VisibleHour == hour && hour != currentHour &&
                    FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.PinEye, out Texture2D eye))
                {
                    Rect eyeRect = new Rect(
                        hourRect.center.x - indicatorSize / 2f,
                        hourRect.yMax - indicatorSize - hourRect.height / 6f,
                        indicatorSize,
                        indicatorSize);
                    GUI.DrawTexture(eyeRect, eye);
                }

                TooltipHandler.TipRegion(
                    hourRect,
                    FormatHour(hour) + " - " + FormatHour((hour + 1) % TimePriorityService.HoursPerDay) +
                    (SelectedHourSet.Contains(hour) ? "\nSelected" : "\nNot selected") +
                    "\nLeft-click to select; Shift-click to add; right-click to remove.");
                hourRect.x += hourRect.width;
            }

            string midnight = FormatHour(0);
            Vector2 midnightSize = Text.CalcSize(midnight);
            DrawTimeLabel(new Rect(hourRect.xMin - midnightSize.x / 2f, bar.yMin + bar.height / 3f, midnightSize.x, lowerHeight), midnight);

            if (FluffyWorkTabGateway.TryGetIcon(FluffyWorkTabIcon.PinClock, out Texture2D clock))
            {
                float currentTimeX = Find.CurrentMap != null
                    ? GenLocalDate.DayPercent(Find.CurrentMap) * bar.width
                    : 0f;
                Rect currentTimeRect = new Rect(
                    bar.xMin + currentTimeX - indicatorSize / 2f,
                    hourRect.yMax - indicatorSize - hourRect.height / 6f,
                    indicatorSize,
                    indicatorSize);
                GUI.DrawTexture(currentTimeRect, clock);
            }

            GUI.color = oldColor;
        }

        private static void HandleHourInput(Rect hourRect, int hour)
        {
            Event evt = Event.current;
            if (evt == null || !hourRect.Contains(evt.mousePosition) ||
                (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag))
            {
                return;
            }

            if (evt.button == 0)
            {
                SelectHour(hour, replace: !evt.shift);
                evt.Use();
            }
            else if (evt.button == 1)
            {
                RemoveHour(hour);
                evt.Use();
            }
        }

        private static void SelectHour(int hour, bool replace)
        {
            hour = Mathf.Clamp(hour, 0, TimePriorityService.HoursPerDay - 1);
            if (replace)
            {
                SelectedHourSet.Clear();
            }

            SelectedHourSet.Add(hour);
            VisibleHour = hour;
        }

        private static void RemoveHour(int hour)
        {
            SelectedHourSet.Remove(hour);
            if (VisibleHour == hour)
            {
                VisibleHour = -1;
            }
        }

        private static void SelectWholeDay()
        {
            SelectedHourSet.Clear();
            for (int hour = 0; hour < TimePriorityService.HoursPerDay; hour++)
            {
                SelectedHourSet.Add(hour);
            }

            VisibleHour = -1;
        }

        private static void ClearInteractiveGeometry()
        {
            _lastInteractiveRect = Rect.zero;
            _lastBarRect = Rect.zero;
            _lastWholeDayButtonRect = Rect.zero;
            _lastNowButtonRect = Rect.zero;
        }

        private static void DrawTimeLabel(Rect rect, string label)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = Color.grey;
            Widgets.Label(rect, label);
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private static string FormatHour(int hour)
        {
            return hour.ToString("D2") + ":00";
        }

        private static bool TryGetWorkColumnSpan(IWorkTabLayoutController layout, Rect inRect, out float start, out float width)
        {
            start = float.MaxValue;
            float end = float.MinValue;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                start = Mathf.Min(start, headerRect.xMin);
                end = Mathf.Max(end, headerRect.xMax);
            }

            if (start == float.MaxValue || end <= start)
            {
                width = 0f;
                return false;
            }

            start = Mathf.Max(inRect.xMin, start);
            end = Mathf.Min(inRect.xMax, end);
            width = Mathf.Max(0f, end - start);
            return width > 1f;
        }

        private static void NotifyLayoutChanged()
        {
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }
}
