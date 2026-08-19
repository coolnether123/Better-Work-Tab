using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.Workloads;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    // Single-purpose file: draw grouped header buttons anchored to the
    // bottom-right (immediately left of the small info/gear button).
    public static class HeaderButtons
    {
        private const float SelectorHeight = BWTBottomBarSelector.Height;
        private const float SelectorMenuWidth = BWTBottomBarSelector.MenuWidth;
        private const float GroupGap = 6f;
        private const float InterControlGap = 6f;
        private const float FluffyTopButtonSize = 30f;
        private const float FluffyTopButtonGap = 4f;
        private const float CompactPreviewActionGap = 3f;
        private const float CompactPreviewCancelWidth = 52f;
        private const float CompactPreviewApplyWidth = 48f;
        private const float CompactSelectorMainWidth = 54f;
        private const float CompactOptionalSelectorMainWidth = 40f;

        private const float WorkloadPopoverGap = 5f;
        private const float WorkloadPopoverWidth = 330f;
        private const float WorkloadPopoverRowHeight = 25f;

        private enum WorkloadFooterPopoverKind
        {
            None,
            Picker,
            Editor,
            ApplyConfirmation
        }

        private static WorkloadFooterPopoverKind _workloadFooterPopover;
        private static string _workloadFooterEditBuffer = string.Empty;
        private static string _workloadFooterEditStableId = string.Empty;
        private static bool _workloadFooterEditCreatesNew;
        private static bool _workloadFooterEditSaveAs;
        private static bool _workloadFooterConfirmDoNotAskAgain;
        private static Vector2 _workloadFooterScroll;
        private static Rect _workloadFooterPopoverRect;
        private static Rect _workloadFooterEditFieldRect;
        private static Rect _workloadFooterEditConfirmRect;
        private static Rect _workloadFooterEditCancelRect;
        private static Rect _workloadFooterConfirmApplyRect;
        private static Rect _workloadFooterConfirmCancelRect;

        // Strict Sleek renders its PawnTable against a different bottom-room
        // contract than BWT's host. The table needs only this small upward
        // shift to clear the footer controls; BWT's own host already inherits
        // vanilla's bottom room and must not stack the full bar height again.
        internal const float StrictSleekBottomTableShift = 12f;

        public struct BottomButtonRects
        {
            public Rect RulesetMain;
            public Rect RulesetMenu;
            public Rect WorkloadMain;
            public Rect WorkloadMenu;
            public Rect WorkloadSaveAs;
            public Rect WorkloadUpdate;
            public Rect WorkloadCancel;
            public Rect WorkloadApply;
            public bool HasRuleset;
            public bool HasWorkload;
            public bool HasWorkloadMenu;
            public bool HasWorkloadPreview;
            public bool HasWorkloadSaveAs;
            public bool HasWorkloadUpdate;
            public bool CompactWorkloadMain;

            /// <summary>
            /// The left edge of everything in the row, so the footer hint text
            /// knows where it has to stop.
            /// </summary>
            public float LeftEdge;

            public bool ContainsRuleset(Vector2 position)
            {
                return HasRuleset && (RulesetMain.Contains(position) || RulesetMenu.Contains(position));
            }

            public bool ContainsWorkload(Vector2 position)
            {
                return HasWorkload &&
                       (WorkloadMain.Contains(position) ||
                        (HasWorkloadMenu && WorkloadMenu.Contains(position)));
            }

            public bool ContainsWorkloadPreviewActions(Vector2 position)
            {
                return HasWorkloadPreview &&
                       ((HasWorkloadSaveAs && WorkloadSaveAs.Contains(position)) ||
                        (HasWorkloadUpdate && WorkloadUpdate.Contains(position)) ||
                        WorkloadCancel.Contains(position) ||
                        WorkloadApply.Contains(position));
            }

            public bool ContainsWorkloadFooter(Vector2 position)
            {
                return ContainsWorkload(position) || ContainsWorkloadPreviewActions(position);
            }
        }

        private enum WorkloadPreviewAction
        {
            SaveAs,
            Update,
            Cancel,
            Apply
        }

        private struct TopButtonRects
        {
            public Rect Priority;
            public Rect Scheduler;
            public Rect Expand;
        }

        /// <summary>
        /// The single description of the bottom-right row. Both the painter and
        /// every hit test read it, so a control cannot be drawn in one place and
        /// clicked in another.
        /// </summary>
        public static BottomButtonRects GetBottomButtonRects(Rect inRect, Rect gearRect)
        {
            BottomButtonRects rects = new BottomButtonRects();

            float rowLeft = Mathf.Min(inRect.xMin, inRect.xMax);
            float rowRight = Mathf.Max(inRect.xMin, inRect.xMax);
            float rowHeight = Mathf.Min(SelectorHeight, Mathf.Max(0f, inRect.height));
            if (rowHeight <= 0f || rowRight <= rowLeft)
            {
                rects.LeftEdge = rowLeft;
                return rects;
            }

            // The row sits on the settings icon's baseline rather than working
            // out its own distance from the bottom edge. Two independent
            // calculations of "just above the bottom" drift the moment either
            // one's padding changes; deriving from the icon means the row cannot
            // end up on a different line from it.
            float y = Mathf.Clamp(
                gearRect.yMax - rowHeight,
                inRect.yMin,
                Mathf.Max(inRect.yMin, inRect.yMax - rowHeight));
            float xRight = Mathf.Clamp(
                gearRect.x - InterControlGap,
                rowLeft,
                rowRight);

            var settings = BetterWorkTabMod.Settings;
            bool workloadsEnabled = settings?.enableWorkloads ?? true;
            bool hasWorkloadComponent =
                Current.Game?.GetComponent<GameComponent_BWTWorldSettings>() != null;
            WorkloadPreviewController preview = workloadsEnabled && hasWorkloadComponent
                ? WorkloadPreviewController.Current
                : null;
            bool previewActive = preview?.IsActive == true;

            if (previewActive)
            {
                LayoutBoundedWorkloadPreview(
                    rowLeft,
                    xRight,
                    y,
                    rowHeight,
                    settings?.enableAutoAssignFeature ?? true,
                    preview.HasSemanticDiff,
                    ref rects);
                return rects;
            }

            if (settings?.enableAutoAssignFeature ?? true)
            {
                float width = BWTBottomBarSelector.MeasureWidth(RuleBuilderGateway.CurrentRulesetLabel());
                rects.RulesetMenu = new Rect(xRight - SelectorMenuWidth, y, SelectorMenuWidth, rowHeight);
                rects.RulesetMain = new Rect(rects.RulesetMenu.x - width, y, width, rowHeight);
                rects.HasRuleset = true;
                xRight = rects.RulesetMain.x - GroupGap;
            }

            if (workloadsEnabled && hasWorkloadComponent)
            {
                float width = BWTBottomBarSelector.MeasureWidth(WorkloadLabel());
                bool showWorkloadMenu = true;
                rects.WorkloadMenu = new Rect(xRight - SelectorMenuWidth, y, SelectorMenuWidth, rowHeight);
                rects.WorkloadMain = new Rect(rects.WorkloadMenu.x - width, y, width, rowHeight);

                rects.HasWorkload = true;
                rects.HasWorkloadMenu = showWorkloadMenu;
                xRight = rects.WorkloadMain.x - GroupGap;

            }

            rects.LeftEdge = Mathf.Max(inRect.xMin, xRight);

            return rects;
        }

        private static void LayoutBoundedWorkloadPreview(
            float leftEdge,
            float rightEdge,
            float y,
            float height,
            bool allowRuleset,
            bool includeUpdate,
            ref BottomButtonRects rects)
        {
            float available = Mathf.Max(0f, rightEdge - leftEdge);
            if (available <= 0f)
            {
                rects.LeftEdge = leftEdge;
                return;
            }

            float workloadWidth = CompactSelectorMainWidth;
            float cancelWidth = CompactPreviewCancelWidth;
            float applyWidth = CompactPreviewApplyWidth;
            float actionGap = CompactPreviewActionGap;
            float mandatoryWidth = workloadWidth + cancelWidth + applyWidth + (actionGap * 2f);

            bool showWorkloadMenu = false;
            bool showRuleset = false;
            bool showUpdate = false;
            bool showSaveAs = false;
            float rulesetWidth = CompactOptionalSelectorMainWidth;
            float used = mandatoryWidth;

            // Add optional affordances in reverse removal order. Consequently,
            // narrowing removes Save As, Update, and the ruleset before the three
            // application controls ever surrender their lane.
            if (used + SelectorMenuWidth <= available)
            {
                showWorkloadMenu = true;
                used += SelectorMenuWidth;
            }
            if (allowRuleset &&
                used + GroupGap + rulesetWidth + SelectorMenuWidth <= available)
            {
                showRuleset = true;
                used += GroupGap + rulesetWidth + SelectorMenuWidth;
            }
            float compactUpdateWidth = CompactWorkloadPreviewActionWidth(
                WorkloadPreviewAction.Update);
            if (includeUpdate && used + actionGap + compactUpdateWidth <= available)
            {
                showUpdate = true;
                used += actionGap + compactUpdateWidth;
            }
            float compactSaveAsWidth = CompactWorkloadPreviewActionWidth(
                WorkloadPreviewAction.SaveAs);
            if (used + actionGap + compactSaveAsWidth <= available)
            {
                showSaveAs = true;
                used += actionGap + compactSaveAsWidth;
            }
            if (available < mandatoryWidth)
            {
                showWorkloadMenu = false;
                showRuleset = false;
                showUpdate = false;
                showSaveAs = false;
                actionGap = available >= 24f
                    ? Mathf.Min(CompactPreviewActionGap, available / 24f)
                    : 0f;
                float controlWidth = Mathf.Max(0f, available - (actionGap * 2f));
                workloadWidth = controlWidth * 0.4f;
                cancelWidth = controlWidth * 0.31f;
                applyWidth = controlWidth - workloadWidth - cancelWidth;
                used = available;
            }
            else
            {
                float spare = available - used;
                float desiredWorkloadWidth = Mathf.Max(
                    workloadWidth,
                    BWTBottomBarSelector.MeasureWidth(WorkloadLabel()));
                float workloadGrowth = Mathf.Min(spare, desiredWorkloadWidth - workloadWidth);
                workloadWidth += workloadGrowth;
                spare -= workloadGrowth;

                if (showRuleset && spare > 0f)
                {
                    float desiredRulesetWidth = Mathf.Max(
                        rulesetWidth,
                        BWTBottomBarSelector.MeasureWidth(RuleBuilderGateway.CurrentRulesetLabel()));
                    float rulesetGrowth = Mathf.Min(spare, desiredRulesetWidth - rulesetWidth);
                    rulesetWidth += rulesetGrowth;
                }
            }

            float x = rightEdge;
            if (showRuleset)
            {
                rects.RulesetMenu = TakeFromRight(ref x, leftEdge, SelectorMenuWidth, y, height);
                rects.RulesetMain = TakeFromRight(ref x, leftEdge, rulesetWidth, y, height);
                rects.HasRuleset =
                    rects.RulesetMain.width > 0f && rects.RulesetMenu.width > 0f;
                x -= GroupGap;
            }

            if (showWorkloadMenu)
            {
                rects.WorkloadMenu = TakeFromRight(ref x, leftEdge, SelectorMenuWidth, y, height);
            }
            rects.WorkloadMain = TakeFromRight(ref x, leftEdge, workloadWidth, y, height);
            rects.HasWorkload = rects.WorkloadMain.width > 0f;
            rects.HasWorkloadMenu = showWorkloadMenu && rects.WorkloadMenu.width > 0f;
            rects.CompactWorkloadMain = workloadWidth < CompactSelectorMainWidth;

            x -= actionGap;
            rects.WorkloadApply = TakeFromRight(ref x, leftEdge, applyWidth, y, height);
            x -= actionGap;
            rects.WorkloadCancel = TakeFromRight(ref x, leftEdge, cancelWidth, y, height);
            if (showUpdate)
            {
                x -= actionGap;
                rects.WorkloadUpdate = TakeFromRight(ref x, leftEdge, compactUpdateWidth, y, height);
            }
            if (showSaveAs)
            {
                x -= actionGap;
                rects.WorkloadSaveAs = TakeFromRight(ref x, leftEdge, compactSaveAsWidth, y, height);
            }
            rects.HasWorkloadPreview =
                rects.HasWorkload &&
                rects.WorkloadCancel.width > 0f &&
                rects.WorkloadApply.width > 0f;
            rects.HasWorkloadSaveAs = showSaveAs && rects.WorkloadSaveAs.width > 0f;
            rects.HasWorkloadUpdate = showUpdate && rects.WorkloadUpdate.width > 0f;
            rects.LeftEdge = Mathf.Max(leftEdge, x);
        }

        private static Rect TakeFromRight(
            ref float rightEdge,
            float leftEdge,
            float width,
            float y,
            float height)
        {
            rightEdge = Mathf.Max(leftEdge, rightEdge);
            float safeWidth = Mathf.Min(
                Mathf.Max(0f, width),
                rightEdge - leftEdge);
            Rect rect = new Rect(rightEdge - safeWidth, y, safeWidth, height);
            rightEdge = rect.xMin;
            return rect;
        }

        private static float CompactWorkloadPreviewActionWidth(
            WorkloadPreviewAction action)
        {
            switch (action)
            {
                case WorkloadPreviewAction.SaveAs:
                    return 58f;
                case WorkloadPreviewAction.Update:
                    return 54f;
                case WorkloadPreviewAction.Cancel:
                    return CompactPreviewCancelWidth;
                case WorkloadPreviewAction.Apply:
                    return CompactPreviewApplyWidth;
                default:
                    return CompactPreviewApplyWidth;
            }
        }

        public static bool TryHandleTopRightFluffyStyleInput(
            IWorkTabLayoutController layout,
            Rect inRect,
            Event evt)
        {
            if (!ShouldShowTopRightFluffyStyle() ||
                evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0)
            {
                return false;
            }

            TopButtonRects rects = GetTopButtonRects(inRect);
            if (rects.Priority.Contains(evt.mousePosition))
            {
                bool prioritiesEnabled = WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                    Find.PlaySettings?.useWorkPriorities ?? true);
                ToggleManualPriorities(!prioritiesEnabled);
                evt.Use();
                return true;
            }

            if (rects.Scheduler.Contains(evt.mousePosition))
            {
                if (!ToggleScheduler(layout))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                evt.Use();
                return true;
            }

            if (rects.Expand.Contains(evt.mousePosition))
            {
                ToggleAllVisibleSubWork(layout);
                evt.Use();
                return true;
            }

            return false;
        }

        public static void DrawTopRightFluffyStyle(IWorkTabLayoutController layout, Rect inRect)
        {
            if (!ShouldShowTopRightFluffyStyle())
            {
                return;
            }

            TopButtonRects rects = GetTopButtonRects(inRect);
            bool prioritiesEnabled = WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                Find.PlaySettings?.useWorkPriorities ?? true);
            if (DrawFluffyTopButton(
                    rects.Priority,
                    prioritiesEnabled ? FluffyWorkTabIcon.PrioritiesDetailed : FluffyWorkTabIcon.PrioritiesSimple,
                    prioritiesEnabled ? "Manual priorities" : "Simple priorities",
                    prioritiesEnabled ? "1" : "Y"))
            {
                ToggleManualPriorities(!prioritiesEnabled);
            }

            bool plannerVisible = FluffyTimeScheduleAssigner.IsOpen || TimePriorityScheduleEditor.IsVisible;
            if (DrawFluffyTopButton(
                    rects.Scheduler,
                    plannerVisible ? FluffyWorkTabIcon.PrioritiesTimed : FluffyWorkTabIcon.PrioritiesWholeDay,
                    plannerVisible ? "Close time priorities" : "Open time priorities",
                    plannerVisible ? "T" : "D"))
            {
                if (!ToggleScheduler(layout))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
            }

            bool anyExpanded = SubWorkDrilldownState.IsExpandBesideActive;
            if (DrawFluffyTopButton(
                    rects.Expand,
                    anyExpanded ? FluffyWorkTabIcon.Collapse : FluffyWorkTabIcon.Expand,
                    anyExpanded ? "Collapse all specific jobs" : "Expand all specific jobs",
                    anyExpanded ? "-" : "+"))
            {
                ToggleAllVisibleSubWork(layout);
            }
        }

        private static bool ToggleScheduler(IWorkTabLayoutController layout)
        {
            return FluffyTimeScheduleAssigner.IsEnabled
                ? FluffyTimeScheduleAssigner.Toggle()
                : TimePriorityScheduleEditor.ToggleFirstVisiblePrioritySchedule(layout);
        }

        public static float GetTopRightReservedWidth()
        {
            return ShouldShowTopRightFluffyStyle()
                ? (FluffyTopButtonSize * 3f) + (FluffyTopButtonGap * 2f) + InterControlGap
                : 0f;
        }

        private static bool ShouldShowTopRightFluffyStyle()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (!FluffyWorkTabGateway.FluffyStyleFeaturesEnabled ||
                !BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FluffyStyleFeatures,
                    settings?.enableFluffyStyleFeatures ?? true) ||
                !FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab)
            {
                return false;
            }

            return FluffyWorkTabGateway.IsPresent
                ? BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FluffyStyleTopButtons,
                    settings?.showFluffyStyleTopButtons ??
                        DefaultSettings.showFluffyStyleTopButtons)
                : BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FluffyStyleStandaloneTopButtons,
                    settings?.showStandaloneFluffyStyleTopButtons ??
                        DefaultSettings.showStandaloneFluffyStyleTopButtons);
        }

        private static TopButtonRects GetTopButtonRects(Rect inRect)
        {
            Rect priority = new Rect(
                inRect.xMax - FluffyTopButtonSize,
                inRect.yMin,
                FluffyTopButtonSize,
                FluffyTopButtonSize);

            Rect scheduler = priority;
            scheduler.x -= FluffyTopButtonSize + FluffyTopButtonGap;

            Rect expand = scheduler;
            expand.x -= FluffyTopButtonSize + FluffyTopButtonGap;

            return new TopButtonRects
            {
                Priority = priority,
                Scheduler = scheduler,
                Expand = expand
            };
        }

        // Public entry point called by the window.
        public static void DrawBottomRightGrouped(Rect inRect, Rect gearRect)
        {
            BottomButtonRects rects = GetBottomButtonRects(inRect, gearRect);
            if (rects.HasRuleset)
            {
                DrawAutoAssignGroup(rects);
            }

            if (rects.HasWorkloadPreview)
            {
                DrawWorkloadPreviewActions(rects);
            }

            if (rects.HasWorkload)
            {
                DrawWorkloadGroup(rects);
            }

            DrawWorkloadFooterPopover(inRect, rects);
            if (_workloadFooterPopover == WorkloadFooterPopoverKind.None)
            {
                WorkloadPreviewController.Current?.UpdateFooterInspectionHover(
                    rects.HasWorkloadUpdate ? rects.WorkloadUpdate : Rect.zero);
            }
            else
            {
                WorkloadPreviewController.Current?.UpdateFooterInspectionHover(Rect.zero);
            }

        }

        /// <summary>
        /// Reserves only the workload footer's already-drawn IMGUI rectangles.
        /// The event is intentionally left available for the widgets below to
        /// process; returning true only prevents the Work-grid router from
        /// interpreting a footer click as a priority edit.
        /// </summary>
        public static bool TryHandleWorkloadFooterInput(
            Rect inRect,
            Rect gearRect,
            Event evt)
        {
            if (evt == null)
            {
                return false;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview?.IsActive == true &&
                _workloadFooterPopover != WorkloadFooterPopoverKind.None &&
                !_workloadFooterEditSaveAs)
            {
                CloseWorkloadFooterPopover();
            }

            bool mouseEvent = evt.type == EventType.MouseDown ||
                              evt.type == EventType.MouseUp ||
                              evt.type == EventType.ScrollWheel;
            bool editingKeyboard = _workloadFooterPopover == WorkloadFooterPopoverKind.Editor &&
                                   (evt.type == EventType.KeyDown ||
                                    evt.type == EventType.KeyUp ||
                                    evt.type == EventType.ValidateCommand) &&
                                   GUI.GetNameOfFocusedControl() == "BWT.WorkloadFooterEditor";
            if (!mouseEvent && !editingKeyboard)
            {
                return false;
            }

            BottomButtonRects rects = GetBottomButtonRects(inRect, gearRect);
            if (!rects.HasWorkload)
            {
                if (_workloadFooterPopover != WorkloadFooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                return false;
            }

            if (evt.type == EventType.ScrollWheel &&
                preview?.ShouldRouteInspectionWheel(evt, rects.WorkloadUpdate) == true)
            {
                return false;
            }

            if (editingKeyboard ||
                _workloadFooterPopoverRect.Contains(evt.mousePosition) ||
                rects.ContainsWorkloadFooter(evt.mousePosition))
            {
                return true;
            }

            if (_workloadFooterPopover != WorkloadFooterPopoverKind.None)
            {
                CloseWorkloadFooterPopover();
            }

            return false;
        }

        public static void ResetWorkloadFooterState()
        {
            CloseWorkloadFooterPopover();
            _workloadFooterScroll = Vector2.zero;
            WorkloadSurfaceCoordinator.Reset();
        }

        private static void DrawAutoAssignGroup(BottomButtonRects rects)
        {
            string name = RuleBuilderGateway.CurrentRulesetLabel();
            bool hasRuleset = RuleBuilderGateway.HasCurrentRuleset();

            if (BWTBottomBarSelector.DrawMain(
                    rects.RulesetMain,
                    BWTBottomBarIcons.Ruleset,
                    name,
                    hasRuleset,
                    hasRuleset
                        ? "BWT_BottomBar_RulesetTooltip".Translate(name)
                        : "BWT_BottomBar_RulesetTooltipEmpty".Translate()))
            {
                if (hasRuleset)
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    RuleBuilderGateway.ApplyCurrentRuleset();
                }
                else
                {
                    // Applying nothing looked like a broken button. With no
                    // ruleset to apply, the useful thing is the list.
                    Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
                }
            }

            if (BWTBottomBarSelector.DrawMenu(rects.RulesetMenu, "BWT_BottomBar_RulesetMenuTooltip".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
            }
        }

        /// <summary>
        /// The name the workload picker shows. Read by the geometry pass as well
        /// as the painter, so the button is always sized for the text it draws.
        /// </summary>
        private static string WorkloadLabel()
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview?.IsActive == true)
            {
                return preview.SourceLabel + " • preview";
            }

            string currentLabel = WorkloadGateway.CurrentLabel();
            return currentLabel.AnyNonWhitespace()
                ? currentLabel
                : "BWT_BottomBar_WorkloadEmpty".Translate().ToString();
        }

        private static void DrawWorkloadGroup(BottomButtonRects rects)
        {
            if (Current.Game?.GetComponent<GameComponent_BWTWorldSettings>() == null)
            {
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            bool hasWorkload = WorkloadGateway.HasCurrentWorkload();
            string name = WorkloadLabel();
            bool legacyMode = WorkloadGateway.CurrentMode == WorkloadBackendMode.Legacy;
            string selectorTooltip = preview?.IsActive == true
                ? preview.ActivePreviewSwitchBlockedMessage
                : hasWorkload
                    ? (legacyMode
                        ? "BWT_BottomBar_WorkloadTooltip".Translate(name)
                        : "Open a non-destructive Workload 2.0 preview for " + name + ".")
                    : "BWT_BottomBar_WorkloadTooltipEmpty".Translate();

            if (DrawWorkloadMainControl(
                    rects.WorkloadMain,
                    name,
                    hasWorkload,
                    selectorTooltip,
                    rects.CompactWorkloadMain))
            {
                if (preview?.IsActive == true)
                {
                    ReportWorkloadFailure(preview.ActivePreviewSwitchBlockedMessage);
                }
                else if (hasWorkload && legacyMode)
                {
                    BeginLegacyWorkloadApply();
                }
                else if (hasWorkload && !legacyMode)
                {
                    if (preview == null)
                    {
                        Messages.Message(
                            "Modern workload preview is unavailable.",
                            MessageTypeDefOf.RejectInput,
                            false);
                    }
                    else
                    {
                        QueuePreviewLifecycleAction(
                            preview,
                            () => preview.BeginCurrentPreview(),
                            notifyPawnTables: false);
                    }
                }
                else
                {
                    OpenWorkloadFooterPicker();
                }
            }

            if (rects.HasWorkloadMenu && DrawWorkloadMenuControl(
                    rects.WorkloadMenu,
                    preview?.IsActive == true
                        ? preview.ActivePreviewSwitchBlockedMessage
                        : "BWT_BottomBar_WorkloadMenuTooltip".Translate()))
            {
                if (preview?.IsActive == true)
                {
                    ReportWorkloadFailure(preview.ActivePreviewSwitchBlockedMessage);
                }
                else
                {
                    OpenWorkloadFooterPicker();
                }
            }
        }

        private static void DrawWorkloadPreviewActions(BottomButtonRects rects)
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview == null || !preview.IsActive)
            {
                return;
            }

            if (rects.HasWorkloadSaveAs)
            {
                DrawWorkloadPreviewButton(
                    rects.WorkloadSaveAs,
                    "Save As",
                    () => QueuePreviewLifecycleAction(
                        preview,
                        BeginWorkloadPreviewSaveAsEditor,
                        notifyPawnTables: false),
                    preview.CanForkPreview,
                    preview.CommitBlockedMessage);
            }
            if (rects.HasWorkloadUpdate)
            {
                DrawWorkloadPreviewButton(
                    rects.WorkloadUpdate,
                    "Update",
                    () => QueuePreviewLifecycleAction(
                        preview,
                        () => preview.UpdatePreview(),
                        notifyPawnTables: true),
                    preview.CanUpdatePreview,
                    preview.CommitBlockedMessage.AnyNonWhitespace()
                        ? preview.CommitBlockedMessage
                        : "Update applies this semantic diff and replaces the same workload template. " +
                          "Hover to inspect changed pawn/worktype cells. Scroll here to move the Work tab.");
            }

            DrawWorkloadPreviewButton(
                rects.WorkloadCancel,
                PreviewActionLabel(rects.WorkloadCancel, "Cancel", "C"),
                () => QueuePreviewLifecycleAction(
                    preview,
                    () => preview.CancelPreview(),
                    notifyPawnTables: true),
                enabled: true,
                tooltip: "Cancel the preview and keep live work priorities unchanged.");
            DrawWorkloadPreviewButton(
                rects.WorkloadApply,
                PreviewActionLabel(rects.WorkloadApply, "Apply", "A"),
                () => QueuePreviewLifecycleAction(
                    preview,
                    () => preview.ApplyPreview(),
                    notifyPawnTables: true),
                preview.CanApplyPreview,
                preview.CommitBlockedMessage);
        }

        private static void DrawWorkloadPreviewButton(
            Rect rect,
            string label,
            Action onClicked,
            bool enabled,
            string tooltip)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            bool clicked = Widgets.ButtonText(rect, label, active: enabled);
            if (tooltip.AnyNonWhitespace())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            if (clicked && enabled)
            {
                onClicked?.Invoke();
            }
        }

        private static void QueuePreviewLifecycleAction(
            WorkloadPreviewController preview,
            Func<bool> action,
            bool notifyPawnTables)
        {
            preview?.QueueLifecycleAction(
                action,
                succeeded =>
                {
                    if (succeeded && notifyPawnTables)
                    {
                        MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                    }
                });
        }

        private static bool DrawWorkloadMainControl(
            Rect rect,
            string value,
            bool hasValue,
            string tooltip,
            bool compact)
        {
            if (compact)
            {
                bool clicked = Widgets.ButtonText(rect, "W", active: true);
                TooltipHandler.TipRegion(rect, tooltip);
                return clicked;
            }

            return BWTBottomBarSelector.DrawMain(
                rect,
                BWTBottomBarIcons.Workload,
                value,
                hasValue,
                tooltip);
        }

        private static string PreviewActionLabel(Rect rect, string fullLabel, string compactLabel)
        {
            return rect.width >= 44f ? fullLabel : compactLabel;
        }

        private static bool DrawWorkloadMenuControl(Rect rect, string tooltip)
        {
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            bool clicked = Widgets.ButtonText(rect, "...");
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            TooltipHandler.TipRegion(rect, tooltip);
            return clicked;
        }

        private static bool DrawFluffyTopButton(
            Rect rect,
            FluffyWorkTabIcon icon,
            string tooltip,
            string fallbackLabel)
        {
            bool clicked;
            if (FluffyWorkTabGateway.TryGetIcon(icon, out Texture2D texture))
            {
                clicked = Widgets.ButtonImage(rect, texture, Color.white, GenUI.MouseoverColor);
            }
            else
            {
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                clicked = Widgets.ButtonText(rect, fallbackLabel);
                Text.Anchor = previousAnchor;
            }

            TooltipHandler.TipRegion(rect, tooltip);
            MouseoverSounds.DoRegion(rect);
            return clicked;
        }

        private static bool ToggleManualPriorities(bool enabled)
        {
            bool current = WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                Find.PlaySettings?.useWorkPriorities ?? true);
            if (current == enabled)
            {
                return true;
            }

            if (!WorkTabEffectiveStateRuntime.TrySetManualMode(enabled))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            return true;
        }

        private static void ToggleAllVisibleSubWork(IWorkTabLayoutController layout)
        {
            if (SubWorkDrilldownState.IsExpandBesideActive)
            {
                SubWorkDrilldownState.CollapseAllExpandBeside();
                WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return;
            }

            if (layout?.Columns == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            bool expandedAny = false;
            HashSet<string> seenWorkTypes = new HashSet<string>();
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTypeDef workType = layout.Columns[i].Column?.workType;
                string defName = workType?.defName;
                if (defName.NullOrEmpty() ||
                    !seenWorkTypes.Add(defName) ||
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType).Count == 0)
                {
                    continue;
                }

                SubWorkDrilldownState.ToggleExpandBeside(workType);
                expandedAny = true;
            }

            if (!expandedAny)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static void OpenWorkloadFooterPicker()
        {
            if (_workloadFooterPopover == WorkloadFooterPopoverKind.Picker)
            {
                CloseWorkloadFooterPopover();
                return;
            }

            WorkloadSurfaceCoordinator.RegisterFooterCloser(CloseWorkloadFooterPopover);
            if (!WorkloadSurfaceCoordinator.TryOpenFooter())
            {
                return;
            }
            _workloadFooterPopover = WorkloadFooterPopoverKind.Picker;
            _workloadFooterEditBuffer = string.Empty;
            _workloadFooterEditStableId = string.Empty;
            _workloadFooterEditCreatesNew = false;
            _workloadFooterEditSaveAs = false;
            _workloadFooterScroll = Vector2.zero;
        }

        private static void CloseWorkloadFooterPopover()
        {
            _workloadFooterPopover = WorkloadFooterPopoverKind.None;
            _workloadFooterEditBuffer = string.Empty;
            _workloadFooterEditStableId = string.Empty;
            _workloadFooterEditCreatesNew = false;
            _workloadFooterEditSaveAs = false;
            _workloadFooterConfirmDoNotAskAgain = false;
            _workloadFooterPopoverRect = Rect.zero;
            _workloadFooterEditFieldRect = Rect.zero;
            _workloadFooterEditConfirmRect = Rect.zero;
            _workloadFooterEditCancelRect = Rect.zero;
            _workloadFooterConfirmApplyRect = Rect.zero;
            _workloadFooterConfirmCancelRect = Rect.zero;
            WorkloadSurfaceCoordinator.NotifyFooterClosed();
        }

        private static void DrawWorkloadFooterPopover(Rect inRect, BottomButtonRects rects)
        {
            if (WorkloadPreviewController.Current?.IsActive == true &&
                !_workloadFooterEditSaveAs)
            {
                if (_workloadFooterPopover != WorkloadFooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                _workloadFooterPopoverRect = Rect.zero;
                return;
            }

            if (_workloadFooterPopover == WorkloadFooterPopoverKind.None || !rects.HasWorkload)
            {
                if (!rects.HasWorkload && _workloadFooterPopover != WorkloadFooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                _workloadFooterPopoverRect = Rect.zero;
                return;
            }

            float desiredHeight = _workloadFooterPopover == WorkloadFooterPopoverKind.Picker
                ? 276f
                : _workloadFooterPopover == WorkloadFooterPopoverKind.Editor
                    ? 112f
                    : 136f;
            float width = Mathf.Min(
                WorkloadPopoverWidth,
                Mathf.Max(210f, inRect.width - 8f));
            float height = Mathf.Min(
                desiredHeight,
                Mathf.Max(84f, inRect.height - 8f));
            Rect anchor = rects.WorkloadMain;
            float x = Mathf.Clamp(
                anchor.xMax - width,
                inRect.xMin + 4f,
                Mathf.Max(inRect.xMin + 4f, inRect.xMax - width - 4f));
            float y = anchor.yMin - height - WorkloadPopoverGap;
            if (y < inRect.yMin + 4f)
            {
                y = anchor.yMax + WorkloadPopoverGap;
            }

            y = Mathf.Clamp(
                y,
                inRect.yMin + 4f,
                Mathf.Max(inRect.yMin + 4f, inRect.yMax - height - 4f));
            Rect panel = new Rect(x, y, width, height);
            _workloadFooterPopoverRect = panel;
            _workloadFooterEditFieldRect = Rect.zero;
            _workloadFooterEditConfirmRect = Rect.zero;
            _workloadFooterEditCancelRect = Rect.zero;
            _workloadFooterConfirmApplyRect = Rect.zero;
            _workloadFooterConfirmCancelRect = Rect.zero;

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            try
            {
                Widgets.DrawBoxSolidWithOutline(
                    panel,
                    new Color(0.055f, 0.07f, 0.08f, 0.97f),
                    new Color(0.38f, 0.52f, 0.55f, 0.75f));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                switch (_workloadFooterPopover)
                {
                    case WorkloadFooterPopoverKind.Picker:
                        DrawWorkloadFooterPicker(panel);
                        break;
                    case WorkloadFooterPopoverKind.Editor:
                        DrawWorkloadFooterEditor(panel);
                        break;
                    case WorkloadFooterPopoverKind.ApplyConfirmation:
                        DrawWorkloadFooterApplyConfirmation(panel);
                        break;
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
            }
        }

        private static void DrawWorkloadFooterPicker(Rect panel)
        {
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 5f, panel.width - 16f, 20f),
                "Workloads");

            IReadOnlyList<WorkloadDescriptor> workloads = WorkloadGateway.SavedWorkloads();
            WorkloadOperationResult<WorkloadDescriptor> currentResult = WorkloadGateway.GetCurrent();
            string currentId = currentResult.Succeeded ? currentResult.Value?.StableId : string.Empty;
            Rect listRect = new Rect(
                panel.xMin + 6f,
                panel.yMin + 28f,
                panel.width - 12f,
                Mathf.Max(28f, panel.height - 68f));
            float viewWidth = Mathf.Max(1f, listRect.width - 16f);
            float viewHeight = Mathf.Max(listRect.height, workloads.Count * WorkloadPopoverRowHeight);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(listRect, ref _workloadFooterScroll, viewRect);
            if (workloads.Count == 0)
            {
                GUI.color = new Color(0.75f, 0.82f, 0.82f, 0.9f);
                Widgets.Label(new Rect(8f, 7f, viewWidth - 16f, 20f), "No saved workloads.");
                GUI.color = Color.white;
            }
            else
            {
                for (int i = 0; i < workloads.Count; i++)
                {
                    WorkloadDescriptor workload = workloads[i];
                    if (workload == null)
                    {
                        continue;
                    }

                    bool selected = StringComparer.Ordinal.Equals(currentId, workload.StableId);
                    string label = selected ? "[x] " + workload.Label : workload.Label;
                    Rect row = new Rect(
                        0f,
                        i * WorkloadPopoverRowHeight,
                        viewWidth,
                        WorkloadPopoverRowHeight - 2f);
                    if (Widgets.ButtonText(row, label))
                    {
                        SelectWorkloadInline(workload.StableId);
                    }
                }
            }
            Widgets.EndScrollView();

            float actionY = panel.yMax - 30f;
            float actionWidth = (panel.width - 12f - (3f * 4f)) / 4f;
            Rect actions = new Rect(panel.xMin + 6f, actionY, panel.width - 12f, 24f);
            DrawWorkloadFooterButton(
                new Rect(actions.xMin, actions.yMin, actionWidth, actions.height),
                "New",
                () => BeginWorkloadFooterEditor(createNew: true));
            DrawWorkloadFooterButton(
                new Rect(actions.xMin + (actionWidth + 4f), actions.yMin, actionWidth, actions.height),
                "Rename",
                () => BeginWorkloadFooterEditor(createNew: false),
                currentResult.Succeeded);
            DrawWorkloadFooterButton(
                new Rect(actions.xMin + (actionWidth + 4f) * 2f, actions.yMin, actionWidth, actions.height),
                "Delete",
                () => DeleteWorkloadInline(currentId),
                currentResult.Succeeded);
            DrawWorkloadFooterButton(
                new Rect(actions.xMin + (actionWidth + 4f) * 3f, actions.yMin, actionWidth, actions.height),
                "Close",
                CloseWorkloadFooterPopover);
        }

        private static void DrawWorkloadFooterEditor(Rect panel)
        {
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 5f, panel.width - 16f, 20f),
                _workloadFooterEditSaveAs
                    ? "Save workload as"
                    : _workloadFooterEditCreatesNew
                        ? "New workload"
                        : "Rename workload");
            _workloadFooterEditFieldRect = new Rect(
                panel.xMin + 8f,
                panel.yMin + 30f,
                panel.width - 16f,
                26f);
            GUI.SetNextControlName("BWT.WorkloadFooterEditor");
            _workloadFooterEditBuffer = Widgets.TextField(
                _workloadFooterEditFieldRect,
                _workloadFooterEditBuffer,
                64);

            float buttonWidth = (panel.width - 20f) * 0.5f;
            _workloadFooterEditConfirmRect = new Rect(
                panel.xMin + 8f,
                panel.yMax - 30f,
                buttonWidth,
                24f);
            _workloadFooterEditCancelRect = new Rect(
                _workloadFooterEditConfirmRect.xMax + 4f,
                _workloadFooterEditConfirmRect.yMin,
                buttonWidth,
                24f);
            DrawWorkloadFooterButton(
                _workloadFooterEditConfirmRect,
                _workloadFooterEditSaveAs ? "Save As" : "Save",
                CommitWorkloadFooterEditor);
            DrawWorkloadFooterButton(
                _workloadFooterEditCancelRect,
                "Cancel",
                CloseWorkloadFooterPopover);

            Event evt = Event.current;
            if (evt != null &&
                evt.type == EventType.KeyDown &&
                GUI.GetNameOfFocusedControl() == "BWT.WorkloadFooterEditor")
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    CommitWorkloadFooterEditor();
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    CloseWorkloadFooterPopover();
                    evt.Use();
                }
            }
        }

        private static void DrawWorkloadFooterApplyConfirmation(Rect panel)
        {
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 5f, panel.width - 16f, 20f),
                "Apply legacy workload?");
            GUI.color = new Color(0.78f, 0.86f, 0.87f, 0.95f);
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 28f, panel.width - 16f, 32f),
                "Applying this will reset the current work tab priority configuration. Continue?");
            GUI.color = Color.white;
            Widgets.CheckboxLabeled(
                new Rect(panel.xMin + 8f, panel.yMin + 62f, panel.width - 16f, 20f),
                "BWT_DoNotShowAgain".Translate(),
                ref _workloadFooterConfirmDoNotAskAgain);

            float buttonWidth = (panel.width - 20f) * 0.5f;
            _workloadFooterConfirmApplyRect = new Rect(
                panel.xMin + 8f,
                panel.yMax - 30f,
                buttonWidth,
                24f);
            _workloadFooterConfirmCancelRect = new Rect(
                _workloadFooterConfirmApplyRect.xMax + 4f,
                _workloadFooterConfirmApplyRect.yMin,
                buttonWidth,
                24f);
            DrawWorkloadFooterButton(
                _workloadFooterConfirmApplyRect,
                "Apply",
                ConfirmLegacyWorkloadApply);
            DrawWorkloadFooterButton(
                _workloadFooterConfirmCancelRect,
                "Cancel",
                CloseWorkloadFooterPopover);
        }

        private static void DrawWorkloadFooterButton(
            Rect rect,
            string label,
            Action action,
            bool enabled = true)
        {
            bool clicked = Widgets.ButtonText(rect, label, active: enabled);
            if (clicked && enabled)
            {
                action?.Invoke();
            }
        }

        private static void SelectWorkloadInline(string stableId)
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview != null && WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern)
            {
                CloseWorkloadFooterPopover();
                QueuePreviewLifecycleAction(
                    preview,
                    () => preview.SelectWorkload(stableId),
                    notifyPawnTables: true);
                return;
            }

            bool selected = WorkloadGateway.SelectWorkload(stableId).Succeeded;
            if (!selected)
            {
                ReportWorkloadFailure(null);
                return;
            }

            CloseWorkloadFooterPopover();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void BeginWorkloadFooterEditor(bool createNew)
        {
            WorkloadOperationResult<WorkloadDescriptor> current = WorkloadGateway.GetCurrent();
            if (!createNew && !current.Succeeded)
            {
                return;
            }

            WorkloadSurfaceCoordinator.RegisterFooterCloser(CloseWorkloadFooterPopover);
            if (!WorkloadSurfaceCoordinator.TryOpenFooter())
            {
                return;
            }
            _workloadFooterPopover = WorkloadFooterPopoverKind.Editor;
            _workloadFooterEditCreatesNew = createNew;
            _workloadFooterEditSaveAs = false;
            _workloadFooterEditStableId = createNew ? string.Empty : current.Value.StableId;
            if (createNew)
            {
                IReadOnlyList<WorkloadDescriptor> workloads = WorkloadGateway.SavedWorkloads();
                int number = 1;
                string candidate;
                do
                {
                    candidate = "Workload " + number++;
                }
                while (ContainsWorkloadLabel(workloads, candidate));

                _workloadFooterEditBuffer = candidate;
            }
            else
            {
                _workloadFooterEditBuffer = current.Value.Label ?? string.Empty;
            }
        }

        private static bool BeginWorkloadPreviewSaveAsEditor()
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview == null || !preview.IsActive || !preview.CanForkPreview)
            {
                return false;
            }

            _workloadFooterPopover = WorkloadFooterPopoverKind.Editor;
            _workloadFooterEditCreatesNew = false;
            _workloadFooterEditSaveAs = true;
            _workloadFooterEditStableId = preview.SourceStableId;
            _workloadFooterEditBuffer = preview.SourceLabel + " copy";
            _workloadFooterScroll = Vector2.zero;
            return true;
        }

        private static void CommitWorkloadFooterEditor()
        {
            string label = (_workloadFooterEditBuffer ?? string.Empty).Trim();
            if (label.Length == 0)
            {
                ReportWorkloadFailure("A workload name is required.");
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (_workloadFooterEditSaveAs)
            {
                if (preview == null || !preview.IsActive)
                {
                    ReportWorkloadFailure("The workload preview is no longer active.");
                    return;
                }

                CloseWorkloadFooterPopover();
                QueuePreviewLifecycleAction(
                    preview,
                    () => preview.ForkPreview(label),
                    notifyPawnTables: true);
                return;
            }

            if (preview != null && WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern)
            {
                bool createNew = _workloadFooterEditCreatesNew;
                string stableId = _workloadFooterEditStableId;
                CloseWorkloadFooterPopover();
                if (createNew)
                {
                    QueuePreviewLifecycleAction(
                        preview,
                        () =>
                        {
                            WorkloadDescriptor unusedDescriptor;
                            return preview.CreateWorkload(label, out unusedDescriptor);
                        },
                        notifyPawnTables: true);
                }
                else
                {
                    QueuePreviewLifecycleAction(
                        preview,
                        () => preview.RenameWorkload(stableId, label),
                        notifyPawnTables: true);
                }

                return;
            }

            bool succeeded;
            WorkloadDescriptor descriptor;
            if (_workloadFooterEditCreatesNew)
            {
                succeeded = TryCreateWorkloadThroughGateway(label, out descriptor);
            }
            else
            {
                descriptor = null;
                succeeded = WorkloadGateway.RenameWorkload(
                    _workloadFooterEditStableId,
                    label).Succeeded;
            }

            if (!succeeded || (_workloadFooterEditCreatesNew && descriptor == null))
            {
                ReportWorkloadFailure(null);
                return;
            }

            CloseWorkloadFooterPopover();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void DeleteWorkloadInline(string stableId)
        {
            if (stableId.NullOrEmpty())
            {
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview != null && WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern)
            {
                CloseWorkloadFooterPopover();
                QueuePreviewLifecycleAction(
                    preview,
                    () => preview.DeleteWorkload(stableId),
                    notifyPawnTables: true);
                return;
            }

            bool deleted = WorkloadGateway.DeleteWorkload(stableId).Succeeded;
            if (!deleted)
            {
                ReportWorkloadFailure(null);
                return;
            }

            CloseWorkloadFooterPopover();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static bool TryCreateWorkloadThroughGateway(
            string label,
            out WorkloadDescriptor descriptor)
        {
            WorkloadOperationResult<WorkloadDescriptor> result = WorkloadGateway.CreateWorkload(label);
            descriptor = result.Value;
            if (!result.Succeeded)
            {
                ReportWorkloadFailure(result.Message);
            }

            return result.Succeeded;
        }

        private static bool ContainsWorkloadLabel(
            IReadOnlyList<WorkloadDescriptor> workloads,
            string label)
        {
            if (workloads == null)
            {
                return false;
            }

            for (int i = 0; i < workloads.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(workloads[i]?.Label, label))
                {
                    return true;
                }
            }

            return false;
        }

        private static void BeginLegacyWorkloadApply()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings?.warnOnApplyWorkload == true)
            {
                WorkloadSurfaceCoordinator.RegisterFooterCloser(CloseWorkloadFooterPopover);
                if (!WorkloadSurfaceCoordinator.TryOpenFooter())
                {
                    return;
                }
                _workloadFooterPopover = WorkloadFooterPopoverKind.ApplyConfirmation;
                _workloadFooterConfirmDoNotAskAgain = false;
                return;
            }

            ApplyLegacyWorkloadInline();
        }

        private static void ConfirmLegacyWorkloadApply()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (_workloadFooterConfirmDoNotAskAgain && settings != null)
            {
                settings.warnOnApplyWorkload = false;
                settings.Write();
            }

            CloseWorkloadFooterPopover();
            ApplyLegacyWorkloadInline();
        }

        private static void ApplyLegacyWorkloadInline()
        {
            WorkloadOperationResult result = WorkloadGateway.ApplyCurrentWorkload();
            if (!result.Succeeded)
            {
                ReportWorkloadFailure(result.Message);
                return;
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void ReportWorkloadFailure(string message)
        {
            if (!message.AnyNonWhitespace())
            {
                message = "The workload operation could not be completed.";
            }

            Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }
    }
}
