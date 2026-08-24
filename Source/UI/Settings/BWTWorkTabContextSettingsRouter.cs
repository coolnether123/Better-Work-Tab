using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Features.Application;
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
                    return CreateWorkloadContextRequest(
                        "Workload Buttons",
                        "Settings related to workload buttons, preview reveal controls, inspection highlights, saved workloads, and warnings.",
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

            if (rects.ContainsWorkloadFooter(mousePosition))
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

        static BWTWorkloadSettingsOwnershipPolicy()
        {
            WorkloadPresentationServices.Register(
                CreateApplyWriter,
                TryGetStageableScalarKind,
                () => NotifyGlobalSettingsChanged());
        }

        private static readonly HashSet<string> Metadata =
            BuildMetadata();

        private static readonly HashSet<SettingDefinition> PreparedDefinitions =
            new HashSet<SettingDefinition>();

        private static readonly Dictionary<SettingDefinition, FieldInfo> PreparedFields =
            new Dictionary<SettingDefinition, FieldInfo>();

        private static readonly Dictionary<string, BWTWorkloadSettingDefinitionState>
            PreparedPresentationSettings =
                new Dictionary<string, BWTWorkloadSettingDefinitionState>(StringComparer.Ordinal);

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

        private static BWTWorkloadPresentationSnapshot _snapshot =
            BWTWorkloadPresentationSnapshot.CreateInactive(
                new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal));
        private static IWorkTabPresentationPreviewPort _previewPort;
        private static string _observedPreviewIdentity = string.Empty;
        private static long _snapshotSettingsRevision = long.MinValue;
        private static bool _snapshotValid;
        private static long _globalSettingsRevision;
        private static bool _legacyModeBeforeInteraction;
        private static bool _legacyModeBeforeInteractionCaptured;
        private static bool _lastObservedLegacyMode;
        private static bool _lastObservedLegacyModeValid;
        private static bool _suppressNextGlobalSettingsWrite;
        [ThreadStatic]
        private static int _authorizedGlobalSettingsWriteDepth;

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
            if (definition == null ||
                !Metadata.Contains(definition.Id) ||
                !PreparedDefinitions.Add(definition))
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
                    out BWTWorkloadSettingDefinitionState rowState))
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
                    When = _ => Describe(definition).IsBlocked,
                    Reason = _ => Describe(definition).BlockReason,
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
            IDictionary<string, WorkloadScalarValue> globalValues =
                _snapshot.GlobalValues;
            try
            {
                BWTSettingsRegistry.EnsureInitialized();
                globalValues = CapturePreparedPresentationValues(BetterWorkTabMod.Settings);
            }
            catch (Exception ex)
            {
                globalValues = CapturePreparedPresentationValues(
                    BetterWorkTabMod.Settings);
                SetSnapshot(BWTWorkloadPresentationSnapshot.Failed(
                    globalValues,
                    "The global Better Work Tab presentation values could not be read safely: " + ex.Message,
                    _observedPreviewIdentity,
                    !string.IsNullOrEmpty(_observedPreviewIdentity)));
                return;
            }

            BWTWorkloadPresentationSnapshot next;
            try
            {
                IWorkTabPresentationPreviewPort previewPort = _previewPort;
                if (previewPort == null || !previewPort.IsPreviewActive)
                {
                    next = BWTWorkloadPresentationSnapshot.CreateInactive(globalValues);
                }
                else
                {
                    if (previewPort.TryReadPresentationPreview(
                            out WorkTabPresentationPreviewState preview,
                            out string reason))
                    {
                        next = string.IsNullOrEmpty(preview.Identity)
                            ? BWTWorkloadPresentationSnapshot.Failed(
                                globalValues,
                                "The active workload preview has no exact session identity.",
                                string.Empty)
                            : BWTWorkloadPresentationSnapshot.FromPreview(
                                preview,
                                globalValues);
                    }
                    else
                    {
                        next = BWTWorkloadPresentationSnapshot.Failed(
                            globalValues,
                            string.IsNullOrEmpty(reason)
                                ? "The active workload preview could not be read safely."
                                : reason,
                            _observedPreviewIdentity,
                            isActive: true);
                    }
                }
            }
            catch (Exception ex)
            {
                next = BWTWorkloadPresentationSnapshot.Failed(
                    globalValues,
                    "The active workload preview could not be read safely: " + ex.Message,
                    _observedPreviewIdentity,
                    isActive: _previewPort?.IsPreviewActive == true);
            }

            SetSnapshot(next);
        }

        internal static void EnsureFresh()
        {
            if (!_snapshotValid || _snapshotSettingsRevision != _globalSettingsRevision)
            {
                Refresh();
            }
        }

        private static void SetSnapshot(BWTWorkloadPresentationSnapshot next)
        {
            if (!next.IsActive ||
                !StringComparer.Ordinal.Equals(_snapshot.Identity, next.Identity))
            {
                BlockedReasons.Clear();
            }

            _snapshot = next;
            _snapshotValid = true;
            _snapshotSettingsRevision = _globalSettingsRevision;

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                // Reset icons mutate the field directly and do not raise the
                // drawer's OnSettingInteracted callback. Keep the last drawn
                // value so that a reset still crosses the same gateway as a
                // normal toggle.
                _lastObservedLegacyMode = WorkloadModeService.CurrentMode ==
                    WorkloadBackendMode.Legacy;
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
            // Restore the gateway's source value before asking it to perform
            // the transition, so ResolveMode sees the real current backend.
            settings.useLegacyWorkloads = previousLegacy;
            if (IsPreviewActive)
            {
                // Workload preview owns only its projected state. The mode
                // selector is a global backend preference and must not
                // transition or persist while the preview is open.
                _lastObservedLegacyMode = previousLegacy;
                _lastObservedLegacyModeValid = true;
                Messages.Message(
                    "BWT_Settings_WorkloadMode_PreviewBlocked".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            WorkloadOperationResult transition = WorkloadModeService.TryTransition(
                requestedLegacy ? WorkloadBackendMode.Legacy : WorkloadBackendMode.Modern);
            if (transition.Succeeded)
            {
                _lastObservedLegacyMode = WorkloadModeService.CurrentMode ==
                    WorkloadBackendMode.Legacy;
                _lastObservedLegacyModeValid = true;
                return;
            }

            settings.useLegacyWorkloads = previousLegacy;
            _lastObservedLegacyMode = previousLegacy;
            _lastObservedLegacyModeValid = true;
            Messages.Message(
                string.IsNullOrEmpty(transition.Message)
                    ? "BWT_Settings_WorkloadMode_ChangeFailed".Translate().ToString()
                    : transition.Message,
                MessageTypeDefOf.RejectInput,
                false);
        }

        internal static void Invalidate()
        {
            _snapshotValid = false;
            SuppressedOnChanged.Clear();
            _suppressNextGlobalSettingsWrite = false;
        }

        internal static long GlobalSettingsRevision => _globalSettingsRevision;

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
            if (_globalSettingsRevision < long.MaxValue)
            {
                _globalSettingsRevision++;
            }

            InvalidateWorkTabPresentationCore(includesHeaderSetting);
            return _globalSettingsRevision;
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

        internal static WorkloadPresentationSettingsTransaction CreateApplyWriter() =>
            new WorkloadPresentationSettingsTransaction(
                new BWTWorkloadPresentationSettingsStore());

        /// <summary>
        /// The only active-preview exception to the global-settings write
        /// guard is the backend's explicit commit/rollback writer. Normal
        /// settings UI code never receives this lease.
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
            out BWTWorkloadSettingDefinitionState state)
        {
            state = null;
            if (definition == null ||
                field == null ||
                definition.ValueGetter != null ||
                definition.ValueSetter != null)
            {
                return false;
            }

            WorkloadScalarKind kind;
            if (definition.Type == SettingType.Bool && field.FieldType == typeof(bool))
            {
                kind = WorkloadScalarKind.Boolean;
            }
            else if ((definition.Type == SettingType.Int ||
                      definition.Type == SettingType.NumericInt) &&
                     field.FieldType == typeof(int))
            {
                kind = WorkloadScalarKind.Integer;
            }
            else if (definition.Type == SettingType.Color && field.FieldType == typeof(Color))
            {
                kind = WorkloadScalarKind.String;
            }
            else
            {
                return false;
            }

            var preparedState = new BWTWorkloadSettingDefinitionState(
                definition,
                definition.Type,
                kind,
                field);
            if (!TryToScalar(
                    preparedState,
                    definition.DefaultValue,
                    true,
                    out WorkloadScalarValue defaultScalar))
            {
                return false;
            }

            preparedState.DefaultScalar = defaultScalar;
            state = preparedState;
            return true;
        }

        private static void InstallStageableDefinition(
            BWTWorkloadSettingDefinitionState state)
        {
            SettingDefinition definition = state.Definition;
            definition.Type = SettingType.Custom;
            definition.CustomDrawer = (rect, label, tooltip, settingsObject, disabled) =>
                DrawStageableSettingRow(state, rect, label, tooltip, settingsObject, disabled);
            definition.CustomHasNonDefaultValue = _ => HasStageableNonDefaultValue(state);
            definition.CustomReset = settingsObject =>
            {
                WorkloadScalarValue defaultValue = state.DefaultScalar;
                BWTWorkloadSettingOwnershipState ownership = Describe(definition);
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
                            "Global settings are protected while a workload preview is active. Use the explicit workload ownership action first.";
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
            BWTWorkloadSettingDefinitionState state,
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

            if (!TryGetCachedGlobalScalar(state, out WorkloadScalarValue fallback))
            {
                return false;
            }

            BWTWorkloadSettingOwnershipState ownership = Describe(state.Definition);
            bool stageOwned = CanStageOwnedSetting(ownership);
            bool previewOwnedButUnreadable = ownership.IsPreviewActive &&
                ownership.IsWorkloadOwnedInActiveTemplate &&
                ownership.IsBlocked;
            bool showOwnershipAction = ownership.IsPreviewActive &&
                !ownership.IsBlocked &&
                IsStageablePresentationSetting(state.Definition.Id);
            bool effectiveReadFailed = false;
            WorkloadScalarValue current = fallback;
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
                const float ownershipActionWidth = 96f;
                const float ownershipActionGap = 6f;
                Rect ownershipRect = new Rect(
                    rect.xMax - ownershipActionWidth,
                    rect.y + 2f,
                    ownershipActionWidth,
                    Mathf.Max(0f, rect.height - 4f));
                valueRect.width = Mathf.Max(
                    0f,
                    valueRect.width - ownershipActionWidth - ownershipActionGap);
                string actionLabel = ownership.IsWorkloadOwnedInActiveTemplate
                    ? "Use global"
                    : "Use in workload";
                string actionTooltip = ownership.IsWorkloadOwnedInActiveTemplate
                    ? "Release this selected setting from the workload preview. The global setting remains unchanged."
                    : "Acquire this allowlisted setting for the active workload preview. The global setting remains unchanged.";
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
                    if (current.Kind != WorkloadScalarKind.Boolean)
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
                        WorkloadScalarValue.FromBoolean(boolValue),
                        stageOwned);

                case SettingType.Int:
                case SettingType.NumericInt:
                    if (current.Kind != WorkloadScalarKind.Integer)
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
                        WorkloadScalarValue.FromInteger(intValue),
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
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            WorkloadScalarValue value,
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
            BWTWorkloadSettingDefinitionState state,
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
            BWTWorkloadSettingDefinitionState state,
            Color color)
        {
            return TryStageScalar(
                state,
                ToColorScalar(color),
                suppressDrawerCallbacks: false,
                out _);
        }

        private static bool HasStageableNonDefaultValue(BWTWorkloadSettingDefinitionState state)
        {
            if (state == null)
            {
                return false;
            }

            WorkloadScalarValue defaultValue = state.DefaultScalar;
            if (!TryGetCachedGlobalScalar(state, out WorkloadScalarValue fallback))
            {
                return false;
            }

            BWTWorkloadSettingOwnershipState ownership = Describe(state.Definition);
            WorkloadScalarValue current = fallback;
            if (CanStageOwnedSetting(ownership) &&
                !TryGetEffectivePresentationValue(state, fallback, out current))
            {
                return false;
            }

            return !current.Equals(defaultValue);
        }

        private static bool CanStageOwnedSetting(
            BWTWorkloadSettingOwnershipState ownership)
        {
            return ownership.IsPreviewActive &&
                ownership.IsWorkloadOwnedInActiveTemplate &&
                !ownership.IsBlocked;
        }

        private static bool TryStageScalar(
            BWTWorkloadSettingDefinitionState state,
            WorkloadScalarValue value,
            bool suppressDrawerCallbacks,
            out string reason)
        {
            reason = string.Empty;
            if (state == null || value.Kind != state.ScalarKind)
            {
                reason = "The workload preview does not support this setting value type.";
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
                "The projected presentation setting could not be changed safely.",
                "The projected presentation setting could not be synchronized.",
                suppressDrawerCallbacks,
                out reason);
        }

        private static bool TryAcquirePresentationSetting(
            BWTWorkloadSettingDefinitionState state,
            WorkloadScalarValue globalValue,
            out string reason)
        {
            string settingId = state?.Definition?.Id;
            return TryChangePresentationOwnership(
                settingId,
                new WorkTabPresentationPreviewMutation(
                    WorkTabPresentationPreviewMutationKind.Acquire,
                    settingId,
                    globalValue),
                "The presentation setting could not be acquired safely.",
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
                    WorkloadScalarValue.Empty),
                "The presentation ownership could not be removed safely.",
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
                reason = "This setting is outside the BWT-local workload ownership allowlist.";
                return false;
            }

            BWTWorkloadSettingOwnershipState ownership = Describe(settingId);
            if (!ownership.IsPreviewActive || ownership.IsBlocked)
            {
                reason = ownership.BlockReason ??
                    "The active workload preview is not available for ownership changes.";
                return false;
            }

            return TryPreviewMutation(
                settingId,
                mutation,
                rejectionReason,
                "The presentation ownership change could not be synchronized.",
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
                reason = "The active workload preview is not available for editing.";
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
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            WorkloadScalarValue value,
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
                    ? "Settings import and restore defaults are disabled while the active workload preview cannot be read safely."
                    : _snapshot.FailureReason;
                return true;
            }

            reason = "Settings import and restore defaults are disabled while a workload preview is active. Close the preview first; global settings remain unchanged.";
            return true;
        }

        private static bool TryGetEffectivePresentationValue(
            BWTWorkloadSettingDefinitionState state,
            WorkloadScalarValue fallback,
            out WorkloadScalarValue value)
        {
            value = fallback;
            if (state == null)
            {
                return false;
            }

            BWTWorkloadPresentationSnapshot snapshot = PresentationSnapshot;
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
            BWTWorkloadSettingOwnershipState state = Describe(definition);
            if (!state.IsWorkloadOwnedInActiveTemplate &&
                !state.IsBlocked &&
                !state.CanAcquireWorkloadOwnership)
            {
                return label;
            }

            string marker = state.CanAcquireWorkloadOwnership &&
                !state.IsWorkloadOwnedInActiveTemplate
                ? "Global / use in workload"
                : !state.IsWorkloadOwnedInActiveTemplate
                ? "Preview safety block"
                : state.IsBlocked
                    ? "Preview read-only"
                    : state.IsChangedByActivePreview
                        ? "Preview changed"
                        : "Workload-owned";
            if (state.WillRevertToGlobalOutsidePreview)
            {
                marker += " / global after preview";
            }

            return label + "  -  " + marker;
        }

        internal static string DecorateTooltip(
            SettingDefinition definition,
            string translatedTooltip)
        {
            BWTWorkloadSettingOwnershipState state = Describe(definition);
            if (!state.IsWorkloadOwnedInActiveTemplate &&
                !state.IsBlocked &&
                !state.CanAcquireWorkloadOwnership)
            {
                return translatedTooltip;
            }

            string ownership = state.CanAcquireWorkloadOwnership &&
                !state.IsWorkloadOwnedInActiveTemplate
                ? "This setting is global. It is protected while the workload preview is active; use the explicit 'Use in workload' action to stage ownership without changing global settings."
                : !state.IsWorkloadOwnedInActiveTemplate
                ? "The active workload preview could not be read safely, so this control is disabled until the preview is closed or readable again."
                : state.IsBlocked
                    ? state.IsChangedByActivePreview
                        ? "The active workload preview contains a different value for this setting, but this control is read-only and cannot commit presentation changes."
                        : "The active workload preview owns this setting, but this control is read-only and cannot commit presentation changes."
                    : state.IsChangedByActivePreview
                        ? "The active workload preview changed this setting. Further edits are staged into the workload preview."
                        : "The active workload preview owns this setting. Edits are staged into the workload preview.";
            if (state.WillRevertToGlobalOutsidePreview)
            {
                ownership += " It will return to the global setting when the preview closes.";
            }

            if (state.IsBlocked)
            {
                ownership += " This control is blocked during preview because this setting is outside the BWT-local staging allowlist; the global setting is unchanged.";
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
                    : _snapshot.OwnsPresentationSettings
                        ? "Supported presentation controls are staged into this workload preview; global settings remain unchanged."
                        : "Global settings are protected during this workload preview. Use 'Use in workload' on an allowlisted setting to stage selected ownership.";
                Widgets.Label(
                    textRect,
                    bannerText);
                TooltipHandler.TipRegion(
                    bannerRect,
                    !_snapshot.ReadSucceeded
                        ? "Presentation controls, import, and restore-default operations are disabled until the active workload preview can be read safely. Global settings remain unchanged."
                        : "Supported BWT-local presentation controls edit the projected workload state. Settings outside the local staging allowlist remain read-only; global settings remain unchanged.");
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }

            inRect.yMin += bannerHeight + bannerGap;
        }

        private static BWTWorkloadSettingOwnershipState Describe(SettingDefinition definition) =>
            Describe(definition?.Id);

        private static BWTWorkloadSettingOwnershipState Describe(string settingId)
        {
            EnsureSnapshot();
            bool known = !string.IsNullOrEmpty(settingId) && Metadata.Contains(settingId);
            var state = new BWTWorkloadSettingOwnershipState
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
                    "Global settings are protected while a workload preview is active.";
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
                    ? "The active workload preview could not be read safely."
                    : _snapshot.FailureReason;
            }

            return IsStageablePresentationSetting(settingId)
                ? "The workload-owned presentation setting could not be staged safely; the global setting is unchanged."
                : "This setting is not in the BWT-local workload presentation editing allowlist. It is read-only during preview; the global setting is unchanged.";
        }

        private static bool IsStageablePresentationSetting(string settingId) =>
            !string.IsNullOrEmpty(settingId) && StageablePresentationSettingIds.Contains(settingId);

        private static IDictionary<string, WorkloadScalarValue> CapturePreparedPresentationValues(
            BetterWorkTabSettings settings)
        {
            return WorkloadPresentationValueCache.Capture(
                PreparedPresentationSettings.Keys,
                _snapshot.GlobalValues,
                (string settingId, out WorkloadScalarValue value) =>
                {
                    value = WorkloadScalarValue.Empty;
                    return PreparedPresentationSettings.TryGetValue(
                               settingId, out BWTWorkloadSettingDefinitionState state) &&
                        TryReadGlobalScalar(state, settings, out value);
                },
                (string settingId, out WorkloadScalarValue value) =>
                {
                    value = WorkloadScalarValue.Empty;
                    return PreparedPresentationSettings.TryGetValue(
                        settingId, out BWTWorkloadSettingDefinitionState state) &&
                        (value = state.DefaultScalar).Kind == state.ScalarKind;
                });
        }

        private static bool TryGetCachedGlobalScalar(
            BWTWorkloadSettingDefinitionState state,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            return state != null &&
                PresentationSnapshot.TryGetGlobalValue(state.Definition.Id, out value) &&
                value.Kind == state.ScalarKind;
        }

        internal static bool TryReadGlobalScalar(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            return state?.Field != null && settingsObject != null &&
                TryToScalar(state, state.Field.GetValue(settingsObject), false, out value);
        }

        internal static bool TryReadGlobalExact(
            BWTWorkloadSettingDefinitionState state,
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
            out BWTWorkloadSettingDefinitionState state)
        {
            state = null;
            return IsStageablePresentationSetting(settingId) &&
                PreparedPresentationSettings.TryGetValue(settingId, out state);
        }

        internal static bool TryWriteGlobalScalar(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            WorkloadScalarValue value,
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
            BWTWorkloadSettingDefinitionState state,
            WorkloadScalarValue value,
            out object exactValue,
            out string reason)
        {
            exactValue = null;
            reason = string.Empty;
            if (state == null || value.Kind != state.ScalarKind)
            {
                reason = "The setting value does not match its workload scalar type.";
                return false;
            }

            switch (value.Kind)
            {
                case WorkloadScalarKind.Boolean:
                    exactValue = value.BooleanValue;
                    return true;
                case WorkloadScalarKind.Integer:
                    int normalized = NormalizeStagedInteger(
                        state.Definition.Id,
                        value.IntegerValue);
                    if (normalized != value.IntegerValue)
                    {
                        reason = "The workload value is not in this setting's canonical range.";
                        return false;
                    }

                    exactValue = normalized;
                    return true;
                case WorkloadScalarKind.String:
                    if (TryGetColor(value, out Color color))
                    {
                        if (!StringComparer.Ordinal.Equals(
                                value.StringValue,
                                ToColorScalar(color).StringValue))
                        {
                            reason = "The workload color is not in the canonical RGBA form.";
                            return false;
                        }

                        exactValue = color;
                        return true;
                    }

                    break;
            }

            reason = "The workload scalar could not be normalized for the Better Work Tab setting.";
            return false;
        }

        internal static bool TryWriteGlobalExact(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            object value,
            out string reason)
        {
            reason = string.Empty;
            if (IsPreviewActive && _authorizedGlobalSettingsWriteDepth <= 0)
            {
                reason = "Global Better Work Tab settings are protected while a workload preview is active.";
                return false;
            }

            if (state == null || settingsObject == null ||
                !IsExactValueCompatible(state, value))
            {
                reason = "The exact Better Work Tab setting value has an incompatible type.";
                return false;
            }

            if (state.Field == null)
            {
                reason = "The Better Work Tab setting field could not be resolved.";
                return false;
            }

            state.Field.SetValue(settingsObject, value);
            return true;
        }

        private static bool IsExactValueCompatible(
            BWTWorkloadSettingDefinitionState state,
            object value)
        {
            return state != null && value != null &&
                ((state.ScalarKind == WorkloadScalarKind.Boolean && value is bool) ||
                 (state.ScalarKind == WorkloadScalarKind.Integer && value is int) ||
                 (state.ScalarKind == WorkloadScalarKind.String && value is Color));
        }

        internal static bool TryToScalar(
            BWTWorkloadSettingDefinitionState state,
            object raw,
            bool normalizeInteger,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            if (state == null)
            {
                return false;
            }

            if (state.ScalarKind == WorkloadScalarKind.Boolean && raw is bool boolean)
            {
                value = WorkloadScalarValue.FromBoolean(boolean);
                return true;
            }

            if (state.ScalarKind == WorkloadScalarKind.Integer && raw is int integer)
            {
                value = WorkloadScalarValue.FromInteger(normalizeInteger
                    ? NormalizeStagedInteger(state.Definition.Id, integer)
                    : integer);
                return true;
            }

            if (state.ScalarKind == WorkloadScalarKind.String && raw is Color color)
            {
                value = ToColorScalar(color);
                return true;
            }

            return false;
        }

        private static WorkloadScalarValue ToColorScalar(Color color)
        {
            return WorkloadScalarValue.FromString(
                "#" + ColorUtility.ToHtmlStringRGBA(color));
        }

        internal static bool TryGetColor(
            WorkloadScalarValue value,
            out Color color)
        {
            color = default(Color);
            if (value.Kind != WorkloadScalarKind.String ||
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
            BWTWorkloadSettingOwnershipState state = Describe(definition);
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

        internal static BWTWorkloadPresentationSnapshot PresentationSnapshot
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
            WorkTabApplication.Current?.PublishAtomicMutation(
                WorkTabApplicationDimensions.Presentation,
                durable: false,
                broadScope: true,
                mirrorExternal: false);
        }

        private static bool TryGetStageableScalarKind(
            string settingId,
            out WorkloadScalarKind kind)
        {
            kind = WorkloadScalarKind.Empty;
            if (!TryGetStageableDefinition(settingId, out BWTWorkloadSettingDefinitionState state))
            {
                return false;
            }

            kind = state.ScalarKind;
            return true;
        }

        private static bool IsHeaderPresentationSetting(string settingId)
        {
            return string.Equals(settingId, HeadersAngled, StringComparison.Ordinal) ||
                string.Equals(settingId, DragdropRemoveHeaderUnderline, StringComparison.Ordinal) ||
                string.Equals(settingId, HeadersAngleRotation, StringComparison.Ordinal) ||
                string.Equals(settingId, "headers.horizontalOffset", StringComparison.Ordinal) ||
                string.Equals(settingId, "headers.angledColor", StringComparison.Ordinal) ||
                string.Equals(settingId, HeadersUnderlineColor, StringComparison.Ordinal);
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
                "columns.movedMarkerColor",
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
    /// Only scalar types that V2 can represent are intentionally supported.
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

    internal sealed class BWTWorkloadSettingDefinitionState
    {
        internal BWTWorkloadSettingDefinitionState(
            SettingDefinition definition,
            SettingType originalType,
            WorkloadScalarKind scalarKind,
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
        internal WorkloadScalarKind ScalarKind { get; }
        internal FieldInfo Field { get; }
        internal WorkloadScalarValue DefaultScalar { get; set; }
        internal Action<Color, Action<Color>> OpenColorPicker { get; }

        private void OpenPreparedColorPicker(Color initialColor, Action<Color> _)
        {
            BWTWorkloadSettingsOwnershipPolicy.OpenColorPicker(this, initialColor);
        }
    }

    internal sealed class BWTWorkloadPresentationSettingsStore : IWorkloadPresentationSettingsStore
    {
        public object Identity => BetterWorkTabMod.Settings;
        public long Revision => BWTWorkloadSettingsOwnershipPolicy.GlobalSettingsRevision;

        public bool TryRead(string settingId, out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null &&
                BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                    settingId, out BWTWorkloadSettingDefinitionState state) &&
                BWTWorkloadSettingsOwnershipPolicy.TryReadGlobalExact(
                    state, settings, out object exact) &&
                BWTWorkloadSettingsOwnershipPolicy.TryToScalar(
                    state, exact, false, out value);
        }

        public bool TryCanonicalize(
            string settingId,
            WorkloadScalarValue requested,
            out WorkloadScalarValue canonical,
            out string reason)
        {
            canonical = WorkloadScalarValue.Empty;
            reason = string.Empty;
            return BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                       settingId, out BWTWorkloadSettingDefinitionState state) &&
                BWTWorkloadSettingsOwnershipPolicy.TryGetCanonicalGlobalExact(
                    state, requested, out object exact, out reason) &&
                BWTWorkloadSettingsOwnershipPolicy.TryToScalar(
                    state, exact, false, out canonical);
        }

        public bool TryWrite(
            string settingId,
            WorkloadScalarValue value,
            out string reason)
        {
            reason = string.Empty;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null &&
                BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                    settingId, out BWTWorkloadSettingDefinitionState state) &&
                BWTWorkloadSettingsOwnershipPolicy.TryGetCanonicalGlobalExact(
                    state, value, out object exact, out reason) &&
                BWTWorkloadSettingsOwnershipPolicy.TryWriteGlobalExact(
                    state, settings, exact, out reason);
        }

        public long BeginOwnedMutation(IEnumerable<string> settingIds) =>
            BWTWorkloadSettingsOwnershipPolicy.BeginOwnedGlobalSettingsMutation(settingIds);

        public IDisposable BeginWriteLease() =>
            BWTWorkloadSettingsOwnershipPolicy.BeginAuthorizedGlobalSettingsWrite();

        public bool TryPersist(out string reason)
        {
            reason = string.Empty;
            try
            {
                BetterWorkTabMod.Settings?.Write();
                return BetterWorkTabMod.Settings != null;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }

    internal struct BWTWorkloadSettingOwnershipState
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

    internal sealed class BWTWorkloadPresentationSnapshot
    {
        private readonly Dictionary<string, WorkloadScalarValue> _globalValues;
        private readonly Dictionary<string, Color> _colors;
        private readonly Dictionary<string, WorkloadIntent<WorkloadSettingValue>> _afterIntents;
        private readonly HashSet<string> _changedSettings;

        private BWTWorkloadPresentationSnapshot(
            bool isActive,
            bool readSucceeded,
            string identity,
            string failureReason,
            IDictionary<string, WorkloadScalarValue> globalValues,
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> beforeIntents,
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> afterIntents)
        {
            IsActive = isActive;
            ReadSucceeded = readSucceeded;
            Identity = identity ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
            _globalValues = new Dictionary<string, WorkloadScalarValue>(
                globalValues, StringComparer.Ordinal);
            _colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, WorkloadScalarValue> entry in _globalValues)
            {
                CacheColor(entry.Key, entry.Value);
            }

            Dictionary<string, WorkloadIntent<WorkloadSettingValue>> beforeIntentMap =
                CopyPresentationIntents(beforeIntents);
            _afterIntents = CopyPresentationIntents(afterIntents);
            _changedSettings = new HashSet<string>(beforeIntentMap.Keys, StringComparer.Ordinal);
            foreach (KeyValuePair<string, WorkloadIntent<WorkloadSettingValue>> entry in _afterIntents)
            {
                if (beforeIntentMap.TryGetValue(entry.Key, out WorkloadIntent<WorkloadSettingValue> before) &&
                    before.Equals(entry.Value))
                {
                    _changedSettings.Remove(entry.Key);
                }
                else
                {
                    _changedSettings.Add(entry.Key);
                }

                if (!IsOwned(entry.Value))
                {
                    continue;
                }

                OwnsPresentationSettings = true;
                if (entry.Value.HasValue)
                {
                    CacheColor(entry.Key, entry.Value.Value.Scalar);
                }
            }
        }

        internal bool IsActive { get; }
        internal bool ReadSucceeded { get; }
        internal bool OwnsPresentationSettings { get; }
        internal string Identity { get; }
        internal string FailureReason { get; }

        internal static BWTWorkloadPresentationSnapshot CreateInactive(
            IDictionary<string, WorkloadScalarValue> globalValues)
        {
            return new BWTWorkloadPresentationSnapshot(
                false,
                true,
                string.Empty,
                string.Empty,
                globalValues,
                null,
                null);
        }

        internal static BWTWorkloadPresentationSnapshot FromPreview(
            WorkTabPresentationPreviewState preview,
            IDictionary<string, WorkloadScalarValue> globalValues)
        {
            return new BWTWorkloadPresentationSnapshot(
                true,
                true,
                preview.Identity,
                string.Empty,
                globalValues,
                preview.BeforeIntents,
                preview.AfterIntents);
        }

        internal static BWTWorkloadPresentationSnapshot Failed(
            IDictionary<string, WorkloadScalarValue> globalValues,
            string reason,
            string identity,
            bool isActive = true)
        {
            return new BWTWorkloadPresentationSnapshot(
                isActive,
                false,
                identity,
                reason,
                globalValues,
                null,
                null);
        }

        internal IDictionary<string, WorkloadScalarValue> GlobalValues => _globalValues;

        internal bool TryGetGlobalValue(string settingId, out WorkloadScalarValue value)
        {
            return _globalValues.TryGetValue(settingId, out value);
        }

        internal void SetGlobalValue(string settingId, WorkloadScalarValue value)
        {
            _globalValues[settingId] = value;
            CacheColor(settingId, value);
        }

        internal bool OwnsPresentationSetting(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) &&
                _afterIntents.TryGetValue(
                    settingId,
                    out WorkloadIntent<WorkloadSettingValue> intent) &&
                IsOwned(intent);
        }

        internal bool IsChanged(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) && _changedSettings.Contains(settingId);
        }

        internal bool TryResolveOwnedScalar(
            string settingId,
            WorkloadScalarKind expectedKind,
            WorkloadScalarValue fallback,
            out WorkloadScalarValue value)
        {
            value = fallback;
            if (!_afterIntents.TryGetValue(
                    settingId,
                    out WorkloadIntent<WorkloadSettingValue> intent) ||
                !IsOwned(intent))
            {
                return false;
            }

            if (intent.IsClear)
            {
                return true;
            }

            if (!intent.HasValue ||
                intent.Value.Scalar.Kind != expectedKind)
            {
                return false;
            }

            value = intent.Value.Scalar;
            return true;
        }

        internal bool GetBool(string settingId)
        {
            return TryGetValue(settingId, out WorkloadScalarValue value) &&
                value.Kind == WorkloadScalarKind.Boolean && value.BooleanValue;
        }

        internal int GetInt(string settingId)
        {
            return TryGetValue(settingId, out WorkloadScalarValue value) &&
                value.Kind == WorkloadScalarKind.Integer
                ? value.IntegerValue
                : 0;
        }

        internal Color GetColor(string settingId)
        {
            return _colors.TryGetValue(settingId, out Color color)
                ? color
                : default(Color);
        }

        private bool TryGetValue(string settingId, out WorkloadScalarValue value)
        {
            if (_afterIntents.TryGetValue(
                    settingId,
                    out WorkloadIntent<WorkloadSettingValue> intent) &&
                IsOwned(intent) && intent.HasValue)
            {
                value = intent.Value.Scalar;
                return true;
            }

            return _globalValues.TryGetValue(settingId, out value);
        }

        private void CacheColor(string settingId, WorkloadScalarValue value)
        {
            if (!BWTWorkloadSettingsOwnershipPolicy.TryGetColor(value, out Color color))
            {
                return;
            }

            _colors[settingId] = color;
        }

        private static bool IsOwned(WorkloadIntent<WorkloadSettingValue> intent)
        {
            return intent.IsClear ||
                (intent.HasValue &&
                 intent.Value.Ownership == WorkloadSettingOwnership.WorkloadOwned);
        }

        private static Dictionary<string, WorkloadIntent<WorkloadSettingValue>>
            CopyPresentationIntents(
                IReadOnlyList<WorkloadPresentationSettingIntentEntry> entries)
        {
            var intents = new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(
                StringComparer.Ordinal);
            if (entries == null)
            {
                return intents;
            }

            // WorkloadProjectedState is the canonical owner of legacy-to-typed
            // promotion and release normalization. Its intent list therefore
            // already contains every effective Set/Clear entry exactly once.
            for (int i = 0; i < entries.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry = entries[i];
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
