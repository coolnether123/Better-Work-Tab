using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.UI.RuleBuilder;
using RimWorld;
using Spine.UI.Animation;
using UnityEngine;
using Verse;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal enum RuleBuilder2Surface
    {
        Main,
        GeneratedReview
    }

    public sealed class Window_RuleBuilder2 : Window
    {
        private readonly RuleBuilder2Layout layout = new RuleBuilder2Layout();
        private readonly RuleBuilder2Ruleset seededRuleset;
        private readonly bool previewOnOpen;
        private readonly bool persistRuleset = true;

        private RuleBuilder2FlowController flowController;
        private RuleBuilder2MasterDetailView masterDetailView;
        private RuleBuilder2GeneratedDraftView generatedDraftView;
        private RuleBuilder2EditorView editorView;
        private RuleBuilder2TargetPickerView targetPickerView;
        private RuleBuilder2ConditionsView conditionsView;
        private RuleBuilder2ActionScheduleView actionScheduleView;
        private RuleBuilder2PreviewView previewView;

        private float windowOpenProgress;
        private RuleBuilder2Surface activeSurface = RuleBuilder2Surface.Main;
        private RuleBuilder2Surface previousSurface = RuleBuilder2Surface.Main;
        private float surfaceTransition = 1f;
        private Rect surfaceTransitionOrigin = Rect.zero;
        private bool hasSurfaceTransitionOrigin;
        private bool renamingRuleset;
        private string renameBuffer = "";
        private bool workTabDockPending;
        private bool hasWorkTabDockReferenceRect;
        private Rect workTabDockReferenceRect;

        internal RuleBuilder2Surface ActiveSurface => activeSurface;
        internal RuleBuilder2EditorView EditorView => editorView;

        public Window_RuleBuilder2()
        {
            forcePause = false;
            doCloseX = true;
            preventCameraMotion = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;
        }

        internal Window_RuleBuilder2(RuleBuilder2Ruleset seededRuleset, bool previewOnOpen)
            : this(seededRuleset, previewOnOpen, true)
        {
        }

        internal Window_RuleBuilder2(RuleBuilder2Ruleset seededRuleset, bool previewOnOpen, bool persistRuleset)
            : this()
        {
            this.seededRuleset = seededRuleset;
            this.previewOnOpen = previewOnOpen;
            this.persistRuleset = persistRuleset;
        }

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(layout.Metrics.PreferredWindowWidth, Verse.UI.screenWidth - layout.Metrics.ScreenMargin),
            Mathf.Min(layout.Metrics.PreferredWindowHeight, Verse.UI.screenHeight - layout.Metrics.ScreenMargin));

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            workTabDockPending = !DockToWorkTabIfOpen();
            if (workTabDockPending)
            {
                SetWorkTabDockReferenceRect(windowRect);
            }
            windowOpenProgress = 0f;
        }

        private bool DockToWorkTabIfOpen()
        {
            if (!(Find.MainTabsRoot?.OpenTab?.TabWindow is Better_Work_Tab.UI.MainTabWindow_BetterWork workTab))
            {
                return false;
            }

            ApplyWorkTabDock(workTab);
            workTabDockPending = false;
            return true;
        }

        private void UpdateWorkTabDocking()
        {
            if (!workTabDockPending)
            {
                return;
            }

            if (hasWorkTabDockReferenceRect && PositionChangedMeaningfully(windowRect, workTabDockReferenceRect))
            {
                workTabDockPending = false;
                return;
            }

            DockToWorkTabIfOpen();
        }

        private void ApplyWorkTabDock(Better_Work_Tab.UI.MainTabWindow_BetterWork workTab)
        {
            const float margin = 10f;
            const float gap = 6f;

            Rect usableRect = GetUsableScreenRect(margin);
            Rect workRect = workTab.windowRect;
            float maxWidth = Mathf.Max(1f, Mathf.Min(usableRect.width, Verse.UI.screenWidth - layout.Metrics.ScreenMargin));
            float maxHeight = Mathf.Max(1f, Mathf.Min(usableRect.height, Verse.UI.screenHeight - layout.Metrics.ScreenMargin));
            float minWidth = Mathf.Min(layout.Metrics.MinimumWindowWidth, maxWidth);
            float minHeight = Mathf.Min(layout.Metrics.MinimumWindowHeight, maxHeight);
            float preferredWidth = Mathf.Clamp(windowRect.width, minWidth, maxWidth);
            float preferredHeight = Mathf.Clamp(windowRect.height, minHeight, maxHeight);

            float availableAbove = Mathf.Max(0f, workRect.yMin - gap - usableRect.yMin);
            if (availableAbove >= minHeight)
            {
                float height = Mathf.Clamp(preferredHeight, minHeight, availableAbove);
                float width = preferredWidth;
                float x = AlignToWorkTabRight(workRect, width, usableRect);
                float y = workRect.yMin - gap - height;
                SetAutoDockRect(new Rect(x, y, width, height), usableRect);
                return;
            }

            float dockedWidth = Mathf.Clamp(minWidth, 1f, maxWidth);
            SetAutoDockRect(new Rect(
                usableRect.xMax - dockedWidth,
                usableRect.y,
                dockedWidth,
                maxHeight), usableRect);
        }

        private static Rect GetUsableScreenRect(float margin)
        {
            margin = Mathf.Max(0f, margin);
            return new Rect(
                margin,
                margin,
                Mathf.Max(1f, Verse.UI.screenWidth - (margin * 2f)),
                Mathf.Max(1f, Verse.UI.screenHeight - (margin * 2f)));
        }

        private void SetAutoDockRect(Rect rect, Rect usableRect)
        {
            float width = Mathf.Clamp(rect.width, 1f, usableRect.width);
            float height = Mathf.Clamp(rect.height, 1f, usableRect.height);
            Rect clamped = new Rect(
                Mathf.Clamp(rect.x, usableRect.xMin, usableRect.xMax - width),
                Mathf.Clamp(rect.y, usableRect.yMin, usableRect.yMax - height),
                width,
                height);

            windowRect = clamped;
            SetWorkTabDockReferenceRect(clamped);
        }

        private void SetWorkTabDockReferenceRect(Rect rect)
        {
            workTabDockReferenceRect = rect;
            hasWorkTabDockReferenceRect = true;
        }

        private static float AlignToWorkTabRight(Rect workRect, float width, Rect usableRect)
        {
            return Mathf.Clamp(
                workRect.xMax - width,
                usableRect.xMin,
                usableRect.xMax - width);
        }

        private static bool PositionChangedMeaningfully(Rect a, Rect b)
        {
            const float threshold = 2f;
            return Mathf.Abs(a.x - b.x) > threshold ||
                   Mathf.Abs(a.y - b.y) > threshold;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            RuleBuilderGateway.EnsureRuleBuilder2ServicesRegistered();
            flowController = new RuleBuilder2FlowController(seededRuleset, previewOnOpen, persistRuleset);
            targetPickerView = new RuleBuilder2TargetPickerView(this, flowController, layout);
            conditionsView = new RuleBuilder2ConditionsView(this, flowController, layout);
            actionScheduleView = new RuleBuilder2ActionScheduleView(this, flowController, layout);
            previewView = new RuleBuilder2PreviewView(this, flowController, layout);
            editorView = new RuleBuilder2EditorView(
                this,
                flowController,
                layout,
                targetPickerView,
                conditionsView,
                actionScheduleView,
                previewView);
            generatedDraftView = new RuleBuilder2GeneratedDraftView(this, flowController, layout, editorView);
            masterDetailView = new RuleBuilder2MasterDetailView(this, flowController, layout, editorView);

            flowController.PreOpen();
            targetPickerView.ExpandTargetParentIfSpecificJob(flowController.ActiveCard?.Target);
            RuleBuilder2WorkTabBridge.Register(this);

            // Window by window, not step by step: opening the builder is the
            // opportunity to say what a rule is. RimWorld's tutor decides
            // whether to actually show it.
            BWTConcepts.TeachWorkRules();
            if (flowController.PreviewOnOpen)
            {
                activeSurface = RuleBuilder2Surface.Main;
                previousSurface = RuleBuilder2Surface.Main;
                editorView.MapCheckExpanded = true;
            }
        }

        public override void PostClose()
        {
            RuleBuilder2WorkTabBridge.Unregister(this);
            if (flowController != null && flowController.PersistRuleset)
            {
                flowController.SaveRulesetIfPersistent(flowController.Ruleset);
                BetterWorkTabMod.Settings.Write();
            }

            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            EnsureMinimumWindowSize();
            UpdateWorkTabDocking();
            actionScheduleView.ClearSchedulePaintOnMouseUp();
            UpdateWindowOpenAnimation();
            UpdateSurfaceAnimation();

            float openEase = SpineEasing.SmoothStep01(windowOpenProgress);
            float slide = 12f * (1f - openEase);
            Rect animatedRect = new Rect(inRect.x, inRect.y + slide, inRect.width, Mathf.Max(0f, inRect.height - slide));
            Color previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * Mathf.Lerp(0.55f, 1f, openEase));

            RuleBuilder2WindowRects windowRects = layout.Window(animatedRect);
            DrawHeader(windowRects.Header);
            DrawSurfaceTransition(windowRects.Body);
            GUI.color = previousColor;
        }

        private void EnsureMinimumWindowSize()
        {
            float maxWidth = Mathf.Max(320f, Verse.UI.screenWidth - layout.Metrics.ScreenMargin);
            float maxHeight = Mathf.Max(320f, Verse.UI.screenHeight - layout.Metrics.ScreenMargin);
            float minWidth = Mathf.Min(layout.Metrics.MinimumWindowWidth, maxWidth);
            float minHeight = Mathf.Min(layout.Metrics.MinimumWindowHeight, maxHeight);

            float width = Mathf.Clamp(windowRect.width, minWidth, maxWidth);
            float height = Mathf.Clamp(windowRect.height, minHeight, maxHeight);
            float x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Verse.UI.screenWidth - width));
            float y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Verse.UI.screenHeight - height));
            if (Mathf.Abs(width - windowRect.width) > 0.01f ||
                Mathf.Abs(height - windowRect.height) > 0.01f ||
                Mathf.Abs(x - windowRect.x) > 0.01f ||
                Mathf.Abs(y - windowRect.y) > 0.01f)
            {
                windowRect = new Rect(x, y, width, height);
            }
        }

        private void UpdateWindowOpenAnimation()
        {
            windowOpenProgress = SpineEasing.Move01(
                windowOpenProgress,
                1f,
                layout.Metrics.WindowOpenAnimationSeconds,
                BetterWorkTabMod.Settings?.ruleBuilder2EnableAnimations ?? true);
        }

        internal void AcceptWorkTabSelection(RuleBuilder2WorkTabSelection selection)
        {
            flowController.AcceptWorkTabSelection(selection);
            targetPickerView.ExpandTargetParentIfSpecificJob(flowController.ActiveCard?.Target);
            ShowMainSurface();
            editorView.ResetEditorScroll();
        }

        internal void PreviewWorkTabSelection(RuleBuilder2WorkTabSelection selection)
        {
            flowController.PreviewWorkTabSelection(selection);
        }

        internal void ClearWorkTabPreview()
        {
            flowController.ClearWorkTabPreview();
        }

        internal bool IsTargetSelected(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return flowController.IsTargetSelected(workType, workGiver);
        }

        internal bool TryGetActiveTarget(out WorkTypeDef workType, out WorkGiverDef workGiver)
        {
            workType = flowController.ActiveCard?.Target?.ResolveWorkType();
            workGiver = flowController.ActiveCard?.Target?.ResolveWorkGiver();
            return workType != null;
        }

        private void DrawHeader(Rect rect)
        {
            RuleBuilder2HeaderRects header = layout.Header(rect);
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(header.Title, T("BWT_RuleBuilder2_Title"));

            Text.Font = GameFont.Small;
            DrawRulesetControl(header.Name, header.Tools);
            Widgets.CheckboxLabeled(header.Enabled, T("BWT_RuleBuilder2_Enabled"), ref flowController.Ruleset.Enabled);

            if (Widgets.ButtonText(header.Done, T("BWT_Done")))
            {
                Close();
            }

            if (Widgets.ButtonText(header.Apply, T("BWT_Apply")))
            {
                flowController.ApplyRuleset();
            }

            if (!string.IsNullOrEmpty(flowController.ApplyStatus))
            {
                GUI.color = Color.yellow;
                Widgets.Label(header.Status, flowController.ApplyStatus);
                GUI.color = Color.white;
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRulesetControl(Rect rect, Rect tools)
        {
            if (renamingRuleset)
            {
                renameBuffer = Widgets.TextField(rect, renameBuffer ?? "");
                bool commit = Event.current.type == EventType.KeyDown &&
                              (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                bool clickedAway = Event.current.type == EventType.MouseDown && !rect.Contains(Event.current.mousePosition);
                if (commit || clickedAway)
                {
                    CommitRulesetRename();
                    if (commit)
                    {
                        Event.current.Use();
                    }
                }

                return;
            }

            string label = (flowController.Ruleset?.Name).NullOrEmpty()
                ? T("BWT_RuleBuilder2_NewRulesetName")
                : flowController.Ruleset.Name;
            if (Widgets.ButtonText(rect, TruncateToWidth(label + " \u25BE", rect.width - 8f)))
            {
                ShowRulesetMenu();
            }
            TooltipHandler.TipRegion(rect, T("BWT_RuleBuilder2_RulesetMenu_Tooltip"));

            if (Widgets.ButtonText(tools, "..."))
            {
                ShowRulesetToolsMenu();
            }
            TooltipHandler.TipRegion(tools, T("BWT_RuleBuilder2_More_Tooltip"));
        }

        private void CommitRulesetRename()
        {
            string trimmed = (renameBuffer ?? "").Trim();
            if (!trimmed.NullOrEmpty())
            {
                flowController.Ruleset.Name = trimmed;
                flowController.SaveRulesetIfPersistent(flowController.Ruleset);
            }

            renamingRuleset = false;
        }

        private void ShowRulesetMenu()
        {
            var options = new List<FloatMenuOption>();
            bool addedRuleset = false;
            foreach (RuleBuilder2Ruleset ruleset in RuleBuilder2RulesetStore.Saved(BetterWorkTabMod.Settings)
                         .Where(ruleset => ruleset != null))
            {
                RuleBuilder2Ruleset local = ruleset;
                string label = local == flowController.Ruleset ? "\u2713 " + local.Name : local.Name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    flowController.SwitchRuleset(local);
                    targetPickerView.ExpandTargetParentIfSpecificJob(flowController.ActiveCard?.Target);
                    editorView.ResetEditorScroll();
                    ShowMainSurface();
                }));
                addedRuleset = true;
            }

            if (!addedRuleset)
            {
                options.Add(new FloatMenuOption(T("BWT_NoRuleset"), null));
            }

            options.Add(new FloatMenuOption("----------", null));
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_CreateBlank"), () =>
            {
                RuleBuilder2Ruleset created = flowController.CreateAndStoreBlankRuleset();
                flowController.SwitchRuleset(created);
                editorView.ResetEditorScroll();
                ShowMainSurface();
            }));
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_Duplicate"), () =>
            {
                flowController.DuplicateCurrentRuleset();
                editorView.ResetEditorScroll();
                ShowMainSurface();
            }));
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_Rename"), () =>
            {
                renameBuffer = flowController.Ruleset?.Name ?? "";
                renamingRuleset = true;
            }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRulesetToolsMenu()
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_ImportClassic"), () =>
            {
                flowController.ImportClassicRuleset(() =>
                {
                    targetPickerView.ExpandTargetParentIfSpecificJob(flowController.ActiveCard?.Target);
                    editorView.ResetEditorScroll();
                    ShowMainSurface();
                });
            }));
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_CopyDefault"), () =>
            {
                flowController.CopyDefaultRuleset();
                editorView.ResetEditorScroll();
                ShowMainSurface();
            }));
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_ExportClassic"), flowController.ExportClassicCompatibleRuleset));
            options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_OpenClassic"), () => Find.WindowStack.Add(new Better_Work_Tab.UI.RuleBuilder.Window_RulesetBuilder())));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawSurfaceTransition(Rect rect)
        {
            float eased = SpineEasing.SmoothStep01(surfaceTransition);
            if (surfaceTransition < 1f && previousSurface != activeSurface)
            {
                if (Event.current.type != EventType.Repaint)
                {
                    if (rect.Contains(Event.current.mousePosition) && IsPointerEvent(Event.current))
                    {
                        Event.current.Use();
                    }

                    return;
                }

                float previousAlpha = (1f - surfaceTransition) * 0.75f;
                DrawSurface(rect, previousSurface, previousAlpha);
                DrawSurface(rect, activeSurface, Mathf.Clamp01(eased));
                DrawSurfaceExpansion(rect, eased);
                return;
            }

            DrawSurface(rect, activeSurface, 1f);
        }

        private static bool IsPointerEvent(Event evt)
        {
            return evt.type == EventType.MouseDown ||
                   evt.type == EventType.MouseUp ||
                   evt.type == EventType.MouseDrag ||
                   evt.type == EventType.ScrollWheel;
        }

        private void DrawSurface(Rect rect, RuleBuilder2Surface surface, float alpha)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * alpha);
            if (surface == RuleBuilder2Surface.GeneratedReview)
            {
                generatedDraftView.DrawGeneratedDraftQueue(rect);
            }
            else
            {
                masterDetailView.DrawMasterDetail(rect);
            }
            GUI.color = previousColor;
        }

        private void UpdateSurfaceAnimation()
        {
            surfaceTransition = SpineEasing.Move01(
                surfaceTransition,
                1f,
                layout.Metrics.SurfaceAnimationSeconds,
                BetterWorkTabMod.Settings?.ruleBuilder2EnableAnimations ?? true);
        }

        internal void ShowDashboardSurface()
        {
            ShowMainSurface();
            flowController.ActiveCard = null;
        }

        internal void ShowRuleCardSurface()
        {
            ShowMainSurface();
        }

        internal void ShowRuleCardSurface(Rect origin)
        {
            SetSurface(RuleBuilder2Surface.Main, origin);
        }

        internal void ShowMainSurface()
        {
            SetSurface(RuleBuilder2Surface.Main);
        }

        internal void ShowGeneratedReviewSurface()
        {
            BWTConcepts.TeachRuleSuggestions();
            SetSurface(RuleBuilder2Surface.GeneratedReview);
        }

        private void SetSurface(RuleBuilder2Surface surface)
        {
            SetSurface(surface, Rect.zero);
        }

        private void SetSurface(RuleBuilder2Surface surface, Rect origin)
        {
            if (activeSurface == surface)
            {
                return;
            }

            previousSurface = activeSurface;
            activeSurface = surface;
            surfaceTransition = 0f;
            hasSurfaceTransitionOrigin = origin.width > 0f && origin.height > 0f;
            surfaceTransitionOrigin = origin;
        }

        private void DrawSurfaceExpansion(Rect targetRect, float progress)
        {
            if (!hasSurfaceTransitionOrigin)
            {
                return;
            }

            Rect from = surfaceTransitionOrigin;
            Rect to = targetRect.ContractedBy(10f);
            Rect current = new Rect(
                Mathf.Lerp(from.x, to.x, progress),
                Mathf.Lerp(from.y, to.y, progress),
                Mathf.Lerp(from.width, to.width, progress),
                Mathf.Lerp(from.height, to.height, progress));

            Color previous = GUI.color;
            GUI.color = new Color(0.9f, 0.82f, 0.55f, 0.45f * (1f - progress));
            Widgets.DrawBox(current, 2);
            GUI.color = previous;
        }

        internal void ConfirmActiveCardForSmokeTest()
        {
            if (flowController.ActiveCard != null && !flowController.ActiveCard.IsConfirmed)
            {
                flowController.ConfirmCard(flowController.ActiveCard);
            }
        }

        internal void ShowDashboardForSmokeTest()
        {
            ShowDashboardSurface();
        }

        internal void GenerateDraftForSmokeTest()
        {
            flowController.GenerateDraftFromCurrentWorkTab();
            flowController.ShowPreview = false;
            flowController.RefreshPreview();
            ShowGeneratedReviewSurface();
        }

        internal void ShowActionScheduleForSmokeTest()
        {
            ShowRuleCardSurface();
            flowController.ShowPreview = false;
            flowController.ActiveCard = flowController.GetVisibleCards()
                .FirstOrDefault(card => card.Action?.Kind == RuleBuilder2ActionKind.SetTimeSchedule || card.Action?.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule)
                ?? flowController.ActiveCard;
            if (flowController.ActiveCard?.Action != null)
            {
                flowController.ActiveCard.NormalizeActionForTarget();
                flowController.ActiveCard.Action.EnsureSchedule(flowController.ActiveCard.Action.Priority);
            }

            editorView.ResetEditorScroll();
        }

        internal void RefreshPreviewForSmokeTest()
        {
            ShowRuleCardSurface();
            flowController.ShowPreview = true;
            editorView.MapCheckExpanded = true;
            flowController.RefreshPreview();
        }

        internal void ScrollToPreviewForSmokeTest()
        {
            flowController.ShowPreview = true;
            editorView.MapCheckExpanded = true;
            flowController.RefreshPreview();
            editorView.EditorScrollY = 420f;
        }
    }
}
