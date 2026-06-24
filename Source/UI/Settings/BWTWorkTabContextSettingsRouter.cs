using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.UI.Settings
{
    internal static class BWTWorkTabContextSettingsRouter
    {
        private const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;

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

        private static BWTSettingsFocusRequest BuildContextSettingsRequest(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            bool skillOnly,
            bool ctrlOnly)
        {
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
                        SubWorkTransitionAnimation,
                        SubWorkTransitionStyle,
                        SubWorkDisabledParentMode,
                        UiTimePriorityPlannerPrototype,
                        UiChronosPointerTimePriority,
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
                    UiTimePriorityPlannerPrototype,
                    UiChronosPointerTimePriority,
                    UiTimePriorityHourDivider,
                    FeaturesSubWorkJobs,
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
                        SubWorkTransitionAnimation,
                        SubWorkTransitionStyle,
                        LayoutCtrlDrag,
                        FeaturesDragdrop,
                        LayoutDragColumns,
                        LayoutDragThreshold,
                        LayoutDragHoverDelay,
                        DragdropEnableGrouping);
                }

                return CreateContextRequest(
                    isSubWorkHeader ? "Sub-work Headers" : "Work Headers",
                    "Settings related to work headers, column dragging, marker lines, workloads, rulesets, and sub-work drilldown.",
                    HeadersHeader,
                    true,
                    HeadersHeader,
                    HeadersAngled,
                    DragdropRemoveHeaderUnderline,
                    HeadersAngleRotation,
                    HeadersUseVerticalStackingForCJK,
                    "headers.cjkVerticalKerning",
                    "headers.angledColor",
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
                    SubWorkOpenModifier,
                    SubWorkOpenButton,
                    SubWorkTransitionAnimation,
                    SubWorkTransitionStyle,
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
                    SubWorkGlobalVanillaPriorityBoxes,
                    SubWorkTransitionAnimation,
                    SubWorkTransitionStyle,
                    SubWorkAutoExpandColumns,
                    SubWorkEvenlyExpandColumns,
                    UiTimePriorityPlannerPrototype,
                    UiChronosPointerTimePriority);
            }

            if (layout != null && TryGetTimePriorityContext(layout, mousePosition))
            {
                return CreateContextRequest(
                    "Time Priority",
                    "Settings related to time priority rows and Chronos Pointer integration.",
                    UiTimePriorityPlannerPrototype,
                    true,
                    UiTimePriorityPlannerPrototype,
                    UiChronosPointerTimePriority,
                    UiTimePriorityHourDivider,
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

            if (TryGetBottomCountersContext(inRect, mousePosition))
            {
                return CreateContextRequest(
                    "Bottom Counters",
                    "Settings related to the bottom-left pawn and bed counters.",
                    LayoutPawnCount,
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

            if (GetInfoIconRect(inRect).Contains(mousePosition))
            {
                return CreateContextRequest(
                    "Settings Shortcut",
                    "Settings related to UI controls and settings management.",
                    FeaturesUiElements,
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
                    SubWorkTransitionAnimation,
                    SubWorkTransitionStyle);
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
                PriorityHeader,
                UiContextSettingsHint,
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
                Rect headerRect = GetAnimatedHeaderRect(column);
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

                Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
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

            float y = layout.TableOrigin.y + layout.HeaderHeight + TimePriorityPlannerPrototype.HeaderPinnedRowsHeight;
            Rect rect = new Rect(
                layout.TableOrigin.x,
                y,
                Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                Mathf.Max(1f, SubWorkDrilldownBarRenderer.ReservedRowHeight));
            return rect.Contains(mousePosition);
        }

        private static bool TryGetTimePriorityContext(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            if (layout == null || TimePriorityPlannerPrototype.HeaderPinnedRowsHeight <= 0.5f)
            {
                return false;
            }

            Rect rect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight,
                Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                TimePriorityPlannerPrototype.HeaderPinnedRowsHeight);
            return rect.Contains(mousePosition);
        }

        private static bool TryGetManualPrioritiesContext(Vector2 mousePosition)
        {
            return new Rect(5f, 5f, 220f, 90f).Contains(mousePosition);
        }

        private static bool TryGetPriorityLegendContext(Rect inRect, Vector2 mousePosition)
        {
            return new Rect(350f, inRect.y + 2f, 470f, 36f).Contains(mousePosition);
        }

        private static bool TryGetBottomCountersContext(Rect inRect, Vector2 mousePosition)
        {
            return new Rect(inRect.x + 6f, inRect.yMax - 48f, inRect.width * 0.5f, 24f).Contains(mousePosition);
        }

        private static bool TryGetBottomInstructionsContext(Rect inRect, Vector2 mousePosition)
        {
            return new Rect(inRect.x, inRect.yMax - 30f, inRect.width * 0.55f, 30f).Contains(mousePosition);
        }

        private static bool TryGetBottomButtonContext(
            Rect inRect,
            Vector2 mousePosition,
            out bool isWorkloadButton,
            out bool isRulesetButton)
        {
            isWorkloadButton = false;
            isRulesetButton = false;

            HeaderButtons.BottomButtonRects rects = HeaderButtons.GetBottomButtonRects(inRect, GetInfoIconRect(inRect));
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
            Rect exitRect = new Rect(
                inRect.xMax - buttonSize - RightEdgeMargin,
                inRect.y + 8f,
                buttonSize,
                buttonSize);
            return exitRect.Contains(mousePosition);
        }

        private static Rect GetAnimatedHeaderRect(WorkTabLayoutColumn column)
        {
            float offset = ColumnReorderAnimationState.GetHeaderOffset(column);
            return Mathf.Abs(offset) > 0.01f
                ? new Rect(column.HeaderRect.x + offset, column.HeaderRect.y, column.HeaderRect.width, column.HeaderRect.height)
                : column.HeaderRect;
        }

        private static Rect GetInfoIconRect(Rect inRect)
        {
            return new Rect(
                inRect.xMax - InfoIconSize - RightEdgeMargin,
                inRect.yMax - InfoIconSize - 10f,
                InfoIconSize,
                InfoIconSize);
        }
    }
}
