using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.Workloads
{
    internal sealed class WorkloadFooterFeature : IHeaderFooterFeature
    {
        internal static readonly WorkloadFooterFeature Instance =
            new WorkloadFooterFeature();

        private const float CompactPreviewActionGap = 3f;
        private const float CompactPreviewCancelWidth = 52f;
        private const float CompactPreviewApplyWidth = 48f;
        private const float FooterPanelGap = 5f;
        private const float FooterPanelWidth = 330f;

        private enum FooterPopoverKind
        {
            None,
            Editor,
            ApplyConfirmation
        }

        private FooterPopoverKind _workloadFooterPopover;
        private string _workloadFooterEditBuffer = string.Empty;
        private string _workloadFooterEditStableId = string.Empty;
        private bool _workloadFooterEditCreatesNew;
        private bool _workloadFooterEditSaveAs;
        private bool _workloadFooterConfirmDoNotAskAgain;
        private Rect _workloadFooterPopoverRect;
        private Rect _workloadFooterEditFieldRect;
        private Rect _workloadFooterEditConfirmRect;
        private Rect _workloadFooterEditCancelRect;
        private Rect _workloadFooterConfirmApplyRect;
        private Rect _workloadFooterConfirmCancelRect;
        private PreviewAction? _pressedPreviewAction;
        private FooterPopoverAction? _pressedFooterPopoverAction;
        private FooterControl? _pressedFooterControl;

        private enum PreviewAction
        {
            SaveAs,
            Update,
            Cancel,
            Apply
        }

        private enum FooterPopoverAction
        {
            Save,
            Cancel,
            Apply
        }

        private enum FooterControl
        {
            Main,
            Menu
        }

        public bool IsPopoverOpen =>
            _workloadFooterPopover != FooterPopoverKind.None;

        public bool TryLayout(
            HeaderFooterLayoutContext context,
            ref HeaderButtons.BottomButtonRects rects)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            bool workloadsEnabled = settings?.enableWorkloads ?? true;
            bool hasWorkloadComponent =
                WorkTabGameRoots.For(Current.Game) != null;
            WorkloadPreviewController preview = workloadsEnabled && hasWorkloadComponent
                ? WorkloadPreviewController.Current
                : null;
            bool previewActive = preview?.IsActive == true;

            if (!workloadsEnabled || !hasWorkloadComponent)
            {
                return false;
            }

            if (previewActive)
            {
                LayoutBoundedPreview(
                    context.LeftEdge,
                    context.RightEdge,
                    context.Y,
                    context.Height,
                    context.AllowRuleset,
                    context.RulesetWidth,
                    preview.HasSemanticDiff,
                    ref rects);
            }
            else
            {
                LayoutNormal(
                    context.LeftEdge,
                    context.RightEdge,
                    context.Y,
                    context.Height,
                    context.AllowRuleset,
                    context.RulesetWidth,
                    true,
                    ref rects);
            }

            return true;
        }
        private void LayoutNormal(
            float leftEdge,
            float rightEdge,
            float y,
            float height,
            bool allowRuleset,
            float rulesetWidth,
            bool allowWorkload,
            ref HeaderButtons.BottomButtonRects rects)
        {
            float available = Mathf.Max(0f, rightEdge - leftEdge);
            if (available <= 0f)
            {
                rects.LeftEdge = leftEdge;
                return;
            }

            float workloadWidth = allowWorkload
                ? HeaderButtons.MeasureSelectorWidth(WorkloadGeometryLabel())
                : 0f;
            bool showOptionalMenu = allowWorkload;
            bool showRuleset = allowRuleset;

            if (!allowWorkload)
            {
                rulesetWidth = Mathf.Min(rulesetWidth, available - HeaderButtons.SelectorMenuWidth);
                showRuleset = allowRuleset && rulesetWidth > 0f;
            }

            // Keep the normal footer on its established right-hand baseline,
            // but let only the workload naming half compact before the optional
            // ruleset group gives way. The shared ruleset selector keeps its
            // normal measured geometry instead of acquiring workload-specific
            // narrow-tab metrics.
            float minimumWorkloadWidth = HeaderButtons.PreferredSelectorMainWidth;
            float minimumBothWidth = minimumWorkloadWidth + HeaderButtons.SelectorMenuWidth +
                HeaderButtons.GroupGap + rulesetWidth + HeaderButtons.SelectorMenuWidth;
            if (allowWorkload && showRuleset && available >= minimumBothWidth)
            {
                float preferred = workloadWidth + HeaderButtons.SelectorMenuWidth +
                    HeaderButtons.GroupGap + rulesetWidth + HeaderButtons.SelectorMenuWidth;
                float deficit = Mathf.Max(0f, preferred - available);
                float workloadReduction = Mathf.Min(
                    deficit,
                    Mathf.Max(0f, workloadWidth - minimumWorkloadWidth));
                workloadWidth -= workloadReduction;
                deficit -= workloadReduction;
                if (deficit > 0f)
                {
                    showRuleset = false;
                }
            }
            else if (allowWorkload && showRuleset)
            {
                showRuleset = false;
            }

            if (showOptionalMenu)
            {
                float minimumWorkloadGroup = minimumWorkloadWidth + HeaderButtons.SelectorMenuWidth;
                if (available < minimumWorkloadGroup)
                {
                    showOptionalMenu = false;
                    workloadWidth = available;
                }
                else if (!showRuleset)
                {
                    workloadWidth = Mathf.Min(
                        workloadWidth,
                        available - HeaderButtons.SelectorMenuWidth);
                }
            }

            float x = rightEdge;
            if (showRuleset)
            {
                rects.RulesetMenu = HeaderButtons.TakeFromRight(
                    ref x,
                    leftEdge,
                    HeaderButtons.SelectorMenuWidth,
                    y,
                    height);
                rects.RulesetMain = HeaderButtons.TakeFromRight(
                    ref x,
                    leftEdge,
                    rulesetWidth,
                    y,
                    height);
                rects.HasRuleset = rects.RulesetMain.width > 0f &&
                    rects.RulesetMenu.width > 0f;
                x -= HeaderButtons.GroupGap;
            }

            if (showOptionalMenu)
            {
                rects.OptionalMenu = HeaderButtons.TakeFromRight(
                    ref x,
                    leftEdge,
                    HeaderButtons.SelectorMenuWidth,
                    y,
                    height);
            }

            rects.OptionalMain = HeaderButtons.TakeFromRight(
                ref x,
                leftEdge,
                workloadWidth,
                y,
                height);
            rects.HasOptional = allowWorkload && rects.OptionalMain.width > 0f;
            rects.HasOptionalMenu = allowWorkload &&
                showOptionalMenu && rects.OptionalMenu.width > 0f;
            rects.CompactOptionalMain = rects.OptionalMain.width <
                HeaderButtons.PreferredSelectorMainWidth - 0.01f;
            rects.LeftEdge = Mathf.Max(leftEdge, x);
        }

        private void LayoutBoundedPreview(
            float leftEdge,
            float rightEdge,
            float y,
            float height,
            bool allowRuleset,
            float rulesetWidth,
            bool includeUpdate,
            ref HeaderButtons.BottomButtonRects rects)
        {
            float available = Mathf.Max(0f, rightEdge - leftEdge);
            if (available <= 0f)
            {
                rects.LeftEdge = leftEdge;
                return;
            }

            float workloadWidth = HeaderButtons.PreferredSelectorMainWidth;
            float cancelWidth = CompactPreviewCancelWidth;
            float applyWidth = CompactPreviewApplyWidth;
            float actionGap = CompactPreviewActionGap;
            float mandatoryWidth = workloadWidth + cancelWidth + applyWidth + (actionGap * 2f);

            bool showOptionalMenu = false;
            bool showRuleset = false;
            bool showUpdate = false;
            bool showSaveAs = false;
            float used = mandatoryWidth;

            // Add optional affordances in reverse removal order. Consequently,
            // narrowing removes Save As, Update, and the ruleset before the three
            // application controls ever surrender their lane.
            if (used + HeaderButtons.SelectorMenuWidth <= available)
            {
                showOptionalMenu = true;
                used += HeaderButtons.SelectorMenuWidth;
            }
            if (allowRuleset &&
                used + HeaderButtons.GroupGap + rulesetWidth + HeaderButtons.SelectorMenuWidth <= available)
            {
                showRuleset = true;
                used += HeaderButtons.GroupGap + rulesetWidth + HeaderButtons.SelectorMenuWidth;
            }
            float compactUpdateWidth = CompactPreviewActionWidth(
                PreviewAction.Update);
            if (includeUpdate && used + actionGap + compactUpdateWidth <= available)
            {
                showUpdate = true;
                used += actionGap + compactUpdateWidth;
            }
            float compactSaveAsWidth = CompactPreviewActionWidth(
                PreviewAction.SaveAs);
            if (used + actionGap + compactSaveAsWidth <= available)
            {
                showSaveAs = true;
                used += actionGap + compactSaveAsWidth;
            }
            if (available < mandatoryWidth)
            {
                showOptionalMenu = false;
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
                    HeaderButtons.MeasureSelectorWidth(WorkloadGeometryLabel()));
                float workloadGrowth = Mathf.Min(spare, desiredWorkloadWidth - workloadWidth);
                workloadWidth += workloadGrowth;
            }

            float x = rightEdge;
            if (showRuleset)
            {
                rects.RulesetMenu = HeaderButtons.TakeFromRight(ref x, leftEdge, HeaderButtons.SelectorMenuWidth, y, height);
                rects.RulesetMain = HeaderButtons.TakeFromRight(ref x, leftEdge, rulesetWidth, y, height);
                rects.HasRuleset =
                    rects.RulesetMain.width > 0f && rects.RulesetMenu.width > 0f;
                x -= HeaderButtons.GroupGap;
            }

            if (showOptionalMenu)
            {
                rects.OptionalMenu = HeaderButtons.TakeFromRight(ref x, leftEdge, HeaderButtons.SelectorMenuWidth, y, height);
            }
            rects.OptionalMain = HeaderButtons.TakeFromRight(ref x, leftEdge, workloadWidth, y, height);
            rects.HasOptional = rects.OptionalMain.width > 0f;
            rects.HasOptionalMenu = showOptionalMenu && rects.OptionalMenu.width > 0f;
            rects.CompactOptionalMain = workloadWidth < HeaderButtons.PreferredSelectorMainWidth;

            x -= actionGap;
            rects.OptionalApply = HeaderButtons.TakeFromRight(ref x, leftEdge, applyWidth, y, height);
            x -= actionGap;
            rects.OptionalCancel = HeaderButtons.TakeFromRight(ref x, leftEdge, cancelWidth, y, height);
            if (showUpdate)
            {
                x -= actionGap;
                rects.OptionalUpdate = HeaderButtons.TakeFromRight(ref x, leftEdge, compactUpdateWidth, y, height);
            }
            if (showSaveAs)
            {
                x -= actionGap;
                rects.OptionalSaveAs = HeaderButtons.TakeFromRight(ref x, leftEdge, compactSaveAsWidth, y, height);
            }
            rects.HasOptionalPreview =
                rects.HasOptional &&
                rects.OptionalCancel.width > 0f &&
                rects.OptionalApply.width > 0f;
            rects.HasOptionalSaveAs = showSaveAs && rects.OptionalSaveAs.width > 0f;
            rects.HasOptionalUpdate = showUpdate && rects.OptionalUpdate.width > 0f;
            rects.LeftEdge = Mathf.Max(leftEdge, x);
        }

        private float CompactPreviewActionWidth(
            PreviewAction action)
        {
            switch (action)
            {
                case PreviewAction.SaveAs:
                    return 58f;
                case PreviewAction.Update:
                    return 54f;
                case PreviewAction.Cancel:
                    return CompactPreviewCancelWidth;
                case PreviewAction.Apply:
                    return CompactPreviewApplyWidth;
                default:
                    return CompactPreviewApplyWidth;
            }
        }

        private string WorkloadLabel()
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview?.IsActive == true)
            {
                return preview.SourceLabel;
            }

            string currentLabel = WorkloadGateway.CurrentLabel();
            return currentLabel.AnyNonWhitespace()
                ? currentLabel
                : "BWT_BottomBar_WorkloadEmpty".Translate().ToString();
        }

        private string WorkloadGeometryLabel()
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            return preview?.IsActive == true
                ? preview.SourceLabel
                : WorkloadLabel();
        }

        private void DrawWorkloadGroup(HeaderButtons.BottomButtonRects rects)
        {
            if (WorkTabGameRoots.For(Current.Game) == null)
            {
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            bool hasWorkload = WorkloadGateway.HasCurrentWorkload();
            string name = WorkloadLabel();
            string selectorTooltip = preview?.IsActive == true
                ? preview.ActivePreviewSwitchBlockedMessage
                : hasWorkload
                    ? (WorkloadGateway.CurrentMode == WorkloadBackendMode.Legacy
                        ? "BWT_BottomBar_WorkloadTooltip".Translate(name)
                        : "BWT_Workload_OpenPreviewTooltip".Translate(name))
                    : "BWT_BottomBar_WorkloadTooltipEmpty".Translate();

            DrawOptionalMainControl(
                rects.OptionalMain,
                name,
                hasWorkload,
                selectorTooltip,
                rects.CompactOptionalMain);

            if (rects.HasOptionalMenu)
            {
                DrawOptionalMenuControl(
                    rects.OptionalMenu,
                    preview?.IsActive == true
                        ? preview.ActivePreviewSwitchBlockedMessage
                        : "BWT_BottomBar_WorkloadMenuTooltip".Translate());
            }
        }

        private void DrawPreviewActions(HeaderButtons.BottomButtonRects rects)
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview?.IsActive != true)
            {
                return;
            }

            if (rects.HasOptionalSaveAs)
            {
                DrawWorkloadPreviewButton(
                    rects.OptionalSaveAs,
                    "BWT_Workload_SaveAs".Translate(),
                    preview.CanForkPreview,
                    preview.CommitBlockedMessage.AnyNonWhitespace()
                        ? preview.CommitBlockedMessage
                        : "BWT_Workload_SaveAsTooltip".Translate());
            }
            if (rects.HasOptionalUpdate)
            {
                DrawWorkloadPreviewButton(
                    rects.OptionalUpdate,
                    "BWT_Workload_Save".Translate(),
                    preview.CanUpdatePreview,
                    preview.CommitBlockedMessage.AnyNonWhitespace()
                        ? preview.CommitBlockedMessage
                        : "BWT_Workload_SaveTooltip".Translate());
            }

            DrawWorkloadPreviewButton(
                rects.OptionalCancel,
                PreviewActionLabel(rects.OptionalCancel, "BWT_Workload_Cancel".Translate(), "C"),
                preview.CanCancelPreview,
                preview.IsMultiplayerCommitInFlight
                    ? preview.MultiplayerStatusExplanation
                    : "BWT_Workload_CancelTooltip".Translate());
            DrawWorkloadPreviewButton(
                rects.OptionalApply,
                PreviewActionLabel(rects.OptionalApply, "BWT_Workload_Apply".Translate(), "A"),
                preview.CanApplyPreview,
                preview.CommitBlockedMessage);
        }

        private void DrawWorkloadPreviewButton(
            Rect rect,
            string label,
            bool enabled,
            string tooltip)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            Widgets.ButtonText(rect, label, active: enabled);
            if (tooltip.AnyNonWhitespace())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        private void QueuePreviewLifecycleAction(
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

        private void DrawOptionalMainControl(
            Rect rect,
            string value,
            bool hasValue,
            string tooltip,
            bool compact)
        {
            if (compact)
            {
                Widgets.ButtonText(rect, "W", active: true);
                TooltipHandler.TipRegion(rect, tooltip);
                return;
            }

            BWTBottomBarSelector.DrawMain(
                rect,
                value,
                hasValue,
                tooltip);
        }

        private string PreviewActionLabel(Rect rect, string fullLabel, string compactLabel)
        {
            return rect.width >= 44f ? fullLabel : compactLabel;
        }

        private void DrawOptionalMenuControl(Rect rect, string tooltip)
        {
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.ButtonText(rect, "...");
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            TooltipHandler.TipRegion(rect, tooltip);
        }

       public void Draw(HeaderButtons.BottomButtonRects rects)
        {
            if (rects.HasOptionalPreview)
            {
                DrawPreviewActions(rects);
            }

            if (rects.HasOptional)
            {
                DrawWorkloadGroup(rects);
            }

            if (_workloadFooterPopover == FooterPopoverKind.None)
            {
                WorkloadPreviewController.Current?.UpdateFooterInspectionHover(
                    rects.HasOptionalSaveAs ? rects.OptionalSaveAs : Rect.zero,
                    rects.HasOptionalUpdate ? rects.OptionalUpdate : Rect.zero,
                    rects.HasOptionalPreview ? rects.OptionalApply : Rect.zero);
            }
            else
            {
                WorkloadPreviewController.Current?.UpdateFooterInspectionHover(
                    Rect.zero,
                    Rect.zero);
            }
        }

        public void DrawPopoverOnTop(
            Rect inRect,
            Rect gearRect,
            HeaderButtons.BottomButtonRects rects)
        {
            if (_workloadFooterPopover == FooterPopoverKind.None)
            {
                return;
            }

            DrawWorkloadFooterPopover(inRect, rects);
        }
        public bool TryHandleInput(
            Rect inRect,
            Rect gearRect,
            Event evt,
            HeaderButtons.BottomButtonRects rects)
        {
            if (evt == null)
            {
                return false;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview?.IsActive == true &&
                _workloadFooterPopover != FooterPopoverKind.None &&
                !_workloadFooterEditSaveAs)
            {
                CloseWorkloadFooterPopover();
            }

            bool mouseEvent = evt.type == EventType.MouseDown ||
                              evt.type == EventType.MouseUp ||
                              evt.type == EventType.ScrollWheel;
            bool editingKeyboard = _workloadFooterPopover == FooterPopoverKind.Editor &&
                                   (evt.type == EventType.KeyDown ||
                                    evt.type == EventType.KeyUp ||
                                    evt.type == EventType.ValidateCommand) &&
                                   GUI.GetNameOfFocusedControl() == "BWT.WorkloadFooterEditor";
            if (!mouseEvent && !editingKeyboard)
            {
                return false;
            }

            if (!rects.HasOptional)
            {
                if (_workloadFooterPopover != FooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                return false;
            }

            if (preview?.IsActive == true && evt.button == 0 && !evt.alt)
            {
                bool overPreviewAction = TryResolvePreviewAction(
                    rects,
                    evt.mousePosition,
                    out PreviewAction hoveredAction);
                if (evt.type == EventType.MouseDown && overPreviewAction)
                {
                    _pressedPreviewAction = hoveredAction;
                    evt.Use();
                    return true;
                }

                if (evt.type == EventType.MouseUp &&
                    (overPreviewAction || _pressedPreviewAction.HasValue))
                {
                    PreviewAction? pressedAction = _pressedPreviewAction;
                    _pressedPreviewAction = null;
                    if (overPreviewAction && pressedAction == hoveredAction)
                    {
                        if (hoveredAction == PreviewAction.SaveAs &&
                            preview.CanForkPreview)
                        {
                            QueuePreviewLifecycleAction(
                                preview,
                                BeginWorkloadPreviewSaveAsEditor,
                                notifyPawnTables: false);
                        }
                        else if (hoveredAction == PreviewAction.Update &&
                                 preview.CanUpdatePreview)
                        {
                            QueuePreviewLifecycleAction(
                                preview,
                                () => preview.UpdatePreview(),
                                notifyPawnTables: true);
                        }
                        else if (hoveredAction == PreviewAction.Cancel &&
                                 preview.CanCancelPreview)
                        {
                            QueuePreviewLifecycleAction(
                                preview,
                                () => preview.CancelPreview(),
                                notifyPawnTables: false);
                        }
                        else if (hoveredAction == PreviewAction.Apply &&
                                 preview.CanApplyPreview)
                        {
                            QueuePreviewLifecycleAction(
                                preview,
                                () => preview.ApplyPreview(),
                                notifyPawnTables: true);
                        }
                    }

                    // The footer owns both halves of the click. Do not make a later
                    // grid or tutorial pass preserve this event for a drawing callback.
                    evt.Use();
                    return true;
                }
            }

            if (_workloadFooterPopover != FooterPopoverKind.None &&
                evt.button == 0 &&
                !evt.alt)
            {
                bool overPopoverAction = TryResolveFooterPopoverAction(
                    evt.mousePosition,
                    out FooterPopoverAction hoveredPopoverAction);
                if (evt.type == EventType.MouseDown && overPopoverAction)
                {
                    _pressedFooterPopoverAction = hoveredPopoverAction;
                    evt.Use();
                    return true;
                }

                if (evt.type == EventType.MouseUp &&
                    (overPopoverAction || _pressedFooterPopoverAction.HasValue))
                {
                    FooterPopoverAction? pressedAction =
                        _pressedFooterPopoverAction;
                    _pressedFooterPopoverAction = null;
                    if (overPopoverAction && pressedAction == hoveredPopoverAction)
                    {
                        if (hoveredPopoverAction == FooterPopoverAction.Save)
                        {
                            CommitWorkloadFooterEditor();
                        }
                        else if (hoveredPopoverAction == FooterPopoverAction.Apply)
                        {
                            ConfirmLegacyOptionalApply();
                        }
                        else
                        {
                            CloseWorkloadFooterPopover();
                        }
                    }

                    evt.Use();
                    return true;
                }
            }

            if (evt.button == 0 && !evt.alt)
            {
                bool overFooterControl = TryResolveFooterControl(
                    rects,
                    evt.mousePosition,
                    out FooterControl hoveredFooterControl);
                if (evt.type == EventType.MouseDown && overFooterControl)
                {
                    _pressedFooterControl = hoveredFooterControl;
                    evt.Use();
                    return true;
                }

                if (evt.type == EventType.MouseUp &&
                    (overFooterControl || _pressedFooterControl.HasValue))
                {
                    FooterControl? pressedControl = _pressedFooterControl;
                    _pressedFooterControl = null;
                    if (overFooterControl && pressedControl == hoveredFooterControl)
                    {
                        ExecuteFooterControl(hoveredFooterControl, preview);
                    }

                    evt.Use();
                    return true;
                }
            }

            // Contextual settings gets first refusal for Alt-clicks. If its
            // binding is unavailable, still reserve the visible footer hit
            // region so the gesture cannot fall through to a normal action.
            if (evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                evt.alt &&
                rects.ContainsOptionalFooter(evt.mousePosition))
            {
                evt.Use();
                return true;
            }

            if (evt.type == EventType.ScrollWheel &&
                preview?.IsActive == true &&
                rects.ContainsOptionalFooter(evt.mousePosition))
            {
                // The window-level viewport owner handles wheel input over the
                // footer. Do not consume it here or let the grid see it as a
                // priority gesture.
                return false;
            }

            if (editingKeyboard ||
                _workloadFooterPopoverRect.Contains(evt.mousePosition) ||
                rects.ContainsOptionalFooter(evt.mousePosition))
            {
                // Routing stops the interaction pass, but the grid is still rendered
                // later in this IMGUI event. Consume reserved footer input so native
                // cell drawing cannot interpret a wheel or secondary click underneath.
                evt.Use();
                return true;
            }

            if (_workloadFooterPopover != FooterPopoverKind.None)
            {
                CloseWorkloadFooterPopover();
            }

            return false;
        }

        public void Reset()
        {
            CloseWorkloadFooterPopover();
            _pressedPreviewAction = null;
            _pressedFooterPopoverAction = null;
            _pressedFooterControl = null;
            WorkloadSurfaceCoordinator.Reset();
        }

        private void OpenWorkloadFooterPicker()
        {
            if (_workloadFooterPopover != FooterPopoverKind.None)
            {
                CloseWorkloadFooterPopover();
            }

            if (!WorkloadSurfaceCoordinator.TryOpenFooter())
            {
                return;
            }

            try
            {
                Find.WindowStack.Add(new FloatMenu(BuildWorkloadPickerOptions()));
            }
            finally
            {
                // FloatMenu owns its own dismissal and input capture. Do not
                // leave the footer surface registered after handing control to
                // the normal RimWorld menu stack.
                WorkloadSurfaceCoordinator.NotifyFooterClosed();
            }
        }

        private bool TryResolvePreviewAction(
            HeaderButtons.BottomButtonRects rects,
            Vector2 position,
            out PreviewAction action)
        {
            if (rects.HasOptionalSaveAs && rects.OptionalSaveAs.Contains(position))
            {
                action = PreviewAction.SaveAs;
                return true;
            }

            if (rects.HasOptionalUpdate && rects.OptionalUpdate.Contains(position))
            {
                action = PreviewAction.Update;
                return true;
            }

            if (rects.HasOptionalPreview && rects.OptionalCancel.Contains(position))
            {
                action = PreviewAction.Cancel;
                return true;
            }

            if (rects.HasOptionalPreview && rects.OptionalApply.Contains(position))
            {
                action = PreviewAction.Apply;
                return true;
            }

            action = default(PreviewAction);
            return false;
        }

        private bool TryResolveFooterPopoverAction(
            Vector2 position,
            out FooterPopoverAction action)
        {
            if (_workloadFooterPopover == FooterPopoverKind.Editor &&
                _workloadFooterEditConfirmRect.width > 0f &&
                _workloadFooterEditConfirmRect.Contains(position))
            {
                action = FooterPopoverAction.Save;
                return true;
            }

            if (_workloadFooterPopover == FooterPopoverKind.ApplyConfirmation &&
                _workloadFooterConfirmApplyRect.width > 0f &&
                _workloadFooterConfirmApplyRect.Contains(position))
            {
                action = FooterPopoverAction.Apply;
                return true;
            }

            Rect cancelRect = _workloadFooterPopover == FooterPopoverKind.Editor
                ? _workloadFooterEditCancelRect
                : _workloadFooterConfirmCancelRect;
            if (cancelRect.width > 0f && cancelRect.Contains(position))
            {
                action = FooterPopoverAction.Cancel;
                return true;
            }

            action = default(FooterPopoverAction);
            return false;
        }

        private bool TryResolveFooterControl(
            HeaderButtons.BottomButtonRects rects,
            Vector2 position,
            out FooterControl control)
        {
            if (rects.HasOptional && rects.OptionalMain.Contains(position))
            {
                control = FooterControl.Main;
                return true;
            }

            if (rects.HasOptionalMenu && rects.OptionalMenu.Contains(position))
            {
                control = FooterControl.Menu;
                return true;
            }

            control = default(FooterControl);
            return false;
        }

        private void ExecuteFooterControl(
            FooterControl control,
            WorkloadPreviewController preview)
        {
            if (preview?.IsActive == true)
            {
                ReportWorkloadFailure(preview.ActivePreviewSwitchBlockedMessage);
                return;
            }

            if (control == FooterControl.Menu)
            {
                OpenWorkloadFooterPicker();
                return;
            }

            bool hasWorkload = WorkloadGateway.HasCurrentWorkload();
            if (!hasWorkload)
            {
                BeginWorkloadFooterEditor(createNew: true);
                return;
            }

            if (WorkloadGateway.CurrentMode == WorkloadBackendMode.Legacy)
            {
                BeginLegacyOptionalApply();
                return;
            }

            if (preview == null)
            {
                Messages.Message(
                    "BWT_Workload_PreviewUnavailable".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            QueuePreviewLifecycleAction(
                preview,
                () => preview.BeginCurrentPreview(),
                notifyPawnTables: false);
        }

        private List<FloatMenuOption> BuildWorkloadPickerOptions()
        {
            var options = new List<FloatMenuOption>();
            IReadOnlyList<WorkloadDescriptor> workloads = WorkloadGateway.SavedWorkloads();
            WorkloadOperationResult<WorkloadDescriptor> currentResult = WorkloadGateway.GetCurrent();
            string currentId = currentResult.Succeeded
                ? currentResult.Value?.StableId
                : string.Empty;

            if (workloads.Count == 0)
            {
                options.Add(new FloatMenuOption("BWT_Workload_NoSaved".Translate(), null));
            }
            else
            {
                for (int i = 0; i < workloads.Count; i++)
                {
                    WorkloadDescriptor workload = workloads[i];
                    if (workload == null || workload.StableId.NullOrEmpty())
                    {
                        continue;
                    }

                    string stableId = workload.StableId;
                    string label = workload.Label ?? string.Empty;
                    bool selected = StringComparer.Ordinal.Equals(currentId, stableId);
                    options.Add(new FloatMenuOption(
                        selected ? "[x] " + label : label,
                        () => SelectWorkloadInline(stableId)));
                }
            }

            options.Add(new FloatMenuOption(
                "BWT_Workload_New".Translate(),
                () => BeginWorkloadFooterEditor(createNew: true)));
            if (!currentId.NullOrEmpty())
            {
                string stableId = currentId;
                options.Add(new FloatMenuOption(
                    "BWT_Workload_Actions".Translate(),
                    () => OpenWorkloadManagementMenu(stableId)));
            }

            return options;
        }

        private void OpenWorkloadManagementMenu(string stableId)
        {
            if (stableId.NullOrEmpty() || !WorkloadSurfaceCoordinator.TryOpenFooter())
            {
                return;
            }

            try
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(
                        "BWT_Workload_Rename".Translate(),
                        () => BeginWorkloadFooterEditor(
                            createNew: false,
                            stableId: stableId)),
                    new FloatMenuOption(
                        "BWT_Workload_Delete".Translate(),
                        () => DeleteWorkloadInline(stableId))
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
            finally
            {
                WorkloadSurfaceCoordinator.NotifyFooterClosed();
            }
        }

        private void CloseWorkloadFooterPopover()
        {
            if (GUI.GetNameOfFocusedControl() == "BWT.WorkloadFooterEditor")
            {
                GUI.FocusControl(null);
            }

            _pressedFooterPopoverAction = null;
            _workloadFooterPopover = FooterPopoverKind.None;
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

        private void DrawWorkloadFooterPopover(Rect inRect, HeaderButtons.BottomButtonRects rects)
        {
            if (WorkloadPreviewController.Current?.IsActive == true &&
                !_workloadFooterEditSaveAs)
            {
                if (_workloadFooterPopover != FooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                _workloadFooterPopoverRect = Rect.zero;
                return;
            }

            if (_workloadFooterPopover == FooterPopoverKind.None || !rects.HasOptional)
            {
                if (!rects.HasOptional && _workloadFooterPopover != FooterPopoverKind.None)
                {
                    CloseWorkloadFooterPopover();
                }

                _workloadFooterPopoverRect = Rect.zero;
                return;
            }

            float desiredHeight = _workloadFooterPopover == FooterPopoverKind.Editor
                ? 112f
                : 136f;
            float width = Mathf.Min(
                FooterPanelWidth,
                Mathf.Max(1f, inRect.width - 8f));
            float height = Mathf.Min(
                desiredHeight,
                Mathf.Max(84f, inRect.height - 8f));
            Rect anchor = rects.OptionalMain;
            float x = Mathf.Clamp(
                anchor.xMax - width,
                inRect.xMin + 4f,
                Mathf.Max(inRect.xMin + 4f, inRect.xMax - width - 4f));
            float y = anchor.yMin - height - FooterPanelGap;
            if (y < inRect.yMin + 4f)
            {
                y = anchor.yMax + FooterPanelGap;
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
                    case FooterPopoverKind.Editor:
                        DrawWorkloadFooterEditor(panel);
                        break;
                    case FooterPopoverKind.ApplyConfirmation:
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

        private void DrawWorkloadFooterEditor(Rect panel)
        {
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 5f, panel.width - 16f, 20f),
                _workloadFooterEditSaveAs
                    ? "BWT_Workload_SaveAsTitle".Translate().ToString()
                    : _workloadFooterEditCreatesNew
                        ? "BWT_Workload_NewTitle".Translate().ToString()
                        : "BWT_Workload_RenameTitle".Translate().ToString());
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
                _workloadFooterEditSaveAs
                    ? "BWT_Workload_SaveAs".Translate().ToString()
                    : "BWT_Workload_Save".Translate().ToString());
            DrawWorkloadFooterButton(
                _workloadFooterEditCancelRect,
                "BWT_Workload_Cancel".Translate());

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

        private void DrawWorkloadFooterApplyConfirmation(Rect panel)
        {
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 5f, panel.width - 16f, 20f),
                "BWT_Workload_ApplyLegacyTitle".Translate());
            GUI.color = new Color(0.78f, 0.86f, 0.87f, 0.95f);
            Widgets.Label(
                new Rect(panel.xMin + 8f, panel.yMin + 28f, panel.width - 16f, 32f),
                "BWT_Workload_ApplyLegacyBody".Translate());
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
                "BWT_Workload_Apply".Translate());
            DrawWorkloadFooterButton(
                _workloadFooterConfirmCancelRect,
                "BWT_Workload_Cancel".Translate());
        }

        private void DrawWorkloadFooterButton(
            Rect rect,
            string label,
            bool enabled = true)
        {
            Widgets.ButtonText(rect, label, active: enabled);
        }

        private void SelectWorkloadInline(string stableId)
        {
            bool selected = WorkloadGateway.SelectWorkload(stableId).Succeeded;
            if (!selected)
            {
                ReportWorkloadFailure(null);
                return;
            }

            CloseWorkloadFooterPopover();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private void BeginWorkloadFooterEditor(bool createNew, string stableId = null)
        {
            WorkloadOperationResult<WorkloadDescriptor> current = WorkloadGateway.GetCurrent();
            if (!createNew &&
                (!current.Succeeded ||
                 current.Value == null ||
                 stableId.NullOrEmpty() ||
                 !StringComparer.Ordinal.Equals(current.Value.StableId, stableId)))
            {
                return;
            }

            WorkloadSurfaceCoordinator.RegisterFooterCloser(CloseWorkloadFooterPopover);
            if (!WorkloadSurfaceCoordinator.TryOpenFooter())
            {
                return;
            }
            _workloadFooterPopover = FooterPopoverKind.Editor;
            _workloadFooterEditCreatesNew = createNew;
            _workloadFooterEditSaveAs = false;
            _workloadFooterEditStableId = createNew ? string.Empty : stableId;
            if (createNew)
            {
                IReadOnlyList<WorkloadDescriptor> workloads = WorkloadGateway.SavedWorkloads();
                int number = 1;
                string candidate;
                do
                {
                    candidate = "BWT_Workload_DefaultName".Translate(number++);
                }
                while (ContainsOptionalLabel(workloads, candidate));

                _workloadFooterEditBuffer = candidate;
            }
            else
            {
                _workloadFooterEditBuffer = current.Value.Label ?? string.Empty;
            }
        }

        private bool BeginWorkloadPreviewSaveAsEditor()
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview == null || !preview.IsActive || !preview.CanForkPreview)
            {
                return false;
            }

            _workloadFooterPopover = FooterPopoverKind.Editor;
            _workloadFooterEditCreatesNew = false;
            _workloadFooterEditSaveAs = true;
            _workloadFooterEditStableId = preview.SourceStableId;
            _workloadFooterEditBuffer = "BWT_Workload_CopyName".Translate(preview.SourceLabel);
            return true;
        }

        private void CommitWorkloadFooterEditor()
        {
            string label = (_workloadFooterEditBuffer ?? string.Empty).Trim();
            if (label.Length == 0)
            {
                ReportWorkloadFailure("BWT_Workload_NameRequired".Translate());
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (_workloadFooterEditSaveAs)
            {
                if (preview == null || !preview.IsActive)
                {
                    ReportWorkloadFailure("BWT_Workload_PreviewEnded".Translate());
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

        private void DeleteWorkloadInline(string stableId)
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

        private bool TryCreateWorkloadThroughGateway(
            string label,
            out WorkloadDescriptor descriptor)
        {
            WorkloadOperationResult<WorkloadDescriptor> result = WorkloadGateway.CreateWorkload(label);
            descriptor = result.Value;
            if (!result.Succeeded)
            {
                ReportWorkloadFailure(WorkloadPresentationResolver.Resolve(result));
            }

            return result.Succeeded;
        }

        private bool ContainsOptionalLabel(
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

        private void BeginLegacyOptionalApply()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings?.warnOnApplyWorkload == true)
            {
                WorkloadSurfaceCoordinator.RegisterFooterCloser(CloseWorkloadFooterPopover);
                if (!WorkloadSurfaceCoordinator.TryOpenFooter())
                {
                    return;
                }
                _workloadFooterPopover = FooterPopoverKind.ApplyConfirmation;
                _workloadFooterConfirmDoNotAskAgain = false;
                return;
            }

            ApplyLegacyWorkloadInline();
        }

        private void ConfirmLegacyOptionalApply()
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

        private void ApplyLegacyWorkloadInline()
        {
            WorkloadOperationResult result = WorkloadGateway.ApplyCurrentWorkload();
            if (!result.Succeeded)
            {
                ReportWorkloadFailure(WorkloadPresentationResolver.Resolve(result));
                return;
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private void ReportWorkloadFailure(string message)
        {
            if (!message.AnyNonWhitespace())
            {
                message = "BWT_Workload_OperationFailed".Translate();
            }

            Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }
    }
}
