using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.Headers;
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
                        "Settings for the specific-job view and its workload display options.",
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
                    "Settings for Work-type labels, angled headers, column markers, and workload display options.",
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
                    return CreateWorkloadContextRequest(
                        "Workload Buttons",
                        "Controls workload buttons, preview animation, inspection highlights, saved workloads, and warnings.",
                        FeaturesWorkloads,
                        false,
                        FeaturesWorkloads,
                        WorkloadsPreviewRevealAnimation,
                        WorkloadsPreviewRevealSpeed,
                        WorkloadsInspectionHighlights,
                        WorkloadsInspectionOpacity,
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
                    "Settings for the Work tab layout, counters, legend, and display. During a workload preview, you can edit supported settings. Other settings are hidden.",
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
            return WorkTabChromeGeometry.GetManualPrioritiesContextRect().Contains(mousePosition);
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

            if (rects.ContainsOptionalFooter(mousePosition))
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
        private static WorkTabApplication _application;

        internal static void BindApplication(WorkTabApplication application)
        {
            _application = application;
        }

        private static readonly HashSet<string> Metadata =
            BuildMetadata();

        // Metadata is the prepared global-value/visibility set, not the
        // workload ownership set. Global-only consumers must be captured here
        // while remaining absent from StageablePresentationSettingIds below.

        private static readonly HashSet<SettingDefinition> PreparedDefinitions =
            new HashSet<SettingDefinition>();

        // Keep an index of the complete schema, not only presentation rows.
        // Preview visibility must retain the hierarchy path to an editable
        // child while removing unrelated locked rows.
        private static readonly Dictionary<string, SettingDefinition>
            PreparedDefinitionsById =
                new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);

        private static readonly HashSet<string> PreviewStructuralDefinitionIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool _previewStructureDirty;

        private static readonly Dictionary<SettingDefinition, FieldInfo> PreparedFields =
            new Dictionary<SettingDefinition, FieldInfo>();

        private static readonly Dictionary<string, BWTPresentationSettingDefinitionState>
            PreparedPresentationSettings =
                new Dictionary<string, BWTPresentationSettingDefinitionState>(StringComparer.Ordinal);

        private static readonly HashSet<string> StageablePresentationSettingIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                LayoutPawnCount,
                LayoutBedCount,
                UiPriorityLegend,
                UiDragInstructions,
                HeadersAngled,
                DragdropRemoveHeaderUnderline,
                HeadersAngleRotation,
                "headers.horizontalOffset",
                "headers.angledColor",
                HeadersUnderlineColor,
                WorkloadsPreviewRevealAnimation,
                WorkloadsPreviewRevealSpeed,
                WorkloadsInspectionHighlights,
                WorkloadsInspectionOpacity
            };

        private static readonly Dictionary<string, string> BlockedReasons =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly HashSet<string> SuppressedOnChanged =
            new HashSet<string>(StringComparer.Ordinal);

        private static BWTPresentationSnapshot _snapshot =
            BWTPresentationSnapshot.CreateInactive(
                new Dictionary<string, PresentationValue>(StringComparer.Ordinal));
        private static IWorkTabPresentationPreviewPort _previewPort;
        private static string _observedPreviewIdentity = string.Empty;
        private static long _snapshotSettingsRevision = long.MinValue;
        private static bool _snapshotValid;
        private static bool _legacyModeBeforeInteraction;
        private static bool _legacyModeBeforeInteractionCaptured;
        private static bool _lastObservedLegacyMode;
        private static bool _lastObservedLegacyModeValid;
        private static bool _suppressNextGlobalSettingsWrite;
        [ThreadStatic]
        private static int _authorizedGlobalSettingsWriteDepth;
        private static Func<bool, bool, string> _presentationModeTransition;

        /// <summary>
        /// Registers the optional preview boundary used by the settings drawer.
        /// A null registration deliberately leaves the drawer fully global and
        /// testable without a workload implementation.
        /// </summary>
        internal static void RegisterPresentationPreviewPort(
            IWorkTabPresentationPreviewPort previewPort)
        {
            if (ReferenceEquals(_previewPort, previewPort))
            {
                return;
            }

            _previewPort = previewPort;
            _observedPreviewIdentity = string.Empty;
            Invalidate();
        }

        internal static void PrepareDefinition(SettingDefinition definition)
        {
            if (definition == null || !PreparedDefinitions.Add(definition))
            {
                return;
            }

            if (!string.IsNullOrEmpty(definition.Id))
            {
                PreparedDefinitionsById[definition.Id] = definition;
                _previewStructureDirty = true;
            }

            InstallPreviewVisibility(definition);

            if (!Metadata.Contains(definition.Id))
            {
                return;
            }

            FieldInfo field = FindSettingsField(definition);
            if (field != null)
            {
                PreparedFields[definition] = field;
            }

            SettingType originalType = definition.Type;
            if (TryCreatePresentationDefinitionState(
                    definition,
                    field,
                    out BWTPresentationSettingDefinitionState rowState))
            {
                PreparedPresentationSettings[definition.Id] = rowState;
                if (IsStageablePresentationSetting(definition.Id))
                {
                    WrapStageableOnChanged(definition);
                    InstallStageableDefinition(rowState);
                }
            }
            else if (definition.Type == SettingType.Enum)
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

            if (RequiresPreviewSuppression(originalType) &&
                !IsStageablePresentationSetting(definition.Id))
            {
                if (definition.Suppressions == null)
                {
                    definition.Suppressions = new List<SettingSuppression>();
                }

                definition.Suppressions.Add(new SettingSuppression
                {
                    // Keep structural rows visible without activating a
                    // suppression that would block their editable children.
                    When = _ => !IsPreviewStructuralDefinition(definition) &&
                        Describe(definition).IsBlocked,
                    Reason = _ => IsPreviewStructuralDefinition(definition)
                        ? string.Empty
                        : Describe(definition).BlockReason,
                    SuppressorSettingId = PreviewSuppressorId,
                    LinkLabel = "Workload preview"
                });
            }

            if (string.Equals(definition.ParentId, HeadersAngled, StringComparison.Ordinal))
            {
                if (definition.Suppressions == null)
                {
                    definition.Suppressions = new List<SettingSuppression>();
                }

                definition.Suppressions.Add(new SettingSuppression
                {
                    When = _ => !IsAngledHeaderModeEnabled(),
                    Reason = _ => "Enable angled Work headers to edit this setting.",
                    SuppressorSettingId = HeadersAngled,
                    LinkLabel = "Angled headers"
                });
            }
        }

        internal static void Refresh()
        {
            IDictionary<string, PresentationValue> globalValues =
                _snapshot.GlobalValues;
            try
            {
                BWTSettingsRegistry.EnsureInitialized();
                globalValues = CapturePreparedPresentationValues(BetterWorkTabMod.Settings);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[BWT] Workload settings global presentation snapshot read failed: " + ex);
                globalValues = CapturePreparedPresentationValues(
                    BetterWorkTabMod.Settings);
                SetSnapshot(BWTPresentationSnapshot.Failed(
                    globalValues,
                    "Couldn't read the global display settings.",
                    _observedPreviewIdentity,
                    !string.IsNullOrEmpty(_observedPreviewIdentity)));
                return;
            }

            BWTPresentationSnapshot next;
            try
            {
                IWorkTabPresentationPreviewPort previewPort = _previewPort;
                if (previewPort == null || !previewPort.IsPreviewActive)
                {
                    next = BWTPresentationSnapshot.CreateInactive(globalValues);
                }
                else
                {
                    if (previewPort.TryReadPresentationPreview(
                            out WorkTabPresentationPreviewState preview,
                            out string reason))
                    {
                        next = string.IsNullOrEmpty(preview.Identity)
                            ? BWTPresentationSnapshot.Failed(
                                globalValues,
                                "The workload preview is no longer available.",
                                string.Empty)
                            : BWTPresentationSnapshot.FromPreview(
                                preview,
                                globalValues);
                    }
                    else
                    {
                        next = BWTPresentationSnapshot.Failed(
                            globalValues,
                            string.IsNullOrEmpty(reason)
                                ? "Couldn't read the workload preview."
                                : reason,
                            _observedPreviewIdentity,
                            isActive: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[BWT] Active workload presentation preview read failed: " + ex);
                next = BWTPresentationSnapshot.Failed(
                    globalValues,
                    "Couldn't read the workload preview.",
                    _observedPreviewIdentity,
                    isActive: _previewPort?.IsPreviewActive == true);
            }

            SetSnapshot(next);
        }

        internal static void EnsureFresh()
        {
            if (!_snapshotValid || _snapshotSettingsRevision != WorkTabPresentationRevision.Current)
            {
                Refresh();
            }
        }

        private static void SetSnapshot(BWTPresentationSnapshot next)
        {
            if (!next.IsActive ||
                !StringComparer.Ordinal.Equals(_snapshot.Identity, next.Identity))
            {
                BlockedReasons.Clear();
            }

            _snapshot = next;
            _snapshotValid = true;
            _snapshotSettingsRevision = WorkTabPresentationRevision.Current;

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
                    : requestedLegacy;
            _legacyModeBeforeInteractionCaptured = false;

            if (requestedLegacy == previousLegacy)
            {
                return;
            }

            // The shared drawer writes the bool before invoking OnChanged.
            // Restore the source value before asking the optional backend
            // adapter to perform the transition.
            settings.useLegacyWorkloads = previousLegacy;
            if (IsPreviewActive)
            {
                _lastObservedLegacyMode = previousLegacy;
                _lastObservedLegacyModeValid = true;
                Messages.Message(
                    "BWT_Settings_WorkloadMode_PreviewBlocked".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (TryTransitionMode(requestedLegacy, persist: true, out string reason))
            {
                _lastObservedLegacyMode = requestedLegacy;
                _lastObservedLegacyModeValid = true;
                return;
            }

            settings.useLegacyWorkloads = previousLegacy;
            _lastObservedLegacyMode = previousLegacy;
            _lastObservedLegacyModeValid = true;
            Messages.Message(
                string.IsNullOrEmpty(reason)
                    ? "Couldn't change workload mode."
                    : reason,
                MessageTypeDefOf.RejectInput,
                false);
        }

        internal static void RegisterPresentationModeTransition(
            Func<bool, bool, string> transition)
        {
            _presentationModeTransition = transition;
        }

        internal static bool TryTransitionMode(
            bool useLegacy,
            bool persist,
            out string reason)
        {
            reason = string.Empty;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                reason = "No Better Work Tab settings are available.";
                return false;
            }

            try
            {
                long revisionBeforeTransition = WorkTabPresentationRevision.Current;
                if (_presentationModeTransition != null)
                {
                    reason = _presentationModeTransition(useLegacy, persist) ?? string.Empty;
                    if (!string.IsNullOrEmpty(reason))
                    {
                        return false;
                    }
                }
                else
                {
                    settings.useLegacyWorkloads = useLegacy;
                    if (persist)
                    {
                        settings.Write();
                    }
                }
                if (WorkTabPresentationRevision.Current == revisionBeforeTransition)
                {
                    NotifyGlobalSettingsChanged(AdvancedWorkloadsLegacy);
                }
            }
            catch (Exception ex)
            {
                reason = "Workload mode transition failed: " + ex.Message;
                return false;
            }
            return true;
        }

        internal static void Invalidate()
        {
            _snapshotValid = false;
            SuppressedOnChanged.Clear();
            _suppressNextGlobalSettingsWrite = false;
        }

        internal static long GlobalSettingsRevision => WorkTabPresentationRevision.Current;

        /// <summary>
        /// Called by the optional preview boundary whenever its immutable
        /// presentation identity changes. Settings never needs the concrete
        /// session object to invalidate its prepared projection.
        /// </summary>
        internal static void ObservePreviewIdentity(string previewIdentity)
        {
            string safeIdentity = previewIdentity ?? string.Empty;
            if (StringComparer.Ordinal.Equals(_observedPreviewIdentity, safeIdentity))
            {
                return;
            }

            _observedPreviewIdentity = safeIdentity;
            BlockedReasons.Clear();
            InvalidateWorkTabPresentationCore(includesHeaderSetting: true);
        }

        /// <summary>
        /// The one local route for ordinary settings writes. It advances the
        /// cache token before invalidating presentation consumers, including
        /// compatibility-owned writes that bypass the settings drawer.
        /// </summary>
        internal static long NotifyGlobalSettingsChanged(string settingId = null)
        {
            return AdvanceGlobalSettingsRevision(IsHeaderPresentationSetting(settingId));
        }

        internal static long BeginOwnedGlobalSettingsMutation(
            IEnumerable<string> settingIds)
        {
            return AdvanceGlobalSettingsRevision(IncludesHeaderSetting(settingIds));
        }

        private static long AdvanceGlobalSettingsRevision(bool includesHeaderSetting)
        {
            long revision = WorkTabPresentationRevision.Advance();
            InvalidateWorkTabPresentationCore(includesHeaderSetting);
            return revision;
        }

        internal static void WriteSettings(BetterWorkTabSettings settings)
        {
            if (_suppressNextGlobalSettingsWrite)
            {
                _suppressNextGlobalSettingsWrite = false;
                return;
            }

            if (IsPreviewActive && _authorizedGlobalSettingsWriteDepth <= 0)
            {
                return;
            }

            settings?.Write();
        }

        internal static bool IsPreviewActive => _previewPort?.IsPreviewActive == true;

        /// <summary>
        /// The active preview may authorize an explicit global-settings
        /// transaction while ordinary drawer writes remain suppressed.
        /// Ownership of the lease stays with the caller that owns the
        /// transaction; disposing it restores the normal write guard.
        /// </summary>
        internal static IDisposable BeginAuthorizedGlobalSettingsWrite()
        {
            _authorizedGlobalSettingsWriteDepth++;
            return new GlobalSettingsWriteLease();
        }

        private sealed class GlobalSettingsWriteLease : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_authorizedGlobalSettingsWriteDepth > 0)
                {
                    _authorizedGlobalSettingsWriteDepth--;
                }
            }
        }

        private static bool TryCreatePresentationDefinitionState(
            SettingDefinition definition,
            FieldInfo field,
            out BWTPresentationSettingDefinitionState state)
        {
            state = null;
            if (definition == null ||
                field == null ||
                definition.ValueGetter != null ||
                definition.ValueSetter != null)
            {
                return false;
            }

            PresentationValueKind kind;
            if (definition.Type == SettingType.Bool && field.FieldType == typeof(bool))
            {
                kind = PresentationValueKind.Boolean;
            }
            else if ((definition.Type == SettingType.Int ||
                      definition.Type == SettingType.NumericInt) &&
                     field.FieldType == typeof(int))
            {
                kind = PresentationValueKind.Integer;
            }
            else if (definition.Type == SettingType.Color && field.FieldType == typeof(Color))
            {
                kind = PresentationValueKind.String;
            }
            else
            {
                return false;
            }

            var preparedState = new BWTPresentationSettingDefinitionState(
                definition,
                definition.Type,
                kind,
                field);
            if (!TryToScalar(
                    preparedState,
                    definition.DefaultValue,
                    true,
                    out PresentationValue defaultScalar))
            {
                return false;
            }

            preparedState.DefaultScalar = defaultScalar;
            state = preparedState;
            return true;
        }

        private static void InstallStageableDefinition(
            BWTPresentationSettingDefinitionState state)
        {
            SettingDefinition definition = state.Definition;
            definition.Type = SettingType.Custom;
            definition.CustomDrawer = (rect, label, tooltip, settingsObject, disabled) =>
                DrawStageableSettingRow(state, rect, label, tooltip, settingsObject, disabled);
            definition.CustomHasNonDefaultValue = _ => HasStageableNonDefaultValue(state);
            definition.CustomReset = settingsObject =>
            {
                PresentationValue defaultValue = state.DefaultScalar;
                BWTPresentationSettingOwnershipState ownership = Describe(definition);
                if (ownership.IsPreviewActive)
                {
                    // CustomReset cannot report failure to the shared drawer,
                    // which always invokes OnChanged and the page write callback
                    // after this delegate returns. Suppress both callbacks even
                    // when projected staging fails so reset remains fail-closed.
                    SuppressSettingsCallbacks(definition.Id);
                    if (CanStageOwnedSetting(ownership))
                    {
                        TryStageScalar(
                            state,
                            defaultValue,
                            suppressDrawerCallbacks: false,
                            out _);
                    }
                    else if (!ownership.IsBlocked)
                    {
                        BlockedReasons[definition.Id] =
                            "This setting uses the global value. Select 'Use in workload' to edit it here.";
                    }

                    return;
                }

                TryWriteGlobalScalar(state, settingsObject, defaultValue, out _);
            };
        }

        private static void WrapStageableOnChanged(SettingDefinition definition)
        {
            Action<object> existingOnChanged = definition.OnChanged;
            definition.OnChanged = settingsObject =>
            {
                if (ConsumeSuppressedOnChanged(definition.Id))
                {
                    return;
                }

                existingOnChanged?.Invoke(settingsObject);
            };
        }

        private static bool DrawStageableSettingRow(
            BWTPresentationSettingDefinitionState state,
            Rect rect,
            string label,
            string tooltip,
            object settingsObject,
            bool disabled)
        {
            if (state == null || !(settingsObject is BetterWorkTabSettings))
            {
                return false;
            }

            if (!TryGetCachedGlobalScalar(state, out PresentationValue fallback))
            {
                return false;
            }

            BWTPresentationSettingOwnershipState ownership = Describe(state.Definition);
            bool stageOwned = CanStageOwnedSetting(ownership);
            bool previewOwnedButUnreadable = ownership.IsPreviewActive &&
                ownership.IsWorkloadOwnedInActiveTemplate &&
                ownership.IsBlocked;
            bool showOwnershipAction = ownership.IsPreviewActive &&
                !ownership.IsBlocked &&
                IsStageablePresentationSetting(state.Definition.Id);
            bool effectiveReadFailed = false;
            PresentationValue current = fallback;
            if (stageOwned)
            {
                if (!TryGetEffectivePresentationValue(state, fallback, out current))
                {
                    current = fallback;
                    effectiveReadFailed = true;
                }
            }

            Rect valueRect = rect;
            if (showOwnershipAction)
            {
                const float ownershipActionMinimumWidth = 96f;
                const float ownershipActionHorizontalPadding = 16f;
                string fullActionLabel = ownership.IsWorkloadOwnedInActiveTemplate
                    ? "Use global"
                    : "Use in workload";
                string compactActionLabel = ownership.IsWorkloadOwnedInActiveTemplate
                    ? "Global"
                    : "Workload";
                float fullActionWidth = Mathf.Max(
                    ownershipActionMinimumWidth,
                    Text.CalcSize(fullActionLabel).x + ownershipActionHorizontalPadding);
                float compactActionWidth = Mathf.Max(
                    64f,
                    Text.CalcSize(compactActionLabel).x + ownershipActionHorizontalPadding);
                string actionLabel = fullActionLabel;
                float actionWidth = fullActionWidth;
                if (state.OriginalType == SettingType.NumericInt)
                {
                    BWTWorkloadPresentationNumericOwnershipLayout numericLayout =
                        BWTWorkloadPresentationNumericLayout.Create(
                            rect.x,
                            rect.width,
                            fullActionWidth,
                            compactActionWidth);
                    if (!numericLayout.CanFit)
                    {
                        // Do not draw a clipped ownership action across the
                        // neutral numeric row below the supported one-row
                        // geometry.
                        showOwnershipAction = false;
                    }
                    else
                    {
                        actionLabel = numericLayout.UseCompactActionLabel
                            ? compactActionLabel
                            : fullActionLabel;
                        actionWidth = numericLayout.ActionWidth;
                        valueRect = new Rect(
                            rect.x,
                            rect.y,
                            numericLayout.NumericInputWidth,
                            rect.height);
                    }
                }
                else if (rect.width < fullActionWidth)
                {
                    if (rect.width < compactActionWidth)
                    {
                        showOwnershipAction = false;
                    }
                    else
                    {
                        actionLabel = compactActionLabel;
                        actionWidth = compactActionWidth;
                    }
                }

                if (showOwnershipAction)
                {
                    Rect ownershipRect = new Rect(
                        rect.xMax - actionWidth,
                        rect.y + 2f,
                        actionWidth,
                        Mathf.Max(0f, rect.height - 4f));
                    if (state.OriginalType != SettingType.NumericInt)
                    {
                        valueRect.xMax = ownershipRect.xMin -
                            BWTWorkloadPresentationNumericLayout.OwnershipActionGap;
                    }

                    string actionTooltip = ownership.IsWorkloadOwnedInActiveTemplate
                        ? "Use the global setting in this preview."
                        : "Add this setting to the workload preview. Global settings won't change.";
                    if (SettingWidgets.DrawButton(
                            ownershipRect,
                            actionLabel,
                            actionTooltip,
                            disabled))
                    {
                        bool changed = ownership.IsWorkloadOwnedInActiveTemplate
                            ? TryReleasePresentationSetting(state.Definition.Id, out _)
                            : TryAcquirePresentationSetting(
                                state,
                                fallback,
                                out _);
                        if (!changed)
                        {
                            SuppressSettingsCallbacks(state.Definition.Id);
                        }

                        return false;
                    }
                }
            }

            // An unowned allowlisted row is deliberately rendered from the
            // global value but disabled. Its only active control is the
            // explicit ownership action above; no global field may be written
            // while the preview is open.
            bool rowDisabled = disabled || previewOwnedButUnreadable ||
                effectiveReadFailed ||
                (ownership.IsPreviewActive && !stageOwned);
            switch (state.OriginalType)
            {
                case SettingType.Bool:
                    if (current.Kind != PresentationValueKind.Boolean)
                    {
                        return false;
                    }

                    bool boolValue = current.BooleanValue;
                    bool boolChanged = state.Definition.EmphasizeAsHeader ||
                        state.Definition.ControlsChildVisibility
                        ? SettingWidgets.DrawHeaderBool(
                            valueRect,
                            label,
                            ref boolValue,
                            state.Definition.HeaderColor,
                            tooltip,
                            rowDisabled)
                        : SettingWidgets.DrawBool(
                            valueRect,
                            label,
                            ref boolValue,
                            tooltip,
                            rowDisabled);
                    if (!boolChanged)
                    {
                        return false;
                    }

                    return ApplyDrawnScalarValue(
                        state,
                        settingsObject,
                        PresentationValue.FromBoolean(boolValue),
                        stageOwned);

                case SettingType.Int:
                case SettingType.NumericInt:
                    if (current.Kind != PresentationValueKind.Integer)
                    {
                        return false;
                    }

                    int intValue = current.IntegerValue;
                    int min = state.Definition.MinValue.HasValue
                        ? Mathf.RoundToInt(state.Definition.MinValue.Value)
                        : int.MinValue;
                    int max = state.Definition.MaxValue.HasValue
                        ? Mathf.RoundToInt(state.Definition.MaxValue.Value)
                        : int.MaxValue;
                    if (min > max)
                    {
                        int temporary = min;
                        min = max;
                        max = temporary;
                    }

                    bool intChanged = state.OriginalType == SettingType.NumericInt
                        ? SettingWidgets.DrawNumericInt(
                            valueRect,
                            label,
                            ref intValue,
                            min,
                            max,
                            tooltip,
                            rowDisabled)
                        : SettingWidgets.DrawInt(
                            valueRect,
                            label,
                            ref intValue,
                            min,
                            max,
                            tooltip,
                            rowDisabled);
                    if (!intChanged)
                    {
                        return false;
                    }

                    intValue = NormalizeStagedInteger(state.Definition.Id, intValue);
                    return ApplyDrawnScalarValue(
                        state,
                        settingsObject,
                        PresentationValue.FromInteger(intValue),
                        stageOwned);

                case SettingType.Color:
                    if (!TryGetColor(current, out Color colorValue))
                    {
                        return false;
                    }

                    if (!rowDisabled && Mouse.IsOver(valueRect))
                    {
                        WorkTabColorPreviewController.Instance.PreviewHover(
                            state.Definition,
                            colorValue);
                    }

                    SettingWidgets.DrawColor(
                        valueRect,
                        label,
                        ref colorValue,
                        tooltip,
                        rowDisabled,
                        state.OpenColorPicker);
                    return false;

                default:
                    return false;
            }
        }

        private static bool ApplyDrawnScalarValue(
            BWTPresentationSettingDefinitionState state,
            object settingsObject,
            PresentationValue value,
            bool stageOwned)
        {
            if (stageOwned)
            {
                return TryStageScalar(
                    state,
                    value,
                    suppressDrawerCallbacks: true,
                    out _);
            }

            return TryWriteGlobalScalar(state, settingsObject, value, out _);
        }

        internal static void OpenColorPicker(
            BWTPresentationSettingDefinitionState state,
            Color initialColor)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (state == null || settings == null)
            {
                return;
            }

            bool stageOwned = CanStageOwnedSetting(Describe(state.Definition));
            WorkTabColorPreviewController colorPreview = WorkTabColorPreviewController.Instance;
            colorPreview.BeginPicker(state.Definition, initialColor);

            Color committedColor = initialColor;
            bool previewEnded = false;
            bool previewRestored = false;
            bool closeCommitted = false;

            Action endPreview = () =>
            {
                if (previewEnded)
                {
                    return;
                }

                previewEnded = true;
                colorPreview.EndPicker(state.Definition);
            };

            Action restorePreview = () =>
            {
                if (previewRestored)
                {
                    return;
                }

                previewRestored = true;
                if (stageOwned)
                {
                    TryStageColor(state, committedColor);
                }
            };

            var dialog = new Spine.UI.ColourPicker.Dialog_ColourPicker(
                initialColor,
                (newColor, closing) =>
                {
                    bool accepted = stageOwned
                        ? TryStageColor(state, newColor)
                        : TryCommitGlobalScalar(
                            state,
                            settings,
                            ToColorScalar(newColor),
                            out _);
                    if (accepted)
                    {
                        committedColor = newColor;
                        closeCommitted = closing;
                    }
                },
                previewCallback: newColor =>
                {
                    colorPreview.PreviewPicker(state.Definition, newColor);
                    if (stageOwned)
                    {
                        TryStageColor(state, newColor);
                    }
                });

            dialog.onCancel = () =>
            {
                restorePreview();
                endPreview();
            };
            dialog.onPostClose = () =>
            {
                if (!closeCommitted)
                {
                    restorePreview();
                }

                endPreview();
            };
            Find.WindowStack.Add(dialog);
        }

        private static bool TryStageColor(
            BWTPresentationSettingDefinitionState state,
            Color color)
        {
            return TryStageScalar(
                state,
                ToColorScalar(color),
                suppressDrawerCallbacks: false,
                out _);
        }

        private static bool HasStageableNonDefaultValue(BWTPresentationSettingDefinitionState state)
        {
            if (state == null)
            {
                return false;
            }

            PresentationValue defaultValue = state.DefaultScalar;
            if (!TryGetCachedGlobalScalar(state, out PresentationValue fallback))
            {
                return false;
            }

            BWTPresentationSettingOwnershipState ownership = Describe(state.Definition);
            PresentationValue current = fallback;
            if (CanStageOwnedSetting(ownership) &&
                !TryGetEffectivePresentationValue(state, fallback, out current))
            {
                return false;
            }

            return !current.Equals(defaultValue);
        }

        private static bool CanStageOwnedSetting(
            BWTPresentationSettingOwnershipState ownership)
        {
            return ownership.IsPreviewActive &&
                ownership.IsWorkloadOwnedInActiveTemplate &&
                !ownership.IsBlocked;
        }

        private static bool TryStageScalar(
            BWTPresentationSettingDefinitionState state,
            PresentationValue value,
            bool suppressDrawerCallbacks,
            out string reason)
        {
            reason = string.Empty;
            if (state == null || value.Kind != state.ScalarKind)
            {
                reason = "This setting can't be used in the workload preview.";
                if (state != null)
                {
                    BlockedReasons[state.Definition.Id] = reason;
                }

                return false;
            }

            return TryPreviewMutation(
                state.Definition.Id,
                new WorkTabPresentationPreviewMutation(
                    WorkTabPresentationPreviewMutationKind.Set,
                    state.Definition.Id,
                    value),
                "Couldn't update this workload setting.",
                "Couldn't sync this workload setting.",
                suppressDrawerCallbacks,
                out reason);
        }

        private static bool TryAcquirePresentationSetting(
            BWTPresentationSettingDefinitionState state,
            PresentationValue globalValue,
            out string reason)
        {
            string settingId = state?.Definition?.Id;
            return TryChangePresentationOwnership(
                settingId,
                new WorkTabPresentationPreviewMutation(
                    WorkTabPresentationPreviewMutationKind.Acquire,
                    settingId,
                    globalValue),
                "Couldn't add this setting to the workload.",
                out reason);
        }

        private static bool TryReleasePresentationSetting(
            string settingId,
            out string reason)
        {
            return TryChangePresentationOwnership(
                settingId,
                new WorkTabPresentationPreviewMutation(
                    WorkTabPresentationPreviewMutationKind.Release,
                    settingId,
                    PresentationValue.Empty),
                "Couldn't use the global setting.",
                out reason);
        }

        private static bool TryChangePresentationOwnership(
            string settingId,
            WorkTabPresentationPreviewMutation mutation,
            string rejectionReason,
            out string reason)
        {
            reason = string.Empty;
            if (!IsStageablePresentationSetting(settingId))
            {
                reason = "This setting isn't available in workload previews.";
                return false;
            }

            BWTPresentationSettingOwnershipState ownership = Describe(settingId);
            if (!ownership.IsPreviewActive || ownership.IsBlocked)
            {
                reason = ownership.BlockReason ??
                    "This workload preview isn't available for changes.";
                return false;
            }

            return TryPreviewMutation(
                settingId,
                mutation,
                rejectionReason,
                "Couldn't sync this workload setting.",
                false,
                out reason);
        }

        private static bool TryPreviewMutation(
            string settingId,
            WorkTabPresentationPreviewMutation mutation,
            string rejectionReason,
            string synchronizationReason,
            bool suppressDrawerCallbacks,
            out string reason)
        {
            reason = string.Empty;
            IWorkTabPresentationPreviewPort previewPort = _previewPort;
            if (previewPort == null || !previewPort.IsPreviewActive)
            {
                reason = "This workload preview isn't available for editing.";
            }
            else
            {
                if (previewPort.TryMutatePresentation(mutation, out string mutationReason))
                {
                    InvalidateWorkTabPresentation(settingId);
                    Refresh();
                    if (suppressDrawerCallbacks)
                    {
                        SuppressSettingsCallbacks(settingId);
                    }

                    return true;
                }

                reason = string.IsNullOrEmpty(mutationReason)
                    ? rejectionReason
                    : mutationReason;
            }

            BlockedReasons[settingId ?? string.Empty] = reason;
            return false;
        }

        private static bool TryCommitGlobalScalar(
            BWTPresentationSettingDefinitionState state,
            object settingsObject,
            PresentationValue value,
            out string reason)
        {
            reason = string.Empty;
            if (!TryWriteGlobalScalar(state, settingsObject, value, out reason))
            {
                return false;
            }

            state.Definition.OnChanged?.Invoke(settingsObject);
            if (settingsObject is BetterWorkTabSettings settings)
            {
                WriteSettings(settings);
            }

            return true;
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
                    ? "Settings import and Restore defaults are unavailable because this workload preview can't be read."
                    : _snapshot.FailureReason;
                return true;
            }

            reason = "Close the workload preview before importing settings or restoring defaults.";
            return true;
        }

        private static bool TryGetEffectivePresentationValue(
            BWTPresentationSettingDefinitionState state,
            PresentationValue fallback,
            out PresentationValue value)
        {
            value = fallback;
            if (state == null)
            {
                return false;
            }

            BWTPresentationSnapshot snapshot = PresentationSnapshot;
            if (!snapshot.IsActive || !snapshot.ReadSucceeded)
            {
                return false;
            }

            if (fallback.Kind != state.ScalarKind)
            {
                return false;
            }

            return snapshot.TryResolveOwnedScalar(
                state.Definition.Id,
                state.ScalarKind,
                fallback,
                out value);
        }

        internal static string DecorateLabel(
            SettingDefinition definition,
            string translatedLabel)
        {
            string label = translatedLabel ?? definition?.Label ?? definition?.Id ?? string.Empty;
            // Visibility, the banner, and the ownership button explain
            // preview state. Keep the setting's own label unchanged.
            return label;
        }

        internal static string DecorateTooltip(
            SettingDefinition definition,
            string translatedTooltip)
        {
            if (IsPreviewStructuralDefinition(definition))
            {
                return translatedTooltip;
            }

            BWTPresentationSettingOwnershipState state = Describe(definition);
            if (!state.IsWorkloadOwnedInActiveTemplate &&
                !state.IsBlocked &&
                !state.CanAcquireWorkloadOwnership)
            {
                return translatedTooltip;
            }

            string ownership = state.CanAcquireWorkloadOwnership &&
                !state.IsWorkloadOwnedInActiveTemplate
                ? "This is a global setting. Select 'Use in workload' to change it for this workload."
                : !state.IsWorkloadOwnedInActiveTemplate
                ? "This preview can't be read. Close it, then try again."
                : state.IsBlocked
                    ? "This setting is unavailable in workload previews."
                    : "Changes here apply to this workload.";
            if (state.WillRevertToGlobalOutsidePreview)
            {
                ownership += " The global value is used again when this preview closes.";
            }

            return string.IsNullOrEmpty(translatedTooltip)
                ? ownership
                : translatedTooltip + "\n\n" + ownership;
        }

        internal static void DrawPreviewBannerIfNeeded(ref Rect inRect)
        {
            EnsureSnapshot();
            if (!_snapshot.IsActive)
            {
                return;
            }

            const float bannerMinimumHeight = 34f;
            const float bannerGap = 6f;
            const float bannerHorizontalPadding = 9f;
            const float bannerVerticalPadding = 6f;
            string bannerText = !_snapshot.ReadSucceeded
                ? "This workload preview can't be read. Display settings are unavailable."
                : _snapshot.OwnsPresentationSettings
                    ? "Changes to supported display settings apply to this workload. Global settings won't change."
                    : "Select 'Use in workload' to edit a supported display setting.";
            float bannerHeight = bannerMinimumHeight;
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                float textWidth = Mathf.Max(
                    1f,
                    inRect.width - (bannerHorizontalPadding * 2f));
                bannerHeight = Mathf.Max(
                    bannerMinimumHeight,
                    Text.CalcHeight(bannerText, textWidth) +
                        (bannerVerticalPadding * 2f));
                Rect bannerRect = new Rect(
                    inRect.x,
                    inRect.y,
                    inRect.width,
                    bannerHeight);
                GUI.color = new Color(0.20f, 0.34f, 0.46f, 0.95f);
                Widgets.DrawBoxSolid(bannerRect, GUI.color);
                GUI.color = new Color(0.45f, 0.72f, 0.92f, 0.95f);
                Widgets.DrawBox(bannerRect, 1);
                GUI.color = Color.white;
                Rect textRect = new Rect(
                    bannerRect.x + bannerHorizontalPadding,
                    bannerRect.y + bannerVerticalPadding,
                    Mathf.Max(0f, bannerRect.width - (bannerHorizontalPadding * 2f)),
                    Mathf.Max(0f, bannerRect.height - (bannerVerticalPadding * 2f)));
                Widgets.Label(textRect, bannerText);
                TooltipHandler.TipRegion(
                    bannerRect,
                    !_snapshot.ReadSucceeded
                        ? "Display settings, import, and Restore defaults are unavailable until this preview can be read."
                        : "Supported display settings apply to this workload. Other display settings are hidden.");
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }

            inRect.yMin += bannerHeight + bannerGap;
        }

        private static BWTPresentationSettingOwnershipState Describe(SettingDefinition definition) =>
            Describe(definition?.Id);

        private static BWTPresentationSettingOwnershipState Describe(string settingId)
        {
            EnsureSnapshot();
            bool known = !string.IsNullOrEmpty(settingId) && Metadata.Contains(settingId);
            var state = new BWTPresentationSettingOwnershipState
            {
                IsKnown = known,
                IsPreviewActive = _snapshot.IsActive
            };

            if (!_snapshot.IsActive)
            {
                return state;
            }

            if (!known)
            {
                state.IsBlocked = true;
                state.BlockReason =
                    "This setting is unavailable while a workload preview is open.";
                return state;
            }

            if (!_snapshot.ReadSucceeded)
            {
                state.IsBlocked = true;
                state.BlockReason = ResolveBlockReason(settingId);
                return state;
            }

            // A dimension ownership bit is not ownership of every setting in
            // that dimension. Only entries represented by the active template
            // may be marked or blocked here.
            if (!_snapshot.OwnsPresentationSetting(settingId))
            {
                state.CanAcquireWorkloadOwnership =
                    IsStageablePresentationSetting(settingId);
                state.IsBlocked = !state.CanAcquireWorkloadOwnership;
                state.BlockReason = state.IsBlocked
                    ? ResolveBlockReason(settingId)
                    : string.Empty;
                return state;
            }

            state.IsWorkloadOwnedInActiveTemplate = true;
            state.IsChangedByActivePreview = _snapshot.IsChanged(settingId);
            state.WillRevertToGlobalOutsidePreview = true;
            state.IsBlocked = !IsStageablePresentationSetting(settingId);
            state.BlockReason = state.IsBlocked
                ? ResolveBlockReason(settingId)
                : string.Empty;
            return state;
        }

        private static string ResolveBlockReason(string settingId)
        {
            if (BlockedReasons.TryGetValue(settingId ?? string.Empty, out string reason) &&
                !string.IsNullOrEmpty(reason))
            {
                return reason;
            }

            if (_snapshot.IsActive && !_snapshot.ReadSucceeded)
            {
                return string.IsNullOrEmpty(_snapshot.FailureReason)
                    ? "Couldn't read the workload preview."
                    : _snapshot.FailureReason;
            }

            return IsStageablePresentationSetting(settingId)
                ? "This setting couldn't be added to the workload."
                : "This setting is unavailable in workload previews.";
        }

        private static bool IsStageablePresentationSetting(string settingId) =>
            !string.IsNullOrEmpty(settingId) && StageablePresentationSettingIds.Contains(settingId);

        private static void InstallPreviewVisibility(SettingDefinition definition)
        {
            Func<object, bool> existingVisibility = definition.VisibleWhen;
            definition.VisibleWhen = settingsObject =>
            {
                // The shared hierarchy treats a missing settings object as an
                // optional, predicate-free context. Preserve that contract,
                // then add the preview-only visibility rule.
                if (settingsObject != null &&
                    existingVisibility != null &&
                    !existingVisibility(settingsObject))
                {
                    return false;
                }

                return IsPreviewVisibleDefinition(definition) &&
                    IsVisibleThroughStageableAncestors(definition);
            };
        }

        private static bool IsPreviewVisibleDefinition(SettingDefinition definition)
        {
            EnsureSnapshot();
            if (!_snapshot.IsActive)
            {
                return true;
            }

            // A failed active-preview read has no trustworthy projected value
            // to present. Keep the page fail-closed and let its banner explain
            // why its presentation rows are temporarily unavailable.
            if (!_snapshot.ReadSucceeded)
            {
                return false;
            }

            return IsStageablePresentationSetting(definition?.Id) ||
                IsPreviewStructuralDefinition(definition);
        }

        private static bool IsVisibleThroughStageableAncestors(
            SettingDefinition definition)
        {
            SettingDefinition current = definition;
            while (current != null &&
                   !string.IsNullOrEmpty(current.ParentId) &&
                   PreparedDefinitionsById.TryGetValue(
                       current.ParentId,
                       out SettingDefinition parent))
            {
                if (parent.ControlsChildVisibility &&
                    parent.Type == SettingType.Custom &&
                    PreparedPresentationSettings.TryGetValue(
                        parent.Id,
                        out BWTPresentationSettingDefinitionState parentState) &&
                    parentState.OriginalType == SettingType.Bool &&
                    TryGetCachedGlobalScalar(parentState, out PresentationValue fallback))
                {
                    PresentationValue effective = fallback;
                    if (CanStageOwnedSetting(Describe(parent)) &&
                        !TryGetEffectivePresentationValue(
                            parentState,
                            fallback,
                            out effective))
                    {
                        return false;
                    }

                    if (effective.Kind != PresentationValueKind.Boolean ||
                        !effective.BooleanValue)
                    {
                        return false;
                    }
                }

                current = parent;
            }

            return true;
        }

        private static bool IsPreviewStructuralDefinition(SettingDefinition definition)
        {
            if (!IsPreviewActive || definition == null || string.IsNullOrEmpty(definition.Id))
            {
                return false;
            }

            EnsurePreviewStructure();
            return PreviewStructuralDefinitionIds.Contains(definition.Id);
        }

        private static void EnsurePreviewStructure()
        {
            if (!_previewStructureDirty)
            {
                return;
            }

            PreviewStructuralDefinitionIds.Clear();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stageableId in StageablePresentationSettingIds)
            {
                if (!PreparedDefinitionsById.TryGetValue(
                        stageableId,
                        out SettingDefinition current))
                {
                    continue;
                }

                visited.Clear();
                while (current != null &&
                       !string.IsNullOrEmpty(current.ParentId) &&
                       visited.Add(current.Id) &&
                       PreparedDefinitionsById.TryGetValue(
                           current.ParentId,
                           out SettingDefinition parent))
                {
                    PreviewStructuralDefinitionIds.Add(parent.Id);
                    current = parent;
                }
            }

            _previewStructureDirty = false;
        }

        private static IDictionary<string, PresentationValue> CapturePreparedPresentationValues(
            BetterWorkTabSettings settings)
        {
            var values = new Dictionary<string, PresentationValue>(StringComparer.Ordinal);
            foreach (string settingId in PreparedPresentationSettings.Keys)
            {
                try
                {
                    if (PreparedPresentationSettings.TryGetValue(
                            settingId,
                            out BWTPresentationSettingDefinitionState state) &&
                        TryReadGlobalScalar(state, settings, out PresentationValue live))
                    {
                        values[settingId] = live;
                        continue;
                    }
                }
                catch
                {
                    // Preserve the previous field value or prepared default
                    // when one setting reader fails; another field must not be
                    // affected by the failure.
                }

                if (_snapshot.GlobalValues.TryGetValue(
                        settingId,
                        out PresentationValue cached))
                {
                    values[settingId] = cached;
                    continue;
                }

                if (PreparedPresentationSettings.TryGetValue(
                        settingId,
                        out BWTPresentationSettingDefinitionState fallback) &&
                    fallback.DefaultScalar.Kind == fallback.ScalarKind)
                {
                    values[settingId] = fallback.DefaultScalar;
                }
            }

            return values;
        }

        private static bool TryGetCachedGlobalScalar(
            BWTPresentationSettingDefinitionState state,
            out PresentationValue value)
        {
            value = PresentationValue.Empty;
            return state != null &&
                PresentationSnapshot.TryGetGlobalValue(state.Definition.Id, out value) &&
                value.Kind == state.ScalarKind;
        }

        internal static bool TryReadGlobalScalar(
            BWTPresentationSettingDefinitionState state,
            object settingsObject,
            out PresentationValue value)
        {
            value = PresentationValue.Empty;
            return state?.Field != null && settingsObject != null &&
                TryToScalar(state, state.Field.GetValue(settingsObject), false, out value);
        }

        internal static bool TryReadGlobalExact(
            BWTPresentationSettingDefinitionState state,
            object settingsObject,
            out object value)
        {
            value = state?.Field == null || settingsObject == null
                ? null
                : state.Field.GetValue(settingsObject);
            return IsExactValueCompatible(state, value);
        }

        internal static bool TryGetStageableDefinition(
            string settingId,
            out BWTPresentationSettingDefinitionState state)
        {
            state = null;
            return IsStageablePresentationSetting(settingId) &&
                PreparedPresentationSettings.TryGetValue(settingId, out state);
        }

        internal static bool TryWriteGlobalScalar(
            BWTPresentationSettingDefinitionState state,
            object settingsObject,
            PresentationValue value,
            out string reason)
        {
            if (!TryGetCanonicalGlobalExact(state, value, out object exact, out reason) ||
                !TryWriteGlobalExact(state, settingsObject, exact, out reason))
            {
                return false;
            }

            _snapshot.SetGlobalValue(state.Definition.Id, value);
            return true;
        }

        internal static bool TryGetCanonicalGlobalExact(
            BWTPresentationSettingDefinitionState state,
            PresentationValue value,
            out object exactValue,
            out string reason)
        {
            exactValue = null;
            reason = string.Empty;
            if (state == null || value.Kind != state.ScalarKind)
            {
                reason = "This value doesn't match this setting.";
                return false;
            }

            switch (value.Kind)
            {
                case PresentationValueKind.Boolean:
                    exactValue = value.BooleanValue;
                    return true;
                case PresentationValueKind.Integer:
                    int normalized = NormalizeStagedInteger(
                        state.Definition.Id,
                        value.IntegerValue);
                    if (normalized != value.IntegerValue)
                    {
                        reason = "This workload value is outside this setting's range.";
                        return false;
                    }

                    exactValue = normalized;
                    return true;
                case PresentationValueKind.String:
                    if (TryGetColor(value, out Color color))
                    {
                        if (!StringComparer.Ordinal.Equals(
                                value.StringValue,
                                ToColorScalar(color).StringValue))
                        {
                            reason = "This workload color is invalid.";
                            return false;
                        }

                        exactValue = color;
                        return true;
                    }

                    break;
            }

            reason = "Couldn't prepare this workload setting.";
            return false;
        }

        internal static bool TryWriteGlobalExact(
            BWTPresentationSettingDefinitionState state,
            object settingsObject,
            object value,
            out string reason)
        {
            reason = string.Empty;
            if (IsPreviewActive && _authorizedGlobalSettingsWriteDepth <= 0)
            {
                reason = "Global settings can't change while a workload preview is open.";
                return false;
            }

            if (state == null || settingsObject == null ||
                !IsExactValueCompatible(state, value))
            {
                reason = "This setting has the wrong value type.";
                return false;
            }

            if (state.Field == null)
            {
                reason = "Couldn't find this setting.";
                return false;
            }

            state.Field.SetValue(settingsObject, value);
            return true;
        }

        private static bool IsExactValueCompatible(
            BWTPresentationSettingDefinitionState state,
            object value)
        {
            return state != null && value != null &&
                ((state.ScalarKind == PresentationValueKind.Boolean && value is bool) ||
                 (state.ScalarKind == PresentationValueKind.Integer && value is int) ||
                 (state.ScalarKind == PresentationValueKind.String && value is Color));
        }

        internal static bool TryToScalar(
            BWTPresentationSettingDefinitionState state,
            object raw,
            bool normalizeInteger,
            out PresentationValue value)
        {
            value = PresentationValue.Empty;
            if (state == null)
            {
                return false;
            }

            if (state.ScalarKind == PresentationValueKind.Boolean && raw is bool boolean)
            {
                value = PresentationValue.FromBoolean(boolean);
                return true;
            }

            if (state.ScalarKind == PresentationValueKind.Integer && raw is int integer)
            {
                value = PresentationValue.FromInteger(normalizeInteger
                    ? NormalizeStagedInteger(state.Definition.Id, integer)
                    : integer);
                return true;
            }

            if (state.ScalarKind == PresentationValueKind.String && raw is Color color)
            {
                value = ToColorScalar(color);
                return true;
            }

            return false;
        }

        private static PresentationValue ToColorScalar(Color color)
        {
            return PresentationValue.FromString(
                "#" + ColorUtility.ToHtmlStringRGBA(color));
        }

        internal static bool TryGetColor(
            PresentationValue value,
            out Color color)
        {
            color = default(Color);
            if (value.Kind != PresentationValueKind.String ||
                string.IsNullOrEmpty(value.StringValue))
            {
                return false;
            }

            string encoded = value.StringValue.StartsWith("#", StringComparison.Ordinal)
                ? value.StringValue
                : "#" + value.StringValue;
            return ColorUtility.TryParseHtmlString(encoded, out color);
        }

        private static int NormalizeStagedInteger(string settingId, int value)
        {
            if (string.Equals(settingId, HeadersAngleRotation, StringComparison.Ordinal))
            {
                return Mathf.Clamp(
                    Mathf.RoundToInt(value / 5f) * 5,
                    -90,
                    90);
            }

            return value;
        }

        private static bool IsAngledHeaderModeEnabled() =>
            BWTWorkTabEffectiveSettings.GetBool(HeadersAngled);

        private static bool TryHandleEnumMutation(
            SettingDefinition definition,
            object settingsObject,
            object value)
        {
            BWTPresentationSettingOwnershipState state = Describe(definition);
            if (!state.IsPreviewActive)
            {
                return false;
            }

            if (state.IsBlocked || state.IsWorkloadOwnedInActiveTemplate || !state.IsKnown)
            {
                return BlockAndHandle(
                    definition.Id,
                    ResolveBlockReason(definition.Id));
            }

            return false;
        }

        private static bool BlockAndHandle(string settingId, string reason)
        {
            BlockedReasons[settingId ?? string.Empty] = reason;
            SuppressSettingsCallbacks(settingId);
            InvalidateWorkTabPresentation();
            return true;
        }

        private static void SuppressSettingsCallbacks(string settingId)
        {
            if (!string.IsNullOrEmpty(settingId))
            {
                SuppressedOnChanged.Add(settingId);
            }

            _suppressNextGlobalSettingsWrite = true;
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
            if (settingsObject != null && definition != null &&
                PreparedFields.TryGetValue(definition, out FieldInfo field))
            {
                field.SetValue(settingsObject, value);
            }
        }

        private static FieldInfo FindSettingsField(SettingDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.FieldName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;
            for (Type type = typeof(BetterWorkTabSettings); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(definition.FieldName, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static void EnsureSnapshot()
        {
            EnsureFresh();
        }

        internal static BWTPresentationSnapshot PresentationSnapshot
        {
            get
            {
                EnsureSnapshot();
                return _snapshot;
            }
        }

        internal static void InvalidateWorkTabPresentation(string settingId = null)
        {
            InvalidateWorkTabPresentationCore(IsHeaderPresentationSetting(settingId));
        }

        internal static void InvalidateWorkTabPresentation(
            IEnumerable<string> settingIds)
        {
            InvalidateWorkTabPresentationCore(IncludesHeaderSetting(settingIds));
        }

        private static void InvalidateWorkTabPresentationCore(
            bool includesHeaderSetting)
        {
            _snapshotValid = false;
            WorkTabEffectiveStateRuntime.InvalidateRenderPass();
            if (includesHeaderSetting)
            {
                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            }

            // Presentation changes use the same centralized completion path
            // as other accepted Work-tab dimensions. This is non-durable: the
            // settings store and preview port retain their own revision and
            // persistence contracts.
            (_application ?? WorkTabApplication.Current)?.PublishAtomicMutation(
                WorkTabApplicationDimensions.Presentation,
                durable: false,
                broadScope: true,
                mirrorExternal: false);
        }

        private static bool IsHeaderPresentationSetting(string settingId)
        {
            return string.Equals(settingId, HeadersAngled, StringComparison.Ordinal) ||
                string.Equals(settingId, DragdropRemoveHeaderUnderline, StringComparison.Ordinal) ||
                string.Equals(settingId, HeadersAngleRotation, StringComparison.Ordinal) ||
                string.Equals(settingId, "headers.horizontalOffset", StringComparison.Ordinal) ||
                string.Equals(settingId, "headers.angledColor", StringComparison.Ordinal) ||
                string.Equals(settingId, HeadersUnderlineColor, StringComparison.Ordinal) ||
                string.Equals(settingId, ColumnsShowMovedIndicator, StringComparison.Ordinal) ||
                string.Equals(settingId, "columns.showMovedColorTint", StringComparison.Ordinal) ||
                string.Equals(settingId, HeadersUseVerticalStackingForCJK, StringComparison.Ordinal) ||
                string.Equals(settingId, "columns.movedMarkerColor", StringComparison.Ordinal);
        }

        private static bool IncludesHeaderSetting(IEnumerable<string> settingIds)
        {
            if (settingIds != null)
            {
                foreach (string settingId in settingIds)
                {
                    if (IsHeaderPresentationSetting(settingId))
                    {
                        return true;
                    }
                }
            }

            return false;
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
                case SettingType.Button:
                    return true;
                default:
                    return false;
            }
        }

        private static HashSet<string> BuildMetadata()
        {
            return new HashSet<string>(StringComparer.Ordinal)
            {
                HeadersCustomWorkLabels,
                HeadersAngled,
                DragdropRemoveHeaderUnderline,
                HeadersAngleRotation,
                HeadersUseVerticalStackingForCJK,
                "headers.cjkVerticalKerning",
                "headers.angledColor",
                HeadersUnderlineColor,
                "headers.horizontalOffset",
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
                FluffyStyleStandaloneTopButtons,
                FeaturesOverlay,
                FeaturesHighlights,
                FeaturesDividers,
                FeaturesSubWorkJobs,
                DragdropEnableGrouping,
                LayoutWorkTabMinimumWidth,
                LayoutWorkTabMaxVisiblePawns,
                LayoutWorkTabTopSpace,
                LayoutPawnCount,
                LayoutBedCount,
                UiPriorityLegend,
                UiDragInstructions,
                UiContextSettingsHint,
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
                "dividers.highlight",
                DividersLabels,
                DividersCollapse,
                DividersAnimations,
                LayoutDividerHeight,
                LayoutDividerAlpha,
                ColumnsShowMovedIndicator,
                ColumnsShowBaselineLine,
                "columns.showMovedColorTint",
                "columns.movedMarkerColor",
                AdvancedScrollWheelPriority,
                UiTimePrioritySchedules,
                UiTimePriorityHourDivider,
                UiTimePriorityCopyPasteButtons,
                UiTimePrioritySourceColumnHighlight,
                UiChronosPointerTimePriorityIncidents,
                WorkloadsPreviewRevealAnimation,
                WorkloadsPreviewRevealSpeed,
                WorkloadsInspectionHighlights,
                WorkloadsInspectionOpacity,
                WorkloadsWarnOnApply
            };
        }
    }

    /// <summary>
    /// BWT-owned read boundary for presentation values. Work-tab consumers use
    /// this adapter instead of mixing live settings with preview projection.
    /// Only scalar types that the presentation preview boundary can represent are intentionally supported.
    /// </summary>
    internal static class BWTWorkTabEffectiveSettings
    {
        internal static bool GetBool(string settingId) =>
            BWTWorkloadSettingsOwnershipPolicy.PresentationSnapshot.GetBool(settingId);

        internal static int GetInt(string settingId) =>
            BWTWorkloadSettingsOwnershipPolicy.PresentationSnapshot.GetInt(settingId);

        internal static Color GetColor(string settingId) =>
            BWTWorkloadSettingsOwnershipPolicy.PresentationSnapshot.GetColor(settingId);
    }

    internal sealed class BWTPresentationSettingDefinitionState
    {
        internal BWTPresentationSettingDefinitionState(
            SettingDefinition definition,
            SettingType originalType,
            PresentationValueKind scalarKind,
            FieldInfo field)
        {
            Definition = definition;
            OriginalType = originalType;
            ScalarKind = scalarKind;
            Field = field;
            OpenColorPicker = OpenPreparedColorPicker;
        }

        internal SettingDefinition Definition { get; }
        internal SettingType OriginalType { get; }
        internal PresentationValueKind ScalarKind { get; }
        internal FieldInfo Field { get; }
        internal PresentationValue DefaultScalar { get; set; }
        internal Action<Color, Action<Color>> OpenColorPicker { get; }

        private void OpenPreparedColorPicker(Color initialColor, Action<Color> _)
        {
            BWTWorkloadSettingsOwnershipPolicy.OpenColorPicker(this, initialColor);
        }
    }

    internal struct BWTPresentationSettingOwnershipState
    {
        internal bool IsKnown;
        internal bool IsPreviewActive;
        internal bool IsWorkloadOwnedInActiveTemplate;
        internal bool IsChangedByActivePreview;
        internal bool WillRevertToGlobalOutsidePreview;
        internal bool CanAcquireWorkloadOwnership;
        internal bool IsBlocked;
        internal string BlockReason;
    }

    internal sealed class BWTPresentationSnapshot
    {
        private readonly Dictionary<string, PresentationValue> _globalValues;
        private readonly Dictionary<string, Color> _colors;
        private readonly Dictionary<string, PresentationIntent> _afterIntents;
        private readonly HashSet<string> _changedSettings;

        private BWTPresentationSnapshot(
            bool isActive,
            bool readSucceeded,
            string identity,
            string failureReason,
            IDictionary<string, PresentationValue> globalValues,
            IReadOnlyList<PresentationIntentEntry> beforeIntents,
            IReadOnlyList<PresentationIntentEntry> afterIntents)
        {
            IsActive = isActive;
            ReadSucceeded = readSucceeded;
            Identity = identity ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
            _globalValues = new Dictionary<string, PresentationValue>(
                globalValues, StringComparer.Ordinal);
            _colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, PresentationValue> entry in _globalValues)
            {
                CacheColor(entry.Key, entry.Value);
            }

            Dictionary<string, PresentationIntent> beforeIntentMap =
                CopyPresentationIntents(beforeIntents);
            _afterIntents = CopyPresentationIntents(afterIntents);
            _changedSettings = new HashSet<string>(beforeIntentMap.Keys, StringComparer.Ordinal);
            foreach (KeyValuePair<string, PresentationIntent> entry in _afterIntents)
            {
                if (beforeIntentMap.TryGetValue(entry.Key, out PresentationIntent before) &&
                    before.Equals(entry.Value))
                {
                    _changedSettings.Remove(entry.Key);
                }
                else
                {
                    _changedSettings.Add(entry.Key);
                }

                if (!entry.Value.IsOwned)
                {
                    continue;
                }

                OwnsPresentationSettings = true;
                if (entry.Value.HasValue)
                {
                    CacheColor(entry.Key, entry.Value.Value);
                }
            }
        }

        internal bool IsActive { get; }
        internal bool ReadSucceeded { get; }
        internal bool OwnsPresentationSettings { get; }
        internal string Identity { get; }
        internal string FailureReason { get; }

        internal static BWTPresentationSnapshot CreateInactive(
            IDictionary<string, PresentationValue> globalValues)
        {
            return new BWTPresentationSnapshot(
                false,
                true,
                string.Empty,
                string.Empty,
                globalValues,
                null,
                null);
        }

        internal static BWTPresentationSnapshot FromPreview(
            WorkTabPresentationPreviewState preview,
            IDictionary<string, PresentationValue> globalValues)
        {
            return new BWTPresentationSnapshot(
                true,
                true,
                preview.Identity,
                string.Empty,
                globalValues,
                preview.BeforeIntents,
                preview.AfterIntents);
        }

        internal static BWTPresentationSnapshot Failed(
            IDictionary<string, PresentationValue> globalValues,
            string reason,
            string identity,
            bool isActive = true)
        {
            return new BWTPresentationSnapshot(
                isActive,
                false,
                identity,
                reason,
                globalValues,
                null,
                null);
        }

        internal IDictionary<string, PresentationValue> GlobalValues => _globalValues;

        internal bool TryGetGlobalValue(string settingId, out PresentationValue value)
        {
            return _globalValues.TryGetValue(settingId, out value);
        }

        internal void SetGlobalValue(string settingId, PresentationValue value)
        {
            _globalValues[settingId] = value;
            CacheColor(settingId, value);
        }

        internal bool OwnsPresentationSetting(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) &&
                _afterIntents.TryGetValue(
                    settingId,
                    out PresentationIntent intent) &&
                intent.IsOwned;
        }

        internal bool IsChanged(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) && _changedSettings.Contains(settingId);
        }

        internal bool TryResolveOwnedScalar(
            string settingId,
            PresentationValueKind expectedKind,
            PresentationValue fallback,
            out PresentationValue value)
        {
            value = fallback;
            if (!_afterIntents.TryGetValue(
                    settingId,
                    out PresentationIntent intent) ||
                !intent.IsOwned)
            {
                return false;
            }

            if (intent.IsClear)
            {
                return true;
            }

            if (!intent.HasValue || !IsSameKind(intent.Value.Kind, expectedKind))
            {
                return false;
            }

            value = intent.Value;
            return true;
        }

        internal bool GetBool(string settingId)
        {
            return TryGetValue(settingId, out PresentationValue value) &&
                value.Kind == PresentationValueKind.Boolean && value.BooleanValue;
        }

        internal int GetInt(string settingId)
        {
            return TryGetValue(settingId, out PresentationValue value) &&
                value.Kind == PresentationValueKind.Integer
                ? value.IntegerValue
                : 0;
        }

        internal Color GetColor(string settingId)
        {
            return _colors.TryGetValue(settingId, out Color color)
                ? color
                : default(Color);
        }

        private bool TryGetValue(string settingId, out PresentationValue value)
        {
            if (_afterIntents.TryGetValue(
                    settingId,
                    out PresentationIntent intent) &&
                intent.IsOwned && intent.HasValue)
            {
                value = intent.Value;
                return true;
            }

            return _globalValues.TryGetValue(settingId, out value);
        }

        private void CacheColor(string settingId, PresentationValue value)
        {
            if (!BWTWorkloadSettingsOwnershipPolicy.TryGetColor(value, out Color color))
            {
                return;
            }

            _colors[settingId] = color;
        }

        private static bool IsSameKind(
            PresentationValueKind presentationKind,
            PresentationValueKind expectedKind)
        {
            return (int)presentationKind == (int)expectedKind;
        }

        private static Dictionary<string, PresentationIntent>
            CopyPresentationIntents(
                IReadOnlyList<PresentationIntentEntry> entries)
        {
            var intents = new Dictionary<string, PresentationIntent>(
                StringComparer.Ordinal);
            if (entries == null)
            {
                return intents;
            }

            // The preview model is the canonical owner of legacy-to-typed
            // promotion and release normalization. Its intent list therefore
            // already contains every effective Set/Clear entry exactly once.
            for (int i = 0; i < entries.Count; i++)
            {
                PresentationIntentEntry entry = entries[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Key) &&
                    !entry.Intent.IsNoOpinion)
                {
                    intents[entry.Key] = entry.Intent;
                }
            }

            return intents;
        }
    }
}
