using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
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
                UiAutoMaxPriority,
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

            if (layout != null &&
                SubWorkHeaderAffordance.TryGetFocusedBadgeTarget(
                    layout,
                    out WorkTabLayoutColumn _,
                    out Rect focusedBadgeRect) &&
                focusedBadgeRect.Contains(mousePosition))
            {
                return CreateContextRequest(
                    "Focused Specific-jobs Button",
                    "Settings for the fixed return button under the specific job nearest the original Work-header position.",
                    SubWorkFocusedHeaderBadge,
                    true,
                    FeaturesSubWorkJobs,
                    SubWorkFocusedHeaderBadge,
                    SubWorkHeaderBadge,
                    SubWorkOpenButton,
                    SubWorkOpenModifier,
                    SubWorkDrilldownStyle);
            }

            if (layout != null &&
                SubWorkHeaderAffordance.TryGetOpenBadgeTarget(
                    layout,
                    mousePosition,
                    out WorkTypeDef _,
                    out Rect _,
                    out WorkTabLayoutColumn _))
            {
                return CreateContextRequest(
                    "Sub-work Header Button",
                    "Setting that controls the two-line sub-work button under work headers.",
                    SubWorkHeaderBadge,
                    true,
                    FeaturesSubWorkJobs,
                    SubWorkHeaderBadge,
                    SubWorkOpenButton,
                    SubWorkOpenModifier,
                    SubWorkDrilldownStyle);
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
                        SubWorkHeaderBadge,
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
                        HighlightsDisableBestPawn,
                        HighlightsBestPawnBackground);
                }

                return CreateContextRequest(
                    isSubWorkCell ? "Sub-work Priority Cell" : "Priority Cell",
                    "Settings related to priority cells, skill overlays, max priorities, time priority, and sub-work priority behavior.",
                    PriorityHeader,
                    true,
                    PriorityHeader,
                    PriorityModeSetting,
                    UiMaxPriority,
                    UiAutoMaxPriority,
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
                    SubWorkHeaderBadge,
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
                        SubWorkHeaderBadge,
                        SubWorkRestoreCursor,
                        SubWorkTransitionMode,
                        SubWorkTransitionSpeed,
                        LayoutCtrlDrag,
                        FeaturesDragdrop,
                        LayoutDragColumns,
                        LayoutDragThreshold,
                        LayoutDragHoverDelay);
                }

                return CreateContextRequest(
                    isSubWorkHeader ? "Sub-work Headers" : "Work Headers",
                    "Settings related to work headers, column dragging, marker lines, workloads, rulesets, and sub-work drilldown.",
                    HeadersHeader,
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
                    WorkloadsPersistDividers,
                    FeaturesAutoassign,
                    AutoassignViewMode,
                    AutoassignWarnOnApply,
                    FeaturesSubWorkJobs,
                    SubWorkHeaderBadge,
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
                    LayoutDragRows,
                    WorkloadsPersistDividers);
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
                    UiAutoMaxPriority,
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
                    UiAutoMaxPriority,
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
                        WorkloadsPersistDividers,
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
                        AutoassignConfirm,
                        AutoassignWarnOnApply,
                        AutoassignResetBefore,
                        AutoassignVisual,
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
                LayoutWorkTabMaxHeight,
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
                isSubWorkHeader = isWorkHeader && SubWorkDrilldownState.IsActive;
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

                isSubWorkCell = SubWorkDrilldownState.IsActive &&
                    SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column) >= 0;
                return true;
            }

            return false;
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

    }
}
