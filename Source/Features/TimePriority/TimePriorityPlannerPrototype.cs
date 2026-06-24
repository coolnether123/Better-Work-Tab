using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Features.TimePriority
{
    /// <summary>
    /// Visual prototype for per-hour work priorities. This intentionally stores local UI-only
    /// values until the UX is validated and the execution/save model is designed.
    /// </summary>
    internal static class TimePriorityPlannerPrototype
    {
        private const int HoursPerDay = 24;
        private const float AnimationSeconds = 0.22f;
        private const float PanelPadding = 8f;
        private const float PawnLabelWidth = 112f;
        private const float HeaderHeight = 37f;
        private const float TimelineRowHeight = 28f;
        private const float MaxPanelWidth = 780f;
        private const float MinPanelWidth = 420f;
        private const float InlineDividerHeight = 34f;
        private const float InlineChronosHeight = 10f;
        private const float InlineHourLabelHeight = 16f;
        private const float InlineTimelineHeight = 24f;
        private const float InlineTimelineVerticalInset = 3f;
        private const float InlineMinimumHourWidth = 7f;

        private static readonly List<CellHit> LastCellHits = new List<CellHit>(HoursPerDay * 4);
        private static readonly List<CopyPasteHit> LastCopyPasteHits = new List<CopyPasteHit>(8);
        private static readonly List<ScheduleCellDiagnostic> LastScheduleCellDiagnostics = new List<ScheduleCellDiagnostic>(HoursPerDay * 4);
        private const string AgentOpenRequestFileName = "BWTTimePriorityOpen.request";
        private static readonly PawnDivider ActiveDivider = new PawnDivider
        {
            DividerColor = new Color(0.16f, 0.17f, 0.14f, 0.92f),
            ShowLabel = true,
            LabelFont = GameFont.Small,
            IsCollapsed = false,
            Height = InlineDividerHeight
        };
        private static Session _session;
        private static Rect _lastPanelRect;
        private static Rect _lastCloseRect;
        private static bool _isClosing;
        private static float _closingStartedAt;
        private static Rect _closingSourceRect;

        internal static bool IsEnabled =>
            BetterWorkTabMod.Settings?.enableTimePriorityPlannerPrototype ??
            DefaultSettings.enableTimePriorityPlannerPrototype;

        internal static float HeaderPinnedRowsHeight =>
            IsEnabled && _session?.IsGlobal == true
                ? InlineDividerHeight * GetProgress()
                : 0f;

        internal static int LayoutSignature
        {
            get
            {
                if (!IsEnabled || _session == null)
                {
                    return 0;
                }

                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (int)_session.Kind;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_session.WorkTypeDefName ?? string.Empty);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_session.TargetDefName ?? string.Empty);
                    hash = hash * 31 + _session.PawnIds.Count;
                    for (int i = 0; i < _session.PawnIds.Count; i++)
                    {
                        hash = hash * 31 + _session.PawnIds[i];
                    }

                    hash = hash * 31 + (_isClosing ? 1 : 0);
                    hash = hash * 31 + Mathf.RoundToInt(HeaderPinnedRowsHeight * 2f);
                    return hash;
                }
            }
        }

        internal static bool OwnsMousePosition(Vector2 mousePosition)
        {
            return IsEnabled &&
                _session != null &&
                _lastPanelRect.width > 0f &&
                _lastPanelRect.height > 0f &&
                _lastPanelRect.Contains(mousePosition);
        }

        internal static bool OwnsCurrentMousePosition
        {
            get
            {
                Event evt = Event.current;
                return evt != null && OwnsMousePosition(evt.mousePosition);
            }
        }

        internal static bool ShouldHighlightSourceColumn(WorkTabLayoutColumn column)
        {
            if (!IsEnabled ||
                _session == null ||
                !(BetterWorkTabMod.Settings?.keepTimePrioritySourceColumnHighlighted ??
                  DefaultSettings.keepTimePrioritySourceColumnHighlighted) ||
                !(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                return false;
            }

            if (_session.Kind == TimePriorityTargetKind.WorkType)
            {
                return !SubWorkDrilldownState.IsActive &&
                    string.Equals(column.Column.workType?.defName, _session.WorkTypeDefName, StringComparison.Ordinal);
            }

            return SubWorkDrilldownState.IsActive &&
                string.Equals(SubWorkDrilldownState.ActiveWorkType?.defName, _session.WorkTypeDefName, StringComparison.Ordinal) &&
                SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out WorkGiver workGiver, out _) &&
                string.Equals(workGiver?.def?.defName, _session.TargetDefName, StringComparison.Ordinal);
        }

        internal static void CloseForWorkModeTransition()
        {
            if (!IsEnabled || _session == null || _isClosing)
            {
                return;
            }

            StartCloseAnimation(GetTimelineAnimationSource());
        }

        internal static bool TryGetTransientDivider(out int pawnId, out PawnDivider divider)
        {
            pawnId = 0;
            divider = null;
            FinishCloseIfComplete();
            if (!IsEnabled || _session == null || _session.IsGlobal || _session.PawnIds.Count == 0)
            {
                return false;
            }

            pawnId = _session.PawnIds[0];
            ActiveDivider.DividerName = _session.TargetLabel + " time priorities";
            ActiveDivider.Height = InlineDividerHeight;
            ActiveDivider.IsCollapsed = false;
            divider = ActiveDivider;
            return true;
        }

        internal static bool IsTransientDivider(PawnDivider divider)
        {
            return ReferenceEquals(divider, ActiveDivider);
        }

        internal static void OpenForFloatMenu(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver = null)
        {
            if (!IsEnabled || pawn == null || workType == null)
            {
                return;
            }

            int currentPriority;
            TimePriorityTarget target;
            if (workGiver != null)
            {
                int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                currentPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
                target = TimePriorityTarget.ForWorkGiver(
                    pawn,
                    workType,
                    workGiver,
                    WorkGiverDisplayNameService.HeaderLabel(workGiver));
            }
            else
            {
                currentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                target = TimePriorityTarget.ForWorkType(pawn, workType);
            }

            Rect sourceRect = new Rect(Verse.UI.screenWidth / 2f - 12f, Verse.UI.screenHeight / 2f - 12f, 24f, 24f);
            _session = new Session(new TargetInfo(pawn, target, sourceRect, currentPriority));
            TimePriorityService.GetPrioritiesForDisplay(target, currentPriority);
            NotifyLayoutChanged();
            Find.MainTabsRoot.SetCurrentTab(DefDatabase<MainButtonDef>.GetNamedSilentFail("Work"));
        }

        internal static bool OpenForPriorityBox(TimePriorityTarget target, Rect priorityBoxRect, int currentPriority)
        {
            if (!IsEnabled || string.IsNullOrEmpty(target.WorkTypeDefName))
            {
                return false;
            }

            _isClosing = false;
            _closingStartedAt = 0f;
            _closingSourceRect = Rect.zero;

            var info = new TargetInfo(null, target, priorityBoxRect, currentPriority);
            if (_session != null && _session.Matches(target))
            {
                if (!_session.PawnIds.Contains(target.PawnId))
                {
                    _session.PawnIds.Add(target.PawnId);
                }

                _session.SourceBoxRect = priorityBoxRect;
                _session.StartedAt = Time.realtimeSinceStartup;
                TimePriorityService.GetPrioritiesForDisplay(target, currentPriority);
                NotifyLayoutChanged();
                return true;
            }

            _session = new Session(info);
            NotifyLayoutChanged();
            return true;
        }

        internal static void TryOpenAgentRequestedSession(IWorkTabLayoutController layout)
        {
            if (!IsEnabled || layout == null || !IsAgentHarnessEnabled())
            {
                return;
            }

            string requestPath = Path.Combine(Path.GetTempPath(), AgentOpenRequestFileName);
            if (!File.Exists(requestPath))
            {
                return;
            }

            string requestedWorkType = string.Empty;
            try
            {
                requestedWorkType = File.ReadAllText(requestPath).Trim();
                File.Delete(requestPath);
            }
            catch (Exception ex)
            {
                Log.Warning("[BWT] Could not consume time-priority agent request: " + ex.Message);
            }

            if (!TryFindAgentWorkTypeTarget(layout, requestedWorkType, out TargetInfo target))
            {
                Log.Warning("[BWT] Time-priority agent request could not find a target for work type: " + requestedWorkType);
                return;
            }

            _session = new Session(target);
            TimePriorityService.GetPrioritiesForDisplay(target.TimeTarget, target.CurrentPriority);
            NotifyLayoutChanged();
        }

        internal static bool TryHandleInput(IWorkTabLayoutController layout, Event evt)
        {
            if (!IsEnabled)
            {
                FinishClose();
                return false;
            }

            if (layout == null || evt == null)
            {
                return false;
            }

            if (_session != null && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Close();
                evt.Use();
                return true;
            }

            if (_session != null &&
                !_isClosing &&
                (evt.type == EventType.MouseDown || evt.type == EventType.ScrollWheel) &&
                TryHandlePanelInput(evt))
            {
                return true;
            }

            if (evt.type != EventType.MouseDown ||
                evt.button != 0 ||
                !IsControlHeld(evt) ||
                _isClosing ||
                BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }

            if (!TryGetPriorityTarget(layout, evt.mousePosition, out TargetInfo target))
            {
                return false;
            }

            if (!Mouse.IsOver(target.PriorityBoxRect))
            {
                return false;
            }

            ToggleTarget(target);
            evt.Use();
            return true;
        }

        private static bool IsAgentHarnessEnabled()
        {
            if (GenCommandLine.CommandLineArgPassed("rw-agent"))
            {
                return true;
            }

            return File.Exists(Path.Combine(Path.GetTempPath(), "RimWorldAgent", "enable.txt"));
        }

        internal static void Draw(IWorkTabLayoutController layout)
        {
            FinishCloseIfComplete();
            if (TimePriorityScheduleTransferFeedback.ConsumeCloseRequest())
            {
                StartCloseAnimation(GetTimelineAnimationSource());
            }

            EventType eventType = Event.current.type;
            if (!IsEnabled ||
                _session == null ||
                layout == null ||
                (eventType != EventType.Repaint &&
                 eventType != EventType.MouseDown &&
                 eventType != EventType.MouseUp))
            {
                return;
            }

            if (!TryResolveVisibleTarget(layout, out WorkTabLayoutColumn column, out List<RowDrawInfo> rows))
            {
                return;
            }

            if (rows.Count == 0)
            {
                FinishClose();
                return;
            }

            float progress = GetProgress();
            Color oldColor = GUI.color;

            DrawInlineEditor(layout, column, rows, progress);

            GUI.color = oldColor;

            FinishCloseIfComplete();
        }

        private static bool TryHandlePanelInput(Event evt)
        {
            if (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && _lastCloseRect.Contains(evt.mousePosition))
            {
                StartCloseAnimation(_lastCloseRect);
                evt.Use();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return true;
            }

            if (TryHandleCopyPasteInput(evt))
            {
                return true;
            }

            if (TryHandleTimeCellInput(evt))
            {
                return true;
            }

            if (!_lastPanelRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button != 0)
            {
                evt.Use();
                return true;
            }

            if (evt.type == EventType.ScrollWheel && !(BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false))
            {
                return false;
            }

            evt.Use();
            return true;
        }

        private static bool TryHandleTimeCellInput(Event evt)
        {
            if (evt.type == EventType.ScrollWheel && !(BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false))
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button != 0 && evt.button != 1)
            {
                return false;
            }

            for (int i = 0; i < LastCellHits.Count; i++)
            {
                CellHit hit = LastCellHits[i];
                if (!hit.Rect.Contains(evt.mousePosition))
                {
                    continue;
                }

                if (IsControlHeld(evt))
                {
                    StartCloseAnimation(GetPointRect(evt.mousePosition));
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    evt.Use();
                    return true;
                }

                int current = TimePriorityService.GetPriorityAtHour(hit.Target, hit.FallbackPriority, hit.Hour);
                int next = GetNextPriorityForInput(current, evt);
                TimePriorityService.SetPriorityAtHourSynced(hit.Target, hit.Hour, next, hit.FallbackPriority);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                evt.Use();
                return true;
            }

            return false;
        }

        private static bool TryHandleCopyPasteInput(Event evt)
        {
            if (evt.type != EventType.MouseDown || evt.button != 0 || !ShowCopyPasteButtons)
            {
                return false;
            }

            for (int i = 0; i < LastCopyPasteHits.Count; i++)
            {
                CopyPasteHit hit = LastCopyPasteHits[i];
                if (hit.CopyRect.Contains(evt.mousePosition))
                {
                    CopySchedule(hit);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    evt.Use();
                    return true;
                }

                if (hit.PasteRect.Contains(evt.mousePosition) &&
                    TimePriorityScheduleClipboard.HasSnapshot)
                {
                    PasteSchedule(hit);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    evt.Use();
                    return true;
                }
            }

            return false;
        }

        private static void CopySchedule(CopyPasteHit hit)
        {
            TimePriorityScheduleClipboard.CopyFrom(hit.Target, hit.FallbackPriority, hit.Label);
            TimePriorityScheduleTransferFeedback.StartCopy(hit.Target);
            Messages.Message("Copied " + hit.Label + " time priorities.", MessageTypeDefOf.PositiveEvent, false);
        }

        private static void PasteSchedule(CopyPasteHit hit)
        {
            if (!TimePriorityScheduleClipboard.TryGetSnapshot(out TimePriorityScheduleSnapshot snapshot))
            {
                return;
            }

            int[] beforePriorities = TimePriorityService.GetPrioritiesForDisplay(hit.Target, hit.FallbackPriority);
            int[] afterPriorities = snapshot.CopyPriorities();
            EnsureTargetVisibleForTransfer(hit);
            TimePriorityService.SetPrioritiesSynced(hit.Target, afterPriorities, hit.FallbackPriority);
            TimePriorityScheduleTransferFeedback.StartPaste(hit.Target, beforePriorities, afterPriorities, closeWhenComplete: true);
            Messages.Message("Pasted " + snapshot.Label + " time priorities.", MessageTypeDefOf.PositiveEvent, false);
        }

        private static void EnsureTargetVisibleForTransfer(CopyPasteHit hit)
        {
            if (_session == null ||
                !_session.Matches(hit.Target) ||
                _session.PawnIds.Contains(hit.Target.PawnId))
            {
                return;
            }

            _session.PawnIds.Add(hit.Target.PawnId);
            _session.SourceBoxRect = hit.Rect;
            _session.StartedAt = Time.realtimeSinceStartup;
            NotifyLayoutChanged();
        }

        private static int GetNextPriorityForInput(int currentPriority, Event evt)
        {
            if (evt.type == EventType.ScrollWheel)
            {
                int direction = evt.delta.y > 0f ? -1 : 1;
                return Find.PlaySettings.useWorkPriorities
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(currentPriority, direction)
                    : ToggleNonManualPriority(currentPriority);
            }

            if (Find.PlaySettings.useWorkPriorities)
            {
                if (evt.button == 1)
                {
                    return WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, evt.button);
                }

                return WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, evt.button);
            }

            if (evt.button != 0)
            {
                return WorkPrioritySystem.ClampPriority(currentPriority);
            }

            return ToggleNonManualPriority(currentPriority);
        }

        private static int ToggleNonManualPriority(int currentPriority)
        {
            return currentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
        }

        private static void ToggleTarget(TargetInfo target)
        {
            if (_session != null && _session.Matches(target))
            {
                int existingIndex = _session.PawnIds.IndexOf(target.PawnId);
                if (existingIndex >= 0)
                {
                    if (_session.PawnIds.Count == 1)
                    {
                        Close();
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        return;
                    }

                    _session.PawnIds.RemoveAt(existingIndex);
                    _session.StartedAt = Time.realtimeSinceStartup;
                    _session.SourceBoxRect = target.PriorityBoxRect;
                    NotifyLayoutChanged();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    return;
                }

                _session.PawnIds.Add(target.PawnId);
                TimePriorityService.GetPrioritiesForDisplay(target.TimeTarget, target.CurrentPriority);
                _session.StartedAt = Time.realtimeSinceStartup;
                _session.SourceBoxRect = target.PriorityBoxRect;
                NotifyLayoutChanged();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return;
            }

            _session = new Session(target);
            NotifyLayoutChanged();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static void Close()
        {
            StartCloseAnimation(_lastCloseRect != Rect.zero ? _lastCloseRect : _session?.SourceBoxRect ?? Rect.zero);
        }

        private static void StartCloseAnimation(Rect sourceRect)
        {
            if (_session == null)
            {
                FinishClose();
                return;
            }

            _isClosing = true;
            _closingStartedAt = Time.realtimeSinceStartup;
            _closingSourceRect = sourceRect != Rect.zero ? sourceRect : _session.SourceBoxRect;
            NotifyLayoutChanged();
        }

        private static void FinishClose()
        {
            _session = null;
            _isClosing = false;
            _closingStartedAt = 0f;
            _closingSourceRect = Rect.zero;
            LastCellHits.Clear();
            LastCopyPasteHits.Clear();
            LastScheduleCellDiagnostics.Clear();
            _lastPanelRect = Rect.zero;
            _lastCloseRect = Rect.zero;
            NotifyLayoutChanged();
        }

        internal static bool TryDrawScheduleCopyPasteWorkPrioritiesCell(Rect rect, Pawn pawn)
        {
            if (!IsEnabled ||
                _session == null ||
                pawn == null ||
                pawn.Dead ||
                pawn.workSettings == null ||
                !pawn.workSettings.EverWork)
            {
                return false;
            }

            if (!ShowCopyPasteButtons)
            {
                return true;
            }

            int currentPriority = GetFallbackPriority(pawn);
            TimePriorityTarget target = _session.GetTargetForPawn(pawn);
            var hit = new CopyPasteHit(
                target,
                currentPriority,
                GetScheduleLabel(pawn),
                new Rect(rect.x, rect.y, CopyPasteUI.CopyPasteColumnWidth, 30f));

            LastCopyPasteHits.Add(hit);
            DrawScheduleCopyPasteButtons(hit, GetProgress());
            return true;
        }

        internal static bool TryGetCopyPasteSettingsContext(Vector2 mousePosition)
        {
            if (!IsEnabled || !ShowCopyPasteButtons)
            {
                return false;
            }

            for (int i = 0; i < LastCopyPasteHits.Count; i++)
            {
                CopyPasteHit hit = LastCopyPasteHits[i];
                if (hit.CopyRect.Contains(mousePosition) || hit.PasteRect.Contains(mousePosition))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void AppendScheduleGeometryDiagnostics(System.Text.StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            builder.AppendLine("timePriorityActive=" + (_session != null));
            builder.AppendLine("timePriorityCells=" + LastScheduleCellDiagnostics.Count);
            for (int i = 0; i < LastScheduleCellDiagnostics.Count; i++)
            {
                ScheduleCellDiagnostic diagnostic = LastScheduleCellDiagnostics[i];
                builder.AppendLine(
                    "timePriorityCell=" + i
                    + " target=\"" + diagnostic.TargetKey + "\""
                    + " hour=" + diagnostic.Hour
                    + " priority=" + diagnostic.Priority
                    + " cellRect=" + FormatRect(diagnostic.CellRect)
                    + " boxRect=" + FormatRect(diagnostic.BoxRect)
                    + " labelRect=" + FormatRect(diagnostic.LabelRect)
                    + " centerDelta=(" + Format(diagnostic.LabelRect.center.x - diagnostic.BoxRect.center.x)
                    + "," + Format(diagnostic.LabelRect.center.y - diagnostic.BoxRect.center.y) + ")");
            }
        }

        private static void FinishCloseIfComplete()
        {
            if (_isClosing && Time.realtimeSinceStartup - _closingStartedAt >= AnimationSeconds)
            {
                FinishClose();
            }
        }

        private static Rect GetPointRect(Vector2 point)
        {
            return new Rect(point.x - 1f, point.y - 1f, 2f, 2f);
        }

        private static void NotifyLayoutChanged()
        {
            PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static bool TryGetPriorityTarget(IWorkTabLayoutController layout, Vector2 mousePosition, out TargetInfo target)
        {
            target = default;

            if (!layout.TryGetRowAt(mousePosition, out WorkTabLayoutRow row) ||
                row.Pawn == null ||
                !TryGetBodyPriorityColumn(layout, mousePosition, out WorkTabLayoutColumn column))
            {
                return TryGetGlobalPriorityTarget(layout, mousePosition, out target);
            }

            Rect rowRect = layout.GetScreenRect(row);
            Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
            Rect priorityBoxRect = GetPriorityBoxRect(cellRect);

            if (SubWorkDrilldownState.IsActive)
            {
                if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out WorkGiver workGiver, out _) ||
                    workGiver?.def == null ||
                    SubWorkDrilldownState.ActiveWorkType == null)
                {
                    return false;
                }

                int fallback = WorkPrioritySystem.GetPriorityForPawnWorkType(row.Pawn, SubWorkDrilldownState.ActiveWorkType);
                int currentPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(row.Pawn, workGiver.def, fallback);
                target = TargetInfo.ForWorkGiver(
                    row.Pawn,
                    SubWorkDrilldownState.ActiveWorkType,
                    workGiver.def,
                    WorkGiverDisplayNameService.HeaderLabel(workGiver.def, WorkGiverHeaderLabelStyle.Standard),
                    priorityBoxRect,
                    currentPriority);
                return true;
            }

            WorkTypeDef workType = column.Column?.workType;
            if (workType == null)
            {
                return false;
            }

            int priority = WorkPrioritySystem.GetPriorityForPawnWorkType(row.Pawn, workType);
            target = TargetInfo.ForWorkType(
                row.Pawn,
                workType,
                workType.labelShort?.CapitalizeFirst() ?? workType.LabelCap.ToString(),
                priorityBoxRect,
                priority);
            return true;
        }

        private static bool TryFindAgentWorkTypeTarget(IWorkTabLayoutController layout, string requestedWorkType, out TargetInfo target)
        {
            target = default;
            if (layout?.Rows == null || layout.Columns == null)
            {
                return false;
            }

            WorkTypeDef requested = string.IsNullOrEmpty(requestedWorkType)
                ? null
                : DefDatabase<WorkTypeDef>.GetNamedSilentFail(requestedWorkType);

            WorkTabLayoutColumn selectedColumn = default;
            bool foundColumn = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    column.Column.workType == null)
                {
                    continue;
                }

                if (requested == null || column.Column.workType == requested)
                {
                    selectedColumn = column;
                    foundColumn = true;
                    break;
                }
            }

            if (!foundColumn)
            {
                return false;
            }

            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow row = layout.Rows[i];
                if (row.Pawn == null ||
                    row.Pawn.Dead ||
                    row.Pawn.workSettings == null ||
                    !row.Pawn.workSettings.EverWork)
                {
                    continue;
                }

                Rect rowRect = layout.GetScreenRect(row);
                Rect cellRect = new Rect(selectedColumn.HeaderRect.x, rowRect.y, selectedColumn.Width, rowRect.height);
                Rect priorityBoxRect = GetPriorityBoxRect(cellRect);
                WorkTypeDef workType = selectedColumn.Column.workType;
                int priority = WorkPrioritySystem.GetPriorityForPawnWorkType(row.Pawn, workType);
                target = TargetInfo.ForWorkType(
                    row.Pawn,
                    workType,
                    workType.labelShort?.CapitalizeFirst() ?? workType.LabelCap.ToString(),
                    priorityBoxRect,
                    priority);
                return true;
            }

            return false;
        }

        private static bool TryGetGlobalPriorityTarget(IWorkTabLayoutController layout, Vector2 mousePosition, out TargetInfo target)
        {
            target = default;
            if (!SubWorkDrilldownState.IsActive ||
                layout?.Columns == null ||
                layout.Table == null)
            {
                return false;
            }

            Rect globalRowRect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight + HeaderPinnedRowsHeight,
                Mathf.Max(layout.Table.Size.x - 16f, 1f),
                SubWorkDrilldownState.GlobalRowVisibleHeight);
            if (!globalRowRect.Contains(mousePosition))
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    mousePosition.x < column.HeaderRect.xMin ||
                    mousePosition.x > column.HeaderRect.xMax ||
                    !SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out WorkGiver workGiver, out _) ||
                    workGiver?.def == null)
                {
                    continue;
                }

                Rect cellRect = new Rect(column.HeaderRect.x, globalRowRect.y, column.Width, globalRowRect.height);
                Rect priorityBoxRect = GetPriorityBoxRect(cellRect);
                int priority = WorkGiverReassignmentManager.GetWorkGiverPriority(null, workGiver.def, WorkPrioritySystem.GetDefaultEnabledPriority());
                target = TargetInfo.ForWorkGiver(
                    null,
                    SubWorkDrilldownState.ActiveWorkType,
                    workGiver.def,
                    WorkGiverDisplayNameService.HeaderLabel(workGiver.def, WorkGiverHeaderLabelStyle.Standard),
                    priorityBoxRect,
                    priority);
                return true;
            }

            return false;
        }

        private static bool TryGetBodyPriorityColumn(IWorkTabLayoutController layout, Vector2 mousePosition, out WorkTabLayoutColumn column)
        {
            column = default;
            var columns = layout.Columns;
            if (columns == null)
            {
                return false;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                WorkTabLayoutColumn candidate = columns[i];
                if (!(candidate.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                Rect bounds = new Rect(candidate.HeaderRect.x, layout.TableOrigin.y, candidate.Width, layout.Table?.Size.y ?? 0f);
                if (bounds.Contains(mousePosition))
                {
                    column = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveVisibleTarget(IWorkTabLayoutController layout, out WorkTabLayoutColumn column, out List<RowDrawInfo> rows)
        {
            column = default;
            rows = new List<RowDrawInfo>();

            if (_session == null || layout.Columns == null || layout.Rows == null)
            {
                return false;
            }

            bool foundColumn = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn candidate = layout.Columns[i];
                if (!(candidate.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                if (_session.Kind == TimePriorityTargetKind.WorkType)
                {
                    if (!SubWorkDrilldownState.IsActive &&
                        candidate.Column.workType?.defName == _session.WorkTypeDefName)
                    {
                        column = candidate;
                        foundColumn = true;
                        break;
                    }
                }
                else if (SubWorkDrilldownState.IsActive &&
                         SubWorkDrilldownState.ActiveWorkType?.defName == _session.WorkTypeDefName &&
                         SubWorkDrilldownState.TryGetWorkGiverForColumn(candidate.Column, out WorkGiver workGiver, out _) &&
                         workGiver?.def?.defName == _session.TargetDefName)
                {
                    column = candidate;
                    foundColumn = true;
                    break;
                }
            }

            if (!foundColumn)
            {
                return false;
            }

            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow row = layout.Rows[i];
                Pawn pawn = row.Pawn;
                if (pawn == null || !_session.PawnIds.Contains(pawn.thingIDNumber))
                {
                    continue;
                }

                rows.Add(new RowDrawInfo(pawn, layout.GetScreenRect(row)));
            }

            if (_session.IsGlobal)
            {
                Rect globalRowRect = new Rect(
                    layout.TableOrigin.x,
                    layout.TableOrigin.y + layout.HeaderHeight + HeaderPinnedRowsHeight,
                    Mathf.Max(layout.Table?.Size.x ?? 1f, 1f),
                    SubWorkDrilldownState.GlobalRowVisibleHeight);
                rows.Add(new RowDrawInfo(null, globalRowRect));
            }

            return true;
        }

        private static void DrawHighlights(IWorkTabLayoutController layout, WorkTabLayoutColumn column, List<RowDrawInfo> rows, float progress)
        {
            Color columnColor = new Color(0.95f, 0.73f, 0.18f, 0.12f * progress);
            Color boxColor = new Color(1f, 0.85f, 0.2f, 0.92f * progress);

            float yMin = column.HeaderRect.yMax;
            float yMax = yMin;
            for (int i = 0; i < rows.Count; i++)
            {
                yMin = Mathf.Min(yMin, rows[i].RowRect.yMin);
                yMax = Mathf.Max(yMax, rows[i].RowRect.yMax);
            }

            Rect columnRect = new Rect(column.HeaderRect.x, yMin, column.Width, Mathf.Max(0f, yMax - yMin));
            Widgets.DrawBoxSolid(columnRect, columnColor);

            GUI.color = boxColor;
            Widgets.DrawBox(GetHeaderHighlightRect(column), 2);
            for (int i = 0; i < rows.Count; i++)
            {
                Rect cellRect = new Rect(column.HeaderRect.x, rows[i].RowRect.y, column.Width, rows[i].RowRect.height);
                Widgets.DrawBox(GetPriorityBoxRect(cellRect).ExpandedBy(2f), 1);
            }

            Widgets.DrawBox(_session.SourceBoxRect.ExpandedBy(3f), 2);
            GUI.color = Color.white;
        }

        private static Rect GetHeaderHighlightRect(WorkTabLayoutColumn column)
        {
            WorkTypeDef workType = column.Column?.workType;
            if ((BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders) &&
                workType != null &&
                AngledHeaderCache.TryGetBounds(workType, out Rect angledBounds) &&
                angledBounds.width > 1f &&
                angledBounds.height > 1f)
            {
                return angledBounds.ExpandedBy(2f);
            }

            return column.HeaderRect;
        }

        private static void DrawSpreadLine(Rect panelRect, float progress)
        {
            Vector2 start = _session.SourceBoxRect.center;
            Vector2 end = new Vector2(panelRect.xMin + PanelPadding, panelRect.yMin + HeaderHeight - 8f);
            Color color = new Color(1f, 0.86f, 0.22f, 0.8f * progress);
            Widgets.DrawLine(start, Vector2.Lerp(start, end, progress), color, 2f);
        }

        private static void DrawInlineEditor(IWorkTabLayoutController layout, WorkTabLayoutColumn sourceColumn, List<RowDrawInfo> rows, float progress)
        {
            LastCellHits.Clear();
            LastCopyPasteHits.Clear();
            LastScheduleCellDiagnostics.Clear();

            if (!TryGetWorkColumnSpan(layout, out float timelineX, out float timelineWidth))
            {
                return;
            }

            Rect tableRect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y,
                Mathf.Max(layout.Table?.Size.x ?? PawnTableCompat.GetCachedSize(layout.Table).x, 1f),
                Mathf.Max(GetVisualTableHeight(layout), 1f));
            tableRect.width = Mathf.Max(1f, tableRect.width - 16f);

            Rect firstRow = rows[0].RowRect;
            Rect dividerRect;
            if (_session.IsGlobal)
            {
                float pinnedHeight = Mathf.Max(1f, HeaderPinnedRowsHeight);
                dividerRect = new Rect(
                    tableRect.x,
                    layout.TableOrigin.y + layout.HeaderHeight,
                    tableRect.width,
                    pinnedHeight);
            }
            else if (TryGetTransientDividerRect(layout, out Rect resolvedDividerRect))
            {
                dividerRect = resolvedDividerRect;
            }
            else
            {
                dividerRect = new Rect(
                    tableRect.x,
                    Mathf.Max(tableRect.y + 2f, firstRow.yMin - InlineDividerHeight - 2f),
                    tableRect.width,
                    InlineDividerHeight);
            }

            Rect timelineHeaderRect = new Rect(
                timelineX,
                dividerRect.y,
                timelineWidth,
                dividerRect.height);
            Rect timelineHeaderVisibleRect = GetAccordionRect(timelineHeaderRect, progress);

            Rect combined = dividerRect;
            Rect priorityRowsRect = Rect.zero;
            for (int i = 0; i < rows.Count; i++)
            {
                Rect rowTimelineRect = GetInlineTimelineRect(rows[i].RowRect, timelineX, timelineWidth);
                Rect rowPriorityCursorRect = GetInlinePriorityCursorRect(rowTimelineRect);
                priorityRowsRect = i == 0 ? rowPriorityCursorRect : Union(priorityRowsRect, rowPriorityCursorRect);
                combined = Union(combined, rowTimelineRect);
            }

            _lastPanelRect = combined.ExpandedBy(2f);
            TooltipHandler.ClearTooltipsFrom(_lastPanelRect);

            DrawInlineDivider(dividerRect, timelineHeaderRect, timelineHeaderVisibleRect, priorityRowsRect, progress);
            DrawInlineSpreadLine(sourceColumn, rows[0], timelineX, timelineWidth, progress);

            for (int i = 0; i < rows.Count; i++)
            {
                DrawInlinePawnTimelineRow(layout, rows[i], timelineX, timelineWidth, progress);
            }

            ChronosPointerSupport.DrawTimePriorityScheduleCursorOverlay();
        }

        private static bool TryGetTransientDividerRect(IWorkTabLayoutController layout, out Rect dividerRect)
        {
            dividerRect = Rect.zero;
            if (layout?.Rows == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow row = layout.Rows[i];
                if (row.Divider != null && IsTransientDivider(row.Divider))
                {
                    dividerRect = layout.GetScreenRect(row);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetWorkColumnSpan(IWorkTabLayoutController layout, out float x, out float width)
        {
            x = 0f;
            width = 0f;
            if (layout?.Columns == null)
            {
                return false;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                min = Mathf.Min(min, column.HeaderRect.xMin);
                max = Mathf.Max(max, column.HeaderRect.xMax);
            }

            if (min == float.MaxValue || max <= min)
            {
                return false;
            }

            if (SubWorkDrilldownState.IsActive && layout.Table != null)
            {
                float tableRight = layout.TableOrigin.x + Mathf.Max(layout.Table.Size.x, PawnTableCompat.GetCachedSize(layout.Table).x) - 16f;
                if (tableRight > min)
                {
                    max = Mathf.Max(max, tableRight);
                }
            }

            x = min;
            width = max - min;
            return true;
        }

        private static void DrawInlineDivider(
            Rect dividerRect,
            Rect timelineRect,
            Rect visibleTimelineRect,
            Rect priorityRowsRect,
            float progress)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            GUI.color = new Color(0.05f, 0.055f, 0.05f, 0.34f * progress);
            Widgets.DrawBoxSolid(visibleTimelineRect, GUI.color);
            GUI.color = new Color(1f, 1f, 1f, 0.18f * progress);
            Widgets.DrawLineHorizontal(visibleTimelineRect.xMin, visibleTimelineRect.yMax - 1f, visibleTimelineRect.width);

            Rect chronosRect = GetInlineChronosRect(timelineRect);
            Rect hourLabelRect = GetInlineHourLabelRect(timelineRect);
            if (BetterWorkTabMod.Settings?.showTimePriorityHourDivider ??
                DefaultSettings.showTimePriorityHourDivider)
            {
                GUI.color = new Color(0.95f, 0.85f, 0.55f, 0.38f * progress);
                Widgets.DrawLineHorizontal(visibleTimelineRect.xMin, hourLabelRect.yMin - 1f, visibleTimelineRect.width);
            }

            ChronosPointerSupport.TryDrawTimePriorityTimeline(
                chronosRect,
                priorityRowsRect,
                progress,
                BetterWorkTabMod.Settings?.chronosPointerTimePriorityIncidentOverlay ??
                DefaultSettings.chronosPointerTimePriorityIncidentOverlay);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;

            _lastCloseRect = Rect.zero;
            DrawInlineHourLabels(hourLabelRect, visibleTimelineRect, progress);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static void DrawInlineDaylightBand(Rect timelineRect, float progress)
        {
            Rect bandRect = new Rect(timelineRect.x, timelineRect.yMax - 7f, timelineRect.width, 5f);
            float hourWidth = bandRect.width / HoursPerDay;
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                Color color = hour >= 7 && hour <= 18
                    ? new Color(0.95f, 0.72f, 0.22f, 0.72f * progress)
                    : hour == 6 || hour == 19 || hour == 20
                        ? new Color(0.48f, 0.43f, 0.64f, 0.62f * progress)
                        : new Color(0.14f, 0.18f, 0.30f, 0.70f * progress);
                Widgets.DrawBoxSolid(new Rect(bandRect.x + hour * hourWidth, bandRect.y, Mathf.Max(1f, hourWidth - 0.5f), bandRect.height), color);
            }
        }

        private static Rect GetInlineChronosRect(Rect timelineRect)
        {
            return new Rect(
                timelineRect.x,
                timelineRect.y + 2f,
                timelineRect.width,
                InlineChronosHeight);
        }

        private static Rect GetInlineHourLabelRect(Rect timelineRect)
        {
            return new Rect(
                timelineRect.x,
                timelineRect.yMax - InlineHourLabelHeight - 2f,
                timelineRect.width,
                InlineHourLabelHeight);
        }

        private static void DrawInlineHourLabels(Rect hourLabelRect, Rect visibleTimelineRect, float progress)
        {
            float hourWidth = hourLabelRect.width / HoursPerDay;

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.72f * progress);

            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                Rect labelRect = new Rect(hourLabelRect.x + hour * hourWidth, hourLabelRect.y, hourWidth, hourLabelRect.height);
                if (!visibleTimelineRect.Overlaps(labelRect))
                {
                    continue;
                }

                DrawHourScaleLabel(labelRect, hour.ToString(), progress);
            }
        }

        private static void DrawHourScaleLabel(Rect rect, string label, float progress)
        {
            Color oldColor = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.78f * progress);
            Widgets.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label);

            GUI.color = new Color(1f, 1f, 1f, 0.94f * progress);
            Widgets.Label(rect, label);
            Widgets.Label(new Rect(rect.x + 0.7f, rect.y, rect.width, rect.height), label);

            GUI.color = oldColor;
        }

        private static void DrawInlineSpreadLine(WorkTabLayoutColumn sourceColumn, RowDrawInfo firstRow, float timelineX, float timelineWidth, float progress)
        {
            Rect timelineRect = GetInlineTimelineRect(firstRow.RowRect, timelineX, timelineWidth);
            Vector2 start = _session.SourceBoxRect.center;
            Vector2 end = new Vector2(Mathf.Clamp(sourceColumn.HeaderRect.center.x, timelineRect.xMin, timelineRect.xMax), timelineRect.center.y);
            Widgets.DrawLine(start, Vector2.Lerp(start, end, progress), new Color(1f, 0.86f, 0.22f, 0.78f * progress), 2f);
        }

        private static void DrawInlinePawnTimelineRow(IWorkTabLayoutController layout, RowDrawInfo row, float timelineX, float timelineWidth, float progress)
        {
            Rect rowRect = row.RowRect;
            Color oldColor = GUI.color;

            Rect timelineRect = GetInlineTimelineRect(rowRect, timelineX, timelineWidth);
            Rect visibleTimelineRect = GetAccordionRect(timelineRect, progress);
            Widgets.DrawBoxSolid(visibleTimelineRect, new Color(0.035f, 0.045f, 0.05f, 0.95f * progress));
            int currentPriority = GetFallbackPriority(row.Pawn);
            TimePriorityTarget target = _session.GetTargetForPawn(row.Pawn);
            if (row.Pawn == null)
            {
                DrawInlineCopyPasteControls(row.RowRect, timelineX, target, currentPriority, GetScheduleLabel(row.Pawn), progress);
            }

            DrawInlineTimelineCells(row.Pawn, target, currentPriority, timelineRect, visibleTimelineRect, progress);

            GUI.color = oldColor;
        }

        private static string GetScheduleLabel(Pawn pawn)
        {
            string target = _session?.TargetLabel ?? "work";
            return pawn == null ? target + " global" : pawn.LabelShortCap + " " + target;
        }

        private static void DrawInlineCopyPasteControls(
            Rect rowRect,
            float timelineX,
            TimePriorityTarget target,
            int fallbackPriority,
            string label,
            float progress)
        {
            if (!ShowCopyPasteButtons)
            {
                return;
            }

            Rect controlsRect = new Rect(
                Mathf.Max(rowRect.xMin + 2f, timelineX - CopyPasteUI.CopyPasteColumnWidth),
                rowRect.y + (rowRect.height - 30f) / 2f,
                CopyPasteUI.CopyPasteColumnWidth,
                30f);

            var hit = new CopyPasteHit(target, fallbackPriority, label, controlsRect);
            LastCopyPasteHits.Add(hit);
            DrawScheduleCopyPasteButtons(hit, progress);
        }

        private static void DrawScheduleCopyPasteButtons(CopyPasteHit hit, float progress)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(progress));
            CopyPasteUI.DoCopyPasteButtons(
                hit.Rect,
                () => CopySchedule(hit),
                TimePriorityScheduleClipboard.HasSnapshot
                    ? (Action)(() => PasteSchedule(hit))
                    : null);
            GUI.color = oldColor;
        }

        private static bool ShowCopyPasteButtons =>
            BetterWorkTabMod.Settings?.showTimePriorityCopyPasteButtons ??
            DefaultSettings.showTimePriorityCopyPasteButtons;

        private static Rect GetAccordionRect(Rect fullRect, float progress)
        {
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            float sourceX = Mathf.Clamp(GetTimelineAnimationSource().center.x, fullRect.xMin, fullRect.xMax);
            float xMin = Mathf.Lerp(sourceX, fullRect.xMin, eased);
            float xMax = Mathf.Lerp(sourceX, fullRect.xMax, eased);
            if (xMax < xMin)
            {
                float swap = xMin;
                xMin = xMax;
                xMax = swap;
            }

            return Rect.MinMaxRect(xMin, fullRect.yMin, xMax, fullRect.yMax);
        }

        private static Rect GetInlineTimelineRect(Rect rowRect, float timelineX, float timelineWidth)
        {
            float height = Mathf.Max(1f, rowRect.height - InlineTimelineVerticalInset);
            return new Rect(
                timelineX,
                rowRect.yMin + (rowRect.height - height) / 2f,
                timelineWidth,
                height);
        }

        private static Rect GetInlinePriorityCursorRect(Rect timelineRect)
        {
            float inset = timelineRect.height > 4f ? 2f : 0f;
            return new Rect(
                timelineRect.x,
                timelineRect.y + inset,
                timelineRect.width,
                Mathf.Max(1f, timelineRect.height - inset * 2f));
        }

        private static void DrawInlineTimelineCells(
            Pawn pawn,
            TimePriorityTarget target,
            int currentPriority,
            Rect timelineRect,
            Rect visibleTimelineRect,
            float progress)
        {
            int[] priorities = TimePriorityService.GetPrioritiesForDisplay(target, currentPriority);
            float hourWidth = timelineRect.width / HoursPerDay;
            float sourceX = GetTimelineAnimationSource().center.x;
            float sourceNorm = Mathf.Clamp01((sourceX - timelineRect.xMin) / Mathf.Max(1f, timelineRect.width));

            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                Rect hourRect = new Rect(
                    timelineRect.x + hour * hourWidth + 0.5f,
                    timelineRect.y,
                    Mathf.Max(1f, hourWidth - 1f),
                    timelineRect.height);
                if (!visibleTimelineRect.Overlaps(hourRect))
                {
                    continue;
                }

                float hourNorm = (hour + 0.5f) / HoursPerDay;
                float distance = Mathf.Abs(hourNorm - sourceNorm);
                float waveProgress = Mathf.Clamp01((progress - distance * 0.55f) / 0.45f);
                float cellProgress = Mathf.SmoothStep(0f, 1f, waveProgress);
                int priority = priorities[hour];
                if (TimePriorityScheduleTransferFeedback.TryGetDisplayPriority(target, hour, priority, out int displayPriority))
                {
                    priority = displayPriority;
                }

                bool isCustomHour = TimePriorityService.IsCustomScheduledHour(target, hour, currentPriority);
                DrawSchedulePriorityCell(hourRect, target, priority, hour, cellProgress, isCustomHour);
                LastCellHits.Add(new CellHit(target, currentPriority, hour, hourRect));
            }
        }

        private static Rect GetTimelineAnimationSource()
        {
            return _isClosing && _closingSourceRect != Rect.zero
                ? _closingSourceRect
                : _session.SourceBoxRect;
        }

        private static void DrawSchedulePriorityCell(Rect rect, TimePriorityTarget target, int priority, int hour, float progress, bool isCustomHour)
        {
            if (progress <= 0.001f)
            {
                return;
            }

            Rect drawRect = rect.ContractedBy(1f);
            float pulseAlpha = 0f;
            if (TimePriorityScheduleTransferFeedback.TryGetCellPulse(target, hour, out float pulseScale, out pulseAlpha))
            {
                drawRect = ScaleRect(drawRect, pulseScale);
            }

            DrawTimePriorityBox(drawRect, priority, progress);
            if (isCustomHour)
            {
                PriorityOverrideRing.DrawGoldBorder(drawRect.ExpandedBy(1f), Mouse.IsOver(drawRect), Mouse.IsOver(drawRect) ? 3 : 2);
            }

            if (Event.current.type == EventType.Repaint)
            {
                LastScheduleCellDiagnostics.Add(new ScheduleCellDiagnostic(
                    target.Key,
                    hour,
                    priority,
                    rect,
                    drawRect,
                    GetTimePriorityLabelRect(drawRect)));
            }

            if (pulseAlpha > 0f)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, pulseAlpha);
                Widgets.DrawBox(drawRect.ExpandedBy(1f), 2);
                GUI.color = oldColor;
            }

            if (Mouse.IsOver(drawRect))
            {
                Widgets.DrawBox(drawRect, 2);
                TooltipHandler.TipRegion(
                    drawRect,
                    "Hour " + hour + ": priority " +
                    (priority <= WorkPrioritySystem.DisabledPriority ? "disabled" : priority.ToString()));
            }
        }

        private static Rect ScaleRect(Rect rect, float scale)
        {
            Vector2 center = rect.center;
            float width = rect.width * scale;
            float height = rect.height * scale;
            return new Rect(center.x - width / 2f, center.y - height / 2f, width, height);
        }

        private static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void DrawPanel(Rect rect, List<RowDrawInfo> rows, float progress)
        {
            LastCellHits.Clear();
            LastScheduleCellDiagnostics.Clear();
            _lastPanelRect = rect;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            GUI.color = new Color(0.06f, 0.075f, 0.08f, 0.96f * progress);
            Widgets.DrawBoxSolid(rect, GUI.color);
            GUI.color = new Color(0.95f, 0.73f, 0.18f, 0.82f * progress);
            Widgets.DrawBox(rect, 1);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = new Color(1f, 1f, 1f, 0.92f * progress);
            Rect titleRect = new Rect(rect.x + PanelPadding, rect.y + 3f, rect.width - 42f, 24f);
            Widgets.Label(titleRect, _session.TargetLabel + " time priorities");

            _lastCloseRect = new Rect(rect.xMax - 28f, rect.y + 4f, 22f, 22f);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.35f, 0.28f, progress);
            Widgets.Label(_lastCloseRect, "X");

            DrawDaylightBand(rect, progress);
            DrawHourLabels(rect, progress);

            for (int i = 0; i < rows.Count; i++)
            {
                DrawPawnTimelineRow(rect, rows[i], i, progress);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static void DrawDaylightBand(Rect panelRect, float progress)
        {
            Rect bandRect = GetTimelineRect(panelRect);
            bandRect.y = panelRect.y + 25f;
            bandRect.height = 5f;
            float hourWidth = bandRect.width / HoursPerDay;
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                Color color = hour >= 7 && hour <= 18
                    ? new Color(0.95f, 0.72f, 0.22f, 0.72f * progress)
                    : hour == 6 || hour == 19 || hour == 20
                        ? new Color(0.48f, 0.43f, 0.64f, 0.62f * progress)
                        : new Color(0.14f, 0.18f, 0.30f, 0.70f * progress);
                Widgets.DrawBoxSolid(new Rect(bandRect.x + hour * hourWidth, bandRect.y, hourWidth, bandRect.height), color);
            }
        }

        private static void DrawHourLabels(Rect panelRect, float progress)
        {
            Rect timelineRect = GetTimelineRect(panelRect);
            float hourWidth = timelineRect.width / HoursPerDay;

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.66f * progress);

            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                Rect labelRect = new Rect(timelineRect.x + hour * hourWidth, panelRect.y + 31f, hourWidth, 14f);
                DrawHourScaleLabel(labelRect, hour.ToString(), progress);
            }
        }

        private static void DrawPawnTimelineRow(Rect panelRect, RowDrawInfo row, int rowIndex, float progress)
        {
            Rect rowRect = new Rect(
                panelRect.x + PanelPadding,
                panelRect.y + HeaderHeight + rowIndex * TimelineRowHeight,
                panelRect.width - PanelPadding * 2f,
                TimelineRowHeight);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.86f * progress);
            Widgets.Label(new Rect(rowRect.x, rowRect.y + 2f, PawnLabelWidth - 6f, rowRect.height), row.Pawn.LabelShortCap);

            Rect timelineRect = GetTimelineRect(panelRect);
            timelineRect.y = rowRect.y + 3f;
            timelineRect.height = 22f;

            int currentPriority = GetFallbackPriority(row.Pawn);
            TimePriorityTarget target = _session.GetTargetForPawn(row.Pawn);
            int[] priorities = TimePriorityService.GetPrioritiesForDisplay(target, currentPriority);
            float hourWidth = timelineRect.width / HoursPerDay;

            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                Rect hourRect = new Rect(
                    timelineRect.x + hour * hourWidth + 0.5f,
                    timelineRect.y,
                Mathf.Max(1f, hourWidth - 1f),
                timelineRect.height);
                bool isCustomHour = TimePriorityService.IsCustomScheduledHour(target, hour, currentPriority);
                DrawHourPriorityCell(hourRect, priorities[hour], progress, isCustomHour);
                LastCellHits.Add(new CellHit(target, currentPriority, hour, hourRect));
            }
        }

        private static void DrawHourPriorityCell(Rect rect, int priority, float progress, bool isCustomHour)
        {
            Rect boxRect = rect.ContractedBy(1f);
            DrawTimePriorityBox(boxRect, priority, progress);
            if (isCustomHour)
            {
                PriorityOverrideRing.DrawGoldBorder(boxRect.ExpandedBy(1f), Mouse.IsOver(boxRect), Mouse.IsOver(boxRect) ? 3 : 2);
            }
        }

        private static void DrawTimePriorityBox(Rect rect, int priority, float progress)
        {
            if (progress <= 0.001f)
            {
                return;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            Texture2D bgTex = priority <= WorkPrioritySystem.DisabledPriority
                ? WidgetsWork.WorkBoxBGTex_Bad
                : WidgetsWork.WorkBoxBGTex_Mid;

            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(progress));
            GUI.DrawTexture(rect, bgTex);

            GUI.color = new Color(1f, 1f, 1f, 0.18f * progress);
            Widgets.DrawBox(rect, 1);

            string label = priority <= WorkPrioritySystem.DisabledPriority ? "X" : priority.ToString();
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = rect.width >= 20f && rect.height >= 20f ? GameFont.Medium : GameFont.Tiny;
            Text.WordWrap = false;

            Rect labelRect = GetTimePriorityLabelRect(rect);
            GUI.color = new Color(0f, 0f, 0f, 0.7f * progress);
            Widgets.Label(new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height), label);

            Color labelColor = WorkPrioritySystem.GetPriorityColor(priority);
            GUI.color = new Color(labelColor.r, labelColor.g, labelColor.b, 0.98f * progress);
            Widgets.Label(labelRect, label);
            Widgets.Label(new Rect(labelRect.x + 0.5f, labelRect.y, labelRect.width, labelRect.height), label);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static Rect GetTimePriorityLabelRect(Rect rect)
        {
            return rect.width >= 20f && rect.height >= 20f
                ? rect.ContractedBy(-3f)
                : rect.ContractedBy(-1f);
        }

        private static Rect CalculatePanelRect(IWorkTabLayoutController layout)
        {
            float tableLeft = layout.TableOrigin.x + 8f;
            float tableRight = layout.TableOrigin.x + Mathf.Max(layout.Table?.Size.x ?? 0f, 1f) - 18f;
            float availableWidth = Mathf.Max(MinPanelWidth, tableRight - tableLeft - 8f);
            float width = Mathf.Min(MaxPanelWidth, availableWidth);
            if (width > tableRight - tableLeft)
            {
                width = Mathf.Max(1f, tableRight - tableLeft);
            }

            float x = Mathf.Clamp(_session.SourceBoxRect.xMin - 42f, tableLeft, tableRight - width);
            float height = HeaderHeight + Mathf.Max(1, _session.PawnIds.Count) * TimelineRowHeight + PanelPadding;
            float tableBottom = layout.TableOrigin.y + Mathf.Max(GetVisualTableHeight(layout), 1f) - 8f;
            float tableTop = layout.TableOrigin.y +
                layout.HeaderHeight +
                HeaderPinnedRowsHeight +
                SubWorkDrilldownState.GlobalRowVisibleHeight;
            float y = _session.SourceBoxRect.yMax + 6f;
            if (y + height > tableBottom)
            {
                y = _session.SourceBoxRect.yMin - height - 6f;
            }

            y = Mathf.Clamp(y, tableTop + 2f, Mathf.Max(tableTop + 2f, tableBottom - height));
            return new Rect(x, y, width, height);
        }

        private static float GetVisualTableHeight(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                return 0f;
            }

            return layout.HeaderHeight +
                HeaderPinnedRowsHeight +
                SubWorkDrilldownState.GlobalRowVisibleHeight +
                layout.ContentHeight;
        }

        private static Rect GetTimelineRect(Rect panelRect)
        {
            return new Rect(
                panelRect.x + PanelPadding + PawnLabelWidth,
                panelRect.y,
                Mathf.Max(1f, panelRect.width - PanelPadding * 2f - PawnLabelWidth),
                panelRect.height);
        }

        private static Rect GetPriorityBoxRect(Rect cellRect)
        {
            return WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
        }

        private static int GetFallbackPriority(Pawn pawn)
        {
            if (_session.Kind == TimePriorityTargetKind.WorkType)
            {
                WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(_session.WorkTypeDefName);
                if (pawn == null)
                {
                    return WorkPrioritySystem.GetDefaultEnabledPriority();
                }

                return WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            }

            WorkTypeDef parent = DefDatabase<WorkTypeDef>.GetNamedSilentFail(_session.WorkTypeDefName);
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(_session.TargetDefName);
            if (pawn == null)
            {
                return WorkGiverReassignmentManager.GetWorkGiverPriority(null, giver, WorkPrioritySystem.GetDefaultEnabledPriority());
            }

            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, parent);
            return WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, giver, parentPriority);
        }

        private static float GetProgress()
        {
            float age = Time.realtimeSinceStartup - (_isClosing ? _closingStartedAt : _session.StartedAt);
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / AnimationSeconds));
            return _isClosing ? 1f - progress : progress;
        }

        private static Rect LerpRect(Rect a, Rect b, float t)
        {
            return new Rect(
                Mathf.Lerp(a.x, b.x, t),
                Mathf.Lerp(a.y, b.y, t),
                Mathf.Lerp(a.width, b.width, t),
                Mathf.Lerp(a.height, b.height, t));
        }

        private static string FormatRect(Rect rect)
        {
            return "(" + Format(rect.x) + "," + Format(rect.y) + "," + Format(rect.width) + "," + Format(rect.height) + ")";
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool IsControlHeld(Event evt)
        {
            return evt.control ||
                   (evt.modifiers & EventModifiers.Control) != 0 ||
                   Input.GetKey(KeyCode.LeftControl) ||
                   Input.GetKey(KeyCode.RightControl);
        }

        private readonly struct TargetInfo
        {
            public readonly Pawn Pawn;
            public readonly TimePriorityTarget TimeTarget;
            public readonly int PawnId;
            public readonly TimePriorityTargetKind Kind;
            public readonly string WorkTypeDefName;
            public readonly string TargetDefName;
            public readonly string TargetLabel;
            public readonly Rect PriorityBoxRect;
            public readonly int CurrentPriority;

            public TargetInfo(Pawn pawn, TimePriorityTarget timeTarget, Rect priorityBoxRect, int currentPriority)
            {
                Pawn = pawn;
                TimeTarget = timeTarget;
                PawnId = timeTarget.PawnId;
                Kind = timeTarget.Kind;
                WorkTypeDefName = timeTarget.WorkTypeDefName;
                TargetDefName = timeTarget.TargetDefName;
                TargetLabel = timeTarget.Label;
                PriorityBoxRect = priorityBoxRect;
                CurrentPriority = currentPriority;
            }

            public static TargetInfo ForWorkType(Pawn pawn, WorkTypeDef workType, string label, Rect priorityBoxRect, int currentPriority)
            {
                return new TargetInfo(
                    pawn,
                    TimePriorityTarget.ForWorkType(pawn, workType, label),
                    priorityBoxRect,
                    currentPriority);
            }

            public static TargetInfo ForWorkGiver(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, string label, Rect priorityBoxRect, int currentPriority)
            {
                return new TargetInfo(
                    pawn,
                    TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver, label),
                    priorityBoxRect,
                    currentPriority);
            }
        }

        private sealed class Session
        {
            public readonly TimePriorityTargetKind Kind;
            public readonly string WorkTypeDefName;
            public readonly string TargetDefName;
            public readonly string TargetLabel;
            public readonly List<int> PawnIds = new List<int>();
            public Rect SourceBoxRect;
            public float StartedAt;

            public Session(TargetInfo target)
            {
                Kind = target.Kind;
                WorkTypeDefName = target.WorkTypeDefName;
                TargetDefName = target.TargetDefName;
                TargetLabel = target.TargetLabel;
                SourceBoxRect = target.PriorityBoxRect;
                StartedAt = Time.realtimeSinceStartup;
                PawnIds.Add(target.PawnId);
                TimePriorityService.GetPrioritiesForDisplay(target.TimeTarget, target.CurrentPriority);
            }

            public bool IsGlobal => PawnIds.Count == 1 && PawnIds[0] == TimePriorityTarget.GlobalPawnId;

            public bool Matches(TargetInfo target)
            {
                return Kind == target.Kind &&
                       string.Equals(WorkTypeDefName, target.WorkTypeDefName, StringComparison.Ordinal) &&
                       string.Equals(TargetDefName, target.TargetDefName, StringComparison.Ordinal);
            }

            public bool Matches(TimePriorityTarget target)
            {
                return Kind == target.Kind &&
                       string.Equals(WorkTypeDefName, target.WorkTypeDefName, StringComparison.Ordinal) &&
                       string.Equals(TargetDefName, target.TargetDefName, StringComparison.Ordinal);
            }

            public TimePriorityTarget GetTargetForPawn(Pawn pawn)
            {
                WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(WorkTypeDefName);
                if (Kind == TimePriorityTargetKind.WorkType)
                {
                    return TimePriorityTarget.ForWorkType(pawn, workType, TargetLabel);
                }

                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(TargetDefName);
                return TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver, TargetLabel);
            }
        }

        private readonly struct RowDrawInfo
        {
            public readonly Pawn Pawn;
            public readonly Rect RowRect;

            public RowDrawInfo(Pawn pawn, Rect rowRect)
            {
                Pawn = pawn;
                RowRect = rowRect;
            }
        }

        private readonly struct CellHit
        {
            public readonly TimePriorityTarget Target;
            public readonly int FallbackPriority;
            public readonly int Hour;
            public readonly Rect Rect;

            public CellHit(TimePriorityTarget target, int fallbackPriority, int hour, Rect rect)
            {
                Target = target;
                FallbackPriority = fallbackPriority;
                Hour = hour;
                Rect = rect;
            }
        }

        private readonly struct ScheduleCellDiagnostic
        {
            public readonly string TargetKey;
            public readonly int Hour;
            public readonly int Priority;
            public readonly Rect CellRect;
            public readonly Rect BoxRect;
            public readonly Rect LabelRect;

            public ScheduleCellDiagnostic(
                string targetKey,
                int hour,
                int priority,
                Rect cellRect,
                Rect boxRect,
                Rect labelRect)
            {
                TargetKey = targetKey;
                Hour = hour;
                Priority = priority;
                CellRect = cellRect;
                BoxRect = boxRect;
                LabelRect = labelRect;
            }
        }

        private readonly struct CopyPasteHit
        {
            public readonly TimePriorityTarget Target;
            public readonly int FallbackPriority;
            public readonly string Label;
            public readonly Rect Rect;

            public CopyPasteHit(TimePriorityTarget target, int fallbackPriority, string label, Rect rect)
            {
                Target = target;
                FallbackPriority = fallbackPriority;
                Label = label;
                Rect = rect;
            }

            public Rect CopyRect => new Rect(Rect.x, Rect.y + Rect.height / 2f - 12f, CopyPasteUI.CopyPasteIconWidth, CopyPasteUI.CopyPasteIconHeight);

            public Rect PasteRect => new Rect(CopyRect.xMax, CopyRect.y, CopyPasteUI.CopyPasteIconWidth, CopyPasteUI.CopyPasteIconHeight);
        }
    }
}
