using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.Workloads;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;
using static Better_Work_Tab.UI.Settings.SettingIDs;
using Spine.UI.Tutorial;

namespace Better_Work_Tab.UI.Settings
{
    internal static class BWTWorkTabContextSettingsRouter
    {
        private const float RightEdgeMargin = 10f;

        internal static bool TryBuildFocusRequest(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            bool skillOnly,
            bool ctrlOnly,
            out BWTSettingsFocusRequest request)
        {
            request = BuildContextSettingsRequest(inRect, layout, mousePosition, skillOnly, ctrlOnly);
            Event evt = Event.current;
            if (request != null &&
                evt != null &&
                evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                evt.alt)
            {
                // Repaint is registration-only: do not leave a stale focus
                // request behind every frame, or opening Settings through the
                // normal gear can inherit the last Work-tab pointer. The
                // actual Alt-left MouseDown queues the exact target that Spine
                // will open after it consumes the contextual gesture.
                BWTSettingsContextFocus.Request(request);
            }

            return request != null;
        }

        internal static BWTSettingsFocusRequest BuildPriorityRangeFocusRequest(PriorityMode mode)
        {
            string target = TutorialSettingsRouting.ResolvePriorityTarget(
                mode == PriorityMode.BetterWorkTab
                    ? TutorialPriorityRoutingMode.BetterWorkTab
                    : mode == PriorityMode.Auto
                        ? TutorialPriorityRoutingMode.Auto
                        : mode == PriorityMode.ExternalProvider
                            ? TutorialPriorityRoutingMode.ExternalProvider
                            : TutorialPriorityRoutingMode.Vanilla);
            return CreateContextRequest(
                "BWT_Tutorial_PriorityRange_Filter".Translate(),
                "BWT_Tutorial_PriorityRange_FilterTooltip".Translate(),
                target,
                true,
                PriorityHeader,
                PriorityModeSetting,
                UiMaxPriority,
                UiAutoDisabledPriorityMode,
                UiAutoDisabledPriorityFixedValue);
        }

        private static BWTSettingsFocusRequest BuildContextSettingsRequest(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            bool skillOnly,
            bool ctrlOnly)
        {
            if (TimePriorityScheduleEditor.TryGetCopyPasteSettingsContext(mousePosition))
            {
                return CreateContextRequest(
                    "Schedule Copy/Paste Buttons",
                    "Setting that controls copy and paste buttons for Work tab time-priority schedules.",
                    UiTimePriorityCopyPasteButtons,
                    false,
                    UiTimePrioritySchedules,
                    UiTimePriorityCopyPasteButtons);
            }

            if (layout != null && TryGetPriorityCellContext(layout, mousePosition, out bool isSubWorkCell))
            {
                if (ctrlOnly)
                {
                    return CreateContextRequest(
                        isSubWorkCell ? "Ctrl Sub-work Priority Cell" : "Ctrl Priority Cell",
                        "Settings related to Ctrl interactions on priority cells, including sub-work drilldown and time priority planning.",
                        FeaturesSubWorkJobs,
                        true,
                        FeaturesSubWorkJobs,
                        SubWorkOpenModifier,
                        SubWorkOpenButton,
                        SubWorkRestoreCursorFromPawnCells,
                        SubWorkTransitionMode,
                        SubWorkTransitionSpeed,
                        SubWorkDisabledParentMode,
                        UiTimePrioritySchedules,
                        UiTimePriorityCopyPasteButtons,
                        UiChronosPointerTimePriority,
                        UiTimePrioritySourceColumnHighlight,
                        AdvancedScrollWheelPriority,
                        LayoutCtrlDrag);
                }

                if (skillOnly)
                {
                    return CreateContextRequest(
                        "Skill Overlay",
                        "Settings related to skill overlays and skill numbers for work priority cells.",
                        OverlayHeader,
                        false,
                        FeaturesOverlay,
                        OverlayHeader,
                        OverlayNumbersMode,
                        OverlayBestPawnMode,
                        OverlayHoverCellOverlay,
                        OverlayHoverMode,
                        OverlayHoverScope,
                        ColorsSkillVeryLow,
                        ColorsSkillLow,
                        ColorsSkillGood,
                        ColorsSkillExcellent,
                        ColorsBestPawnOutline,
                        HighlightsBestPawnBackground);
                }

                if (isSubWorkCell)
                {
                    return CreateWorkloadContextRequest(
                        "Specific-job drilldown",
                        "Settings for the specific-job view, its drilldown behavior, and the workload preview that can own these presentation choices.",
                        SubWorkDrilldownStyle,
                        true,
                        SubWorkDrilldownStyle,
                        FeaturesSubWorkJobs,
                        SubWorkOpenModifier,
                        SubWorkOpenButton,
                        SubWorkRestoreCursorFromPawnCells,
                        SubWorkTransitionAnimation,
                        SubWorkTransitionMode,
                        SubWorkTransitionStyle,
                        SubWorkTransitionSpeed,
                        SubWorkDisabledParentMode,
                        FeaturesWorkloads,
                        CompatFluffyWorkTabSpecificJobs);
                }

                return CreateContextRequest(
                    isSubWorkCell ? "Sub-work Priority Cell" : "Priority Cell",
                    "Settings related to priority cells, skill overlays, max priorities, time priority, and sub-work priority behavior.",
                    PriorityHeader,
                    true,
                    PriorityHeader,
                    PriorityModeSetting,
                    UiMaxPriority,
                    UiAutoDisabledPriorityMode,
                    UiAutoDisabledPriorityFixedValue,
                    AdvancedScrollWheelPriority,
                    FeaturesOverlay,
                    OverlayHeader,
                    OverlayNumbersMode,
                    OverlayBestPawnMode,
                    OverlayHoverCellOverlay,
                    OverlayHoverMode,
                    OverlayHoverScope,
                    UiTimePrioritySchedules,
                    UiTimePriorityCopyPasteButtons,
                    UiChronosPointerTimePriority,
                    UiTimePriorityHourDivider,
                    UiTimePrioritySourceColumnHighlight,
                    FeaturesSubWorkJobs,
                    SubWorkCompactPriorityBoxes,
                    SubWorkGlobalVanillaPriorityBoxes,
                    SubWorkDisabledParentMode,
                    SubWorkRestoreCursorFromPawnCells);
            }

            if (layout != null && TryGetHeaderContext(layout, mousePosition, out bool isWorkHeader, out bool isSubWorkHeader))
            {
                if (ctrlOnly)
                {
                    return CreateContextRequest(
                        isSubWorkHeader ? "Ctrl Sub-work Header" : "Ctrl Work Header",
                        "Settings related to Ctrl interactions on work headers, including sub-work drilldown and Ctrl-drag behavior.",
                        FeaturesSubWorkJobs,
                        true,
                        FeaturesSubWorkJobs,
                        SubWorkOpenModifier,
                        SubWorkOpenButton,
                        SubWorkRestoreCursor,
                        SubWorkTransitionMode,
                        SubWorkTransitionSpeed,
                        LayoutCtrlDrag,
                        FeaturesDragdrop,
                        LayoutDragColumns,
                        LayoutDragThreshold,
                        LayoutDragHoverDelay);
                }

                if (isSubWorkHeader)
                {
                    return CreateWorkloadContextRequest(
                        "Specific-job header",
                        "Settings for the visible specific-job header and its drilldown presentation.",
                        SubWorkDrilldownStyle,
                        true,
                        SubWorkDrilldownStyle,
                        FeaturesSubWorkJobs,
                        SubWorkOpenModifier,
                        SubWorkOpenButton,
                        SubWorkRestoreCursor,
                        SubWorkRestoreCursorFromPawnCells,
                        SubWorkTransitionAnimation,
                        SubWorkTransitionMode,
                        SubWorkTransitionStyle,
                        SubWorkTransitionSpeed,
                        FeaturesWorkloads,
                        CompatFluffyWorkTabSpecificJobs);
                }

                return CreateWorkloadContextRequest(
                    "Work-type header",
                    "Settings for Work-type labels, angled header presentation, column markers, and workload-controlled header choices.",
                    HeadersCustomWorkLabels,
                    true,
                    HeadersHeader,
                    HeadersCustomWorkLabels,
                    HeadersAngled,
                    DragdropRemoveHeaderUnderline,
                    HeadersAngleRotation,
                    HeadersUseVerticalStackingForCJK,
                    "headers.cjkVerticalKerning",
                    "headers.angledColor",
                    HeadersUnderlineColor,
                    "headers.horizontalOffset",
                    FeaturesDragdrop,
                    LayoutDragColumns,
                    LayoutDragColumnLineInset,
                    LayoutDragThreshold,
                    LayoutDragHoverDelay,
                    LayoutCtrlDrag,
                    LayoutResetColumns,
                    ColumnsShowMovedIndicator,
                    ColumnsShowBaselineLine,
                    "columns.showMovedColorTint",
                    "columns.movedMarkerColor",
                    ColumnsResetWidths,
                    FeaturesWorkloads,
                    WorkloadsWarnOnApply,
                    FeaturesAutoassign,
                    AutoassignViewMode,
                    AutoassignWarnOnApply,
                    FeaturesSubWorkJobs,
                    SubWorkCrossWorkDragDrop,
                    SubWorkOpenModifier,
                    SubWorkOpenButton,
                    SubWorkTransitionMode,
                    SubWorkTransitionSpeed,
                    CompatFluffyWorkTabHeader,
                    FluffyStyleFeatures,
                    FluffyStyleTopButtons,
                    FluffyStyleStandaloneTopButtons,
                    SubWorkAutoExpandColumns,
                    SubWorkEvenlyExpandColumns,
                    isWorkHeader ? PriorityHeader : null);
            }

            if (layout != null && TryGetSubWorkGlobalRowContext(layout, mousePosition))
            {
                return CreateContextRequest(
                    "Sub-work Global Row",
                    "Settings related to the global priority row shown inside sub-work jobs.",
                    FeaturesSubWorkJobs,
                    true,
                    FeaturesSubWorkJobs,
                    SubWorkCrossWorkDragDrop,
                    SubWorkCompactPriorityBoxes,
                    SubWorkGlobalVanillaPriorityBoxes,
                    SubWorkTransitionMode,
                    SubWorkTransitionSpeed,
                    SubWorkAutoExpandColumns,
                    SubWorkEvenlyExpandColumns,
                    UiTimePrioritySchedules,
                    UiTimePriorityCopyPasteButtons,
                    UiTimePrioritySourceColumnHighlight,
                    UiChronosPointerTimePriority);
            }

            if (layout != null && TryGetTimePriorityContext(layout, mousePosition, out bool isChronosRegion))
            {
                return CreateContextRequest(
                    isChronosRegion ? "Chronos Pointer Time Bar" : "Time Priority",
                    isChronosRegion
                        ? "Settings related to Chronos Pointer integration in the Work tab time-priority schedule."
                        : "Settings related to time priority rows and Chronos Pointer integration.",
                    isChronosRegion ? UiChronosPointerTimePriority : UiTimePrioritySchedules,
                    true,
                    UiTimePrioritySchedules,
                    UiTimePriorityCopyPasteButtons,
                    UiChronosPointerTimePriority,
                    UiTimePriorityHourDivider,
                    UiTimePrioritySourceColumnHighlight,
                    UiChronosPointerTimePriorityIncidents,
                    FeaturesSubWorkJobs,
                    PriorityHeader);
            }

            if (layout != null && TryGetDividerContext(layout, mousePosition))
            {
                return CreateContextRequest(
                    "Dividers",
                    "Settings related to divider visibility, labels, colors, collapse, and animations.",
                    FeaturesDividers,
                    false,
                    FeaturesDividers,
                    DividersShow,
                    DividersCustomColors,
                    DividersLabels,
                    DividersCollapse,
                    DividersAnimations,
                    LayoutDividerHeight,
                    LayoutDividerAlpha,
                    LayoutDragRows);
            }

            if (TryGetManualPrioritiesContext(mousePosition))
            {
                return CreateContextRequest(
                    "Manual Priorities",
                    "Settings related to the manual priorities checkbox and priority controls.",
                    UiManualPriorities,
                    false,
                    FeaturesUiElements,
                    UiManualPriorities,
                    UiPriorityLegend,
                    PriorityHeader,
                    PriorityModeSetting,
                    UiMaxPriority,
                    AdvancedScrollWheelPriority);
            }

            if (TryGetPriorityLegendContext(inRect, mousePosition))
            {
                return CreateContextRequest(
                    "Priority Legend",
                    "Settings related to priority legend text and max priority behavior.",
                    UiPriorityLegend,
                    false,
                    FeaturesUiElements,
                    UiPriorityLegend,
                    UiMaxPriority,
                    PriorityHeader);
            }

            if (TryGetBottomCountersContext(inRect, mousePosition, out bool isBedCounter))
            {
                string targetSettingId = isBedCounter ? LayoutBedCount : LayoutPawnCount;
                return CreateContextRequest(
                    isBedCounter ? "Bed Counter" : "Pawn Counter",
                    isBedCounter
                        ? "Settings related to the bottom-left bed counter."
                        : "Settings related to the bottom-left colonist counter.",
                    targetSettingId,
                    false,
                    FeaturesUiElements,
                    LayoutPawnCount,
                    LayoutBedCount);
            }

            if (TryGetBottomInstructionsContext(inRect, mousePosition))
            {
                return CreateContextRequest(
                    "Bottom Instructions",
                    "Settings related to the bottom-left help text and the behaviors it describes.",
                    UiDragInstructions,
                    false,
                    FeaturesUiElements,
                    UiDragInstructions,
                    UiContextSettingsHint,
                    FeaturesOverlay,
                    FeaturesDragdrop,
                    LayoutCtrlDrag,
                    LayoutDragColumns,
                    LayoutDragRows,
                    AdvancedScrollWheelPriority);
            }

            if (TryGetContextSettingsHintTextContext(inRect, mousePosition))
            {
                return CreateContextRequest(
                    "Alt-Click Settings Hint",
                    "Setting that controls the top-right Alt-click settings hint.",
                    UiContextSettingsHint,
                    false,
                    FeaturesUiElements,
                    UiContextSettingsHint);
            }

            if (TryGetBottomButtonContext(inRect, mousePosition, out bool isWorkloadButton, out bool isRulesetButton))
            {
                if (isWorkloadButton)
                {
                    return CreateContextRequest(
                        "Workload Buttons",
                        "Settings related to workload buttons, saved workloads, warnings, and divider persistence.",
                        FeaturesWorkloads,
                        false,
                        FeaturesWorkloads,
                        WorkloadsWarnOnApply,
                        FeaturesUiElements);
                }

                if (isRulesetButton)
                {
                    return CreateContextRequest(
                        "Ruleset Buttons",
                        "Settings related to rulesets, auto-assign behavior, warnings, and rule builder display.",
                        FeaturesAutoassign,
                        true,
                        FeaturesAutoassign,
                        AutoassignViewMode,
                        AutoassignWarnOnApply,
                        AdvancedAlwaysShowConditionEditors);
                }
            }

            if (WorkTabChromeGeometry.GetInfoIconRect(inRect).Contains(mousePosition))
            {
                return CreateContextRequest(
                    "Settings Shortcut",
                    "Setting that controls the Alt-click settings shortcut hint.",
                    UiContextSettingsHint,
                    false,
                    FeaturesUiElements,
                    UiContextSettingsHint,
                    AdvancedHideSettingResetIcons,
                    AdvancedRestoreDefaults);
            }

            if (SubWorkDrilldownState.IsActive && TryGetSubWorkExitButtonContext(inRect, mousePosition))
            {
                return CreateContextRequest(
                    "Sub-work Back Button",
                    "Settings related to leaving sub-work jobs and cursor restore behavior.",
                    FeaturesSubWorkJobs,
                    true,
                    FeaturesSubWorkJobs,
                    SubWorkRestoreCursor,
                    SubWorkRestoreCursorFromPawnCells,
                    SubWorkTransitionMode,
                    SubWorkTransitionSpeed);
            }

            if (layout != null && TryGetPresentationAreaContext(layout, mousePosition))
            {
                return CreateWorkloadContextRequest(
                    "Work-tab presentation",
                    "Settings for the Work tab's layout, counters, legend, and presentation surface. During a workload preview, supported entries can be previewed; unsupported entries are read-only and never staged.",
                    LayoutWorkTabTopSpace,
                    true,
                    LayoutWorkTabTopSpace,
                    LayoutWorkTabMinimumWidth,
                    LayoutWorkTabMaxVisiblePawns,
                    LayoutPawnCount,
                    LayoutBedCount,
                    UiPriorityLegend,
                    UiDragInstructions,
                    FeaturesUiElements,
                    FeaturesWorkloads,
                    FeaturesHighlights,
                    FeaturesOverlay,
                    HighlightsHeader,
                    OverlayHeader);
            }

            return CreateContextRequest(
                "Work Tab",
                "General settings related to the work tab.",
                FeaturesUiElements,
                false,
                FeaturesUiElements,
                FeaturesClicks,
                FeaturesDragdrop,
                FeaturesDividers,
                FeaturesWorkloads,
                FeaturesAutoassign,
                FeaturesSubWorkJobs,
                CompatFluffyWorkTabHeader,
                FluffyStyleFeatures,
                FluffyStyleTopButtons,
                FluffyStyleStandaloneTopButtons,
                HeadersCustomWorkLabels,
                PriorityHeader,
                UiContextSettingsHint,
                LayoutWorkTabMinimumWidth,
                LayoutWorkTabMaxVisiblePawns,
                LayoutWorkTabTopSpace);
        }

        private static BWTSettingsFocusRequest CreateContextRequest(
            string label,
            string tooltip,
            string targetSettingId,
            bool preferAdvanced,
            params string[] settingIds)
        {
            return new BWTSettingsFocusRequest(label, tooltip, targetSettingId, preferAdvanced, settingIds);
        }

        private static BWTSettingsFocusRequest CreateWorkloadContextRequest(
            string label,
            string tooltip,
            string targetSettingId,
            bool preferAdvanced,
            params string[] settingIds)
        {
            // The specific-job setting is contributed conditionally. Resolve the
            // exact target while the filter still retains every relevant id, and
            // use the always-registered workload section when that contributor is
            // unavailable. Spine remains responsible for the actual bind.
            string resolvedTarget = IsSettingAvailable(targetSettingId)
                ? targetSettingId
                : FeaturesWorkloads;
            return CreateContextRequest(
                label,
                tooltip,
                resolvedTarget,
                preferAdvanced,
                settingIds);
        }

        private static bool IsSettingAvailable(string settingId)
        {
            if (string.IsNullOrEmpty(settingId))
            {
                return false;
            }

            try
            {
                IReadOnlyList<SettingDefinition> definitions = BWTSettingsRegistry.Definitions;
                for (int i = 0; i < definitions.Count; i++)
                {
                    SettingDefinition definition = definitions[i];
                    if (definition != null &&
                        string.Equals(definition.Id, settingId, StringComparison.Ordinal) &&
                        (definition.ShowInSimpleView || definition.ShowInAdvancedView))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[BWT][Settings] Could not resolve contextual target '" +
                    settingId + "': " + ex.Message);
            }

            return false;
        }

        private static bool TryGetHeaderContext(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out bool isWorkHeader,
            out bool isSubWorkHeader)
        {
            isWorkHeader = false;
            isSubWorkHeader = false;
            if (layout?.Columns == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                if (!headerRect.Contains(mousePosition))
                {
                    continue;
                }

                isWorkHeader = column.Column?.Worker is PawnColumnWorker_WorkPriority;
                // Expand-beside children are real Work-tab priority columns,
                // but they do not turn on the focused drilldown flag. Treat
                // the column's layout identity as authoritative so Alt-click
                // reaches specific-job settings in both presentation modes.
                isSubWorkHeader = isWorkHeader && IsSpecificJobColumn(column);
                return true;
            }

            return false;
        }

        private static bool TryGetPriorityCellContext(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out bool isSubWorkCell)
        {
            isSubWorkCell = false;
            if (layout?.Columns == null ||
                !layout.TryGetRowAt(mousePosition, out WorkTabLayoutRow row) ||
                row.Divider != null)
            {
                return false;
            }

            Rect rowRect = layout.GetScreenRect(row);
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(column, rowRect);
                if (!cellRect.Contains(mousePosition))
                {
                    continue;
                }

                isSubWorkCell = IsSpecificJobColumn(column);
                return true;
            }

            return false;
        }

        private static bool IsSpecificJobColumn(WorkTabLayoutColumn column)
        {
            if (column.IsExpandBesideChild)
            {
                return true;
            }

            return SubWorkDrilldownState.IsActive &&
                column.Column != null &&
                SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column) >= 0;
        }

        private static bool TryGetDividerContext(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            return layout != null &&
                layout.TryGetRowAt(mousePosition, out WorkTabLayoutRow row) &&
                row.Divider != null;
        }

        private static bool TryGetSubWorkGlobalRowContext(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            if (!SubWorkDrilldownState.IsActive || layout == null)
            {
                return false;
            }

            Rect rect = WorkGridLayoutMetrics.GetSubWorkVisibleBandRect(layout);
            return rect.Contains(mousePosition);
        }

        private static bool TryGetTimePriorityContext(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out bool isChronosRegion)
        {
            isChronosRegion = false;
            if (layout == null || !TryGetTimePriorityContextRect(layout, out Rect rect))
            {
                return false;
            }

            if (!rect.Contains(mousePosition))
            {
                return false;
            }

            isChronosRegion = mousePosition.y <= rect.y + 14f;
            return true;
        }

        private static bool TryGetTimePriorityContextRect(IWorkTabLayoutController layout, out Rect rect)
        {
            rect = Rect.zero;
            if (layout == null)
            {
                return false;
            }

            if (WorkGridLayoutMetrics.SchedulePinnedHeight > 0.5f)
            {
                rect = new Rect(
                    layout.TableOrigin.x,
                    layout.TableOrigin.y + layout.HeaderHeight,
                    Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                    WorkGridLayoutMetrics.SchedulePinnedHeight);
                return true;
            }

            if (layout.Rows == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow row = layout.Rows[i];
                if (row.Divider == null || !TimePriorityScheduleEditor.IsTransientDivider(row.Divider))
                {
                    continue;
                }

                rect = layout.GetScreenRect(row);
                return true;
            }

            return false;
        }

        private static bool TryGetManualPrioritiesContext(Vector2 mousePosition)
        {
            return new Rect(5f, 5f, 220f, 90f).Contains(mousePosition);
        }

        private static bool TryGetPriorityLegendContext(Rect inRect, Vector2 mousePosition)
        {
            return new Rect(350f, inRect.y + 2f, 470f, 36f).Contains(mousePosition);
        }

        private static bool TryGetBottomCountersContext(
            Rect inRect,
            Vector2 mousePosition,
            out bool isBedCounter)
        {
            isBedCounter = false;
            Rect rect = new Rect(inRect.x + 6f, inRect.yMax - 48f, inRect.width * 0.5f, 24f);
            if (!rect.Contains(mousePosition))
            {
                return false;
            }

            isBedCounter = mousePosition.x > rect.x + 72f;
            return true;
        }

        private static bool TryGetBottomInstructionsContext(Rect inRect, Vector2 mousePosition)
        {
            return new Rect(inRect.x, inRect.yMax - 30f, inRect.width * 0.55f, 30f).Contains(mousePosition);
        }

        private static bool TryGetContextSettingsHintTextContext(Rect inRect, Vector2 mousePosition)
        {
            return WorkTabChromeGeometry.GetContextSettingsHintRect(
                inRect,
                WorkTabContextSettingsHintArea.SettingsFocus).Contains(mousePosition);
        }

        private static bool TryGetBottomButtonContext(
            Rect inRect,
            Vector2 mousePosition,
            out bool isWorkloadButton,
            out bool isRulesetButton)
        {
            isWorkloadButton = false;
            isRulesetButton = false;

            HeaderButtons.BottomButtonRects rects = HeaderButtons.GetBottomButtonRects(
                inRect,
                WorkTabChromeGeometry.GetInfoIconRect(inRect));
            if (rects.ContainsRuleset(mousePosition))
            {
                isRulesetButton = true;
                return true;
            }

            if (rects.ContainsWorkload(mousePosition))
            {
                isWorkloadButton = true;
                return true;
            }

            return false;
        }

        private static bool TryGetSubWorkExitButtonContext(Rect inRect, Vector2 mousePosition)
        {
            const float buttonSize = 24f;
            float topRightReservedWidth = HeaderButtons.GetTopRightReservedWidth();
            Rect exitRect = new Rect(
                inRect.xMax - buttonSize - RightEdgeMargin - topRightReservedWidth,
                inRect.y + 8f,
                buttonSize,
                buttonSize);
            return exitRect.Contains(mousePosition);
        }

        private static bool TryGetPresentationAreaContext(
            IWorkTabLayoutController layout,
            Vector2 mousePosition)
        {
            if (layout?.GeometrySnapshot != null)
            {
                WorkGridGeometrySnapshot geometry = layout.GeometrySnapshot;
                Rect bodyRect = new Rect(
                    geometry.TableOrigin.x,
                    geometry.BodyTop,
                    Mathf.Max(geometry.RowWidth, 1f),
                    Mathf.Max(geometry.ContentHeight, 1f));
                return bodyRect.Contains(mousePosition);
            }

            if (layout == null || layout.Table == null)
            {
                return false;
            }

            Rect fallback = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight + WorkGridLayoutMetrics.GetPinnedRowsHeight(),
                Mathf.Max(layout.Table.Size.x - 16f, 1f),
                Mathf.Max(layout.ContentHeight, 1f));
            return fallback.Contains(mousePosition);
        }

    }

    /// <summary>
    /// BWT's single ownership boundary for settings that a Workload V2 template
    /// may own. The shared settings drawer remains feature-neutral; this policy
    /// supplies BWT metadata, preview labels, and safe suppression.
    /// </summary>
    internal static class BWTWorkloadSettingsOwnershipPolicy
    {
        private const string PreviewSuppressorId = "bwt.workloadPreview";

        private static readonly Dictionary<string, BWTWorkloadSettingMetadata> Metadata =
            BuildMetadata();

        private static readonly HashSet<SettingDefinition> PreparedDefinitions =
            new HashSet<SettingDefinition>();

        private static readonly Dictionary<string, string> BlockedReasons =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly HashSet<string> SuppressedOnChanged =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly IBWTWorkloadPreviewController PreviewController =
            new WorkloadGatewayPreviewController();

        private static BWTWorkloadPreviewSnapshot _snapshot =
            BWTWorkloadPreviewSnapshot.Inactive;
        private static bool _snapshotValid;
        private static bool _legacyModeBeforeInteraction;
        private static bool _legacyModeBeforeInteractionCaptured;
        private static bool _lastObservedLegacyMode;
        private static bool _lastObservedLegacyModeValid;

        internal static void PrepareDefinition(SettingDefinition definition)
        {
            if (definition == null ||
                !Metadata.ContainsKey(definition.Id) ||
                !PreparedDefinitions.Add(definition))
            {
                return;
            }

            if (definition.Type == SettingType.Enum)
            {
                Action<object, object> existingSetter = definition.ValueSetter;
                Action<object> existingOnChanged = definition.OnChanged;

                definition.ValueSetter = (settingsObject, value) =>
                {
                    if (TryHandleEnumMutation(definition, settingsObject, value))
                    {
                        return;
                    }

                    if (existingSetter != null)
                    {
                        existingSetter(settingsObject, value);
                    }
                    else
                    {
                        SetFieldValue(definition, settingsObject, value);
                    }
                };
                definition.OnChanged = settingsObject =>
                {
                    if (ConsumeSuppressedOnChanged(definition.Id))
                    {
                        return;
                    }

                    existingOnChanged?.Invoke(settingsObject);
                };
            }

            if (RequiresPreviewSuppression(definition.Type))
            {
                if (definition.Suppressions == null)
                {
                    definition.Suppressions = new List<SettingSuppression>();
                }

                definition.Suppressions.Add(new SettingSuppression
                {
                    When = _ => IsBlocked(definition.Id, definition.Type),
                    Reason = _ => GetBlockReason(definition.Id, definition.Type),
                    SuppressorSettingId = PreviewSuppressorId,
                    LinkLabel = "Workload preview"
                });
            }
        }

        internal static void Refresh()
        {
            BWTWorkloadPreviewSnapshot next;
            try
            {
                next = PreviewController.Read() ?? BWTWorkloadPreviewSnapshot.Inactive;
            }
            catch (Exception ex)
            {
                next = BWTWorkloadPreviewSnapshot.Failed(
                    "The active workload preview could not be read safely: " + ex.Message);
            }

            if (!next.IsActive)
            {
                BlockedReasons.Clear();
            }
            else if (!StringComparer.Ordinal.Equals(_snapshot.SourceId, next.SourceId))
            {
                BlockedReasons.Clear();
            }

            _snapshot = next;
            _snapshotValid = true;

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                // Reset icons mutate the field directly and do not raise the
                // drawer's OnSettingInteracted callback. Keep the last drawn
                // value so that a reset still crosses the same gateway as a
                // normal toggle.
                _lastObservedLegacyMode = settings.useLegacyWorkloads;
                _lastObservedLegacyModeValid = true;
            }
        }

        internal static void CaptureBeforeSettingInteraction(
            SettingDefinition definition,
            object settingsObject)
        {
            if (definition?.Id != AdvancedWorkloadsLegacy ||
                !(settingsObject is BetterWorkTabSettings settings))
            {
                return;
            }

            _legacyModeBeforeInteraction = settings.useLegacyWorkloads;
            _legacyModeBeforeInteractionCaptured = true;
            _lastObservedLegacyMode = settings.useLegacyWorkloads;
            _lastObservedLegacyModeValid = true;
        }

        internal static void HandleLegacyWorkloadModeChanged(
            BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            bool requestedLegacy = settings.useLegacyWorkloads;
            bool previousLegacy = _legacyModeBeforeInteractionCaptured
                ? _legacyModeBeforeInteraction
                : _lastObservedLegacyModeValid
                    ? _lastObservedLegacyMode
                    : WorkloadGateway.CurrentMode == WorkloadBackendMode.Legacy;
            _legacyModeBeforeInteractionCaptured = false;

            if (requestedLegacy == previousLegacy)
            {
                return;
            }

            // The shared drawer writes the bool before invoking OnChanged.
            // Restore the gateway's source value before asking it to perform
            // the transition, so ResolveMode sees the real current backend.
            settings.useLegacyWorkloads = previousLegacy;
            WorkloadOperationResult result = WorkloadGateway.TryTransitionMode(
                requestedLegacy
                    ? WorkloadBackendMode.Legacy
                    : WorkloadBackendMode.Modern);
            // TryTransitionMode has already persisted a successful transition.
            // Let the shared settings drawer complete its normal write path as
            // an idempotent write. Avoid a one-shot suppression here: a stale
            // suppression can otherwise swallow a later unrelated change.
            if (result.Succeeded)
            {
                _lastObservedLegacyMode = requestedLegacy;
                _lastObservedLegacyModeValid = true;
                return;
            }

            settings.useLegacyWorkloads = previousLegacy;
            _lastObservedLegacyMode = previousLegacy;
            _lastObservedLegacyModeValid = true;
            Messages.Message(
                string.IsNullOrEmpty(result.Message)
                    ? "The workload mode could not be changed safely."
                    : result.Message,
                MessageTypeDefOf.RejectInput,
                false);
        }

        internal static void Invalidate()
        {
            _snapshotValid = false;
            SuppressedOnChanged.Clear();
        }

        internal static void WriteSettings(BetterWorkTabSettings settings)
        {
            settings?.Write();
        }

        internal static bool IsBulkSettingsOperationBlocked(out string reason)
        {
            EnsureSnapshot();
            if (!_snapshot.IsActive)
            {
                reason = string.Empty;
                return false;
            }

            if (!_snapshot.ReadSucceeded)
            {
                reason = string.IsNullOrEmpty(_snapshot.FailureReason)
                    ? "Settings import and restore defaults are disabled while the active workload preview cannot be read safely."
                    : _snapshot.FailureReason;
                return true;
            }

            if (_snapshot.OwnsPresentationSettings)
            {
                reason = "Settings import and restore defaults are disabled while this workload preview owns presentation settings. Close the preview first.";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        internal static bool TryGetEffectivePresentationValue(
            string settingId,
            WorkloadScalarValue fallback,
            out WorkloadScalarValue value)
        {
            value = fallback;
            EnsureSnapshot();
            if (WorkTabEffectiveStateRuntime.IsPreviewActive && !_snapshot.IsActive)
            {
                Refresh();
            }

            if (!_snapshot.IsActive ||
                !_snapshot.ReadSucceeded ||
                !_snapshot.OwnsPresentationSettings ||
                !IsPresentationKeyOwned(settingId))
            {
                return false;
            }

            if (!TryGetSupportedPresentationKind(settingId, out WorkloadScalarKind expectedKind) ||
                fallback.Kind != expectedKind)
            {
                return false;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                value = WorkTabEffectiveStateRuntime.GetPresentationSetting(
                    settingId,
                    fallback);
                return true;
            }

            // The scoped provider is authoritative during the Work-tab pass.
            // Outside it, refresh before reading so a preview edit made by a
            // settings or workload surface cannot leave the layout stale.
            Refresh();
            return _snapshot.After.TryGetValue(settingId, out value);
        }

        internal static string DecorateLabel(
            SettingDefinition definition,
            string translatedLabel)
        {
            string label = translatedLabel ?? definition?.Label ?? definition?.Id ?? string.Empty;
            BWTWorkloadSettingOwnershipState state = Describe(definition);
            if (!state.IsWorkloadOwnedInActiveTemplate && !state.IsBlocked)
            {
                return label;
            }

            string marker = !state.IsWorkloadOwnedInActiveTemplate
                ? "Preview safety block"
                : state.IsBlocked
                    ? "Preview read-only"
                    : state.IsChangedByActivePreview
                        ? "Preview changed"
                        : "Workload-owned";
            return label + "  -  " + marker;
        }

        internal static string DecorateTooltip(
            SettingDefinition definition,
            string translatedTooltip)
        {
            BWTWorkloadSettingOwnershipState state = Describe(definition);
            if (!state.IsWorkloadOwnedInActiveTemplate && !state.IsBlocked)
            {
                return translatedTooltip;
            }

            string ownership = !state.IsWorkloadOwnedInActiveTemplate
                ? "The active workload preview could not be read safely, so this control is disabled until the preview is closed or readable again."
                : state.IsBlocked
                    ? state.IsChangedByActivePreview
                        ? "The active workload preview contains a different value for this setting, but this control is read-only and cannot commit presentation changes."
                        : "The active workload preview owns this setting, but this control is read-only and cannot commit presentation changes."
                    : state.IsChangedByActivePreview
                        ? "The active workload preview changed this setting."
                        : "The active workload preview owns this setting.";
            if (state.WillRevertToGlobalOutsidePreview)
            {
                ownership += " It will return to the global setting when the preview closes.";
            }

            if (state.IsBlocked)
            {
                ownership += " This control is blocked during preview because the current workload commit path cannot apply presentation changes; the global setting is unchanged.";
            }

            return string.IsNullOrEmpty(translatedTooltip)
                ? ownership
                : translatedTooltip + "\n\n" + ownership;
        }

        internal static void DrawPreviewBannerIfNeeded(ref Rect inRect)
        {
            EnsureSnapshot();
            if (!_snapshot.IsActive ||
                (_snapshot.ReadSucceeded && !_snapshot.OwnsPresentationSettings))
            {
                return;
            }

            const float bannerHeight = 34f;
            const float bannerGap = 6f;
            Rect bannerRect = new Rect(inRect.x, inRect.y, inRect.width, bannerHeight);
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            try
            {
                GUI.color = new Color(0.20f, 0.34f, 0.46f, 0.95f);
                Widgets.DrawBoxSolid(bannerRect, GUI.color);
                GUI.color = new Color(0.45f, 0.72f, 0.92f, 0.95f);
                Widgets.DrawBox(bannerRect, 1);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect textRect = bannerRect.ContractedBy(9f);
                string bannerText = !_snapshot.ReadSucceeded
                    ? "Workload preview safety block: presentation ownership could not be read."
                    : "This workload preview owns presentation settings; they are read-only and leave global settings unchanged.";
                Widgets.Label(
                    textRect,
                    bannerText);
                TooltipHandler.TipRegion(
                    bannerRect,
                    !_snapshot.ReadSucceeded
                        ? "Presentation controls, import, and restore-default operations are disabled until the active workload preview can be read safely. Global settings remain unchanged."
                        : "This preview owns presentation settings, but the current workload commit path cannot apply presentation changes. Those controls, import, and restore-default operations are disabled; global settings remain unchanged.");
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }

            inRect.yMin += bannerHeight + bannerGap;
        }

        internal static bool IsGloballyOwned(string settingId)
        {
            return Describe(settingId).IsGloballyOwned;
        }

        internal static bool IsWorkloadOwnedInActiveTemplate(string settingId)
        {
            return Describe(settingId).IsWorkloadOwnedInActiveTemplate;
        }

        internal static bool IsChangedByActivePreview(string settingId)
        {
            return Describe(settingId).IsChangedByActivePreview;
        }

        internal static bool WillRevertToGlobalOutsidePreview(string settingId)
        {
            return Describe(settingId).WillRevertToGlobalOutsidePreview;
        }

        private static BWTWorkloadSettingOwnershipState Describe(
            SettingDefinition definition)
        {
            return Describe(
                definition?.Id,
                definition == null ? SettingType.Header : definition.Type);
        }

        private static BWTWorkloadSettingOwnershipState Describe(string settingId)
        {
            return Describe(settingId, SettingType.Header);
        }

        private static BWTWorkloadSettingOwnershipState Describe(
            string settingId,
            SettingType settingType)
        {
            EnsureSnapshot();
            bool known = !string.IsNullOrEmpty(settingId) && Metadata.ContainsKey(settingId);
            var state = new BWTWorkloadSettingOwnershipState
            {
                IsKnown = known,
                IsGloballyOwned = true,
                IsPreviewActive = _snapshot.IsActive
            };

            if (!known || !_snapshot.IsActive)
            {
                return state;
            }

            if (!_snapshot.ReadSucceeded)
            {
                state.IsBlocked = true;
                state.BlockReason = ResolveBlockReason(settingId, settingType);
                return state;
            }

            if (!_snapshot.OwnsPresentationSettings)
            {
                return state;
            }

            // A dimension ownership bit is not ownership of every setting in
            // that dimension. Only entries represented by the active template
            // may be marked or blocked here.
            if (!IsPresentationKeyOwned(settingId))
            {
                return state;
            }

            state.IsGloballyOwned = false;
            state.IsWorkloadOwnedInActiveTemplate = true;
            state.IsChangedByActivePreview = IsPreviewChanged(settingId);
            state.WillRevertToGlobalOutsidePreview = true;
            state.IsBlocked = true;
            state.BlockReason = ResolveBlockReason(settingId, settingType);
            return state;
        }

        private static bool IsBlocked(string settingId, SettingType settingType)
        {
            return Describe(settingId, settingType).IsBlocked;
        }

        private static string GetBlockReason(string settingId, SettingType settingType)
        {
            return Describe(settingId, settingType).BlockReason;
        }

        private static string ResolveBlockReason(string settingId, SettingType settingType)
        {
            if (BlockedReasons.TryGetValue(settingId ?? string.Empty, out string reason) &&
                !string.IsNullOrEmpty(reason))
            {
                return reason;
            }

            if (_snapshot.IsActive && !_snapshot.ReadSucceeded)
            {
                return string.IsNullOrEmpty(_snapshot.FailureReason)
                    ? "The active workload preview could not be read safely."
                    : _snapshot.FailureReason;
            }

            return TryGetSupportedPresentationKind(settingId, out _)
                ? "Presentation setting changes are not supported by the current workload commit path. This control is read-only during preview; the global setting is unchanged."
                : "This setting type is not representable by the current Workload V2 presentation contract. It is read-only during preview; the global setting is unchanged.";
        }

        private static bool IsPreviewChanged(string settingId)
        {
            bool hasBefore = _snapshot.Before.TryGetValue(settingId, out WorkloadScalarValue before);
            bool hasAfter = _snapshot.After.TryGetValue(settingId, out WorkloadScalarValue after);
            return hasBefore != hasAfter || (hasBefore && !before.Equals(after));
        }

        private static bool TryGetSupportedPresentationKind(
            string settingId,
            out WorkloadScalarKind kind)
        {
            kind = WorkloadScalarKind.Empty;
            if (string.IsNullOrEmpty(settingId) || !Metadata.ContainsKey(settingId))
            {
                return false;
            }

            SettingDefinition definition = null;
            IReadOnlyList<SettingDefinition> definitions = BWTSettingsRegistry.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                SettingDefinition candidate = definitions[i];
                if (candidate != null &&
                    string.Equals(candidate.Id, settingId, StringComparison.Ordinal))
                {
                    definition = candidate;
                    break;
                }
            }

            if (definition == null ||
                string.IsNullOrEmpty(definition.FieldName) ||
                definition.ValueGetter != null ||
                definition.ValueSetter != null)
            {
                return false;
            }

            const BindingFlags fieldFlags = BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;
            FieldInfo field = typeof(BetterWorkTabSettings).GetField(
                definition.FieldName,
                fieldFlags);
            if (field == null)
            {
                return false;
            }

            if (definition.Type == SettingType.Bool && field.FieldType == typeof(bool))
            {
                kind = WorkloadScalarKind.Boolean;
                return true;
            }

            if ((definition.Type == SettingType.Int ||
                 definition.Type == SettingType.NumericInt) &&
                field.FieldType == typeof(int))
            {
                kind = WorkloadScalarKind.Integer;
                return true;
            }

            return false;
        }

        private static bool IsPresentationKeyOwned(string settingId)
        {
            if (string.IsNullOrEmpty(settingId) || !Metadata.ContainsKey(settingId))
            {
                return false;
            }

            // Ownership is determined by the workload entry itself, not by
            // whether BWT can currently render or commit that scalar type.
            // This lets the settings framework mark unsupported enum/color/
            // float entries read-only instead of silently treating them as
            // global settings.
            return _snapshot.Before.ContainsKey(settingId) ||
                _snapshot.After.ContainsKey(settingId);
        }

        private static bool TryHandleEnumMutation(
            SettingDefinition definition,
            object settingsObject,
            object value)
        {
            BWTWorkloadSettingOwnershipState state = Describe(definition);
            if (!state.IsPreviewActive || !state.IsWorkloadOwnedInActiveTemplate)
            {
                return false;
            }

            return BlockAndHandle(
                definition.Id,
                ResolveBlockReason(definition.Id, definition.Type));
        }

        private static bool BlockAndHandle(string settingId, string reason)
        {
            BlockedReasons[settingId ?? string.Empty] = reason;
            MarkHandledMutation(settingId);
            InvalidateWorkTabPresentation();
            return true;
        }

        private static void MarkHandledMutation(string settingId)
        {
            if (!string.IsNullOrEmpty(settingId))
            {
                SuppressedOnChanged.Add(settingId);
            }
        }

        private static bool ConsumeSuppressedOnChanged(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) && SuppressedOnChanged.Remove(settingId);
        }

        private static void SetFieldValue(
            SettingDefinition definition,
            object settingsObject,
            object value)
        {
            FieldInfo field = FindField(definition, settingsObject);
            field?.SetValue(settingsObject, value);
        }

        private static FieldInfo FindField(
            SettingDefinition definition,
            object settingsObject)
        {
            if (definition == null || settingsObject == null ||
                string.IsNullOrEmpty(definition.FieldName))
            {
                return null;
            }

            Type type = settingsObject.GetType();
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;
            while (type != null)
            {
                FieldInfo field = type.GetField(definition.FieldName, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static void EnsureSnapshot()
        {
            if (!_snapshotValid)
            {
                Refresh();
            }
        }

        private static void InvalidateWorkTabPresentation()
        {
            WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.SettingsThemeLanguageScale);
        }

        private static bool RequiresPreviewSuppression(SettingType type)
        {
            switch (type)
            {
                case SettingType.Bool:
                case SettingType.Enum:
                case SettingType.Color:
                case SettingType.Int:
                case SettingType.Float:
                case SettingType.Slider:
                case SettingType.NumericInt:
                case SettingType.Custom:
                case SettingType.DropdownListAdder:
                    return true;
                default:
                    return false;
            }
        }

        private static Dictionary<string, BWTWorkloadSettingMetadata> BuildMetadata()
        {
            var metadata = new Dictionary<string, BWTWorkloadSettingMetadata>(StringComparer.Ordinal);

            Add(metadata, "Work-type header",
                HeadersCustomWorkLabels,
                HeadersAngled,
                DragdropRemoveHeaderUnderline,
                HeadersAngleRotation,
                HeadersUseVerticalStackingForCJK,
                "headers.cjkVerticalKerning",
                "headers.angledColor",
                HeadersUnderlineColor,
                "headers.horizontalOffset");

            Add(metadata, "Specific-job presentation",
                SubWorkGlobalVanillaPriorityBoxes,
                SubWorkCompactPriorityBoxes,
                SubWorkCrossWorkDragDrop,
                SubWorkAutoExpandColumns,
                SubWorkEvenlyExpandColumns,
                SubWorkOpenButton,
                SubWorkOpenModifier,
                SubWorkDrilldownStyle,
                SubWorkRestoreCursor,
                SubWorkRestoreCursorFromPawnCells,
                SubWorkOverrideBreakAnimation,
                SubWorkTransitionAnimation,
                SubWorkTransitionMode,
                SubWorkTransitionStyle,
                SubWorkTransitionSpeed,
                SubWorkDisabledParentMode,
                FluffyStyleFeatures,
                FluffyStyleTopButtons,
                FluffyStyleStandaloneTopButtons);

            Add(metadata, "Work-tab presentation",
                 FeaturesOverlay,
                 FeaturesHighlights,
                 FeaturesDividers,
                 LayoutWorkTabMinimumWidth,
                LayoutWorkTabMaxVisiblePawns,
                LayoutWorkTabTopSpace,
                LayoutPawnCount,
                LayoutBedCount,
                UiPriorityLegend,
                UiDragInstructions,
                UiManualPriorities,
                OverlayNumbersMode,
                OverlayBestPawnMode,
                OverlayHoverCellOverlay,
                OverlayHoverMode,
                OverlayHoverScope,
                HighlightsHover,
                HighlightsHoverColor,
                HighlightsRowHoverColor,
                HighlightsColumnHoverColor,
                HighlightsSelected,
                HighlightsSelectedColor,
                HighlightsSelectedOpacity,
                HighlightsFloatMenu,
                HighlightsFloatMenuColor,
                HighlightsOutlineMode,
                HighlightsSimilar,
                HighlightsSimilarColor,
                HighlightsSimilarOpacity,
                HighlightsBestPawnBackground,
                ColorsSkillVeryLow,
                ColorsSkillLow,
                ColorsSkillGood,
                ColorsSkillExcellent,
                ColorsBestPawnOutline,
                DividersShow,
                DividersCustomColors,
                DividersLabels,
                DividersCollapse,
                DividersAnimations,
                LayoutDividerHeight,
                LayoutDividerAlpha,
                ColumnsShowMovedIndicator,
                ColumnsShowBaselineLine,
                "columns.showMovedColorTint",
                "columns.movedMarkerColor");

            return metadata;
        }

        private static void Add(
            IDictionary<string, BWTWorkloadSettingMetadata> metadata,
            string region,
            params string[] settingIds)
        {
            if (settingIds == null)
            {
                return;
            }

            for (int i = 0; i < settingIds.Length; i++)
            {
                string settingId = settingIds[i];
                if (!string.IsNullOrEmpty(settingId))
                {
                    metadata[settingId] = new BWTWorkloadSettingMetadata(settingId, region);
                }
            }
        }
    }

    /// <summary>
    /// BWT-owned read boundary for presentation values. Work-tab consumers use
    /// this adapter instead of mixing live settings with preview projection.
    /// Only scalar types that V2 can represent are intentionally supported.
    /// </summary>
    internal static class BWTWorkTabEffectiveSettings
    {
        internal static bool GetBool(string settingId, bool fallback)
        {
            if (BWTWorkloadSettingsOwnershipPolicy.TryGetEffectivePresentationValue(
                    settingId,
                    WorkloadScalarValue.FromBoolean(fallback),
                    out WorkloadScalarValue value) &&
                value.Kind == WorkloadScalarKind.Boolean)
            {
                return value.BooleanValue;
            }

            return fallback;
        }

        internal static int GetInt(string settingId, int fallback)
        {
            if (BWTWorkloadSettingsOwnershipPolicy.TryGetEffectivePresentationValue(
                    settingId,
                    WorkloadScalarValue.FromInteger(fallback),
                    out WorkloadScalarValue value) &&
                value.Kind == WorkloadScalarKind.Integer)
            {
                return value.IntegerValue;
            }

            return fallback;
        }
    }

    // This slice intentionally exposes no presentation-staging capability.
    // Ownership is informational/read-only until a real workload commit
    // writer exists; a capability flag here would invite false staging claims.
    internal struct BWTWorkloadSettingOwnershipState
    {
        internal bool IsKnown;
        internal bool IsGloballyOwned;
        internal bool IsPreviewActive;
        internal bool IsWorkloadOwnedInActiveTemplate;
        internal bool IsChangedByActivePreview;
        internal bool WillRevertToGlobalOutsidePreview;
        internal bool IsBlocked;
        internal string BlockReason;
    }

    internal sealed class BWTWorkloadSettingMetadata
    {
        internal BWTWorkloadSettingMetadata(string settingId, string region)
        {
            SettingId = settingId;
            Region = region;
        }

        internal string SettingId { get; }
        internal string Region { get; }
    }

    internal sealed class BWTWorkloadPreviewSnapshot
    {
        internal static readonly BWTWorkloadPreviewSnapshot Inactive =
            new BWTWorkloadPreviewSnapshot(
                false,
                true,
                false,
                string.Empty,
                string.Empty,
                null,
                null);

        private BWTWorkloadPreviewSnapshot(
            bool isActive,
            bool readSucceeded,
            bool ownsPresentationSettings,
            string sourceId,
            string failureReason,
            IDictionary<string, WorkloadScalarValue> before,
            IDictionary<string, WorkloadScalarValue> after)
        {
            IsActive = isActive;
            ReadSucceeded = readSucceeded;
            OwnsPresentationSettings = ownsPresentationSettings;
            SourceId = sourceId ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
            Before = new Dictionary<string, WorkloadScalarValue>(
                before ?? new Dictionary<string, WorkloadScalarValue>(),
                StringComparer.Ordinal);
            After = new Dictionary<string, WorkloadScalarValue>(
                after ?? new Dictionary<string, WorkloadScalarValue>(),
                StringComparer.Ordinal);
        }

        internal bool IsActive { get; }
        internal bool ReadSucceeded { get; }
        internal bool OwnsPresentationSettings { get; }
        internal string SourceId { get; }
        internal string FailureReason { get; }
        internal Dictionary<string, WorkloadScalarValue> Before { get; }
        internal Dictionary<string, WorkloadScalarValue> After { get; }

        internal static BWTWorkloadPreviewSnapshot FromPlan(WorkloadPreviewPlan plan)
        {
            var before = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            var after = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            if (plan?.BeforeState?.PresentationSettings != null)
            {
                for (int i = 0; i < plan.BeforeState.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry =
                        plan.BeforeState.PresentationSettings[i];
                    if (entry != null)
                    {
                        before[entry.Key] = entry.Value;
                    }
                }
            }

            if (plan?.AfterState?.PresentationSettings != null)
            {
                for (int i = 0; i < plan.AfterState.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry =
                        plan.AfterState.PresentationSettings[i];
                    if (entry != null)
                    {
                        after[entry.Key] = entry.Value;
                    }
                }
            }

            WorkloadOwnershipDimensions ownership =
                plan.SourceTemplate?.Definition?.OwnershipDimensions ?? WorkloadOwnershipDimensions.None;
            return new BWTWorkloadPreviewSnapshot(
                true,
                true,
                ownership.Owns(WorkloadStateDimension.PresentationSettings),
                plan.SourceTemplate?.StableId,
                string.Empty,
                before,
                after);
        }

        internal static BWTWorkloadPreviewSnapshot Failed(string reason)
        {
            return new BWTWorkloadPreviewSnapshot(
                true,
                false,
                false,
                string.Empty,
                reason,
                null,
                null);
        }
    }

    internal interface IBWTWorkloadPreviewController
    {
        BWTWorkloadPreviewSnapshot Read();
    }

    internal sealed class WorkloadGatewayPreviewController : IBWTWorkloadPreviewController
    {
        public BWTWorkloadPreviewSnapshot Read()
        {
            if (!WorkloadGateway.IsV2PreviewSessionActive)
            {
                return BWTWorkloadPreviewSnapshot.Inactive;
            }

            WorkloadOperationResult<WorkloadPreviewPlan> result =
                WorkloadGateway.GetV2PreviewPlan();
            if (!result.Succeeded || result.Value == null)
            {
                return BWTWorkloadPreviewSnapshot.Failed(
                    string.IsNullOrEmpty(result.Message)
                        ? "The active workload preview could not be read safely."
                        : result.Message);
            }

            return BWTWorkloadPreviewSnapshot.FromPlan(result.Value);
        }

    }
}
