using System;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Interaction;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Owns the pending sub-work gesture, its target resolution, and the enter/exit transitions.
    /// </summary>
    internal sealed class SubWorkInteractionController
    {
        private readonly WorkTabBodyRenderer _bodyRenderer;
        private readonly WorkTabPriorityInputHandler _priorityInputHandler;

        private bool _pendingSubWorkGesture;
        private Vector2 _pendingSubWorkStart;
        private Rect _pendingSubWorkBounds;
        private WorkTypeDef _pendingSubWorkOpenType;
        private int _pendingSubWorkButton;
        private bool _pendingSubWorkExit;
        private bool _pendingSubWorkRestoreCursor;
        private bool _pendingSubWorkCtrlClickDiscovery;
        private int _suppressSubWorkPriorityMouseDownFrame = -1;

        internal SubWorkInteractionController(
            WorkTabBodyRenderer bodyRenderer,
            WorkTabPriorityInputHandler priorityInputHandler)
        {
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
            _priorityInputHandler = priorityInputHandler ??
                throw new ArgumentNullException(nameof(priorityInputHandler));
        }

        internal void ResetForWindowClose()
        {
            ClearPendingSubWorkGesture();
            _suppressSubWorkPriorityMouseDownFrame = -1;
        }

        internal bool TryHandleSubWorkHeaderOpen(IWorkTabLayoutController layout)
        {
            if (layout == null || SubWorkDrilldownState.IsActive)
            {
                return false;
            }

            // Ctrl is shared by header reordering and sub-work gestures. Once a real column
            // drag has crossed its threshold it owns the gesture, even if the pointer later
            // returns inside the original header before MouseUp.
            if (PawnOrganizerSystem.Instance?.IsDraggingColumn == true)
            {
                ClearPendingSubWorkGesture();
                return false;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown)
            {
                bool matchesConfiguredGesture = SubWorkDrilldownInput.MatchesGesture(evt);
                bool isCtrlClickDiscovery = SubWorkDrilldownInput.ShouldOfferCtrlLeftDiscovery(evt);
                if (!matchesConfiguredGesture && !isCtrlClickDiscovery)
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                if (!TryGetSubWorkOpenTarget(
                        layout,
                        evt.mousePosition,
                        out var workType,
                        out var bounds,
                        out bool fromHeader,
                        out WorkTabLayoutColumn targetColumn))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                if (!matchesConfiguredGesture && !fromHeader)
                {
                    // Ctrl-left is a one-time discovery path for real Work headers,
                    // not an unconditional alternate shortcut for every affordance.
                    ClearPendingSubWorkGesture();
                    return false;
                }

                isCtrlClickDiscovery = isCtrlClickDiscovery && fromHeader;
                if (fromHeader)
                {
                    // The central router owns this gesture before header rendering runs. Clear
                    // the exact header's pending sort click, including the one-time Ctrl-left
                    // discovery path that is not the configured shortcut.
                    AngledHeaderInteraction.ClearPendingHeaderClick(targetColumn.Column);
                }
                Vector2? returnMousePosition = fromHeader
                    ? GuiMousePosition.ToRootUiPosition(evt.mousePosition)
                    : (Vector2?)null;

                BeginPendingSubWorkGesture(
                    start: evt.mousePosition,
                    bounds: bounds,
                    button: evt.button,
                    openType: workType,
                    exit: false,
                    restoreCursor: returnMousePosition.HasValue,
                    ctrlClickDiscovery: isCtrlClickDiscovery);
                MarkSubWorkPriorityMouseDownForSuppression();
                return false;
            }

            if (!_pendingSubWorkGesture || _pendingSubWorkExit)
            {
                return false;
            }

            if (evt.type == EventType.MouseDrag)
            {
                CancelPendingSubWorkIfDragged(evt.mousePosition);
                return false;
            }

            if (evt.type != EventType.MouseUp || evt.button != _pendingSubWorkButton)
            {
                return false;
            }

            bool shouldOpen = IsPendingSubWorkClick(evt.mousePosition) &&
                _pendingSubWorkOpenType != null &&
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_pendingSubWorkOpenType).Count > 0;

            Vector2? storedReturnPosition = _pendingSubWorkRestoreCursor
                ? GuiMousePosition.ToRootUiPosition(_pendingSubWorkStart)
                : (Vector2?)null;
            WorkTypeDef openType = _pendingSubWorkOpenType;
            Rect openBounds = _pendingSubWorkBounds;
            bool ctrlClickDiscovery = _pendingSubWorkCtrlClickDiscovery;
            ClearPendingSubWorkGesture();
            PawnOrganizerSystem.Instance?.CancelPendingDrag();

            if (!shouldOpen)
            {
                return false;
            }

            if (FluffyWorkTabGateway.TryStartSubWorkDrilldownStyleChooser(layout, openType, openBounds))
            {
                evt.Use();
                return true;
            }

            return CompleteSubWorkOpen(
                layout,
                openType,
                storedReturnPosition,
                ctrlClickDiscovery,
                evt);
        }

        internal bool TryHandleSubWorkBadgeClick(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                return false;
            }

            Event evt = Event.current;
            if (evt == null || evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            if (SubWorkDrilldownState.IsActive)
            {
                if (SubWorkHeaderAffordance.TryGetFocusedBadgeTarget(
                        layout,
                        out WorkTabLayoutColumn focusedBadgeColumn,
                        out Rect focusedBadgeRect) &&
                    focusedBadgeRect.Contains(evt.mousePosition))
                {
                    AngledHeaderInteraction.ClearPendingHeaderClick(focusedBadgeColumn.Column);
                    TryExitSubWorkMode(restoreMousePosition: false);
                    evt.Use();
                    return true;
                }

                if (!SubWorkHeaderAffordance.TryGetBackLabelCellRect(layout, out Rect labelCellRect) ||
                    !labelCellRect.Contains(evt.mousePosition))
                {
                    return false;
                }

                TryExitSubWorkMode(restoreMousePosition: false);
                evt.Use();
                return true;
            }

            if (!SubWorkHeaderAffordance.TryGetOpenBadgeTarget(
                    layout,
                    evt.mousePosition,
                    out var workType,
                    out var badgeRect,
                    out WorkTabLayoutColumn targetColumn))
            {
                return false;
            }

            AngledHeaderInteraction.ClearPendingHeaderClick(targetColumn.Column);
            PawnOrganizerSystem.Instance?.CancelPendingDrag();
            MarkSubWorkCtrlClickNoticeDismissed();
            if (FluffyWorkTabGateway.TryStartSubWorkDrilldownStyleChooser(layout, workType, badgeRect))
            {
                evt.Use();
                return true;
            }

            return CompleteSubWorkOpen(
                layout,
                workType,
                GuiMousePosition.ToRootUiPosition(evt.mousePosition),
                ctrlClickDiscovery: false,
                evt: evt);
        }

        internal bool TryHandleSubWorkExitGesture(IWorkTabLayoutController layout)
        {
            if (!SubWorkDrilldownState.HasAnyDrilldown)
            {
                return false;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            // A Ctrl-drag of a BWT sub-work header must reorder the child column. It must
            // never be reinterpreted as the Ctrl-click exit gesture on the final MouseUp.
            if (PawnOrganizerSystem.Instance?.IsDraggingColumn == true)
            {
                ClearPendingSubWorkGesture();
                return false;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                TryExitSubWorkMode(restoreMousePosition: false);
                evt.Use();
                return true;
            }

            if (!SubWorkDrilldownState.IsActive)
            {
                return false;
            }

            if (layout == null)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown)
            {
                if (!SubWorkDrilldownInput.MatchesGesture(evt))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                if (!TryGetSubWorkExitTarget(
                        layout,
                        evt.mousePosition,
                        out var bounds,
                        out bool shouldRestoreCursor))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                BeginPendingSubWorkGesture(
                    start: evt.mousePosition,
                    bounds: bounds,
                    button: evt.button,
                    openType: null,
                    exit: true,
                    restoreCursor: shouldRestoreCursor);
                MarkSubWorkPriorityMouseDownForSuppression();
                return false;
            }

            if (!_pendingSubWorkGesture || !_pendingSubWorkExit)
            {
                return false;
            }

            if (evt.type == EventType.MouseDrag)
            {
                CancelPendingSubWorkIfDragged(evt.mousePosition);
                return false;
            }

            if (evt.type != EventType.MouseUp || evt.button != _pendingSubWorkButton)
            {
                return false;
            }

            bool shouldExit = IsPendingSubWorkClick(evt.mousePosition);
            bool pendingRestoreCursor = _pendingSubWorkRestoreCursor;
            if (!shouldExit)
            {
                ClearPendingSubWorkGesture();
                PawnOrganizerSystem.Instance?.CancelPendingDrag();
                return false;
            }

            bool exited = TryExitSubWorkMode(restoreMousePosition: pendingRestoreCursor);
            if (!exited)
            {
                return false;
            }

            evt.Use();
            return true;
        }

        internal bool TryExitSubWorkMode(bool restoreMousePosition)
        {
            ClearPendingSubWorkGesture();
            PawnOrganizerSystem.Instance?.CancelPendingDrag();

            if (SubWorkDrilldownState.IsActive)
            {
                SubWorkDrilldownBarRenderer.ExitDrilldown(restoreMousePosition: restoreMousePosition);
                return true;
            }

            if (!SubWorkDrilldownState.IsExpandBesideActive)
            {
                return false;
            }

            SubWorkDrilldownState.CollapseAllExpandBeside();
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Columns |
                WorkTabDirtyFlags.HeaderGeometry);
            return true;
        }

        internal void ApplyStyleChooserSelection(
            IWorkTabLayoutController layout,
            WorkTypeDef workType,
            BetterWorkTabSettings.SubWorkDrilldownStyle style,
            int sourceWorkColumnSlot)
        {
            if (workType == null || style == BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen)
            {
                return;
            }

            TimePriorityScheduleEditor.CloseForWorkModeTransition();
            if (style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                if (!SubWorkDrilldownState.IsExpandBesideExpanded(workType))
                {
                    EnterFluffyHostedSubWork(workType);
                }
            }
            else
            {
                if (!SubWorkDrilldownState.IsActive || SubWorkDrilldownState.ActiveWorkType != workType)
                {
                    SubWorkDrilldownState.EnterFromSourceSlot(
                        workType,
                        null,
                        SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout?.Table, layout?.HeaderHeight ?? -1f),
                        null,
                        sourceWorkColumnSlot);
                }
            }

            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
        }

        internal void SuppressPriorityMouseDownIfNeeded(Event evt)
        {
            if (evt == null ||
                evt.type != EventType.MouseDown ||
                _suppressSubWorkPriorityMouseDownFrame != Time.frameCount)
            {
                return;
            }

            evt.Use();
        }

        internal bool TryGetSubWorkOpenTarget(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out WorkTypeDef workType,
            out Rect bounds,
            out bool fromHeader,
            out WorkTabLayoutColumn targetColumn)
        {
            workType = null;
            bounds = default;
            fromHeader = false;
            targetColumn = default;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                if (headerRect.Contains(mousePosition))
                {
                    return TryGetOpenTargetFromColumn(
                        column,
                        headerRect,
                        true,
                        out workType,
                        out bounds,
                        out fromHeader,
                        out targetColumn);
                }
            }

            WorkTabLayoutRow bodyRow;
            WorkTabLayoutColumn bodyColumn;
            if (_bodyRenderer.TryGetRowAt(layout, mousePosition, out bodyRow) &&
                _bodyRenderer.TryGetBodyColumnAt(layout, mousePosition, out bodyColumn) &&
                _bodyRenderer.TryGetPriorityBoxHit(layout, bodyRow, bodyColumn, mousePosition, out Rect priorityBoxRect))
            {
                return TryGetOpenTargetFromColumn(
                    bodyColumn,
                    priorityBoxRect,
                    false,
                    out workType,
                    out bounds,
                    out fromHeader,
                    out targetColumn);
            }

            return false;
        }

        private static bool TryGetOpenTargetFromColumn(
            WorkTabLayoutColumn column,
            Rect candidateBounds,
            bool isHeader,
            out WorkTypeDef workType,
            out Rect bounds,
            out bool fromHeader,
            out WorkTabLayoutColumn targetColumn)
        {
            workType = null;
            bounds = default;
            fromHeader = false;
            targetColumn = default;

            bool isSupportedWorkColumn =
                column.Column?.Worker is PawnColumnWorker_WorkPriority ||
                FluffyWorkTabGateway.IsFluffyColumn(column.Column);
            WorkTypeDef targetWorkType = column.SubWorkParent ?? column.Column?.workType;
            if (!isSupportedWorkColumn || targetWorkType == null)
            {
                return false;
            }

            if (WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(targetWorkType).Count == 0)
            {
                return false;
            }

            workType = targetWorkType;
            bounds = candidateBounds;
            fromHeader = isHeader;
            targetColumn = column;
            return true;
        }

        internal bool TryGetSubWorkExitTarget(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out Rect bounds,
            out bool restoreCursor)
        {
            bounds = default;
            restoreCursor = false;

            Rect headerArea = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y,
                layout.Table.Size.x,
                layout.HeaderHeight);

            if (headerArea.Contains(mousePosition))
            {
                bounds = headerArea;
                restoreCursor = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkExit ?? true;
                return true;
            }

            Rect globalRowArea = WorkGridLayoutMetrics.GetSubWorkVisibleBandRect(layout);

            if (globalRowArea.Contains(mousePosition) &&
                _priorityInputHandler.TryGetGlobalPriorityBoxHit(
                    layout,
                    globalRowArea,
                    mousePosition,
                    out _,
                    out Rect globalPriorityBoxRect))
            {
                bounds = globalPriorityBoxRect;
                restoreCursor = false;
                return true;
            }

            WorkTabLayoutRow bodyRow;
            WorkTabLayoutColumn column;
            if (_bodyRenderer.TryGetRowAt(layout, mousePosition, out bodyRow) &&
                _bodyRenderer.TryGetBodyColumnAt(layout, mousePosition, out column) &&
                _bodyRenderer.TryGetPriorityBoxHit(layout, bodyRow, column, mousePosition, out Rect bodyPriorityBoxRect))
            {
                bounds = bodyPriorityBoxRect;
                restoreCursor = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkPawnCellExit ?? false;
                return true;
            }

            return false;
        }

        private static void ShowCtrlClickDefaultNoticeIfNeeded(bool fromHeader, bool ctrlClickDiscovery)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null ||
                settings.subWorkCtrlClickNoticeDismissed ||
                !fromHeader ||
                !ctrlClickDiscovery)
            {
                return;
            }

            settings.subWorkCtrlClickNoticeDismissed = true;
            settings.Write();
            Find.WindowStack.Add(new Dialog_MessageBox(
                "BWT_SubWork_CtrlClickNotice_Text".Translate(),
                "BWT_SubWork_CtrlClickNotice_UseCtrl".Translate(),
                () =>
                {
                    settings.subWorkDrilldownModifier = BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl;
                    settings.subWorkDrilldownButton = BetterWorkTabSettings.SubWorkDrilldownButton.Left;
                    settings.subWorkCtrlClickNoticeDismissed = true;
                    settings.Write();
                },
                "BWT_SubWork_CtrlClickNotice_Never".Translate(),
                () =>
                {
                    settings.subWorkCtrlClickNoticeDismissed = true;
                    settings.Write();
                },
                "BWT_SubWork_CtrlClickNotice_Title".Translate()));
        }

        private static void MarkSubWorkCtrlClickNoticeDismissed()
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null || settings.subWorkCtrlClickNoticeDismissed)
            {
                return;
            }

            settings.subWorkCtrlClickNoticeDismissed = true;
            settings.Write();
        }

        private static bool CompleteSubWorkOpen(
            IWorkTabLayoutController layout,
            WorkTypeDef workType,
            Vector2? returnMousePosition,
            bool ctrlClickDiscovery,
            Event evt)
        {
            TimePriorityScheduleEditor.CloseForWorkModeTransition();
            if (SubWorkDrilldownState.EffectiveDrilldownStyle() == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                EnterFluffyHostedSubWork(workType);
            }
            else
            {
                SubWorkDrilldownState.Enter(
                    workType,
                    returnMousePosition,
                    SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout.Table, layout.HeaderHeight));
            }

            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
            ShowCtrlClickDefaultNoticeIfNeeded(returnMousePosition.HasValue, ctrlClickDiscovery);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        private static void EnterFluffyHostedSubWork(WorkTypeDef workType)
        {
            SubWorkDrilldownState.ToggleExpandBeside(workType);
        }

        private void BeginPendingSubWorkGesture(
            Vector2 start,
            Rect bounds,
            int button,
            WorkTypeDef openType,
            bool exit,
            bool restoreCursor,
            bool ctrlClickDiscovery = false)
        {
            NativeCursorPosition.CancelPendingMove();
            _pendingSubWorkGesture = true;
            _pendingSubWorkStart = start;
            _pendingSubWorkBounds = bounds;
            _pendingSubWorkButton = button;
            _pendingSubWorkOpenType = openType;
            _pendingSubWorkExit = exit;
            _pendingSubWorkRestoreCursor = restoreCursor;
            _pendingSubWorkCtrlClickDiscovery = ctrlClickDiscovery;
        }

        private void CancelPendingSubWorkIfDragged(Vector2 mousePosition)
        {
            if (!_pendingSubWorkGesture)
            {
                return;
            }

            float threshold = Mathf.Max(1f, BetterWorkTabMod.Settings?.dragThreshold ?? DefaultSettings.dragThreshold);
            if ((mousePosition - _pendingSubWorkStart).magnitude >= threshold)
            {
                ClearPendingSubWorkGesture();
            }
        }

        private bool IsPendingSubWorkClick(Vector2 mousePosition)
        {
            if (!_pendingSubWorkGesture)
            {
                return false;
            }

            float threshold = Mathf.Max(1f, BetterWorkTabMod.Settings?.dragThreshold ?? DefaultSettings.dragThreshold);
            return (mousePosition - _pendingSubWorkStart).magnitude < threshold &&
                _pendingSubWorkBounds.Contains(mousePosition);
        }

        private void ClearPendingSubWorkGesture()
        {
            _pendingSubWorkGesture = false;
            _pendingSubWorkStart = Vector2.zero;
            _pendingSubWorkBounds = default;
            _pendingSubWorkOpenType = null;
            _pendingSubWorkButton = -1;
            _pendingSubWorkExit = false;
            _pendingSubWorkRestoreCursor = false;
            _pendingSubWorkCtrlClickDiscovery = false;
        }

        private void MarkSubWorkPriorityMouseDownForSuppression()
        {
            _suppressSubWorkPriorityMouseDownFrame = Time.frameCount;
        }
    }
}
