using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Spine.UI.Animation;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal enum RuleBuilder2EditorSection
    {
        Target,
        Conditions,
        Action,
        Preview
    }

    internal enum RuleBuilder2Surface
    {
        Dashboard,
        RuleCard
    }

    public sealed class Window_RuleBuilder2 : Window
    {
        private readonly RuleBuilder2Evaluator evaluator = new RuleBuilder2Evaluator();
        private readonly RuleBuilder2ApplyService applyService = new RuleBuilder2ApplyService();
        private readonly RuleBuilder2DraftGenerator draftGenerator = new RuleBuilder2DraftGenerator();
        private readonly RuleBuilder2ClassicRulesetExportService classicRulesetExportService = new RuleBuilder2ClassicRulesetExportService();
        private readonly RuleBuilder2Layout layout = new RuleBuilder2Layout();
        private readonly RuleBuilder2TutorialController tutorial = new RuleBuilder2TutorialController();
        private readonly Dictionary<RuleBuilder2TutorialStep, Rect> tutorialRects = new Dictionary<RuleBuilder2TutorialStep, Rect>();

        private RuleBuilder2Ruleset ruleset;
        private RuleBuilder2Card activeCard;
        private List<RuleBuilder2PreviewResult> preview = new List<RuleBuilder2PreviewResult>();
        private RuleBuilder2WorkTabSelection? selectedWorkTabContext;

        private Vector2 cardListScroll;
        private Vector2 editorScroll;
        private Vector2 previewScroll;
        private string targetSearch = "";
        private string conditionSearch = "";
        private string applyStatus = "";
        private bool showPreview;
        private bool showMatchedPanel;
        private readonly RuleBuilder2Ruleset seededRuleset;
        private readonly bool previewOnOpen;
        private RuleBuilder2Surface activeSurface = RuleBuilder2Surface.Dashboard;
        private RuleBuilder2Surface previousSurface = RuleBuilder2Surface.Dashboard;
        private float surfaceTransition = 1f;
        private Rect surfaceTransitionOrigin = Rect.zero;
        private bool hasSurfaceTransitionOrigin;
        private RuleBuilder2EditorSection activeSection = RuleBuilder2EditorSection.Target;
        private readonly Dictionary<RuleBuilder2EditorSection, float> sectionOpenProgress =
            new Dictionary<RuleBuilder2EditorSection, float>
            {
                { RuleBuilder2EditorSection.Target, 1f },
                { RuleBuilder2EditorSection.Conditions, 0f },
                { RuleBuilder2EditorSection.Action, 0f },
                { RuleBuilder2EditorSection.Preview, 0f }
            };

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
            : this()
        {
            this.seededRuleset = seededRuleset;
            this.previewOnOpen = previewOnOpen;
        }

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(layout.Metrics.PreferredWindowWidth, Verse.UI.screenWidth - layout.Metrics.ScreenMargin),
            Mathf.Min(layout.Metrics.PreferredWindowHeight, Verse.UI.screenHeight - layout.Metrics.ScreenMargin));

        public override void PreOpen()
        {
            base.PreOpen();
            BetterWorkTabMod.Settings.EnsureRuleBuilder2Rulesets();
            if (seededRuleset != null)
            {
                ruleset = seededRuleset;
                BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
            }
            else
            {
                ruleset = BetterWorkTabMod.Settings.CurrentRuleBuilder2Ruleset
                          ?? BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.FirstOrDefault()
                          ?? CreateAndStoreBlankRuleset();
                BetterWorkTabMod.Settings.SetCurrentRuleBuilder2Ruleset(ruleset, writeSettings: false);
            }

            ruleset.EnsureOpenBlankCard();
            activeCard = ruleset.Cards.FirstOrDefault(card => card != null && !card.IsConfirmed) ?? ruleset.Cards.LastOrDefault();
            activeSection = activeCard?.Target?.HasTarget == true
                ? RuleBuilder2EditorSection.Conditions
                : RuleBuilder2EditorSection.Target;
            RuleBuilder2WorkTabBridge.Register(this);
            tutorial.Reset();
            if (previewOnOpen)
            {
                activeSurface = RuleBuilder2Surface.RuleCard;
                previousSurface = RuleBuilder2Surface.RuleCard;
                showPreview = true;
                RefreshPreview();
            }
        }

        public override void PostClose()
        {
            RuleBuilder2WorkTabBridge.Unregister(this);
            if (ruleset != null)
            {
                BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
            }

            BetterWorkTabMod.Settings.Write();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UpdateSectionAnimation();
            UpdateSurfaceAnimation();
            tutorialRects.Clear();
            if (Event.current.type != EventType.Repaint && tutorial.TryHandleInput(inRect, tutorialRects, Event.current))
            {
                return;
            }

            RuleBuilder2WindowRects windowRects = layout.Window(inRect);
            DrawHeader(windowRects.Header);
            DrawSurfaceTransition(windowRects.Body);
            RuleBuilder2WorkTabBridge.DrawRecentSelectionPulse();
            tutorial.Draw(inRect, tutorialRects);
        }

        internal void AcceptWorkTabSelection(RuleBuilder2WorkTabSelection selection)
        {
            selectedWorkTabContext = selection;
            showMatchedPanel = selection.Pawn != null &&
                (BetterWorkTabMod.Settings?.ruleBuilder2ShowMatchedPanel ?? true);

            RuleBuilder2Card card = GetEditableCard();
            card.Target.WorkTypeDefName = selection.WorkType.defName;
            card.Target.WorkGiverDefName = selection.WorkGiver?.defName ?? "";
            card.Target.DisplayLabel = BuildTargetLabel(selection.WorkType, selection.WorkGiver);
            card.Target.Source = selection.Source;
            if (selection.Priority >= 0)
            {
                card.Action.Priority = WorkPrioritySystem.ClampPriority(selection.Priority);
            }

            activeCard = card;
            ShowRuleCardSurface();
            activeSection = RuleBuilder2EditorSection.Conditions;
            RefreshPreview();
            tutorial.ObserveTargetSelected(selection.WorkGiver != null);
        }

        internal bool IsTargetSelected(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            RuleBuilder2Card card = activeCard;
            if (card?.Target == null || workType == null)
            {
                return false;
            }

            string activeWorkType = card.Target.WorkTypeDefName;
            string activeWorkGiver = card.Target.WorkGiverDefName ?? "";
            return activeWorkType == workType.defName &&
                   activeWorkGiver == (workGiver?.defName ?? "");
        }

        private void DrawHeader(Rect rect)
        {
            RuleBuilder2HeaderRects header = layout.Header(rect);
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(header.Title, T("BWT_RuleBuilder2_Title"));

            Text.Font = GameFont.Small;
            ruleset.Name = Widgets.TextField(header.Name, ruleset.Name ?? "");
            Widgets.CheckboxLabeled(header.Enabled, T("BWT_RuleBuilder2_Enabled"), ref ruleset.Enabled);

            if (Widgets.ButtonText(header.Done, T("BWT_Done")))
            {
                Close();
            }

            if (Widgets.ButtonText(header.Apply, T("BWT_Apply")))
            {
                ApplyRuleset();
            }

            if (Widgets.ButtonText(header.Preview, T("BWT_RuleBuilder2_Preview")))
            {
                showPreview = true;
                ShowRuleCardSurface(Rect.zero);
                activeSection = RuleBuilder2EditorSection.Preview;
                RefreshPreview();
                tutorial.ObservePreview();
            }

            if (!string.IsNullOrEmpty(applyStatus))
            {
                GUI.color = Color.yellow;
                Widgets.Label(header.Status, applyStatus);
                GUI.color = Color.white;
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSurfaceTransition(Rect rect)
        {
            float eased = SpineEasing.SmoothStep01(surfaceTransition);
            if (surfaceTransition < 1f && Event.current.type == EventType.Repaint && previousSurface != activeSurface)
            {
                float previousAlpha = (1f - surfaceTransition) * 0.75f;
                DrawSurface(rect, previousSurface, previousAlpha);
                DrawSurface(rect, activeSurface, Mathf.Clamp01(eased));
                DrawSurfaceExpansion(rect, eased);
                return;
            }

            DrawSurface(rect, activeSurface, 1f);
        }

        private void DrawSurface(Rect rect, RuleBuilder2Surface surface, float alpha)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * alpha);
            if (surface == RuleBuilder2Surface.Dashboard)
            {
                DrawDashboard(rect);
            }
            else
            {
                DrawEditor(rect);
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

        private void ShowDashboardSurface()
        {
            SetSurface(RuleBuilder2Surface.Dashboard);
        }

        private void ShowRuleCardSurface()
        {
            ShowRuleCardSurface(Rect.zero);
        }

        private void ShowRuleCardSurface(Rect origin)
        {
            SetSurface(RuleBuilder2Surface.RuleCard, origin);
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

        private void DrawDashboard(Rect rect)
        {
            RuleBuilder2DashboardRects dashboard = layout.Dashboard(rect);
            Widgets.DrawMenuSection(rect);
            tutorialRects[RuleBuilder2TutorialStep.BlankRuleset] = rect;

            Text.Font = GameFont.Medium;
            Widgets.Label(dashboard.Title, T("BWT_RuleBuilder2_Dashboard"));
            Text.Font = GameFont.Small;

            Widgets.DrawMenuSection(dashboard.ActionsCard);

            DrawDashboardButton(dashboard.AddRule, "+ " + T("BWT_RuleBuilder2_AddRule"), () =>
            {
                activeCard = RuleBuilder2Card.CreateBlank(ruleset.Cards.Count);
                ruleset.Cards.Add(activeCard);
                activeSection = RuleBuilder2EditorSection.Target;
                showPreview = false;
                ShowRuleCardSurface(dashboard.AddRule);
            });

            DrawDashboardButton(dashboard.GenerateDraft, T("BWT_RuleBuilder2_GenerateDraft"), () =>
            {
                ruleset = draftGenerator.GenerateFromCurrentWorkTab();
                BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
                activeCard = GetNextDraftCard() ?? ruleset.Cards.FirstOrDefault();
                activeSection = RuleBuilder2EditorSection.Target;
                RefreshPreview();
                ShowDashboardSurface();
            });

            DrawDashboardButton(dashboard.Import, T("BWT_RuleBuilder2_ImportShort"), ImportClassicRuleset);

            if (Widgets.ButtonText(dashboard.More, "..."))
            {
                ShowDashboardMoreMenu();
            }

            GUI.color = Color.gray;
            Widgets.Label(dashboard.Description, ruleset.Description.NullOrEmpty()
                ? T("BWT_RuleBuilder2_DashboardHint")
                : ruleset.Description);
            GUI.color = Color.white;

            tutorialRects[RuleBuilder2TutorialStep.RuleDeck] = dashboard.Content;
            if (ruleset.Source == RuleBuilder2SourceType.GeneratedDraft && GetNextDraftCard() != null)
            {
                DrawDraftReviewQueue(dashboard.Content);
            }
            else
            {
                DrawRulesTable(dashboard.Content);
            }
        }

        private void ShowDashboardMoreMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(T("BWT_RuleBuilder2_CreateBlank"), () =>
                {
                    ruleset = CreateAndStoreBlankRuleset();
                    activeCard = ruleset.Cards[0];
                    activeSection = RuleBuilder2EditorSection.Target;
                    ShowDashboardSurface();
                    preview.Clear();
                }),
                new FloatMenuOption(T("BWT_RuleBuilder2_Duplicate"), () =>
                {
                    var copy = ruleset.Copy();
                    ruleset = copy;
                    BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
                    activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
                    activeSection = activeCard?.Target?.HasTarget == true ? RuleBuilder2EditorSection.Conditions : RuleBuilder2EditorSection.Target;
                    ShowDashboardSurface();
                }),
                new FloatMenuOption(T("BWT_RuleBuilder2_CopyDefault"), () =>
                {
                    WorkAssignmentRuleset classicDefault = BetterWorkTabMod.Settings.SavedRulesets?.FirstOrDefault(rs => rs?.IsDefault == true);
                    if (classicDefault != null)
                    {
                        ruleset = RuleBuilder2ClassicRulesetTranslator.FromClassic(classicDefault);
                        ruleset.Source = RuleBuilder2SourceType.DefaultCopy;
                        BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
                        activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
                        activeSection = activeCard?.Target?.HasTarget == true ? RuleBuilder2EditorSection.Conditions : RuleBuilder2EditorSection.Target;
                        ShowDashboardSurface();
                    }
                }),
                new FloatMenuOption(T("BWT_RuleBuilder2_ExportClassic"), ExportClassicCompatibleRuleset),
                new FloatMenuOption(T("BWT_RuleBuilder2_OpenClassic"), () => Find.WindowStack.Add(new Better_Work_Tab.UI.RuleBuilder.Window_RulesetBuilder()))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawRulesTable(Rect rect)
        {
            RuleBuilder2RulesTableRects table = layout.RulesTable(rect);
            Widgets.DrawMenuSection(rect);
            DrawRulesTableHeader(table.Header);

            var cards = ruleset.Cards?
                .Where(card => card != null)
                .Where(card => card.IsConfirmed || card.Target?.HasTarget == true)
                .OrderBy(card => card.SortOrder)
                .ToList() ?? new List<RuleBuilder2Card>();

            Rect view = new Rect(0f, 0f, table.List.width - 16f, Mathf.Max(table.List.height, cards.Count * layout.Metrics.RuleRowStride + 4f));
            Widgets.BeginScrollView(table.List, ref cardListScroll, view);
            if (cards.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(8f, 8f, view.width - 16f, 28f), T("BWT_RuleBuilder2_NoRulesTable"));
                GUI.color = Color.white;
                Widgets.EndScrollView();
                return;
            }

            float y = 0f;
            foreach (RuleBuilder2Card card in cards)
            {
                Rect row = new Rect(0f, y, view.width, layout.Metrics.RuleRowHeight);
                DrawRuleTableRow(row, card);
                y += layout.Metrics.RuleRowStride;
            }
            Widgets.EndScrollView();
        }

        private void DrawRulesTableHeader(Rect rect)
        {
            RuleBuilder2RuleTableHeaderRects header = layout.RulesTableHeader(rect);
            GUI.color = Color.gray;
            Widgets.Label(header.Enabled, T("BWT_RuleBuilder2_TableOn"));
            Widgets.Label(header.Target, T("BWT_RuleBuilder2_TableTarget"));
            Widgets.Label(header.Conditions, T("BWT_RuleBuilder2_TableConditions"));
            Widgets.Label(header.Action, T("BWT_RuleBuilder2_TableAction"));
            Widgets.Label(header.Edit, T("BWT_RuleBuilder2_TableEdit"));
            GUI.color = Color.white;
        }

        private void DrawRuleTableRow(Rect rect, RuleBuilder2Card card)
        {
            RuleBuilder2RuleTableRowRects row = layout.RuleTableRow(rect);
            bool selected = card == activeCard;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.22f, 0.27f, 0.2f, 0.95f) : new Color(0.13f, 0.13f, 0.13f, 0.95f));
            Widgets.DrawBox(rect, 1);

            Widgets.Checkbox(row.Enabled.position, ref card.Enabled);
            DrawFittedLabel(row.Target, BuildTargetSummary(card));
            DrawFittedLabel(row.Conditions, BuildConditionsSummary(card));
            DrawFittedLabel(row.Action, BuildActionSummary(card));

            if (Widgets.ButtonText(row.Edit, T("BWT_Edit")))
            {
                activeCard = card;
                activeSection = card.Target.HasTarget ? RuleBuilder2EditorSection.Conditions : RuleBuilder2EditorSection.Target;
                ShowRuleCardSurface(new Rect(rect.x, rect.y, rect.width, rect.height));
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            if (Widgets.ButtonText(row.Delete, T("BWT_Delete")))
            {
                ruleset.Cards.Remove(card);
                activeCard = ruleset.Cards.FirstOrDefault(c => !c.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
                ruleset.EnsureOpenBlankCard();
            }

            if (Widgets.ButtonInvisible(row.Select))
            {
                activeCard = card;
                activeSection = card.Target.HasTarget ? RuleBuilder2EditorSection.Conditions : RuleBuilder2EditorSection.Target;
                ShowRuleCardSurface(new Rect(rect.x, rect.y, rect.width, rect.height));
            }
        }

        private void DrawDraftReviewQueue(Rect rect)
        {
            RuleBuilder2DraftQueueRects queue = layout.DraftQueue(rect);
            Widgets.DrawMenuSection(rect);
            RuleBuilder2Card card = GetNextDraftCard();
            if (card == null)
            {
                DrawRulesTable(rect);
                return;
            }

            int total = ruleset.Cards.Count(c => c != null && c.Target?.HasTarget == true);
            int remaining = ruleset.Cards.Count(c => c != null && c.Target?.HasTarget == true && !c.IsConfirmed);
            Text.Font = GameFont.Medium;
            Widgets.Label(queue.Title, T("BWT_RuleBuilder2_DraftQueueTitle").Formatted(total - remaining + 1, total));
            Text.Font = GameFont.Small;

            DrawDraftMiniList(queue.Side, card);
            DrawDraftReviewPanel(queue.Main, card);
        }

        private void DrawDraftMiniList(Rect rect, RuleBuilder2Card selected)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.65f));
            Widgets.DrawBox(rect, 1);
            float y = rect.y + 6f;
            foreach (RuleBuilder2Card card in ruleset.Cards.Where(c => c != null && c.Target?.HasTarget == true).Take(16))
            {
                Rect row = new Rect(rect.x + 6f, y, rect.width - 12f, 24f);
                Widgets.DrawBoxSolid(row, card == selected ? new Color(0.24f, 0.28f, 0.22f, 1f) : new Color(0.15f, 0.15f, 0.15f, 0.85f));
                DrawFittedLabel(new Rect(row.x + 6f, row.y + 3f, row.width - 12f, 20f), card.Name);
                if (Widgets.ButtonInvisible(row))
                {
                    activeCard = card;
                }
                y += 28f;
                if (y > rect.yMax - 26f)
                {
                    break;
                }
            }
        }

        private void DrawDraftReviewPanel(Rect rect, RuleBuilder2Card card)
        {
            RuleBuilder2DraftReviewRects review = layout.DraftReview(rect);
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.95f));
            Widgets.DrawBox(rect, 1);

            Text.Font = GameFont.Medium;
            DrawFittedLabel(review.Title, card.Name);
            Text.Font = GameFont.Small;

            GUI.color = Color.gray;
            Widgets.Label(review.Notes, card.Notes.NullOrEmpty() ? T("BWT_RuleBuilder2_DraftNoReason") : card.Notes);
            GUI.color = Color.white;

            DrawRuleSentence(review.Sentence, card);

            DrawDraftPreview(review.Preview, card);

            if (Widgets.ButtonText(review.Reject, T("BWT_RuleBuilder2_RejectDraft")))
            {
                RejectDraftCard(card);
            }
            if (Widgets.ButtonText(review.Edit, T("BWT_RuleBuilder2_ReviewDraft")))
            {
                activeCard = card;
                showPreview = true;
                activeSection = RuleBuilder2EditorSection.Target;
                RefreshPreview();
                ShowRuleCardSurface(rect);
            }
            if (Widgets.ButtonText(review.Accept, T("BWT_RuleBuilder2_AcceptDraft")))
            {
                AcceptDraftCard(card);
            }
        }

        private void DrawDraftPreview(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.09f, 0.09f, 0.75f));
            Widgets.DrawBox(rect, 1);
            List<RuleBuilder2PreviewResult> results = evaluator.Preview(ruleset, card);
            int matched = results.Count(result => result.Matched);
            int changed = results.Count(result => result.Matched && result.CurrentPriority != result.NewPriority);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), T("BWT_RuleBuilder2_DraftImpact").Formatted(matched, changed).ToString());
            float y = rect.y + 34f;
            foreach (RuleBuilder2PreviewResult result in results.Take(5))
            {
                Rect row = new Rect(rect.x + 8f, y, rect.width - 16f, 26f);
                Widgets.DrawBoxSolid(row, result.Matched ? new Color(0.12f, 0.22f, 0.12f, 0.95f) : new Color(0.16f, 0.14f, 0.12f, 0.95f));
                DrawFittedLabel(new Rect(row.x + 6f, row.y + 4f, 150f, 20f), result.PawnLabel);
                DrawFittedLabel(new Rect(row.x + 164f, row.y + 4f, 120f, 20f), result.Matched ? T("BWT_RuleBuilder2_Matched") : T("BWT_RuleBuilder2_NotMatched"));
                DrawFittedLabel(new Rect(row.x + 292f, row.y + 4f, row.width - 300f, 20f), result.Matched ? result.ActionText : string.Join(", ", result.ConditionsFailed.Take(2).ToArray()));
                y += 30f;
            }
        }

        private RuleBuilder2Card GetNextDraftCard()
        {
            return ruleset.Cards?
                .Where(card => card != null && card.Target?.HasTarget == true && !card.IsConfirmed)
                .OrderBy(card => card.SortOrder)
                .FirstOrDefault();
        }

        private void AcceptDraftCard(RuleBuilder2Card card)
        {
            card.IsConfirmed = true;
            card.IsCollapsed = true;
            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            activeCard = GetNextDraftCard() ?? card;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void RejectDraftCard(RuleBuilder2Card card)
        {
            ruleset.Cards.Remove(card);
            activeCard = GetNextDraftCard() ?? ruleset.Cards.FirstOrDefault();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private void DrawEditor(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            RuleBuilder2Card card = GetEditableCard();
            activeCard = card;

            float activeHeight = GetActiveEditorPanelHeight(card);
            float viewHeight = layout.EditorViewHeight(activeHeight);
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, viewHeight));
            RuleBuilder2EditorRects editor = layout.Editor(view, activeHeight);
            Widgets.BeginScrollView(inner, ref editorScroll, view);

            DrawRuleName(editor.Name, card);
            DrawRuleSentence(editor.Sentence, card);
            DrawActiveEditorPanel(editor.Panel, card);
            DrawConfirmSection(editor.Confirm, card);

            Widgets.EndScrollView();
        }

        private float GetActiveEditorPanelHeight(RuleBuilder2Card card)
        {
            return layout.AnimatedEditorSectionHeight(
                activeSection,
                card?.Target?.HasTarget == true,
                GetSectionProgress(activeSection));
        }

        private void DrawActiveEditorPanel(Rect rect, RuleBuilder2Card card)
        {
            switch (activeSection)
            {
                case RuleBuilder2EditorSection.Target:
                    DrawTargetSection(rect, card);
                    return;
                case RuleBuilder2EditorSection.Conditions:
                    DrawConditionsSection(rect, card);
                    return;
                case RuleBuilder2EditorSection.Action:
                    DrawActionSection(rect, card);
                    return;
                case RuleBuilder2EditorSection.Preview:
                    DrawPreviewSection(rect, card);
                    return;
            }
        }

        private void DrawRuleSentence(Rect rect, RuleBuilder2Card card)
        {
            RuleBuilder2SentenceRects sentence = layout.Sentence(rect);
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.86f));
            Widgets.DrawBox(rect, 1);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(sentence.SetLabel, T("BWT_RuleBuilder2_SentenceSet"));
            GUI.color = Color.white;

            DrawSentenceToken(sentence.Target, BuildTargetSummary(card), RuleBuilder2EditorSection.Target, card);
            GUI.color = Color.gray;
            Widgets.Label(sentence.WhenLabel, T("BWT_RuleBuilder2_SentenceWhen"));
            GUI.color = Color.white;
            DrawSentenceToken(sentence.Conditions, BuildConditionsSummary(card), RuleBuilder2EditorSection.Conditions, card);
            GUI.color = Color.gray;
            Widgets.Label(sentence.ThenLabel, T("BWT_RuleBuilder2_SentenceThen"));
            GUI.color = Color.white;
            DrawSentenceToken(sentence.Action, BuildActionSummary(card), RuleBuilder2EditorSection.Action, card);
            DrawSentenceToken(sentence.Preview, T("BWT_RuleBuilder2_Preview"), RuleBuilder2EditorSection.Preview, card);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSentenceToken(Rect rect, string label, RuleBuilder2EditorSection section, RuleBuilder2Card card)
        {
            bool selected = activeSection == section && activeCard == card;
            Color fill = selected
                ? new Color(0.24f, 0.28f, 0.22f, 0.95f)
                : new Color(0.17f, 0.17f, 0.17f, 0.95f);
            Widgets.DrawBoxSolid(rect, fill);
            Widgets.DrawBox(rect, selected ? 2 : 1);
            DrawFittedLabel(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 6f), label);

            if (Widgets.ButtonInvisible(rect))
            {
                activeCard = card;
                activeSection = section;
                if (section == RuleBuilder2EditorSection.Preview)
                {
                    showPreview = true;
                    RefreshPreview();
                }

                if (activeSurface != RuleBuilder2Surface.RuleCard)
                {
                    ShowRuleCardSurface(rect);
                }

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        private void UpdateSectionAnimation()
        {
            foreach (RuleBuilder2EditorSection section in System.Enum.GetValues(typeof(RuleBuilder2EditorSection)))
            {
                float target = section == activeSection ? 1f : 0f;
                sectionOpenProgress[section] = SpineEasing.Move01(
                    GetSectionProgress(section),
                    target,
                    layout.Metrics.SectionAnimationSeconds,
                    BetterWorkTabMod.Settings?.ruleBuilder2EnableAnimations ?? true);
            }
        }

        private float GetSectionProgress(RuleBuilder2EditorSection section)
        {
            return sectionOpenProgress.TryGetValue(section, out float progress) ? progress : 0f;
        }

        private void DrawRuleName(Rect rect, RuleBuilder2Card card)
        {
            RuleBuilder2RuleNameRects name = layout.RuleName(rect);
            Text.Font = GameFont.Medium;
            Widgets.Label(name.Title, T("BWT_RuleBuilder2_RuleCard"));
            Text.Font = GameFont.Small;
            card.Name = Widgets.TextField(name.Name, card.Name ?? "");

            if (Widgets.ButtonText(name.Dashboard, T("BWT_RuleBuilder2_Dashboard")))
            {
                ShowDashboardSurface();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        private void DrawTargetSection(Rect rect, RuleBuilder2Card card)
        {
            if (activeSection != RuleBuilder2EditorSection.Target)
            {
                DrawCollapsedSection(rect, RuleBuilder2EditorSection.Target, T("BWT_RuleBuilder2_StepTarget"), BuildTargetSummary(card));
                return;
            }

            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            RuleBuilder2TargetSectionRects target = layout.TargetSection(rect, card.Target.HasTarget);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepTarget"));
            tutorialRects[RuleBuilder2TutorialStep.Target] = outerRect;

            if (card.Target.HasTarget)
            {
                string label = BuildTargetLabel(card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
                if (label == T("BWT_RuleBuilder2_WorkFallback") && !card.Target.DisplayLabel.NullOrEmpty())
                {
                    label = card.Target.DisplayLabel;
                }
                Widgets.Label(target.Summary, T("BWT_RuleBuilder2_TargetSummary").Formatted(label).ToString());
                if (Widgets.ButtonText(target.Change, T("BWT_Change")))
                {
                    card.Target.WorkTypeDefName = "";
                    card.Target.WorkGiverDefName = "";
                    activeSection = RuleBuilder2EditorSection.Target;
                }

                GUI.color = Color.gray;
                Widgets.Label(target.Hint, T("BWT_RuleBuilder2_WorkTabHint"));
                GUI.color = Color.white;
                if (Widgets.ButtonText(target.Next, T("BWT_RuleBuilder2_NextConditions")))
                {
                    activeSection = RuleBuilder2EditorSection.Conditions;
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
                GUI.EndGroup();
                return;
            }

            targetSearch = Widgets.TextField(target.Search, targetSearch ?? "");
            DrawTargetList(target.List, card);
            GUI.EndGroup();
        }

        private void DrawTargetList(Rect rect, RuleBuilder2Card card)
        {
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(workType => workType != null)
                .Where(workType => string.IsNullOrEmpty(targetSearch) ||
                                   workType.LabelCap.ToString().ToLowerInvariant().Contains(targetSearch.ToLowerInvariant()) ||
                                   workType.defName.ToLowerInvariant().Contains(targetSearch.ToLowerInvariant()))
                .OrderByDescending(workType => workType.naturalPriority)
                .ToList();

            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef workType = workTypes[i];
                Rect button = layout.GridCell(rect, i, 130f, layout.Metrics.TextFieldHeight, layout.Metrics.SmallGap);
                if (button.yMax > rect.yMax)
                {
                    break;
                }

                string label = GetWorkTypeLabel(workType);
                if (Widgets.ButtonText(button, label))
                {
                    SelectTarget(card, workType, null, RuleBuilder2TargetSource.BuilderList);
                }
            }
        }

        private void DrawConditionsSection(Rect rect, RuleBuilder2Card card)
        {
            if (activeSection != RuleBuilder2EditorSection.Conditions)
            {
                DrawCollapsedSection(rect, RuleBuilder2EditorSection.Conditions, T("BWT_RuleBuilder2_StepConditions"), BuildConditionsSummary(card));
                return;
            }

            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            RuleBuilder2ConditionsSectionRects conditions = layout.ConditionsSection(rect);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepConditions"));
            tutorialRects[RuleBuilder2TutorialStep.Condition] = outerRect;

            conditionSearch = Widgets.TextField(conditions.Search, conditionSearch ?? "");
            if (Widgets.ButtonText(conditions.Next, T("BWT_RuleBuilder2_NextAction")))
            {
                activeSection = RuleBuilder2EditorSection.Action;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            DrawActiveConditions(conditions.Active, card);
            DrawConditionPicker(conditions.Picker, card);
            GUI.EndGroup();
        }

        private void DrawActiveConditions(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.45f));
            Widgets.DrawBox(rect, 1);
            float y = rect.y + 6f;
            var conditions = card.Conditions.Conditions;

            if (conditions.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x + 8f, y, rect.width - 16f, 50f), T("BWT_RuleBuilder2_NoConditions"));
                GUI.color = Color.white;
                return;
            }

            for (int i = 0; i < conditions.Count; i++)
            {
                RuleBuilder2Condition condition = conditions[i];
                Rect row = new Rect(rect.x + 6f, y, rect.width - 12f, layout.Metrics.ConditionRowHeight);
                DrawConditionRow(row, card, condition, i);
                y += layout.Metrics.ConditionRowStride;
            }
        }

        private void DrawConditionRow(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition, int index)
        {
            RuleBuilder2ConditionRowRects row = layout.ConditionRow(rect);
            Widgets.DrawBoxSolid(rect, condition.Enabled ? new Color(0.18f, 0.18f, 0.18f, 0.95f) : new Color(0.11f, 0.11f, 0.11f, 0.95f));
            Widgets.DrawBox(rect, 1);

            Widgets.Checkbox(row.Checkbox.position, ref condition.Enabled);

            string text = RuleBuilder2ConditionCatalog.GetConditionText(condition, card.Target.ResolveWorkType());
            Widgets.Label(row.Label, text);
            DrawConditionInlineEditor(row.Editor, card, condition);

            if (Widgets.ButtonText(row.Up, "^") && index > 0)
            {
                card.Conditions.Conditions.RemoveAt(index);
                card.Conditions.Conditions.Insert(index - 1, condition);
            }

            if (Widgets.ButtonText(row.Down, "v") && index < card.Conditions.Conditions.Count - 1)
            {
                card.Conditions.Conditions.RemoveAt(index);
                card.Conditions.Conditions.Insert(index + 1, condition);
            }

            if (Widgets.ButtonText(row.Remove, "X"))
            {
                card.Conditions.Conditions.Remove(condition);
            }
        }

        private void DrawConditionInlineEditor(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition)
        {
            switch (condition.Kind)
            {
                case RuleBuilder2ConditionKind.SkillMinimum:
                case RuleBuilder2ConditionKind.SkillMaximum:
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 110f, rect.height), GetSkillButtonLabel(condition, card.Target.ResolveWorkType())))
                    {
                        ShowSkillMenu(condition, card.Target.ResolveWorkType());
                    }
                    DrawIntStepper(new Rect(rect.x + 118f, rect.y, 98f, rect.height), ref condition.IntValue, 0, 99);
                    break;
                case RuleBuilder2ConditionKind.Trait:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 180f, rect.height), condition.DefName.NullOrEmpty() ? T("BWT_SelectTrait") : condition.DefName))
                    {
                        ShowDefMenu<TraitDef>(condition);
                    }
                    break;
                case RuleBuilder2ConditionKind.Xenotype:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 180f, rect.height), condition.DefName.NullOrEmpty() ? T("BWT_RuleBuilder2_XenotypeFallback") : condition.DefName))
                    {
                        ShowDefMenu<XenotypeDef>(condition);
                    }
                    break;
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 150f, rect.height), condition.DefName.NullOrEmpty() ? T("BWT_RuleBuilder2_CapacityFallback") : condition.DefName))
                    {
                        ShowDefMenu<PawnCapacityDef>(condition);
                    }
                    DrawFloatStepper(new Rect(rect.x + 158f, rect.y, 110f, rect.height), ref condition.FloatValue, 0f, 2f);
                    break;
                case RuleBuilder2ConditionKind.Gender:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 110f, rect.height), condition.TextValue.NullOrEmpty() ? T("BWT_RuleBuilder2_AnyGender") : condition.TextValue))
                    {
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption(T("BWT_RuleBuilder2_GenderMale"), () => condition.TextValue = Gender.Male.ToString()),
                            new FloatMenuOption(T("BWT_RuleBuilder2_GenderFemale"), () => condition.TextValue = Gender.Female.ToString())
                        }));
                    }
                    break;
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                    Widgets.CheckboxLabeled(new Rect(rect.x, rect.y, 170f, rect.height), T("BWT_RuleBuilder2_Assigned"), ref condition.BoolValue);
                    break;
                default:
                    DrawIntStepper(new Rect(rect.x, rect.y, 98f, rect.height), ref condition.IntValue, 0, WorkPrioritySystem.GetRequestableMaxPriority());
                    break;
            }
        }

        private void DrawConditionPicker(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.45f));
            Widgets.DrawBox(rect, 1);

            float y = rect.y + 6f;
            bool showAdvanced = BetterWorkTabMod.Settings?.ruleBuilder2ShowAdvancedConditions ?? false;
            foreach (var group in RuleBuilder2ConditionCatalog.Grouped(showAdvanced, conditionSearch))
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(rect.x + 8f, y, rect.width - 16f, 22f), group.Key);
                GUI.color = Color.white;
                y += 24f;

                foreach (var def in group)
                {
                    Rect button = new Rect(rect.x + 8f, y, rect.width - 16f, 24f);
                    if (Widgets.ButtonText(button, def.Label))
                    {
                        RuleBuilder2Condition condition = def.Create();
                        card.Conditions.Conditions.Add(condition);
                        tutorial.ObserveConditionAdded();
                    }

                    if (!def.Tooltip.NullOrEmpty())
                    {
                        TooltipHandler.TipRegion(button, def.Tooltip);
                    }

                    y += 28f;
                    if (y > rect.yMax - 24f)
                    {
                        return;
                    }
                }
            }
        }

        private void DrawActionSection(Rect rect, RuleBuilder2Card card)
        {
            if (activeSection != RuleBuilder2EditorSection.Action)
            {
                DrawCollapsedSection(rect, RuleBuilder2EditorSection.Action, T("BWT_RuleBuilder2_StepAction"), BuildActionSummary(card));
                return;
            }

            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            RuleBuilder2ActionSectionRects action = layout.ActionSection(rect);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepAction"));
            tutorialRects[RuleBuilder2TutorialStep.Action] = outerRect;

            if (Widgets.ButtonText(action.Kind, GetActionKindLabel(card.Action.Kind)))
            {
                ShowActionKindMenu(card);
            }

            DrawIntTextEntry(action.Priority, card.Action, 0, WorkPrioritySystem.GetRequestableMaxPriority());
            if (Widgets.ButtonText(action.Next, T("BWT_RuleBuilder2_NextPreview")))
            {
                showPreview = true;
                activeSection = RuleBuilder2EditorSection.Preview;
                RefreshPreview();
                tutorial.ObservePreview();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            if (card.Action.Kind == RuleBuilder2ActionKind.SetTimeSchedule ||
                card.Action.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule)
            {
                tutorialRects[RuleBuilder2TutorialStep.Schedule] = action.Body;
                card.Action.EnsureSchedule(card.Action.Priority);
                DrawSchedule(action.Body, card.Action);
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(action.Body, T("BWT_RuleBuilder2_ScheduleHint"));
                GUI.color = Color.white;
            }
            GUI.EndGroup();
        }

        private void DrawSchedule(Rect rect, RuleBuilder2Action action)
        {
            float cellWidth = rect.width / 24f;
            for (int hour = 0; hour < 24; hour++)
            {
                Rect label = new Rect(rect.x + hour * cellWidth, rect.y, cellWidth, 18f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(label, hour.ToString());

                Rect cell = new Rect(label.x + 1f, rect.y + 22f, cellWidth - 2f, 34f);
                int priority = action.HourlyPriorities[hour];
                Widgets.DrawBoxSolid(cell, WorkPrioritySystem.GetPriorityColor(priority));
                Widgets.DrawBox(cell, 1);
                GUI.color = Color.white;
                Widgets.Label(cell, priority <= 0 ? "X" : priority.ToString());

                if (Mouse.IsOver(cell) && Event.current.type == EventType.MouseDown)
                {
                    int dir = Event.current.button == 1 ? 1 : -1;
                    action.HourlyPriorities[hour] = WorkPrioritySystem.GetPriorityAfterBoundedStep(priority, dir);
                    Event.current.Use();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawPreviewSection(Rect rect, RuleBuilder2Card card)
        {
            if (activeSection != RuleBuilder2EditorSection.Preview)
            {
                DrawCollapsedSection(rect, RuleBuilder2EditorSection.Preview, T("BWT_RuleBuilder2_StepPreview"), BuildPreviewSummary(card));
                return;
            }

            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            RuleBuilder2PreviewSectionRects previewRects = layout.PreviewSection(rect, showMatchedPanel && selectedWorkTabContext.HasValue);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepPreview"));
            tutorialRects[RuleBuilder2TutorialStep.Preview] = outerRect;

            if (Widgets.ButtonText(previewRects.Run, T("BWT_RuleBuilder2_RunPreview")))
            {
                showPreview = true;
                RefreshPreview();
                tutorial.ObservePreview();
            }

            if (showMatchedPanel && selectedWorkTabContext.HasValue)
            {
                DrawMatchedPanel(previewRects.Matched, selectedWorkTabContext.Value);
            }

            if (!showPreview)
            {
                GUI.color = Color.gray;
                Widgets.Label(previewRects.Hint, T("BWT_RuleBuilder2_PreviewHint"));
                GUI.color = Color.white;
                GUI.EndGroup();
                return;
            }

            DrawPreviewRows(previewRects.List, card);
            GUI.EndGroup();
        }

        private void DrawMatchedPanel(Rect rect, RuleBuilder2WorkTabSelection selection)
        {
            tutorialRects[RuleBuilder2TutorialStep.MatchedConditions] = rect;
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.16f, 0.18f, 0.95f));
            Widgets.DrawBox(rect, 1);
            string label = selection.Pawn?.LabelShortCap ?? T("BWT_RuleBuilder2_PawnFallback");
            string target = BuildTargetLabel(selection.WorkType, selection.WorkGiver);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 22f), T("BWT_RuleBuilder2_MatchedConditions") + ": " + label + " / " + target);

            var card = activeCard;
            if (card != null && selection.Pawn != null)
            {
                var result = evaluator.PreviewPawn(card, selection.Pawn, selection.WorkType, selection.WorkGiver);
                string why = result.Matched
                    ? T("BWT_RuleBuilder2_AppliesNewPriority").Formatted(result.NewPriority).ToString()
                    : T("BWT_RuleBuilder2_DoesNotApplyFailed").Formatted(string.Join(", ", result.ConditionsFailed.Take(2).ToArray())).ToString();
                GUI.color = result.Matched ? Color.green : Color.yellow;
                Widgets.Label(new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, 32f), why);
                GUI.color = Color.white;
            }
        }

        private void DrawPreviewRows(Rect rect, RuleBuilder2Card card)
        {
            RefreshPreview();
            var rows = preview.Where(result => result.Target == card.Target || card.Target?.WorkTypeDefName == result.Target?.WorkTypeDefName).ToList();
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, rows.Count * 38f));
            Widgets.BeginScrollView(rect, ref previewScroll, view);
            float y = 0f;
            foreach (RuleBuilder2PreviewResult result in rows)
            {
                Rect row = new Rect(0f, y, view.width, 34f);
                Widgets.DrawBoxSolid(row, result.Matched ? new Color(0.12f, 0.22f, 0.12f, 0.95f) : new Color(0.16f, 0.14f, 0.12f, 0.95f));
                Widgets.DrawBox(row, 1);
                Widgets.Label(new Rect(row.x + 8f, row.y + 5f, 150f, 24f), result.PawnLabel);
                Widgets.Label(new Rect(row.x + 164f, row.y + 5f, 110f, 24f), result.Matched ? T("BWT_RuleBuilder2_Matched") : T("BWT_RuleBuilder2_NotMatched"));
                Widgets.Label(new Rect(row.x + 280f, row.y + 5f, 190f, 24f), T("BWT_RuleBuilder2_CurrentToNew").Formatted(result.CurrentPriority, result.NewPriority).ToString());
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + 476f, row.y + 5f, row.width - 484f, 24f), result.Matched ? result.ActionText : string.Join(", ", result.ConditionsFailed.Take(2).ToArray()));
                GUI.color = Color.white;
                y += 38f;
            }
            Widgets.EndScrollView();
        }

        private void DrawConfirmSection(Rect rect, RuleBuilder2Card card)
        {
            RuleBuilder2ConfirmRects confirm = layout.Confirm(rect);
            tutorialRects[RuleBuilder2TutorialStep.Confirm] = rect;
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.09f, 0.09f, 0.55f));
            if (Widgets.ButtonText(confirm.Confirm, T("BWT_RuleBuilder2_ConfirmCard")))
            {
                ConfirmCard(card);
            }

            if (Widgets.ButtonText(confirm.Done, T("BWT_Done")))
            {
                Close();
            }
        }

        private void ConfirmCard(RuleBuilder2Card card)
        {
            card.IsConfirmed = true;
            card.IsCollapsed = true;
            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            if (string.IsNullOrEmpty(card.Name) || card.Name == T("BWT_RuleBuilder2_NewRule"))
            {
                card.Name = card.Target.DisplayLabel.NullOrEmpty() ? T("BWT_RuleBuilder2_RuleCard") : card.Target.DisplayLabel;
            }

            ruleset.EnsureOpenBlankCard();
            activeCard = ruleset.Cards.FirstOrDefault(c => !c.IsConfirmed) ?? ruleset.Cards.LastOrDefault();
            activeSection = activeCard?.Target?.HasTarget == true
                ? RuleBuilder2EditorSection.Conditions
                : RuleBuilder2EditorSection.Target;
            showPreview = false;
            tutorial.ObserveConfirmed();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void ApplyRuleset()
        {
            RefreshPreview();
            int changed = applyService.Apply(ruleset, out List<string> warnings);
            applyStatus = warnings.Count > 0
                ? T("BWT_RuleBuilder2_ApplyStatusWarnings").Formatted(changed, warnings.Count).ToString()
                : T("BWT_RuleBuilder2_ApplyStatus").Formatted(changed).ToString();
            if (warnings.Count > 0)
            {
                Log.Warning("[BWT] Rule Builder 2.0 apply warnings:\n" + string.Join("\n", warnings.ToArray()));
            }
        }

        private void ExportClassicCompatibleRuleset()
        {
            RuleBuilder2ClassicRulesetExportResult result = classicRulesetExportService.Export(ruleset, BetterWorkTabMod.Settings);
            if (!result.Exported)
            {
                applyStatus = T("BWT_RuleBuilder2_ExportClassicNoRules");
                if (result.Warnings.Count > 0)
                {
                    Log.Warning("[BWT] Rule Builder 2.0 classic export warnings:\n" + string.Join("\n", result.Warnings.ToArray()));
                }

                return;
            }

            applyStatus = result.Warnings.Count > 0
                ? T("BWT_RuleBuilder2_ExportClassicStatusWarnings").Formatted(result.RuleCount, result.Warnings.Count).ToString()
                : T("BWT_RuleBuilder2_ExportClassicStatus").Formatted(result.RuleCount).ToString();

            if (result.Warnings.Count > 0)
            {
                Log.Warning("[BWT] Rule Builder 2.0 classic export warnings:\n" + string.Join("\n", result.Warnings.ToArray()));
            }
        }

        internal void ConfirmActiveCardForSmokeTest()
        {
            if (activeCard != null && !activeCard.IsConfirmed)
            {
                ConfirmCard(activeCard);
            }
        }

        internal void ShowDashboardForSmokeTest()
        {
            ShowDashboardSurface();
        }

        internal void GenerateDraftForSmokeTest()
        {
            ruleset = draftGenerator.GenerateFromCurrentWorkTab();
            BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
            activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
            activeSection = activeCard?.Target?.HasTarget == true
                ? RuleBuilder2EditorSection.Conditions
                : RuleBuilder2EditorSection.Target;
            showPreview = false;
            RefreshPreview();
            ShowDashboardSurface();
        }

        internal void RefreshPreviewForSmokeTest()
        {
            ShowRuleCardSurface();
            showPreview = true;
            activeSection = RuleBuilder2EditorSection.Preview;
            RefreshPreview();
        }

        internal void ScrollToPreviewForSmokeTest()
        {
            showPreview = true;
            activeSection = RuleBuilder2EditorSection.Preview;
            RefreshPreview();
            editorScroll.y = 610f;
        }

        private void RefreshPreview()
        {
            preview = evaluator.Preview(ruleset, activeCard);
        }

        private RuleBuilder2Card GetEditableCard()
        {
            if (activeCard != null && ruleset.Cards.Contains(activeCard))
            {
                return activeCard;
            }

            ruleset.EnsureOpenBlankCard();
            activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.Last();
            return activeCard;
        }

        private RuleBuilder2Ruleset CreateAndStoreBlankRuleset()
        {
            var created = new RuleBuilder2Ruleset
            {
                Name = T("BWT_RuleBuilder2_NewRulesetName"),
                Description = T("BWT_RuleBuilder2_NewRulesetDescription"),
                Source = RuleBuilder2SourceType.Blank
            };
            created.EnsureOpenBlankCard();
            BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(created, makeCurrent: true, writeSettings: false);
            return created;
        }

        private void ImportClassicRuleset()
        {
            var options = new List<FloatMenuOption>();
            foreach (WorkAssignmentRuleset classicRuleset in BetterWorkTabMod.Settings.SavedRulesets ?? new List<WorkAssignmentRuleset>())
            {
                WorkAssignmentRuleset local = classicRuleset;
                options.Add(new FloatMenuOption(local.Name, () =>
                {
                    ruleset = RuleBuilder2ClassicRulesetTranslator.FromClassic(local);
                    BetterWorkTabMod.Settings.SaveOrReplaceRuleBuilder2Ruleset(ruleset, makeCurrent: true, writeSettings: false);
                    activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
                    activeSection = activeCard?.Target?.HasTarget == true ? RuleBuilder2EditorSection.Conditions : RuleBuilder2EditorSection.Target;
                    ShowDashboardSurface();
                }));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_NoClassicRules"), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SelectTarget(RuleBuilder2Card card, WorkTypeDef workType, WorkGiverDef workGiver, RuleBuilder2TargetSource source)
        {
            card.Target.WorkTypeDefName = workType?.defName ?? "";
            card.Target.WorkGiverDefName = workGiver?.defName ?? "";
            card.Target.DisplayLabel = BuildTargetLabel(workType, workGiver);
            card.Target.Source = source;
            tutorial.ObserveTargetSelected(workGiver != null);
            RefreshPreview();
        }

        private static string BuildTargetLabel(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            string work = GetWorkTypeLabel(workType);
            return workGiver == null ? work : work + " -> " + workGiver.LabelCap;
        }

        private static string GetWorkTypeLabel(WorkTypeDef workType)
        {
            if (workType == null)
            {
                return T("BWT_RuleBuilder2_WorkFallback");
            }

            string label = workType.LabelCap.ToString();
            return label.NullOrEmpty() ? workType.defName : label;
        }

        private static void DrawFittedLabel(Rect rect, string label)
        {
            label = label ?? "";
            string fitted = TruncateToWidth(label, rect.width - 4f);
            bool previousWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(rect, fitted);
            Text.WordWrap = previousWordWrap;
            if (fitted != label)
            {
                TooltipHandler.TipRegion(rect, label);
            }
        }

        private void DrawDashboardButton(Rect rect, string label, System.Action action)
        {
            string fitted = TruncateToWidth(label, rect.width - 12f);
            if (Widgets.ButtonText(rect, fitted))
            {
                action?.Invoke();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            if (fitted != label)
            {
                TooltipHandler.TipRegion(rect, label);
            }
        }

        private static void DrawSectionChrome(Rect rect, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.96f));
            Widgets.DrawBox(rect, 1);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.82f, 0.55f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 26f), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawCollapsedSection(Rect rect, RuleBuilder2EditorSection section, string title, string summary)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.96f));
            Widgets.DrawBox(rect, 1);

            Rect iconRect = new Rect(rect.x + 8f, rect.y + 8f, 26f, 26f);
            Widgets.DrawBoxSolid(iconRect, new Color(0.18f, 0.16f, 0.1f, 1f));
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.9f, 0.82f, 0.55f);
            Widgets.Label(iconRect, ">");
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 42f, rect.y + 4f, 190f, 36f), title);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 230f, rect.y + 4f, rect.width - 240f, 36f), summary);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect))
            {
                activeSection = section;
                if (section == RuleBuilder2EditorSection.Preview)
                {
                    showPreview = true;
                    RefreshPreview();
                }

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        private string BuildTargetSummary(RuleBuilder2Card card)
        {
            if (card?.Target?.HasTarget != true)
            {
                return T("BWT_RuleBuilder2_NoTargetSummary");
            }

            string label = BuildTargetLabel(card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
            return label == T("BWT_RuleBuilder2_WorkFallback") && !card.Target.DisplayLabel.NullOrEmpty()
                ? card.Target.DisplayLabel
                : label;
        }

        private string BuildConditionsSummary(RuleBuilder2Card card)
        {
            int count = card?.Conditions?.Conditions?.Count(condition => condition != null && condition.Enabled) ?? 0;
            if (count <= 0)
            {
                return T("BWT_RuleBuilder2_NoConditionsSummary");
            }

            string first = RuleBuilder2ConditionCatalog.GetConditionText(
                card.Conditions.Conditions.FirstOrDefault(condition => condition != null && condition.Enabled),
                card.Target.ResolveWorkType());
            return count == 1
                ? first
                : T("BWT_RuleBuilder2_ConditionsSummary").Formatted(count, first).ToString();
        }

        private string BuildActionSummary(RuleBuilder2Card card)
        {
            if (card?.Action == null)
            {
                return T("BWT_RuleBuilder2_NoActionSummary");
            }

            string target = BuildTargetSummary(card);
            return RuleBuilder2Evaluator.GetActionText(card, card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
        }

        private string BuildPreviewSummary(RuleBuilder2Card card)
        {
            if (!showPreview)
            {
                return T("BWT_RuleBuilder2_PreviewNotRunSummary");
            }

            int matched = preview.Count(result => result.Target == card.Target && result.Matched);
            int total = preview.Count(result => result.Target == card.Target);
            return T("BWT_RuleBuilder2_PreviewSummary").Formatted(matched, total).ToString();
        }

        private static void DrawIntStepper(Rect rect, ref int value, int min, int max)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, 24f, rect.height), "-"))
            {
                value = Mathf.Clamp(value - 1, min, max);
            }
            Widgets.Label(new Rect(rect.x + 28f, rect.y, 42f, rect.height), value.ToString());
            if (Widgets.ButtonText(new Rect(rect.x + 72f, rect.y, 24f, rect.height), "+"))
            {
                value = Mathf.Clamp(value + 1, min, max);
            }
        }

        private static void DrawFloatStepper(Rect rect, ref float value, float min, float max)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, 24f, rect.height), "-"))
            {
                value = Mathf.Clamp(value - 0.05f, min, max);
            }
            Widgets.Label(new Rect(rect.x + 28f, rect.y, 48f, rect.height), value.ToString("0.##"));
            if (Widgets.ButtonText(new Rect(rect.x + 80f, rect.y, 24f, rect.height), "+"))
            {
                value = Mathf.Clamp(value + 0.05f, min, max);
            }
        }

        private static void DrawIntTextEntry(Rect rect, RuleBuilder2Action action, int min, int max)
        {
            action.PriorityBuffer = string.IsNullOrEmpty(action.PriorityBuffer)
                ? action.Priority.ToString()
                : action.PriorityBuffer;
            int priority = action.Priority;
            Widgets.TextFieldNumeric(rect, ref priority, ref action.PriorityBuffer, min, max);
            action.Priority = WorkPrioritySystem.ClampPriority(priority);
        }

        private string GetSkillButtonLabel(RuleBuilder2Condition condition, WorkTypeDef workType)
        {
            SkillDef skill = condition.DefName.NullOrEmpty()
                ? workType?.relevantSkills?.FirstOrDefault()
                : DefDatabase<SkillDef>.GetNamedSilentFail(condition.DefName);
            return skill?.LabelCap.ToString() ?? T("BWT_RuleBuilder2_SkillFallback");
        }

        private void ShowSkillMenu(RuleBuilder2Condition condition, WorkTypeDef workType)
        {
            var options = RuleBuilder2ConditionCatalog.GetSkillOptions(workType)
                .Select(skill => new FloatMenuOption(skill.LabelCap.ToString(), () => condition.DefName = skill.defName))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowDefMenu<T>(RuleBuilder2Condition condition) where T : Def
        {
            var options = DefDatabase<T>.AllDefsListForReading
                .OrderBy(def => def.label)
                .Take(80)
                .Select(def => new FloatMenuOption(def.LabelCap.ToString(), () => condition.DefName = def.defName))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string GetActionKindLabel(RuleBuilder2ActionKind kind)
        {
            switch (kind)
            {
                case RuleBuilder2ActionKind.Disable:
                    return T("BWT_RuleBuilder2_ActionDisable");
                case RuleBuilder2ActionKind.FollowGlobal:
                    return T("BWT_RuleBuilder2_ActionFollowGlobal");
                case RuleBuilder2ActionKind.SetTimeSchedule:
                    return T("BWT_RuleBuilder2_ActionSetTimeSchedule");
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                    return T("BWT_RuleBuilder2_ActionSetSubWorkSchedule");
                default:
                    return T("BWT_RuleBuilder2_ActionSetPriority");
            }
        }

        private void ShowActionKindMenu(RuleBuilder2Card card)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(T("BWT_RuleBuilder2_ActionSetPriority"), () => SetActionKind(card, RuleBuilder2ActionKind.SetPriority)),
                new FloatMenuOption(T("BWT_RuleBuilder2_ActionDisable"), () => SetActionKind(card, RuleBuilder2ActionKind.Disable)),
                new FloatMenuOption(T("BWT_RuleBuilder2_ActionFollowGlobal"), () => SetActionKind(card, RuleBuilder2ActionKind.FollowGlobal)),
                new FloatMenuOption(T("BWT_RuleBuilder2_ActionSetTimeSchedule"), () => SetActionKind(card, RuleBuilder2ActionKind.SetTimeSchedule)),
                new FloatMenuOption(T("BWT_RuleBuilder2_ActionSetSubWorkSchedule"), () => SetActionKind(card, RuleBuilder2ActionKind.SetSubWorkSchedule))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SetActionKind(RuleBuilder2Card card, RuleBuilder2ActionKind kind)
        {
            card.Action.Kind = kind;
            card.Action.EnsureSchedule(card.Action.Priority);
            tutorial.ObserveActionEdited();
            RefreshPreview();
        }

        private static string TruncateToWidth(string text, float width)
        {
            if (text.NullOrEmpty() || width <= 12f || Text.CalcSize(text).x <= width)
            {
                return text ?? "";
            }

            const string ellipsis = "...";
            for (int length = text.Length; length > 0; length--)
            {
                string candidate = text.Substring(0, length).TrimEnd() + ellipsis;
                if (Text.CalcSize(candidate).x <= width)
                {
                    return candidate;
                }
            }

            return ellipsis;
        }

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }
    }
}
