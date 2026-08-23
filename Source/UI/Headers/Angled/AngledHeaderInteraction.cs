using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using Verse.Sound;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Settings;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Handles mouse interactions (clicks, tooltips) for complex header shapes.
    /// This logic is shared between angled and vanilla headers via the IHeaderRenderer abstraction.
    /// </summary>
    /// <summary>
    /// Encapsulates the context required for header interactions to reduce parameter count.
    /// </summary>
    public struct HeaderInteractionContext
    {
        public PawnColumnWorker_WorkPriority Worker;
        public PawnTable Table;
        public AngledLabelDrawer.AngledLabelLayout Layout;
        public Rect Bounds;
        public Vector2[] Quad;
        public bool IsMouseOver;
        public bool ShouldDraw;
        public Rect HeaderRect;
        public IHeaderRenderer Renderer;
        public bool IsVanillaStaggered;
    }

    public static class AngledHeaderInteraction
    {
        private static PawnColumnDef _columnSuppressingClicks = null;
        private static PawnColumnDef _pendingClickColumn = null;

        /// <summary>
        /// Orchestrates the drawing and interaction logic for a header.
        /// </summary>
        public static void HandleInteractions(HeaderInteractionContext ctx)
        {
            var evt = Event.current;
            var workType = ctx.Worker.def.workType;

            // Handle Repaint
            bool isSorted = ctx.Table.SortingBy == ctx.Worker.def;
            bool sortDescending = ctx.Table.SortingDescending;

            if (ctx.ShouldDraw)
            {
                ctx.Renderer.DrawHeader(ctx.Layout, ctx.IsMouseOver, isSorted, sortDescending, ctx.HeaderRect, ctx.Worker.def, ctx.Layout.ShowMarker);
            }

            // Early exit for interaction if not over the header or event is irrelevant
            if (!ctx.IsMouseOver || !HeaderUtility.ShouldHandleHeader(evt.type))
            {
                // If the mouse left the header, clear any pending click for this column
                if (evt.type == EventType.Repaint && !ctx.IsMouseOver && _pendingClickColumn == ctx.Worker.def)
                {
                    _pendingClickColumn = null;
                }
                return;
            }

            if (evt.type == EventType.ScrollWheel &&
                TryHandleShiftPriorityGesture(ctx.Worker, ctx.Table, evt, allowRootGrouping: false))
            {
                return;
            }

            // --- Tooltips ---
            if (evt.type == EventType.Repaint)
            {
                string tooltip = GetTooltip(ctx.Worker, ctx.Table);
                if (!tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(ctx.Bounds, tooltip);
                }
            }

            // --- Click Interactions ---
            // MouseUp is utilized for sorting to differentiate between a standard click and the initiation of a drag operation.
            // If a drag commences, the drag handler consumes the MouseUp event, preventing sorting.
            if (evt.type == EventType.MouseDown)
            {
                bool isCtrlLeftDiscovery =
                    SubWorkDrilldownInput.ShouldOfferCtrlLeftDiscovery(evt) &&
                    workType != null &&
                    !IsPreviewSpecificJobOrderingBlocked() &&
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType).Count > 0;
                if (SubWorkDrilldownInput.MatchesGesture(evt) || isCtrlLeftDiscovery)
                {
                    ClearPendingHeaderClick(ctx.Worker.def);
                    return;
                }

                if (evt.button == 0 || evt.button == 1)
                {
                    if (evt.shift || (evt.modifiers & EventModifiers.Shift) != 0)
                    {
                        if (TryHandleShiftPriorityGesture(ctx.Worker, ctx.Table, evt, allowRootGrouping: true))
                        {
                            return;
                        }
                    }

                    _pendingClickColumn = ctx.Worker.def;
                }
            }
            else if (evt.type == EventType.MouseUp)
            {
                if (SubWorkDrilldownInput.MatchesShortcut(evt))
                {
                    ClearPendingHeaderClick(ctx.Worker.def);
                    return;
                }

                // Only trigger if we released on the same column we pressed down on
                if (_pendingClickColumn != ctx.Worker.def)
                {
                    return;
                }
                _pendingClickColumn = null;

                // Suppress click if this column is currently being dragged or just finished dragging
                if (_columnSuppressingClicks == ctx.Worker.def)
                {
                    return;
                }

                if (evt.button == 0) // Left click
                {
                    HandleLeftClick(ctx.Worker, ctx.Table, evt);
                    evt.Use();
                }
                else if (evt.button == 1) // Right click
                {
                    HandleRightClick(ctx.Worker, ctx.Table, evt);
                    evt.Use();
                }
            }
        }

        private static string GetTooltip(PawnColumnWorker_WorkPriority worker, PawnTable table)
        {
            if (!IsPreviewSpecificJobOrderingBlocked() &&
                SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    worker.def,
                    out _,
                    out _,
                    out _))
            {
                return GetSubWorkTooltip(worker, table);
            }

            if (!IsPreviewSpecificJobOrderingBlocked() && SubWorkDrilldownState.IsActive)
            {
                return GetSubWorkTooltip(worker, table);
            }

            // Replicate vanilla GetHeaderTip from PawnColumnWorker_WorkPriority
            var workType = worker.def.workType;
            
            TaggedString tooltip = WorkTypeDisplayNameService.GerundLabel(workType).Colorize(ColoredText.TipSectionTitleColor)
                + "\n\n" + workType.description 
                + "\n\n" + SpecificWorkListString(workType) 
                + "\n";
            
            if (worker.def.sortable)
            {
                tooltip += "\n" + "ClickToSortByThisColumn".Translate().Colorize(ColoredText.SubtleGrayColor);
            }
            
            if (!Verse.Steam.SteamDeck.IsSteamDeckInNonKeyboardMode)
            {
                if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.DragdropEnableGrouping))
                {
                    tooltip += "\n" + "Shift + click: Select column for group dragging.".Colorize(ColoredText.SubtleGrayColor);
                }
                else if (WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                             Find.PlaySettings?.useWorkPriorities ?? true))
                {
                    tooltip += "\n" + "WorkPriorityShiftClickTip".Translate().Colorize(ColoredText.SubtleGrayColor);
                }
                else
                {
                    tooltip += "\n" + "WorkPriorityShiftClickEnableDisableTip".Translate().Colorize(ColoredText.SubtleGrayColor);
                }
            }

            AppendSubWorkOpenTip(ref tooltip, workType);

            return tooltip.Resolve();
        }

        private static string GetSubWorkTooltip(PawnColumnWorker_WorkPriority worker, PawnTable table)
        {
            if (!SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    worker.def,
                    out var workGiver,
                    out var activeWorkType,
                    out _))
            {
                return string.Empty;
            }

            var def = workGiver.def;
            System.Text.StringBuilder tooltip = new System.Text.StringBuilder(160);

            tooltip.Append(WorkGiverDisplayNameService.FullLabel(def).Colorize(ColoredText.TipSectionTitleColor));

            string workTypeLabel = WorkTypeDisplayNameService.FullLabel(activeWorkType);
            if (!workTypeLabel.NullOrEmpty())
            {
                tooltip.Append("\n").Append("WorkType".Translate()).Append(": ").Append(workTypeLabel);
            }

            if (!def.description.NullOrEmpty())
            {
                tooltip.Append("\n\n").Append(def.description);
            }

            if (worker.def.sortable)
            {
                tooltip.Append("\n\n").Append("ClickToSortByThisColumn".Translate().Colorize(ColoredText.SubtleGrayColor));
            }

            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.DragdropEnableGrouping))
            {
                tooltip.Append("\n").Append("Shift + click: Select column for group dragging.".Colorize(ColoredText.SubtleGrayColor));
            }
            else if (WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                         Find.PlaySettings?.useWorkPriorities ?? true))
            {
                tooltip.Append("\n").Append("WorkPriorityShiftClickTip".Translate().Colorize(ColoredText.SubtleGrayColor));
            }
            else
            {
                tooltip.Append("\n").Append("WorkPriorityShiftClickEnableDisableTip".Translate().Colorize(ColoredText.SubtleGrayColor));
            }

            if (SubWorkDrilldownInput.IsEnabled)
            {
                tooltip.Append("\n")
                    .Append((SubWorkDrilldownInput.GestureLabel().CapitalizeFirst() + ": Back to work types")
                    .Colorize(ColoredText.SubtleGrayColor));
            }

            return tooltip.ToString();
        }
        
        /// <summary>
        /// Builds the list of specific work givers for the work type using BWT's effective order.
        /// </summary>
        private static string SpecificWorkListString(WorkTypeDef def)
        {
            if (IsPreviewSpecificJobOrderingBlocked())
            {
                return "Specific-job ordering is not projected in this workload preview.";
            }

            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
            var workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(def);
            int appended = 0;
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef workGiverDef = workGivers[i]?.def;
                if (workGiverDef == null)
                {
                    continue;
                }

                if (appended > 0)
                {
                    stringBuilder.AppendLine();
                }

                stringBuilder.Append(" - ").Append(GetSpecificWorkTooltipLabel(def, workGiverDef));
                if (workGiverDef.emergency)
                {
                    stringBuilder.Append(" (" + "EmergencyWorkMarker".Translate() + ")");
                }
                appended++;
            }
            return stringBuilder.ToString();
        }

        private static string GetSpecificWorkTooltipLabel(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            string label = WorkGiverDisplayNameService.FullLabel(workGiverDef);
            bool isMoved = WorkGiverReassignmentManager.ShouldShowMovedWorkGiverMarker(workType, workGiverDef);
            var settings = BetterWorkTabMod.Settings;
            bool showMovedMarker = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.ColumnsShowMovedIndicator);
            bool showMovedTint = BWTWorkTabEffectiveSettings.GetBool("columns.showMovedColorTint");
            if (isMoved && showMovedMarker && !label.EndsWith(HeaderUtility.MovedMarker))
            {
                label += HeaderUtility.MovedMarker;
            }

            if (isMoved && showMovedTint)
            {
                label = label.Colorize(HeaderUtility.Colors.MovedMarkerColor);
            }

            return label;
        }

        private static void AppendSubWorkOpenTip(ref TaggedString tooltip, WorkTypeDef workType)
        {
            try
            {
                var workGivers = IsPreviewSpecificJobOrderingBlocked() || workType == null
                    ? null
                    : WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
                if (!SubWorkDrilldownInput.IsEnabled ||
                    workType == null ||
                    workGivers == null ||
                    workGivers.Count == 0)
                {
                    return;
                }

                string gesture = SubWorkDrilldownInput.GestureLabel();
                if (gesture.NullOrEmpty())
                {
                    return;
                }

                gesture = gesture.Trim();
                if (gesture.NullOrEmpty())
                {
                    return;
                }

                gesture = gesture.CapitalizeFirst();
                string workLabel = WorkTypeDisplayNameService.HeaderLabel(workType);

                tooltip += "\n" + (gesture + ": Open " + workLabel + " sub-work jobs").Colorize(ColoredText.SubtleGrayColor);
            }
            catch
            {
                // Header tooltips are best-effort; interaction should keep working if a
                // compatibility layer has incomplete work-giver data for this work type.
            }
        }

        private static void HandleLeftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, Event evt)
        {
            if (!IsPreviewSpecificJobOrderingBlocked() &&
                SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    worker.def,
                    out var movedWorkGiver,
                    out var parentWorkType,
                    out _) &&
                movedWorkGiver?.def != null &&
                WorkGiverReassignmentManager.ShouldShowMovedWorkGiverMarker(parentWorkType, movedWorkGiver.def) &&
                WorkGiverReassignmentManager.TryRestoreWorkGiverToBaseline(movedWorkGiver.def.defName, out _))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                table.SetDirty();
                return;
            }

            // Normal Left Click: Clear selection and handle sorting
            if (Better_Work_Tab.DragDrop.ColumnSelectionManager.HasSelection)
            {
                Better_Work_Tab.DragDrop.ColumnSelectionManager.Clear();
            }

            // Standard Vanilla behavior: Sort by this column (3-state cycle)
            if (table.SortingBy != worker.def)
            {
                // State 1: Sort Ascending
                table.SortBy(worker.def, false);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else if (!table.SortingDescending)
            {
                // State 2: Sort Descending
                table.SortBy(worker.def, true);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else
            {
                // State 3: Clear Sorting (Return to vanilla/default)
                table.SortBy(null, false);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            
            table.SetDirty();
        }

        private static void HandleRightClick(PawnColumnWorker_WorkPriority worker, PawnTable table, Event evt)
        {
            if (!IsPreviewSpecificJobOrderingBlocked() &&
                SubWorkDrilldownState.IsActive &&
                SubWorkDrilldownState.TryGetWorkGiverForColumn(worker.def, out var workGiver, out _))
            {
                HeaderContextMenu.ShowForWorkGiver(worker, table, workGiver.def);
                return;
            }

            if (!IsPreviewSpecificJobOrderingBlocked() &&
                SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    worker.def,
                    out var drawingWorkGiver,
                    out _,
                    out _) &&
                drawingWorkGiver?.def != null)
            {
                HeaderContextMenu.ShowForWorkGiver(worker, table, drawingWorkGiver.def);
                return;
            }

            HeaderContextMenu.ShowForWorkType(worker, table);
        }

        private static void HandleShiftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, int button)
        {
            // Shift-click is a bulk priority gesture. BWT may still render an external
            // authority's values, but it must not write them through the shared pawn store or
            // accidentally turn a presentation gesture into an authority handoff.
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                return;
            }

            if (!IsPreviewSpecificJobOrderingBlocked() &&
                SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    worker.def,
                    out var workGiver,
                    out var parentWorkType,
                    out _))
            {
                HandleSubWorkShiftClick(parentWorkType, workGiver.def, table, button);
                return;
            }

            var workType = worker.def.workType;
            List<Pawn> pawns = table.PawnsListForReading;
            bool useWorkPriorities = WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                Find.PlaySettings?.useWorkPriorities ?? true);

            bool changed = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                    continue;

                int curPriority = WorkTabEffectiveStateRuntime.IsPreviewActive
                    ? WorkTabEffectiveStateRuntime.GetParentPriority(
                        pawn,
                        workType,
                        WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType))
                    : pawn.workSettings.GetPriority(workType);
                int nextPriority;

                if (useWorkPriorities)
                {
                    int direction = button == 0 ? 1 : -1;
                    nextPriority = WorkPrioritySystem.GetPriorityAfterBoundedStep(curPriority, direction);
                }
                else
                {
                    // Vanilla Priorities (On/Off)
                    if (button == 0)
                    {
                        nextPriority = WorkPrioritySystem.GetDefaultEnabledPriority();
                    }
                    else
                    {
                        nextPriority = WorkPrioritySystem.DisabledPriority;
                    }
                }

                if (WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    changed |= WorkPriorityCommandGateway.TrySetPreviewParentPriority(
                        pawn,
                        workType,
                        nextPriority);
                }
                else
                {
                    changed |= ParentPriorityApplication.SetStoredParentPriority(
                        pawn,
                        workType,
                        nextPriority);
                }
            }

            if (changed)
            {
                if (useWorkPriorities)
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                else if (button == 0)
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                else
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();

                if (!WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    table.SetDirty();
                }
            }
        }

        internal static bool TryHandleShiftPriorityGesture(
            PawnColumnWorker_WorkPriority worker,
            PawnTable table,
            Event evt,
            bool allowRootGrouping)
        {
            if (worker == null || table == null || evt == null ||
                !(evt.shift || (evt.modifiers & EventModifiers.Shift) != 0))
            {
                return false;
            }

            bool isSubWork = !IsPreviewSpecificJobOrderingBlocked() &&
                SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    worker.def,
                    out _,
                    out _,
                    out _);
            if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1))
            {
                if (allowRootGrouping &&
                    !isSubWork &&
                    evt.button == 0 &&
                    BWTWorkTabEffectiveSettings.GetBool(SettingIDs.DragdropEnableGrouping))
                {
                    Better_Work_Tab.DragDrop.ColumnSelectionManager.ToggleSelection(worker.def);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    evt.Use();
                    return true;
                }

                HandleShiftClick(worker, table, evt.button);
                evt.Use();
                return true;
            }

            if (evt.type != EventType.ScrollWheel ||
                !BWTWorkTabEffectiveSettings.GetBool(SettingIDs.AdvancedScrollWheelPriority) ||
                Mathf.Abs(evt.delta.y) < 0.01f)
            {
                return false;
            }

            HandleShiftClick(worker, table, evt.delta.y < 0f ? 0 : 1);
            evt.Use();
            return true;
        }

        private static void HandleSubWorkShiftClick(WorkTypeDef workType, WorkGiverDef workGiverDef, PawnTable table, int button)
        {
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                return;
            }

            List<Pawn> pawns = table.PawnsListForReading;
            bool useWorkPriorities = WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                Find.PlaySettings?.useWorkPriorities ?? true);

            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                bool changedInPreview = false;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || pawn.Dead || pawn.workSettings == null ||
                        !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                    {
                        continue;
                    }

                    int parentPriority = WorkTabEffectiveStateRuntime.GetParentPriority(
                        pawn,
                        workType,
                        WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType));
                    int currentPriority = WorkTabEffectiveStateRuntime.TryGetSpecificJobPriority(
                        pawn,
                        workType,
                        workGiverDef,
                        out int projectedPriority)
                        ? WorkPrioritySystem.ClampPriority(projectedPriority)
                        : WorkGiverReassignmentManager.GetWorkGiverPriority(
                            pawn,
                            workGiverDef,
                            parentPriority);
                    int nextPriority = useWorkPriorities
                        ? WorkPrioritySystem.GetPriorityAfterBoundedStep(
                            currentPriority,
                            button == 0 ? 1 : -1)
                        : button == 0
                            ? WorkPrioritySystem.GetDefaultEnabledPriority()
                            : WorkPrioritySystem.DisabledPriority;

                    if (nextPriority != currentPriority)
                    {
                        changedInPreview |= WorkPriorityCommandGateway.SetWorkGiverPriority(
                            pawn.thingIDNumber,
                            workGiverDef,
                            nextPriority);
                    }
                }

                if (changedInPreview)
                {
                    if (useWorkPriorities)
                        SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    else if (button == 0)
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    else
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }

                return;
            }

            var pawnIds = new List<int>();
            var priorities = new List<int>();

            bool changed = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                int defaultPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                int curPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiverDef, defaultPriority);
                int nextPriority;

                if (useWorkPriorities)
                {
                    int direction = button == 0 ? 1 : -1;
                    nextPriority = WorkPrioritySystem.GetPriorityAfterBoundedStep(curPriority, direction);
                }
                else
                {
                    nextPriority = button == 0
                        ? WorkPrioritySystem.GetDefaultEnabledPriority()
                        : WorkPrioritySystem.DisabledPriority;
                }

                if (nextPriority == curPriority)
                {
                    continue;
                }

                pawnIds.Add(pawn.thingIDNumber);
                priorities.Add(nextPriority);
                changed = true;
            }

            if (changed)
            {
                WorkGiverReassignmentManager.SetPawnOverridesBatchSynced(workGiverDef.defName, pawnIds, priorities);

                if (useWorkPriorities)
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                }
                else if (button == 0)
                {
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }
                else
                {
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }

                table.SetDirty();
            }
        }

        /// <summary>
        /// Signal that a column drag has started, to suppress normal click behavior.
        /// </summary>
        public static void NotifyColumnDragStarted(PawnColumnDef column)
        {
            _columnSuppressingClicks = column;
        }

        public static void ResetForWindowClose()
        {
            _columnSuppressingClicks = null;
            _pendingClickColumn = null;
        }

        /// <summary>
        /// Clears any pending click state for a column.
        /// </summary>
        public static void ClearPendingHeaderClick(PawnColumnDef column)
        {
            if (column == null || _columnSuppressingClicks == column)
            {
                _columnSuppressingClicks = null;
            }

            if (column == null || _pendingClickColumn == column)
            {
                _pendingClickColumn = null;
            }
        }

        private static bool IsPreviewSpecificJobOrderingBlocked()
        {
            return WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked;
        }
    }
}
