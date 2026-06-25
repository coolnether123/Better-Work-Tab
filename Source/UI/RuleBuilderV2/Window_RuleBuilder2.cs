using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
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

    public sealed class Window_RuleBuilder2 : Window
    {
        private readonly RuleBuilder2Evaluator evaluator = new RuleBuilder2Evaluator();
        private readonly RuleBuilder2ApplyService applyService = new RuleBuilder2ApplyService();
        private readonly RuleBuilder2DraftGenerator draftGenerator = new RuleBuilder2DraftGenerator();
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
            Mathf.Min(1180f, Verse.UI.screenWidth - 40f),
            Mathf.Min(760f, Verse.UI.screenHeight - 40f));

        public override void PreOpen()
        {
            base.PreOpen();
            BetterWorkTabMod.Settings.EnsureRuleBuilder2Rulesets();
            if (seededRuleset != null)
            {
                ruleset = seededRuleset;
                if (!BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Contains(ruleset))
                {
                    BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Insert(0, ruleset);
                }
            }
            else
            {
                ruleset = BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.FirstOrDefault()
                          ?? CreateAndStoreBlankRuleset();
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
                showPreview = true;
                RefreshPreview();
            }
        }

        public override void PostClose()
        {
            RuleBuilder2WorkTabBridge.Unregister(this);
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UpdateSectionAnimation();
            tutorialRects.Clear();
            if (Event.current.type != EventType.Repaint && tutorial.TryHandleInput(inRect, tutorialRects, Event.current))
            {
                return;
            }

            DrawHeader(new Rect(inRect.x, inRect.y, inRect.width, 46f));

            Rect body = new Rect(inRect.x, inRect.y + 54f, inRect.width, inRect.height - 54f);
            Rect dashboard = new Rect(body.x, body.y, 330f, body.height);
            Rect editor = new Rect(dashboard.xMax + 10f, body.y, body.width - dashboard.width - 10f, body.height);

            DrawDashboard(dashboard);
            DrawEditor(editor);
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
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 12f, rect.y, 220f, rect.height), T("BWT_RuleBuilder2_Title"));

            Text.Font = GameFont.Small;
            Rect nameRect = new Rect(rect.x + 245f, rect.y + 9f, 270f, 28f);
            ruleset.Name = Widgets.TextField(nameRect, ruleset.Name ?? "");

            Rect enabledRect = new Rect(nameRect.xMax + 12f, rect.y + 10f, 110f, 24f);
            Widgets.CheckboxLabeled(enabledRect, T("BWT_RuleBuilder2_Enabled"), ref ruleset.Enabled);

            Rect doneRect = new Rect(rect.xMax - 90f, rect.y + 8f, 80f, 30f);
            if (Widgets.ButtonText(doneRect, T("BWT_Done")))
            {
                Close();
            }

            Rect applyRect = new Rect(doneRect.x - 92f, doneRect.y, 84f, 30f);
            if (Widgets.ButtonText(applyRect, T("BWT_Apply")))
            {
                ApplyRuleset();
            }

            Rect previewRect = new Rect(applyRect.x - 92f, doneRect.y, 84f, 30f);
            if (Widgets.ButtonText(previewRect, T("BWT_RuleBuilder2_Preview")))
            {
                showPreview = true;
                activeSection = RuleBuilder2EditorSection.Preview;
                RefreshPreview();
                tutorial.ObservePreview();
            }

            if (!string.IsNullOrEmpty(applyStatus))
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(previewRect.x - 280f, rect.y + 12f, 270f, 24f), applyStatus);
                GUI.color = Color.white;
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawDashboard(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            tutorialRects[RuleBuilder2TutorialStep.BlankRuleset] = rect;

            Rect inner = rect.ContractedBy(10f);
            float y = inner.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), T("BWT_RuleBuilder2_Dashboard"));
            Text.Font = GameFont.Small;
            y += 34f;

            DrawDashboardButton(ref y, inner, T("BWT_RuleBuilder2_CreateBlank"), () =>
            {
                ruleset = CreateAndStoreBlankRuleset();
                activeCard = ruleset.Cards[0];
                preview.Clear();
            });
            DrawDashboardButton(ref y, inner, T("BWT_RuleBuilder2_Duplicate"), () =>
            {
                var copy = ruleset.Copy();
                BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Add(copy);
                ruleset = copy;
                activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
            });
            DrawDashboardButton(ref y, inner, T("BWT_RuleBuilder2_CopyDefault"), () =>
            {
                WorkAssignmentRuleset legacyDefault = BetterWorkTabMod.Settings.SavedRulesets?.FirstOrDefault(rs => rs?.IsDefault == true);
                if (legacyDefault != null)
                {
                    ruleset = RuleBuilder2MigrationService.FromLegacy(legacyDefault);
                    ruleset.Source = RuleBuilder2SourceType.DefaultCopy;
                    BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Add(ruleset);
                    activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
                }
            });
            DrawDashboardButton(ref y, inner, T("BWT_RuleBuilder2_GenerateDraft"), () =>
            {
                ruleset = draftGenerator.GenerateFromCurrentWorkTab();
                BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Add(ruleset);
                activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
            });
            DrawDashboardButton(ref y, inner, T("BWT_RuleBuilder2_ImportClassic"), ImportClassicRuleset);
            DrawDashboardButton(ref y, inner, T("BWT_RuleBuilder2_OpenClassic"), () => Find.WindowStack.Add(new Better_Work_Tab.UI.RuleBuilder.Window_RulesetBuilder()));

            y += 8f;
            Rect addRect = new Rect(inner.x, y, inner.width, 30f);
            if (Widgets.ButtonText(addRect, "+ " + T("BWT_RuleBuilder2_AddRule")))
            {
                activeCard = RuleBuilder2Card.CreateBlank(ruleset.Cards.Count);
                ruleset.Cards.Add(activeCard);
                activeSection = RuleBuilder2EditorSection.Target;
                showPreview = false;
            }
            y += 38f;

            Rect listRect = new Rect(inner.x, y, inner.width, inner.yMax - y);
            DrawRuleCardList(listRect);
        }

        private void DrawRuleCardList(Rect rect)
        {
            float height = Mathf.Max(rect.height, ruleset.Cards.Count * 78f + 12f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref cardListScroll, viewRect);
            float y = 0f;

            foreach (RuleBuilder2Card card in ruleset.Cards.OrderBy(card => card.SortOrder))
            {
                Rect cardRect = new Rect(0f, y, viewRect.width, 68f);
                DrawSummaryCard(cardRect, card);
                y += 76f;
            }

            Widgets.EndScrollView();
        }

        private void DrawSummaryCard(Rect rect, RuleBuilder2Card card)
        {
            Color bg = card == activeCard ? new Color(0.24f, 0.28f, 0.22f, 0.98f) : new Color(0.16f, 0.16f, 0.16f, 0.98f);
            Widgets.DrawBoxSolid(rect, bg);
            Widgets.DrawBox(rect, 1);

            Rect enabledRect = new Rect(rect.x + 6f, rect.y + 8f, 24f, 24f);
            Widgets.Checkbox(enabledRect.position, ref card.Enabled);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(rect.x + 34f, rect.y + 7f, rect.width - 74f, 22f), card.Name.NullOrEmpty() ? T("BWT_RuleBuilder2_NewRule") : card.Name);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 34f, rect.y + 31f, rect.width - 42f, 30f), card.IsConfirmed ? card.Summary : T("BWT_RuleBuilder2_Unconfirmed"));
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(rect))
            {
                activeCard = card;
                card.IsCollapsed = false;
                activeSection = card.Target.HasTarget ? RuleBuilder2EditorSection.Conditions : RuleBuilder2EditorSection.Target;
                showPreview = false;
            }

            Rect deleteRect = new Rect(rect.xMax - 30f, rect.y + 8f, 22f, 22f);
            if (Widgets.ButtonText(deleteRect, "X"))
            {
                ruleset.Cards.Remove(card);
                activeCard = ruleset.Cards.FirstOrDefault(c => !c.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
            }
        }

        private void DrawEditor(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            RuleBuilder2Card card = GetEditableCard();
            activeCard = card;

            float targetHeight = SectionHeight(RuleBuilder2EditorSection.Target, GetTargetSectionHeight(card));
            float conditionsHeight = SectionHeight(RuleBuilder2EditorSection.Conditions, 265f);
            float actionHeight = SectionHeight(RuleBuilder2EditorSection.Action, 210f);
            float previewHeight = SectionHeight(RuleBuilder2EditorSection.Preview, 260f);
            float viewHeight = 44f + targetHeight + conditionsHeight + actionHeight + previewHeight + 70f + 54f;
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, viewHeight));
            Widgets.BeginScrollView(inner, ref editorScroll, view);
            float y = 0f;

            DrawRuleName(new Rect(0f, y, view.width, 36f), card);
            y += 44f;

            DrawTargetSection(new Rect(0f, y, view.width, targetHeight), card);
            y += targetHeight + 8f;

            DrawConditionsSection(new Rect(0f, y, view.width, conditionsHeight), card);
            y += conditionsHeight + 8f;

            DrawActionSection(new Rect(0f, y, view.width, actionHeight), card);
            y += actionHeight + 8f;

            DrawPreviewSection(new Rect(0f, y, view.width, previewHeight), card);
            y += previewHeight + 8f;

            DrawConfirmSection(new Rect(0f, y, view.width, 70f), card);

            Widgets.EndScrollView();
        }

        private float SectionHeight(RuleBuilder2EditorSection section, float expandedHeight)
        {
            float progress = GetSectionProgress(section);
            float eased = progress * progress * (3f - 2f * progress);
            return Mathf.Lerp(44f, expandedHeight, eased);
        }

        private void UpdateSectionAnimation()
        {
            float step = (BetterWorkTabMod.Settings?.ruleBuilder2EnableAnimations ?? true)
                ? Mathf.Clamp01(Time.unscaledDeltaTime / 0.18f)
                : 1f;

            foreach (RuleBuilder2EditorSection section in System.Enum.GetValues(typeof(RuleBuilder2EditorSection)))
            {
                float target = section == activeSection ? 1f : 0f;
                sectionOpenProgress[section] = Mathf.MoveTowards(GetSectionProgress(section), target, step);
            }
        }

        private float GetSectionProgress(RuleBuilder2EditorSection section)
        {
            return sectionOpenProgress.TryGetValue(section, out float progress) ? progress : 0f;
        }

        private void DrawRuleName(Rect rect, RuleBuilder2Card card)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, 160f, rect.height), T("BWT_RuleBuilder2_RuleCard"));
            Text.Font = GameFont.Small;
            card.Name = Widgets.TextField(new Rect(rect.x + 170f, rect.y + 3f, 320f, 28f), card.Name ?? "");
        }

        private float GetTargetSectionHeight(RuleBuilder2Card card)
        {
            return card.Target.HasTarget ? 118f : 238f;
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
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepTarget"));
            tutorialRects[RuleBuilder2TutorialStep.Target] = outerRect;

            Rect inner = rect.ContractedBy(10f);
            float y = inner.y + 28f;
            if (card.Target.HasTarget)
            {
                string label = BuildTargetLabel(card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
                if (label == T("BWT_RuleBuilder2_WorkFallback") && !card.Target.DisplayLabel.NullOrEmpty())
                {
                    label = card.Target.DisplayLabel;
                }
                Widgets.Label(new Rect(inner.x, y, inner.width - 120f, 26f), T("BWT_RuleBuilder2_TargetSummary").Formatted(label).ToString());
                if (Widgets.ButtonText(new Rect(inner.xMax - 110f, y - 2f, 100f, 28f), T("BWT_Change")))
                {
                    card.Target.WorkTypeDefName = "";
                    card.Target.WorkGiverDefName = "";
                    activeSection = RuleBuilder2EditorSection.Target;
                }

                y += 34f;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inner.x, y, inner.width - 150f, 42f), T("BWT_RuleBuilder2_WorkTabHint"));
                GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(inner.xMax - 140f, y + 2f, 130f, 28f), T("BWT_RuleBuilder2_NextConditions")))
                {
                    activeSection = RuleBuilder2EditorSection.Conditions;
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
                GUI.EndGroup();
                return;
            }

            targetSearch = Widgets.TextField(new Rect(inner.x, y, inner.width, 28f), targetSearch ?? "");
            y += 34f;

            Rect listRect = new Rect(inner.x, y, inner.width, inner.yMax - y);
            DrawTargetList(listRect, card);
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

            float x = rect.x;
            float y = rect.y;
            float buttonWidth = Mathf.Max(130f, (rect.width - 16f) / 4f);
            foreach (WorkTypeDef workType in workTypes)
            {
                Rect button = new Rect(x, y, buttonWidth - 6f, 28f);
                string label = GetWorkTypeLabel(workType);
                if (Widgets.ButtonText(button, label))
                {
                    SelectTarget(card, workType, null, RuleBuilder2TargetSource.BuilderList);
                }

                x += buttonWidth;
                if (x + buttonWidth > rect.xMax)
                {
                    x = rect.x;
                    y += 32f;
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
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepConditions"));
            tutorialRects[RuleBuilder2TutorialStep.Condition] = outerRect;

            Rect inner = rect.ContractedBy(10f);
            float y = inner.y + 28f;
            conditionSearch = Widgets.TextField(new Rect(inner.x, y, inner.width, 28f), conditionSearch ?? "");
            if (Widgets.ButtonText(new Rect(inner.xMax - 120f, y, 120f, 28f), T("BWT_RuleBuilder2_NextAction")))
            {
                activeSection = RuleBuilder2EditorSection.Action;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            y += 36f;

            Rect activeRect = new Rect(inner.x, y, inner.width * 0.52f - 6f, inner.yMax - y);
            Rect pickerRect = new Rect(activeRect.xMax + 12f, y, inner.width * 0.48f - 6f, inner.yMax - y);

            DrawActiveConditions(activeRect, card);
            DrawConditionPicker(pickerRect, card);
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
                Rect row = new Rect(rect.x + 6f, y, rect.width - 12f, 52f);
                DrawConditionRow(row, card, condition, i);
                y += 58f;
            }
        }

        private void DrawConditionRow(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition, int index)
        {
            Widgets.DrawBoxSolid(rect, condition.Enabled ? new Color(0.18f, 0.18f, 0.18f, 0.95f) : new Color(0.11f, 0.11f, 0.11f, 0.95f));
            Widgets.DrawBox(rect, 1);

            Rect checkbox = new Rect(rect.x + 6f, rect.y + 7f, 24f, 24f);
            Widgets.Checkbox(checkbox.position, ref condition.Enabled);

            string text = RuleBuilder2ConditionCatalog.GetConditionText(condition, card.Target.ResolveWorkType());
            Widgets.Label(new Rect(rect.x + 34f, rect.y + 6f, rect.width - 128f, 22f), text);
            DrawConditionInlineEditor(new Rect(rect.x + 34f, rect.y + 28f, rect.width - 128f, 20f), card, condition);

            if (Widgets.ButtonText(new Rect(rect.xMax - 86f, rect.y + 6f, 24f, 22f), "^") && index > 0)
            {
                card.Conditions.Conditions.RemoveAt(index);
                card.Conditions.Conditions.Insert(index - 1, condition);
            }

            if (Widgets.ButtonText(new Rect(rect.xMax - 58f, rect.y + 6f, 24f, 22f), "v") && index < card.Conditions.Conditions.Count - 1)
            {
                card.Conditions.Conditions.RemoveAt(index);
                card.Conditions.Conditions.Insert(index + 1, condition);
            }

            if (Widgets.ButtonText(new Rect(rect.xMax - 30f, rect.y + 6f, 24f, 22f), "X"))
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
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepAction"));
            tutorialRects[RuleBuilder2TutorialStep.Action] = outerRect;

            Rect inner = rect.ContractedBy(10f);
            float y = inner.y + 30f;

            Rect kindRect = new Rect(inner.x, y, 210f, 28f);
            if (Widgets.ButtonText(kindRect, GetActionKindLabel(card.Action.Kind)))
            {
                ShowActionKindMenu(card);
            }

            Rect priorityRect = new Rect(kindRect.xMax + 14f, y, 150f, 28f);
            int max = WorkPrioritySystem.GetRequestableMaxPriority();
            DrawIntTextEntry(priorityRect, card.Action, 0, max);
            if (Widgets.ButtonText(new Rect(inner.xMax - 130f, y, 120f, 28f), T("BWT_RuleBuilder2_NextPreview")))
            {
                showPreview = true;
                activeSection = RuleBuilder2EditorSection.Preview;
                RefreshPreview();
                tutorial.ObservePreview();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            y += 38f;

            if (card.Action.Kind == RuleBuilder2ActionKind.SetTimeSchedule ||
                card.Action.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule)
            {
                tutorialRects[RuleBuilder2TutorialStep.Schedule] = new Rect(inner.x, y, inner.width, 72f);
                card.Action.EnsureSchedule(card.Action.Priority);
                DrawSchedule(new Rect(inner.x, y, inner.width, 72f), card.Action);
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inner.x, y, inner.width, 44f), T("BWT_RuleBuilder2_ScheduleHint"));
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
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_StepPreview"));
            tutorialRects[RuleBuilder2TutorialStep.Preview] = outerRect;

            Rect inner = rect.ContractedBy(10f);
            if (Widgets.ButtonText(new Rect(inner.x, inner.y + 28f, 120f, 28f), T("BWT_RuleBuilder2_RunPreview")))
            {
                showPreview = true;
                RefreshPreview();
                tutorial.ObservePreview();
            }

            if (showMatchedPanel && selectedWorkTabContext.HasValue)
            {
                DrawMatchedPanel(new Rect(inner.x + 130f, inner.y + 28f, inner.width - 130f, 72f), selectedWorkTabContext.Value);
            }

            if (!showPreview)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inner.x, inner.y + 66f, inner.width, 40f), T("BWT_RuleBuilder2_PreviewHint"));
                GUI.color = Color.white;
                GUI.EndGroup();
                return;
            }

            Rect list = new Rect(inner.x, inner.y + 104f, inner.width, inner.height - 108f);
            DrawPreviewRows(list, card);
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
            tutorialRects[RuleBuilder2TutorialStep.Confirm] = rect;
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.09f, 0.09f, 0.55f));
            Rect confirmRect = new Rect(rect.x, rect.y + 12f, 190f, 34f);
            if (Widgets.ButtonText(confirmRect, T("BWT_RuleBuilder2_ConfirmCard")))
            {
                ConfirmCard(card);
            }

            Rect doneRect = new Rect(confirmRect.xMax + 10f, confirmRect.y, 110f, 34f);
            if (Widgets.ButtonText(doneRect, T("BWT_Done")))
            {
                Close();
            }
        }

        private void ConfirmCard(RuleBuilder2Card card)
        {
            card.IsConfirmed = true;
            card.IsCollapsed = true;
            card.Summary = RuleBuilder2MigrationService.BuildSummary(card);
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

        internal void ConfirmActiveCardForSmokeTest()
        {
            if (activeCard != null && !activeCard.IsConfirmed)
            {
                ConfirmCard(activeCard);
            }
        }

        internal void RefreshPreviewForSmokeTest()
        {
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
            BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Add(created);
            return created;
        }

        private void ImportClassicRuleset()
        {
            var options = new List<FloatMenuOption>();
            foreach (WorkAssignmentRuleset legacy in BetterWorkTabMod.Settings.SavedRulesets ?? new List<WorkAssignmentRuleset>())
            {
                WorkAssignmentRuleset local = legacy;
                options.Add(new FloatMenuOption(local.Name, () =>
                {
                    ruleset = RuleBuilder2MigrationService.FromLegacy(local);
                    BetterWorkTabMod.Settings.SavedRuleBuilder2Rulesets.Add(ruleset);
                    activeCard = ruleset.Cards.FirstOrDefault(card => !card.IsConfirmed) ?? ruleset.Cards.FirstOrDefault();
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

        private void DrawDashboardButton(ref float y, Rect inner, string label, System.Action action)
        {
            Rect rect = new Rect(inner.x, y, inner.width, 28f);
            if (Widgets.ButtonText(rect, label))
            {
                action?.Invoke();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            y += 32f;
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

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }
    }
}
