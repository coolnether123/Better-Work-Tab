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
        private const float FeedbackButtonSize = 24f;
        private const float FluffyTopButtonSize = 30f;
        private const float FluffyTopButtonGap = 4f;

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
            public Rect Feedback;
            public bool HasRuleset;
            public bool HasWorkload;

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
                return HasWorkload && (WorkloadMain.Contains(position) || WorkloadMenu.Contains(position));
            }
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

            // The row sits on the settings icon's baseline rather than working
            // out its own distance from the bottom edge. Two independent
            // calculations of "just above the bottom" drift the moment either
            // one's padding changes; deriving from the icon means the row cannot
            // end up on a different line from it.
            float y = gearRect.yMax - SelectorHeight;
            float xRight = gearRect.x - InterControlGap;

            var settings = BetterWorkTabMod.Settings;
            if (settings?.enableAutoAssignFeature ?? true)
            {
                float width = BWTBottomBarSelector.MeasureWidth(RuleBuilderGateway.CurrentRulesetLabel());
                rects.RulesetMenu = new Rect(xRight - SelectorMenuWidth, y, SelectorMenuWidth, SelectorHeight);
                rects.RulesetMain = new Rect(rects.RulesetMenu.x - width, y, width, SelectorHeight);
                rects.HasRuleset = true;
                xRight = rects.RulesetMain.x - GroupGap;
            }

            if (settings?.enableWorkloads ?? true)
            {
                if (Current.Game?.GetComponent<GameComponent_BWTWorldSettings>() != null)
                {
                    float width = BWTBottomBarSelector.MeasureWidth(WorkloadLabel());
                    rects.WorkloadMenu = new Rect(xRight - SelectorMenuWidth, y, SelectorMenuWidth, SelectorHeight);
                    rects.WorkloadMain = new Rect(rects.WorkloadMenu.x - width, y, width, SelectorHeight);
                    rects.HasWorkload = true;
                    xRight = rects.WorkloadMain.x - GroupGap;
                }
            }

            // The beta feedback button anchors to the left end of the row rather
            // than to the settings icon. Sitting between the icon and the pickers
            // made it read as another settings affordance; out here it reads as
            // its own thing, and it keeps its place when a picker is switched off.
            rects.Feedback = new Rect(
                xRight - FeedbackButtonSize,
                y + ((SelectorHeight - FeedbackButtonSize) * 0.5f),
                FeedbackButtonSize,
                FeedbackButtonSize);
            rects.LeftEdge = rects.Feedback.x;
            return rects;
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
                ToggleManualPriorities(!Find.PlaySettings.useWorkPriorities);
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
            bool prioritiesEnabled = Find.PlaySettings.useWorkPriorities;
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

            if (rects.HasWorkload)
            {
                DrawWorkloadGroup(rects);
            }

            DrawWorkloadFooterPopover(inRect, rects);
            BWTBetaFeedbackButton.Draw(rects.Feedback);
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

            // A modern preview owns the single Work-tab editing surface. If
            // an old footer repaint or stale static state survives a layout
            // transition, clear it before routing input so hidden footer
            // rectangles cannot steal events from the preview rail.
            if (WorkloadPreviewController.Current?.IsActive == true)
            {
                if (_workloadFooterPopover != WorkloadFooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                return false;
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

            if (editingKeyboard ||
                _workloadFooterPopoverRect.Contains(evt.mousePosition) ||
                rects.ContainsWorkload(evt.mousePosition))
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
                return preview.SourceLabel + " (preview)";
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

            if (DrawWorkloadMainControl(
                    rects.WorkloadMain,
                    name,
                    hasWorkload,
                    hasWorkload
                        ? (legacyMode
                            ? "BWT_BottomBar_WorkloadTooltip".Translate(name)
                            : "Open a non-destructive Workload 2.0 preview for " + name + ".")
                        : "BWT_BottomBar_WorkloadTooltipEmpty".Translate()))
            {
                if (hasWorkload && legacyMode)
                {
                    BeginLegacyWorkloadApply();
                }
                else if (hasWorkload && !legacyMode)
                {
                    if (preview == null || !preview.BeginCurrentPreview())
                    {
                        string message = preview?.LastMessage;
                        if (message.AnyNonWhitespace())
                        {
                            Messages.Message(message, MessageTypeDefOf.RejectInput, false);
                        }
                    }
                }
                else
                {
                    OpenWorkloadFooterPicker();
                }
            }

            if (DrawWorkloadMenuControl(
                    rects.WorkloadMenu,
                    "BWT_BottomBar_WorkloadMenuTooltip".Translate()))
            {
                OpenWorkloadFooterPicker();
            }
        }

        private static bool DrawWorkloadMainControl(
            Rect rect,
            string value,
            bool hasValue,
            string tooltip)
        {
            return BWTBottomBarSelector.DrawMain(
                rect,
                BWTBottomBarIcons.Workload,
                value,
                hasValue,
                tooltip);
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

        private static void ToggleManualPriorities(bool enabled)
        {
            if (Find.PlaySettings.useWorkPriorities == enabled)
            {
                return;
            }

            WorkPrioritySystem.SetManualPriorities(enabled);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
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

            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    "Specific-job expansion is unavailable while this workload preview owns an unprojected ordering dimension.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
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
            _workloadFooterScroll = Vector2.zero;
        }

        private static void CloseWorkloadFooterPopover()
        {
            _workloadFooterPopover = WorkloadFooterPopoverKind.None;
            _workloadFooterEditBuffer = string.Empty;
            _workloadFooterEditStableId = string.Empty;
            _workloadFooterEditCreatesNew = false;
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
            if (WorkloadPreviewController.Current?.IsActive == true)
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
                _workloadFooterEditCreatesNew ? "New workload" : "Rename workload");
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
                "Save",
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
            bool selected = preview != null
                ? preview.SelectWorkload(stableId)
                : WorkloadGateway.SelectWorkload(stableId).Succeeded;
            if (!selected)
            {
                ReportWorkloadFailure(preview?.LastMessage);
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

        private static void CommitWorkloadFooterEditor()
        {
            string label = (_workloadFooterEditBuffer ?? string.Empty).Trim();
            if (label.Length == 0)
            {
                ReportWorkloadFailure("A workload name is required.");
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            bool succeeded;
            WorkloadDescriptor descriptor;
            if (_workloadFooterEditCreatesNew)
            {
                succeeded = preview != null
                    ? preview.CreateWorkload(label, out descriptor)
                    : TryCreateWorkloadThroughGateway(label, out descriptor);
            }
            else
            {
                descriptor = null;
                succeeded = preview != null
                    ? preview.RenameWorkload(_workloadFooterEditStableId, label)
                    : WorkloadGateway.RenameWorkload(_workloadFooterEditStableId, label).Succeeded;
            }

            if (!succeeded || (_workloadFooterEditCreatesNew && descriptor == null))
            {
                ReportWorkloadFailure(preview?.LastMessage);
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
            bool deleted = preview != null
                ? preview.DeleteWorkload(stableId)
                : WorkloadGateway.DeleteWorkload(stableId).Succeeded;
            if (!deleted)
            {
                ReportWorkloadFailure(preview?.LastMessage);
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
