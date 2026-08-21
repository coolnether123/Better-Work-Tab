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
using Better_Work_Tab.UI.Headers;
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

        private static IReadOnlyDictionary<string, WorkloadScalarKind> _supportedPresentationKinds;

        private static readonly HashSet<SettingDefinition> PreparedDefinitions =
            new HashSet<SettingDefinition>();

        private static readonly Dictionary<SettingDefinition, BWTWorkloadSettingDefinitionState>
            PreparedSettingRows =
                new Dictionary<SettingDefinition, BWTWorkloadSettingDefinitionState>();

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
                HeadersUnderlineColor
            };

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
        private static bool _suppressNextGlobalSettingsWrite;
        [ThreadStatic]
        private static int _authorizedGlobalSettingsWriteDepth;

        internal static void PrepareDefinition(
            SettingDefinition definition,
            IDictionary<string, WorkloadScalarKind> supportedPresentationKinds)
        {
            if (definition == null ||
                !Metadata.ContainsKey(definition.Id) ||
                !PreparedDefinitions.Add(definition))
            {
                return;
            }

            if (supportedPresentationKinds != null &&
                TryDeriveSupportedPresentationKind(
                    definition,
                    out WorkloadScalarKind supportedPresentationKind))
            {
                supportedPresentationKinds[definition.Id] = supportedPresentationKind;
            }

            SettingType originalType = definition.Type;
            if (
                TryCreateStageableDefinitionState(
                    definition,
                    out BWTWorkloadSettingDefinitionState rowState))
            {
                PreparedSettingRows[definition] = rowState;
                WrapStageableOnChanged(definition);
                InstallStageableDefinition(rowState);
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
                !PreparedSettingRows.ContainsKey(definition))
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

            if (string.Equals(definition.ParentId, HeadersAngled, StringComparison.Ordinal))
            {
                if (definition.Suppressions == null)
                {
                    definition.Suppressions = new List<SettingSuppression>();
                }

                definition.Suppressions.Add(new SettingSuppression
                {
                    When = settings => !IsAngledHeaderModeEnabled(settings as BetterWorkTabSettings),
                    Reason = _ => "Enable angled Work headers to edit this setting.",
                    SuppressorSettingId = HeadersAngled,
                    LinkLabel = "Angled headers"
                });
            }
        }

        internal static void PublishSupportedPresentationKinds(
            IReadOnlyDictionary<string, WorkloadScalarKind> supportedPresentationKinds)
        {
            _supportedPresentationKinds = supportedPresentationKinds;
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
            if (IsPreviewActive)
            {
                // Workload preview owns only its projected state. The mode
                // selector is a global backend preference and must not
                // transition or persist while the preview is open.
                _lastObservedLegacyMode = previousLegacy;
                _lastObservedLegacyModeValid = true;
                Messages.Message(
                    "Workload mode cannot be changed while a workload preview is active. Close the preview first.",
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

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
            _supportedPresentationKinds = null;
            _snapshotValid = false;
            SuppressedOnChanged.Clear();
            _suppressNextGlobalSettingsWrite = false;
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

        internal static bool IsPreviewActive => WorkloadGateway.IsV2PreviewSessionActive;

        internal static IBWTWorkloadSettingsApplyWriter CreateApplyWriter()
        {
            return new BWTWorkloadSettingsApplyWriter();
        }

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

        private static bool TryCreateStageableDefinitionState(
            SettingDefinition definition,
            out BWTWorkloadSettingDefinitionState state)
        {
            state = null;
            if (definition == null ||
                !StageablePresentationSettingIds.Contains(definition.Id) ||
                !TryGetDefinitionScalarKind(definition, out WorkloadScalarKind kind))
            {
                return false;
            }

            state = new BWTWorkloadSettingDefinitionState(
                definition,
                definition.Type,
                kind,
                definition.DefaultValue,
                definition.MinValue,
                definition.MaxValue,
                definition.EmphasizeAsHeader,
                definition.ControlsChildVisibility);
            return true;
        }

        private static void InstallStageableDefinition(
            BWTWorkloadSettingDefinitionState state)
        {
            SettingDefinition definition = state.Definition;
            definition.Type = SettingType.Custom;
            definition.CustomDrawer = (rect, label, tooltip, settingsObject, disabled) =>
                DrawStageableSettingRow(state, rect, label, tooltip, settingsObject, disabled);
            definition.CustomHasNonDefaultValue = settingsObject =>
                HasStageableNonDefaultValue(state, settingsObject);
            definition.CustomReset = settingsObject =>
            {
                if (!TryGetDefaultScalar(state, out WorkloadScalarValue defaultValue))
                {
                    return;
                }

                BWTWorkloadSettingOwnershipState ownership = Describe(definition);
                if (ownership.IsPreviewActive)
                {
                    // CustomReset cannot report failure to the shared drawer,
                    // which always invokes OnChanged and the page write callback
                    // after this delegate returns. Suppress both callbacks even
                    // when projected staging fails so reset remains fail-closed.
                    MarkStagedMutation(definition.Id);
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

            if (!TryReadGlobalScalar(state, settingsObject, out WorkloadScalarValue fallback))
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
                if (!TryGetEffectivePresentationValue(state.Definition.Id, fallback, out current))
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
                        MarkStagedMutation(state.Definition.Id);
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
                    bool boolChanged = state.EmphasizeAsHeader || state.ControlsChildVisibility
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
                    int min = state.MinValue.HasValue
                        ? Mathf.RoundToInt(state.MinValue.Value)
                        : int.MinValue;
                    int max = state.MaxValue.HasValue
                        ? Mathf.RoundToInt(state.MaxValue.Value)
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
                        (initialColor, unusedOnSelected) => OpenColorPicker(
                            state,
                            settingsObject,
                            initialColor,
                            stageOwned));
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

            if (!CanWriteGlobalFromSettingsUi(state))
            {
                return false;
            }

            return TryWriteGlobalScalar(state, settingsObject, value, out _);
        }

        private static void OpenColorPicker(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            Color initialColor,
            bool stageOwned)
        {
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
                    TryStageScalar(
                        state,
                        ToColorScalar(committedColor),
                        suppressDrawerCallbacks: false,
                        out _);
                }
            };

            var dialog = new Spine.UI.ColourPicker.Dialog_ColourPicker(
                initialColor,
                (newColor, closing) =>
                {
                    bool accepted = stageOwned
                        ? TryStageScalar(
                            state,
                            ToColorScalar(newColor),
                            suppressDrawerCallbacks: false,
                            out _)
                        : TryCommitGlobalScalar(
                            state,
                            settingsObject,
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
                        TryStageScalar(
                            state,
                            ToColorScalar(newColor),
                            suppressDrawerCallbacks: false,
                            out _);
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

        private static bool HasStageableNonDefaultValue(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject)
        {
            if (state == null || !TryGetDefaultScalar(state, out WorkloadScalarValue defaultValue))
            {
                return false;
            }

            if (!TryReadGlobalScalar(state, settingsObject, out WorkloadScalarValue fallback))
            {
                return false;
            }

            BWTWorkloadSettingOwnershipState ownership = Describe(state.Definition);
            WorkloadScalarValue current = fallback;
            if (CanStageOwnedSetting(ownership) &&
                !TryGetEffectivePresentationValue(state.Definition.Id, fallback, out current))
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
            if (state == null || !TryGetSupportedPresentationKind(state.Definition.Id, out WorkloadScalarKind expectedKind) ||
                value.Kind != expectedKind)
            {
                reason = "The workload preview does not support this setting value type.";
                if (state != null)
                {
                    BlockedReasons[state.Definition.Id] = reason;
                }

                return false;
            }

            WorkloadPreviewController controller = WorkloadPreviewController.Current;
            if (controller == null || !controller.IsActive || controller.ProjectedProvider == null)
            {
                reason = "The active workload preview is not available for editing.";
                BlockedReasons[state.Definition.Id] = reason;
                return false;
            }

            WorkTabEffectiveStateMutationResult result =
                controller.ProjectedProvider.SetPresentationSetting(state.Definition.Id, value);
            if (result.IsBlocked || (!result.Accepted && !result.IsNoOp))
            {
                reason = string.IsNullOrEmpty(result.Reason)
                    ? "The projected presentation setting could not be changed safely."
                    : result.Reason;
                BlockedReasons[state.Definition.Id] = reason;
                WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
                return false;
            }

            if (!controller.SynchronizeAfterInput())
            {
                reason = controller.LastMessage;
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "The projected presentation setting could not be synchronized.";
                }

                BlockedReasons[state.Definition.Id] = reason;
                return false;
            }

            Refresh();
            InvalidateWorkTabPresentation(state.Definition.Id);
            if (suppressDrawerCallbacks)
            {
                MarkStagedMutation(state.Definition.Id);
            }

            return true;
        }

        private static bool TryAcquirePresentationSetting(
            BWTWorkloadSettingDefinitionState state,
            WorkloadScalarValue globalValue,
            out string reason)
        {
            reason = string.Empty;
            if (state == null || !IsStageablePresentationSetting(state.Definition.Id))
            {
                reason = "This setting is outside the BWT-local workload ownership allowlist.";
                return false;
            }

            BWTWorkloadSettingOwnershipState ownership = Describe(state.Definition);
            if (!ownership.IsPreviewActive || ownership.IsBlocked)
            {
                reason = ownership.BlockReason ??
                    "The active workload preview is not available for ownership changes.";
                return false;
            }

            WorkloadPreviewController controller = WorkloadPreviewController.Current;
            if (controller == null || !controller.IsActive || controller.ProjectedProvider == null)
            {
                reason = "The active workload preview is not available for editing.";
                return false;
            }

            WorkTabEffectiveStateMutationResult result =
                controller.ProjectedProvider.AcquirePresentationSetting(
                    state.Definition.Id,
                    globalValue);
            if (result.IsBlocked || (!result.Accepted && !result.IsNoOp))
            {
                reason = string.IsNullOrEmpty(result.Reason)
                    ? "The presentation setting could not be acquired safely."
                    : result.Reason;
                BlockedReasons[state.Definition.Id] = reason;
                WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
                return false;
            }

            if (!controller.SynchronizeAfterInput())
            {
                reason = controller.LastMessage;
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "The presentation ownership change could not be synchronized.";
                }

                BlockedReasons[state.Definition.Id] = reason;
                return false;
            }

            WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
            Refresh();
            InvalidateWorkTabPresentation(state.Definition.Id);
            return true;
        }

        private static bool TryReleasePresentationSetting(
            string settingId,
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

            WorkloadPreviewController controller = WorkloadPreviewController.Current;
            if (controller == null || !controller.IsActive || controller.ProjectedProvider == null)
            {
                reason = "The active workload preview is not available for editing.";
                return false;
            }

            WorkTabEffectiveStateMutationResult result =
                controller.ProjectedProvider.ReleasePresentationSetting(settingId);
            if (result.IsBlocked || (!result.Accepted && !result.IsNoOp))
            {
                reason = string.IsNullOrEmpty(result.Reason)
                    ? "The presentation ownership could not be removed safely."
                    : result.Reason;
                BlockedReasons[settingId] = reason;
                WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
                return false;
            }

            if (!controller.SynchronizeAfterInput())
            {
                reason = controller.LastMessage;
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "The presentation ownership change could not be synchronized.";
                }

                BlockedReasons[settingId] = reason;
                return false;
            }

            WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
            Refresh();
            InvalidateWorkTabPresentation(settingId);
            return true;
        }

        private static bool TryCommitGlobalScalar(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            WorkloadScalarValue value,
            out string reason)
        {
            reason = string.Empty;
            if (!CanWriteGlobalFromSettingsUi(state))
            {
                reason = "The workload-owned setting cannot write global state during preview.";
                return false;
            }

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

        private static bool CanWriteGlobalFromSettingsUi(
            BWTWorkloadSettingDefinitionState state)
        {
            if (state?.Definition == null)
            {
                return false;
            }

            BWTWorkloadSettingOwnershipState ownership = Describe(state.Definition);
            // Every normal settings-UI write is global. During a workload
            // preview the global object is immutable; allowlisted ownership
            // changes must go through the explicit projected action instead.
            return !ownership.IsPreviewActive;
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
                state.BlockReason = ResolveBlockReason(settingId, settingType);
                return state;
            }

            // A dimension ownership bit is not ownership of every setting in
            // that dimension. Only entries represented by the active template
            // may be marked or blocked here.
            if (!IsPresentationKeyOwned(settingId))
            {
                state.CanAcquireWorkloadOwnership =
                    IsStageablePresentationSetting(settingId);
                state.IsBlocked = !state.CanAcquireWorkloadOwnership;
                state.BlockReason = state.IsBlocked
                    ? ResolveBlockReason(settingId, settingType)
                    : string.Empty;
                return state;
            }

            state.IsGloballyOwned = false;
            state.IsWorkloadOwnedInActiveTemplate = true;
            state.IsChangedByActivePreview = IsPreviewChanged(settingId);
            state.WillRevertToGlobalOutsidePreview = true;
            state.CanReleaseWorkloadOwnership = IsStageablePresentationSetting(settingId);
            state.IsBlocked = !IsStageablePresentationSetting(settingId);
            state.BlockReason = state.IsBlocked
                ? ResolveBlockReason(settingId, settingType)
                : string.Empty;
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

            return IsStageablePresentationSetting(settingId)
                ? "The workload-owned presentation setting could not be staged safely; the global setting is unchanged."
                : "This setting is not in the BWT-local workload presentation editing allowlist. It is read-only during preview; the global setting is unchanged.";
        }

        private static bool IsPreviewChanged(string settingId)
        {
            bool hasBeforeIntent = _snapshot.BeforeIntents.TryGetValue(
                settingId,
                out WorkloadIntent<WorkloadSettingValue> beforeIntent);
            bool hasAfterIntent = _snapshot.AfterIntents.TryGetValue(
                settingId,
                out WorkloadIntent<WorkloadSettingValue> afterIntent);
            if (hasBeforeIntent || hasAfterIntent)
            {
                return hasBeforeIntent != hasAfterIntent ||
                    (hasBeforeIntent && !beforeIntent.Equals(afterIntent));
            }

            bool hasBefore = _snapshot.Before.TryGetValue(settingId, out WorkloadScalarValue before);
            bool hasAfter = _snapshot.After.TryGetValue(settingId, out WorkloadScalarValue after);
            return hasBefore != hasAfter || (hasBefore && !before.Equals(after));
        }

        private static bool TryGetSupportedPresentationKind(
            string settingId,
            out WorkloadScalarKind kind)
        {
            kind = WorkloadScalarKind.Empty;
            if (IsStageablePresentationSetting(settingId) &&
                TryGetDefinition(settingId, out SettingDefinition definition) &&
                PreparedSettingRows.TryGetValue(
                    definition,
                    out BWTWorkloadSettingDefinitionState state))
            {
                kind = state.ScalarKind;
                return true;
            }

            if (_supportedPresentationKinds == null)
            {
                BWTSettingsRegistry.EnsureInitialized();
            }

            return _supportedPresentationKinds != null &&
                _supportedPresentationKinds.TryGetValue(settingId, out kind);
        }

        private static bool IsStageablePresentationSetting(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) &&
                StageablePresentationSettingIds.Contains(settingId);
        }

        private static bool TryGetDefinition(
            string settingId,
            out SettingDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(settingId) ||
                !Metadata.ContainsKey(settingId))
            {
                return false;
            }

            IReadOnlyList<SettingDefinition> definitions = BWTSettingsRegistry.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                SettingDefinition candidate = definitions[i];
                if (candidate != null &&
                    string.Equals(candidate.Id, settingId, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetDefinitionScalarKind(
            SettingDefinition definition,
            out WorkloadScalarKind kind)
        {
            kind = WorkloadScalarKind.Empty;
            if (definition == null ||
                string.IsNullOrEmpty(definition.FieldName) ||
                definition.ValueGetter != null ||
                definition.ValueSetter != null)
            {
                return false;
            }

            FieldInfo field = FindSettingsField(definition);
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

            if (definition.Type == SettingType.Color && field.FieldType == typeof(Color))
            {
                // WorkloadScalarValue has no typed Color member. Canonical RGBA
                // text keeps the value deterministic without widening the V2
                // model in this BWT-local settings pass.
                kind = WorkloadScalarKind.String;
                return true;
            }

            return false;
        }

        private static bool TryDeriveSupportedPresentationKind(
            SettingDefinition definition,
            out WorkloadScalarKind kind)
        {
            return TryGetDefinitionScalarKind(definition, out kind);
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
            Type type = typeof(BetterWorkTabSettings);
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    definition.FieldName,
                    flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        internal static bool TryReadGlobalScalar(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            if (state == null || settingsObject == null)
            {
                return false;
            }

            FieldInfo field = FindField(state.Definition, settingsObject);
            if (field == null)
            {
                return false;
            }

            object raw = field.GetValue(settingsObject);
            switch (state.ScalarKind)
            {
                case WorkloadScalarKind.Boolean:
                    if (raw is bool boolValue)
                    {
                        value = WorkloadScalarValue.FromBoolean(boolValue);
                        return true;
                    }
                    break;
                case WorkloadScalarKind.Integer:
                    if (raw is int intValue)
                    {
                        value = WorkloadScalarValue.FromInteger(intValue);
                        return true;
                    }
                    break;
                case WorkloadScalarKind.String:
                    if (raw is Color colorValue)
                    {
                        value = ToColorScalar(colorValue);
                        return true;
                    }
                    break;
            }

            return false;
        }

        internal static bool TryReadGlobalExact(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            out object value)
        {
            value = null;
            if (state == null || settingsObject == null)
            {
                return false;
            }

            FieldInfo field = FindField(state.Definition, settingsObject);
            if (field == null)
            {
                return false;
            }

            object raw = field.GetValue(settingsObject);
            if (!IsExactValueCompatible(state, raw))
            {
                return false;
            }

            value = raw;
            return true;
        }

        internal static bool TryGetStageableDefinition(
            string settingId,
            out BWTWorkloadSettingDefinitionState state)
        {
            state = null;
            return IsStageablePresentationSetting(settingId) &&
                TryGetDefinition(settingId, out SettingDefinition definition) &&
                PreparedSettingRows.TryGetValue(definition, out state);
        }

        internal static bool TryWriteGlobalScalar(
            BWTWorkloadSettingDefinitionState state,
            object settingsObject,
            WorkloadScalarValue value,
            out string reason)
        {
            reason = string.Empty;
            if (IsPreviewActive && _authorizedGlobalSettingsWriteDepth <= 0)
            {
                reason = "Global Better Work Tab settings are protected while a workload preview is active.";
                return false;
            }

            if (state == null || settingsObject == null ||
                value.Kind != state.ScalarKind)
            {
                reason = "The setting value does not match its workload scalar type.";
                return false;
            }

            FieldInfo field = FindField(state.Definition, settingsObject);
            if (field == null)
            {
                reason = "The Better Work Tab setting field could not be resolved.";
                return false;
            }

            switch (state.ScalarKind)
            {
                case WorkloadScalarKind.Boolean:
                    field.SetValue(settingsObject, value.BooleanValue);
                    return true;
                case WorkloadScalarKind.Integer:
                    field.SetValue(
                        settingsObject,
                        NormalizeStagedInteger(state.Definition.Id, value.IntegerValue));
                    return true;
                case WorkloadScalarKind.String:
                    if (TryGetColor(value, out Color colorValue))
                    {
                        field.SetValue(settingsObject, colorValue);
                        return true;
                    }
                    break;
            }

            reason = "The workload scalar could not be converted to the Better Work Tab setting type.";
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

            FieldInfo field = FindField(state.Definition, settingsObject);
            if (field == null)
            {
                reason = "The Better Work Tab setting field could not be resolved.";
                return false;
            }

            field.SetValue(settingsObject, value);
            return true;
        }

        private static bool IsExactValueCompatible(
            BWTWorkloadSettingDefinitionState state,
            object value)
        {
            if (state == null || value == null)
            {
                return false;
            }

            switch (state.ScalarKind)
            {
                case WorkloadScalarKind.Boolean:
                    return value is bool;
                case WorkloadScalarKind.Integer:
                    return value is int;
                case WorkloadScalarKind.String:
                    return value is Color;
                default:
                    return false;
            }
        }

        private static bool TryGetDefaultScalar(
            BWTWorkloadSettingDefinitionState state,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            if (state == null)
            {
                return false;
            }

            switch (state.ScalarKind)
            {
                case WorkloadScalarKind.Boolean:
                    if (state.DefaultValue is bool boolValue)
                    {
                        value = WorkloadScalarValue.FromBoolean(boolValue);
                        return true;
                    }
                    break;
                case WorkloadScalarKind.Integer:
                    if (state.DefaultValue is int intValue)
                    {
                        value = WorkloadScalarValue.FromInteger(
                            NormalizeStagedInteger(state.Definition.Id, intValue));
                        return true;
                    }
                    break;
                case WorkloadScalarKind.String:
                    if (state.DefaultValue is Color colorValue)
                    {
                        value = ToColorScalar(colorValue);
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static WorkloadScalarValue ToColorScalar(Color color)
        {
            return WorkloadScalarValue.FromString(
                "#" + ColorUtility.ToHtmlStringRGBA(color));
        }

        private static bool TryGetColor(
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

        private static bool IsAngledHeaderModeEnabled(BetterWorkTabSettings settings)
        {
            bool fallback = settings?.enableAngledHeaders ??
                DefaultSettings.enableAngledHeaders;
            return BWTWorkTabEffectiveSettings.GetBool(HeadersAngled, fallback);
        }

        private static bool IsPresentationKeyOwned(string settingId)
        {
            if (string.IsNullOrEmpty(settingId) || !Metadata.ContainsKey(settingId))
            {
                return false;
            }

            // Ownership is determined by the workload entry itself, not by
            // whether BWT can currently render or commit that scalar type.
            // Unsupported entries remain explicitly read-only instead of
            // silently falling through to global settings.
            return _snapshot.OwnedKeys.Contains(settingId);
        }

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
                    ResolveBlockReason(definition.Id, definition.Type));
            }

            return false;
        }

        private static bool BlockAndHandle(string settingId, string reason)
        {
            BlockedReasons[settingId ?? string.Empty] = reason;
            MarkHandledMutation(settingId);
            _suppressNextGlobalSettingsWrite = true;
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

        private static void MarkStagedMutation(string settingId)
        {
            MarkHandledMutation(settingId);
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

        internal static void InvalidateWorkTabPresentation(string settingId = null)
        {
            InvalidateWorkTabPresentationCore(IsHeaderPresentationSetting(settingId));
        }

        internal static void InvalidateWorkTabPresentation(
            IEnumerable<string> settingIds)
        {
            bool includesHeaderSetting = false;
            if (settingIds != null)
            {
                foreach (string settingId in settingIds)
                {
                    if (IsHeaderPresentationSetting(settingId))
                    {
                        includesHeaderSetting = true;
                        break;
                    }
                }
            }

            InvalidateWorkTabPresentationCore(includesHeaderSetting);
        }

        private static void InvalidateWorkTabPresentationCore(
            bool includesHeaderSetting)
        {
            WorkTabEffectiveStateRuntime.InvalidateRenderPass();
            if (includesHeaderSetting)
            {
                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            }

            WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Presentation |
                WorkTabDirtyFlags.SettingsThemeLanguageScale);
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

        internal static Color GetColor(string settingId, Color fallback)
        {
            if (BWTWorkloadSettingsOwnershipPolicy.TryGetEffectivePresentationValue(
                    settingId,
                    WorkloadScalarValue.FromString(
                        "#" + ColorUtility.ToHtmlStringRGBA(fallback)),
                    out WorkloadScalarValue value) &&
                value.Kind == WorkloadScalarKind.String &&
                !string.IsNullOrEmpty(value.StringValue) &&
                ColorUtility.TryParseHtmlString(
                    value.StringValue.StartsWith("#", StringComparison.Ordinal)
                        ? value.StringValue
                        : "#" + value.StringValue,
                    out Color color))
            {
                return color;
            }

            return fallback;
        }
    }

    internal sealed class BWTWorkloadSettingDefinitionState
    {
        internal BWTWorkloadSettingDefinitionState(
            SettingDefinition definition,
            SettingType originalType,
            WorkloadScalarKind scalarKind,
            object defaultValue,
            float? minValue,
            float? maxValue,
            bool emphasizeAsHeader,
            bool controlsChildVisibility)
        {
            Definition = definition;
            OriginalType = originalType;
            ScalarKind = scalarKind;
            DefaultValue = defaultValue;
            MinValue = minValue;
            MaxValue = maxValue;
            EmphasizeAsHeader = emphasizeAsHeader;
            ControlsChildVisibility = controlsChildVisibility;
        }

        internal SettingDefinition Definition { get; }
        internal SettingType OriginalType { get; }
        internal WorkloadScalarKind ScalarKind { get; }
        internal object DefaultValue { get; }
        internal float? MinValue { get; }
        internal float? MaxValue { get; }
        internal bool EmphasizeAsHeader { get; }
        internal bool ControlsChildVisibility { get; }
    }

    internal interface IBWTWorkloadSettingsApplyWriter
    {
        bool TryCapture(
            IEnumerable<string> settingIds,
            out BWTWorkloadSettingsSnapshot snapshot,
            out string reason);

        bool TryApply(
            BWTWorkloadSettingsSnapshot snapshot,
            IReadOnlyDictionary<string, WorkloadScalarValue> values,
            bool persist,
            out string reason);

        bool TryRollback(
            BWTWorkloadSettingsSnapshot snapshot,
            bool persist,
            out string reason);
    }

    internal sealed class BWTWorkloadSettingsSnapshot
    {
        internal BWTWorkloadSettingsSnapshot(
            BetterWorkTabSettings settings,
            IDictionary<string, WorkloadScalarValue> values,
            IDictionary<string, object> exactValues)
        {
            Settings = settings;
            Values = new Dictionary<string, WorkloadScalarValue>(
                values ?? new Dictionary<string, WorkloadScalarValue>(),
                StringComparer.Ordinal);
            ExactValues = new Dictionary<string, object>(
                exactValues ?? new Dictionary<string, object>(),
                StringComparer.Ordinal);
        }

        internal BetterWorkTabSettings Settings { get; }
        internal IReadOnlyDictionary<string, WorkloadScalarValue> Values { get; }
        internal IReadOnlyDictionary<string, object> ExactValues { get; }
    }

    /// <summary>
    /// BWT-local future commit seam for workload-owned presentation settings.
    /// The current Workload V2 backend is intentionally outside this Phase 2
    /// scope; the settings drawer never invokes this writer implicitly.
    /// </summary>
    internal sealed class BWTWorkloadSettingsApplyWriter : IBWTWorkloadSettingsApplyWriter
    {
        public bool TryCapture(
            IEnumerable<string> settingIds,
            out BWTWorkloadSettingsSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = string.Empty;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                reason = "Better Work Tab settings are not loaded.";
                return false;
            }

            var values = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            var exactValues = new Dictionary<string, object>(StringComparer.Ordinal);
            if (settingIds == null)
            {
                reason = "No workload-owned presentation settings were supplied.";
                return false;
            }

            foreach (string settingId in settingIds)
            {
                if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        settingId,
                        out BWTWorkloadSettingDefinitionState state))
                {
                    reason = "The setting '" + (settingId ?? string.Empty) +
                        "' is not supported by the BWT-local workload writer.";
                    return false;
                }

                if (values.ContainsKey(settingId))
                {
                    continue;
                }

                if (!BWTWorkloadSettingsOwnershipPolicy.TryReadGlobalScalar(
                        state,
                        settings,
                        out WorkloadScalarValue value))
                {
                    reason = "The setting '" + settingId + "' could not be read.";
                    return false;
                }

                if (!BWTWorkloadSettingsOwnershipPolicy.TryReadGlobalExact(
                        state,
                        settings,
                        out object exactValue))
                {
                    reason = "The setting '" + settingId +
                        "' could not be captured exactly.";
                    return false;
                }

                values.Add(settingId, value);
                exactValues.Add(settingId, exactValue);
            }

            snapshot = new BWTWorkloadSettingsSnapshot(
                settings,
                values,
                exactValues);
            return true;
        }

        public bool TryApply(
            BWTWorkloadSettingsSnapshot snapshot,
            IReadOnlyDictionary<string, WorkloadScalarValue> values,
            bool persist,
            out string reason)
        {
            reason = string.Empty;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (!TryValidateSnapshot(snapshot, settings, out reason) || values == null)
            {
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "No projected presentation values were supplied.";
                }

                return false;
            }

            var keys = new List<string>();
            foreach (KeyValuePair<string, WorkloadScalarValue> pair in values)
            {
                if (!snapshot.Values.ContainsKey(pair.Key))
                {
                    reason = "The projected presentation payload contains an uncaptured setting.";
                    return false;
                }

                if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        pair.Key,
                        out BWTWorkloadSettingDefinitionState state) ||
                    pair.Value.Kind != state.ScalarKind)
                {
                    reason = "The projected presentation payload contains an invalid setting value.";
                    return false;
                }

                keys.Add(pair.Key);
            }

            if (keys.Count == 0)
            {
                return true;
            }

            keys.Sort(StringComparer.Ordinal);
            var currentExactValues = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string key in snapshot.ExactValues.Keys)
            {
                if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        key,
                        out BWTWorkloadSettingDefinitionState state) ||
                    !BWTWorkloadSettingsOwnershipPolicy.TryReadGlobalExact(
                        state,
                        settings,
                        out object currentValue) ||
                    !object.Equals(snapshot.ExactValues[key], currentValue))
                {
                    reason = "Global Better Work Tab settings changed while the workload apply was being prepared.";
                    return false;
                }

                currentExactValues[key] = currentValue;
            }

            using (BWTWorkloadSettingsOwnershipPolicy.BeginAuthorizedGlobalSettingsWrite())
            {
                try
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        string key = keys[i];
                        BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                            key,
                            out BWTWorkloadSettingDefinitionState state);
                        if (!BWTWorkloadSettingsOwnershipPolicy.TryWriteGlobalScalar(
                                state,
                                settings,
                                values[key],
                                out reason))
                        {
                            TryRestoreExactValues(
                                settings,
                                currentExactValues,
                                out string restoreReason);
                            if (!string.IsNullOrEmpty(restoreReason))
                            {
                                reason += " Rollback also failed: " + restoreReason;
                            }

                            BWTWorkloadSettingsOwnershipPolicy.InvalidateWorkTabPresentation(keys);
                            return false;
                        }
                    }

                    if (persist)
                    {
                        settings.Write();
                    }

                    BWTWorkloadSettingsOwnershipPolicy.InvalidateWorkTabPresentation(
                        keys);
                    return true;
                }
                catch (Exception exception)
                {
                    TryRestoreExactValues(
                        settings,
                        currentExactValues,
                        out string restoreReason);
                    reason = "The workload presentation apply failed: " + exception.Message;
                    if (!string.IsNullOrEmpty(restoreReason))
                    {
                        reason += " Rollback also failed: " + restoreReason;
                    }

                    string persistenceReason = TryPersistRestoredState(settings, persist);
                    if (!string.IsNullOrEmpty(persistenceReason))
                    {
                        reason += " " + persistenceReason;
                    }

                    BWTWorkloadSettingsOwnershipPolicy.InvalidateWorkTabPresentation(
                        keys);
                    return false;
                }
            }
        }

        public bool TryRollback(
            BWTWorkloadSettingsSnapshot snapshot,
            bool persist,
            out string reason)
        {
            reason = string.Empty;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (!TryValidateSnapshot(snapshot, settings, out reason))
            {
                return false;
            }

            if (!TryCaptureExactValues(
                    settings,
                    snapshot.ExactValues.Keys,
                    out Dictionary<string, object> currentExactValues,
                    out reason))
            {
                return false;
            }

            using (BWTWorkloadSettingsOwnershipPolicy.BeginAuthorizedGlobalSettingsWrite())
            {
                try
                {
                    if (!TryRestoreExactValues(settings, snapshot.ExactValues, out reason))
                    {
                        TryRestoreExactValues(
                            settings,
                            currentExactValues,
                            out string compensationReason);
                        if (!string.IsNullOrEmpty(compensationReason))
                        {
                            reason += " Compensation also failed: " + compensationReason;
                        }

                        BWTWorkloadSettingsOwnershipPolicy.InvalidateWorkTabPresentation(
                            snapshot.ExactValues.Keys);
                        return false;
                    }

                    if (persist)
                    {
                        settings.Write();
                    }

                    BWTWorkloadSettingsOwnershipPolicy.InvalidateWorkTabPresentation(
                        snapshot.ExactValues.Keys);
                    return true;
                }
                catch (Exception exception)
                {
                    TryRestoreExactValues(
                        settings,
                        currentExactValues,
                        out string compensationReason);
                    reason = "The Better Work Tab settings rollback failed: " +
                        exception.Message;
                    if (!string.IsNullOrEmpty(compensationReason))
                    {
                        reason += " Compensation also failed: " + compensationReason;
                    }

                    string persistenceReason = TryPersistRestoredState(settings, persist);
                    if (!string.IsNullOrEmpty(persistenceReason))
                    {
                        reason += " " + persistenceReason;
                    }

                    BWTWorkloadSettingsOwnershipPolicy.InvalidateWorkTabPresentation(
                        snapshot.ExactValues.Keys);
                    return false;
                }
            }
        }

        private static bool TryCaptureExactValues(
            BetterWorkTabSettings settings,
            IEnumerable<string> settingIds,
            out Dictionary<string, object> values,
            out string reason)
        {
            values = new Dictionary<string, object>(StringComparer.Ordinal);
            reason = string.Empty;
            if (settingIds == null)
            {
                reason = "No Better Work Tab settings were supplied for capture.";
                return false;
            }

            foreach (string settingId in settingIds)
            {
                if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        settingId,
                        out BWTWorkloadSettingDefinitionState state) ||
                    !BWTWorkloadSettingsOwnershipPolicy.TryReadGlobalExact(
                        state,
                        settings,
                        out object value))
                {
                    reason = "The setting '" + (settingId ?? string.Empty) +
                        "' could not be captured exactly.";
                    return false;
                }

                values[settingId] = value;
            }

            return true;
        }

        private static bool TryValidateSnapshot(
            BWTWorkloadSettingsSnapshot snapshot,
            BetterWorkTabSettings settings,
            out string reason)
        {
            reason = string.Empty;
            if (snapshot == null || snapshot.Settings == null ||
                !ReferenceEquals(snapshot.Settings, settings))
            {
                reason = "The Better Work Tab settings snapshot is stale.";
                return false;
            }

            if (snapshot.Values == null || snapshot.ExactValues == null ||
                snapshot.Values.Count != snapshot.ExactValues.Count)
            {
                reason = "The Better Work Tab settings snapshot is incomplete.";
                return false;
            }

            foreach (string key in snapshot.Values.Keys)
            {
                if (!snapshot.ExactValues.ContainsKey(key))
                {
                    reason = "The Better Work Tab settings snapshot key set is inconsistent.";
                    return false;
                }
            }

            return true;
        }

        private static string TryPersistRestoredState(
            BetterWorkTabSettings settings,
            bool persist)
        {
            if (!persist || settings == null)
            {
                return string.Empty;
            }

            try
            {
                settings.Write();
                return string.Empty;
            }
            catch (Exception exception)
            {
                return "The restored Better Work Tab settings could not be persisted: " +
                    exception.Message;
            }
        }

        private static bool TryRestoreExactValues(
            BetterWorkTabSettings settings,
            IReadOnlyDictionary<string, object> values,
            out string reason)
        {
            reason = string.Empty;
            if (settings == null || values == null)
            {
                reason = "The exact Better Work Tab settings restore payload is missing.";
                return false;
            }

            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        key,
                        out BWTWorkloadSettingDefinitionState state) ||
                    !BWTWorkloadSettingsOwnershipPolicy.TryWriteGlobalExact(
                        state,
                        settings,
                        values[key],
                        out reason))
                {
                    if (string.IsNullOrEmpty(reason))
                    {
                        reason = "The setting '" + key +
                            "' could not be restored exactly.";
                    }

                    return false;
                }
            }

            return true;
        }
    }

    internal struct BWTWorkloadSettingOwnershipState
    {
        internal bool IsKnown;
        internal bool IsGloballyOwned;
        internal bool IsPreviewActive;
        internal bool IsWorkloadOwnedInActiveTemplate;
        internal bool IsChangedByActivePreview;
        internal bool WillRevertToGlobalOutsidePreview;
        internal bool CanAcquireWorkloadOwnership;
        internal bool CanReleaseWorkloadOwnership;
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
                null,
                null,
                null,
                null);

        private BWTWorkloadPreviewSnapshot(
            bool isActive,
            bool readSucceeded,
            bool ownsPresentationSettings,
            string sourceId,
            string failureReason,
            IDictionary<string, WorkloadScalarValue> before,
            IDictionary<string, WorkloadScalarValue> after,
            IDictionary<string, WorkloadIntent<WorkloadSettingValue>> beforeIntents,
            IDictionary<string, WorkloadIntent<WorkloadSettingValue>> afterIntents,
            IEnumerable<string> ownedKeys)
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
            BeforeIntents = new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(
                beforeIntents ?? new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(),
                StringComparer.Ordinal);
            AfterIntents = new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(
                afterIntents ?? new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(),
                StringComparer.Ordinal);
            OwnedKeys = new HashSet<string>(
                ownedKeys ?? new string[0],
                StringComparer.Ordinal);
        }

        internal bool IsActive { get; }
        internal bool ReadSucceeded { get; }
        internal bool OwnsPresentationSettings { get; }
        internal string SourceId { get; }
        internal string FailureReason { get; }
        internal Dictionary<string, WorkloadScalarValue> Before { get; }
        internal Dictionary<string, WorkloadScalarValue> After { get; }
        internal Dictionary<string, WorkloadIntent<WorkloadSettingValue>> BeforeIntents { get; }
        internal Dictionary<string, WorkloadIntent<WorkloadSettingValue>> AfterIntents { get; }
        internal HashSet<string> OwnedKeys { get; }

        internal static BWTWorkloadPreviewSnapshot FromPlan(WorkloadPreviewPlan plan)
        {
            var before = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            var after = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            var beforeIntents = new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(
                StringComparer.Ordinal);
            var afterIntents = new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(
                StringComparer.Ordinal);
            var beforeOwnedKeys = new HashSet<string>(StringComparer.Ordinal);
            var afterOwnedKeys = new HashSet<string>(StringComparer.Ordinal);
            CopyPresentationState(
                plan?.BeforeState,
                before,
                beforeIntents,
                beforeOwnedKeys);
            CopyPresentationState(
                plan?.AfterState,
                after,
                afterIntents,
                afterOwnedKeys);

            // Ownership is a property of the current projected state. The
            // before-state remains available through Before/BeforeIntents for
            // semantic diff inspection, but must not keep a released setting
            // visually or interactively latched as workload-owned.
            var ownedKeys = new HashSet<string>(afterOwnedKeys, StringComparer.Ordinal);

            return new BWTWorkloadPreviewSnapshot(
                true,
                true,
                ownedKeys.Count > 0,
                plan.SourceTemplate?.StableId,
                string.Empty,
                before,
                after,
                beforeIntents,
                afterIntents,
                ownedKeys);
        }

        private static void CopyPresentationState(
            WorkloadProjectedState state,
            IDictionary<string, WorkloadScalarValue> values,
            IDictionary<string, WorkloadIntent<WorkloadSettingValue>> intents,
            ISet<string> ownedKeys)
        {
            if (state == null)
            {
                return;
            }

            if (state.PresentationSettings != null)
            {
                for (int i = 0; i < state.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry = state.PresentationSettings[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    WorkloadIntent<WorkloadSettingValue> intent =
                        WorkloadIntent<WorkloadSettingValue>.CreateSet(
                            WorkloadSettingValue.WorkloadOwned(entry.Value));
                    values[entry.Key] = entry.Value;
                    intents[entry.Key] = intent;
                    ownedKeys.Add(entry.Key);
                }
            }

            if (state.PresentationSettingIntents == null)
            {
                return;
            }

            for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry =
                    state.PresentationSettingIntents[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key) ||
                    entry.Intent.IsNoOpinion)
                {
                    continue;
                }

                intents[entry.Key] = entry.Intent;
                if (entry.Intent.State == WorkloadIntentState.Set &&
                    entry.Intent.HasValue &&
                    entry.Intent.Value.Ownership == WorkloadSettingOwnership.WorkloadOwned)
                {
                    values[entry.Key] = entry.Intent.Value.Scalar;
                    ownedKeys.Add(entry.Key);
                }
                else if (entry.Intent.IsClear)
                {
                    values.Remove(entry.Key);
                    ownedKeys.Add(entry.Key);
                }
                else
                {
                    values.Remove(entry.Key);
                    ownedKeys.Remove(entry.Key);
                }
            }
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
                null,
                null,
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
