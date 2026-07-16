using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2FlowController
    {
        private readonly RuleBuilder2Evaluator evaluator = new RuleBuilder2Evaluator();
        private readonly RuleBuilder2ApplyService applyService = new RuleBuilder2ApplyService();
        private readonly RuleBuilder2DraftGenerator draftGenerator = new RuleBuilder2DraftGenerator();
        private readonly RuleBuilder2ClassicRulesetExportService classicRulesetExportService = new RuleBuilder2ClassicRulesetExportService();
        private readonly RuleBuilder2TutorialController tutorial;
        private readonly RuleBuilder2Ruleset seededRuleset;
        private readonly bool previewOnOpen;
        private readonly bool persistRuleset = true;

        internal RuleBuilder2FlowController(
            RuleBuilder2Ruleset seededRuleset,
            bool previewOnOpen,
            bool persistRuleset,
            RuleBuilder2TutorialController tutorial)
        {
            this.seededRuleset = seededRuleset;
            this.previewOnOpen = previewOnOpen;
            this.persistRuleset = persistRuleset;
            this.tutorial = tutorial;
        }

        internal RuleBuilder2Ruleset Ruleset { get; set; }
        internal RuleBuilder2Card ActiveCard { get; set; }
        internal List<RuleBuilder2PreviewResult> Preview { get; private set; } = new List<RuleBuilder2PreviewResult>();
        internal List<RuleBuilder2Card> DraftQueue { get; private set; } = new List<RuleBuilder2Card>();
        internal bool ShowPreview { get; set; }
        internal string ApplyStatus { get; private set; } = "";
        internal RuleBuilder2WorkTabSelection? SelectedWorkTabContext { get; private set; }
        internal RuleBuilder2WorkTabSelection? PreviewedWorkTabContext { get; private set; }
        internal bool ShowMatchedPanel { get; private set; }
        internal bool PreviewOnOpen => previewOnOpen;
        internal bool PersistRuleset => persistRuleset;

        internal void PreOpen()
        {
            RuleBuilder2RulesetStore.Ensure(BetterWorkTabMod.Settings);
            if (seededRuleset != null)
            {
                Ruleset = seededRuleset;
                SaveRulesetIfPersistent(Ruleset);
            }
            else
            {
                Ruleset = RuleBuilder2RulesetStore.Current(BetterWorkTabMod.Settings)
                          ?? RuleBuilder2RulesetStore.Saved(BetterWorkTabMod.Settings).FirstOrDefault()
                          ?? CreateAndStoreBlankRuleset();
                if (persistRuleset)
                {
                    RuleBuilder2RulesetStore.SetCurrent(BetterWorkTabMod.Settings, Ruleset, writeSettings: false);
                }
            }

            ActiveCard = GetVisibleCards().FirstOrDefault(card => card != null && !card.IsConfirmed)
                         ?? GetVisibleCards().FirstOrDefault();
            if (previewOnOpen)
            {
                ShowPreview = true;
                RefreshPreview();
            }
        }

        internal void SaveRulesetIfPersistent(RuleBuilder2Ruleset targetRuleset, bool makeCurrent = true)
        {
            if (!persistRuleset || targetRuleset == null)
            {
                return;
            }

            RuleBuilder2RulesetStore.SaveOrReplace(BetterWorkTabMod.Settings, targetRuleset, makeCurrent, writeSettings: false);
        }

        internal RuleBuilder2Ruleset CreateAndStoreBlankRuleset()
        {
            var created = new RuleBuilder2Ruleset
            {
                Name = T("BWT_RuleBuilder2_NewRulesetName"),
                Description = T("BWT_RuleBuilder2_NewRulesetDescription"),
                Source = RuleBuilder2SourceType.Blank
            };
            SaveRulesetIfPersistent(created);
            return created;
        }

        internal List<RuleBuilder2Card> GetVisibleCards()
        {
            return Ruleset?.Cards?
                .Where(IsVisibleRuleCard)
                .OrderBy(card => card.SortOrder)
                .ToList() ?? new List<RuleBuilder2Card>();
        }

        internal static bool IsVisibleRuleCard(RuleBuilder2Card card)
        {
            return card != null &&
                   (card.IsConfirmed ||
                    card.Target?.HasTarget == true ||
                    (card.Conditions?.Conditions?.Any(condition => condition != null) ?? false));
        }

        internal RuleBuilder2Card AddRule()
        {
            Ruleset.Cards ??= new List<RuleBuilder2Card>();
            RuleBuilder2Card card = RuleBuilder2Card.CreateBlank(GetNextRulesetSortOrder());
            card.IsConfirmed = false;
            card.IsCollapsed = false;
            Ruleset.Cards.Add(card);
            ActiveCard = card;
            ShowPreview = false;
            Preview.Clear();
            return card;
        }

        internal List<RuleBuilder2Card> AddCopiedRules(IEnumerable<RuleBuilder2Card> sourceCards)
        {
            Ruleset.Cards ??= new List<RuleBuilder2Card>();
            var added = new List<RuleBuilder2Card>();
            foreach (RuleBuilder2Card source in sourceCards ?? Enumerable.Empty<RuleBuilder2Card>())
            {
                RuleBuilder2Card copy = source?.Copy();
                if (copy == null)
                {
                    continue;
                }

                copy.IsConfirmed = true;
                copy.IsCollapsed = true;
                copy.SortOrder = GetNextRulesetSortOrder();
                copy.Summary = RuleBuilder2SummaryService.BuildSummary(copy);
                Ruleset.Cards.Add(copy);
                added.Add(copy);
            }

            if (added.Count == 0)
            {
                return added;
            }

            ActiveCard = added[0];
            ShowPreview = false;
            Preview.Clear();
            SaveRulesetIfPersistent(Ruleset);
            RefreshPreview();
            return added;
        }

        internal void ConfirmCard(RuleBuilder2Card card)
        {
            if (card == null)
            {
                return;
            }

            card.IsConfirmed = true;
            card.IsCollapsed = true;
            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            if (string.IsNullOrEmpty(card.Name) || card.Name == T("BWT_RuleBuilder2_NewRule"))
            {
                card.Name = card.Target.DisplayLabel.NullOrEmpty() ? T("BWT_RuleBuilder2_RuleCard") : card.Target.DisplayLabel;
            }

            ActiveCard = card;
            ShowPreview = false;
            tutorial.ObserveConfirmed();
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        internal void DeleteCard(RuleBuilder2Card card)
        {
            if (card == null || Ruleset?.Cards == null)
            {
                return;
            }

            List<RuleBuilder2Card> visible = GetVisibleCards();
            int index = visible.IndexOf(card);
            Ruleset.Cards.Remove(card);
            visible.Remove(card);
            if (visible.Count == 0)
            {
                ActiveCard = null;
                ShowPreview = false;
                Preview.Clear();
                return;
            }

            ActiveCard = visible[Mathf.Clamp(index, 0, visible.Count - 1)];
            ShowPreview = false;
            RefreshPreview();
        }

        internal void ApplyRuleset()
        {
            RefreshPreview();
            int changed = applyService.Apply(Ruleset, out List<string> warnings, persistRuleset);
            ApplyStatus = warnings.Count > 0
                ? T("BWT_RuleBuilder2_ApplyStatusWarnings").Formatted(changed, warnings.Count).ToString()
                : T("BWT_RuleBuilder2_ApplyStatus").Formatted(changed).ToString();
            if (warnings.Count > 0)
            {
                Log.Warning("[BWT] Rule Builder 2.0 apply warnings:\n" + string.Join("\n", warnings.ToArray()));
            }
        }

        internal void ExportClassicCompatibleRuleset()
        {
            RuleBuilder2ClassicRulesetExportResult result = classicRulesetExportService.Export(Ruleset, BetterWorkTabMod.Settings);
            if (!result.Exported)
            {
                ApplyStatus = T("BWT_RuleBuilder2_ExportClassicNoRules");
                if (result.Warnings.Count > 0)
                {
                    Log.Warning("[BWT] Rule Builder 2.0 classic export warnings:\n" + string.Join("\n", result.Warnings.ToArray()));
                }

                return;
            }

            ApplyStatus = result.Warnings.Count > 0
                ? T("BWT_RuleBuilder2_ExportClassicStatusWarnings").Formatted(result.RuleCount, result.Warnings.Count).ToString()
                : T("BWT_RuleBuilder2_ExportClassicStatus").Formatted(result.RuleCount).ToString();

            if (result.Warnings.Count > 0)
            {
                Log.Warning("[BWT] Rule Builder 2.0 classic export warnings:\n" + string.Join("\n", result.Warnings.ToArray()));
            }
        }

        internal void ImportClassicRuleset(System.Action afterImport)
        {
            var options = new List<FloatMenuOption>();
            foreach (WorkAssignmentRuleset classicRuleset in BetterWorkTabMod.Settings.SavedRulesets ?? new List<WorkAssignmentRuleset>())
            {
                WorkAssignmentRuleset local = classicRuleset;
                options.Add(new FloatMenuOption(local.Name, () =>
                {
                    Ruleset = RuleBuilder2ClassicRulesetTranslator.FromClassic(local);
                    SaveRulesetIfPersistent(Ruleset);
                    ActiveCard = GetVisibleCards().FirstOrDefault(card => !card.IsConfirmed) ?? GetVisibleCards().FirstOrDefault();
                    afterImport?.Invoke();
                }));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(T("BWT_RuleBuilder2_NoClassicRules"), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal RuleBuilder2Card GetNextGeneratedSuggestion()
        {
            return DraftQueue?
                .Where(card => card != null && card.Target?.HasTarget == true && !card.IsConfirmed)
                .OrderBy(card => card.SortOrder)
                .FirstOrDefault();
        }

        internal void KeepGeneratedSuggestion(RuleBuilder2Card card)
        {
            if (card == null)
            {
                return;
            }

            DraftQueue.Remove(card);
            Ruleset.Cards ??= new List<RuleBuilder2Card>();
            card.IsConfirmed = true;
            card.IsCollapsed = true;
            card.SortOrder = GetNextRulesetSortOrder();
            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            Ruleset.Cards.Add(card);
            SaveRulesetIfPersistent(Ruleset);
            ActiveCard = GetNextGeneratedSuggestion() ?? card;
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        internal void DiscardGeneratedSuggestion(RuleBuilder2Card card)
        {
            if (card == null)
            {
                return;
            }

            DraftQueue.Remove(card);
            ActiveCard = GetNextGeneratedSuggestion() ?? Ruleset.Cards?.FirstOrDefault();
            UISoundCompat.TickLow.PlayOneShotOnCamera();
        }

        internal void EditGeneratedSuggestion(RuleBuilder2Card card)
        {
            if (card == null)
            {
                return;
            }

            DraftQueue.Remove(card);
            Ruleset.Cards ??= new List<RuleBuilder2Card>();
            card.IsConfirmed = false;
            card.IsCollapsed = false;
            card.SortOrder = GetNextRulesetSortOrder();
            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            Ruleset.Cards.Add(card);
            ActiveCard = card;
            ShowPreview = false;
            RefreshPreview();
            UISoundCompat.TickLow.PlayOneShotOnCamera();
        }

        internal void GenerateDraftFromCurrentWorkTab()
        {
            RuleBuilder2Ruleset generated = draftGenerator.GenerateFromCurrentWorkTab();
            DraftQueue = generated.Cards?
                .Where(card => card != null && card.Target?.HasTarget == true)
                .OrderBy(card => card.SortOrder)
                .Select((card, index) =>
                {
                    card.IsConfirmed = false;
                    card.IsCollapsed = false;
                    card.SortOrder = index;
                    return card;
                })
                .ToList() ?? new List<RuleBuilder2Card>();
            ActiveCard = GetNextGeneratedSuggestion() ?? Ruleset.Cards?.FirstOrDefault();
            RefreshPreview();
        }

        internal void ReorderVisibleCards(IList<RuleBuilder2Card> orderedCards)
        {
            if (orderedCards == null || orderedCards.Count == 0)
            {
                return;
            }

            for (int i = 0; i < orderedCards.Count; i++)
            {
                if (orderedCards[i] != null)
                {
                    orderedCards[i].SortOrder = i;
                }
            }

            SaveRulesetIfPersistent(Ruleset);
            RefreshPreview();
        }

        internal List<RuleBuilder2RulePickerSource> BuildRuleBuilder2PickerSources()
        {
            string currentId = Ruleset?.StableId ?? "";
            return RuleBuilder2RulesetStore.Saved(BetterWorkTabMod.Settings)
                .Where(ruleset => ruleset != null && ruleset.StableId != currentId)
                .Select(ruleset => RuleBuilder2RulePickerSource.FromRuleset(ruleset.Name, ruleset))
                .Where(source => source.Cards.Count > 0)
                .ToList();
        }

        internal List<RuleBuilder2RulePickerSource> BuildClassicPickerSources()
        {
            return (BetterWorkTabMod.Settings.SavedRulesets ?? new List<WorkAssignmentRuleset>())
                .Where(ruleset => ruleset != null)
                .Select(ruleset => RuleBuilder2RulePickerSource.FromRuleset(
                    ruleset.Name,
                    RuleBuilder2ClassicRulesetTranslator.FromClassic(ruleset)))
                .Where(source => source.Cards.Count > 0)
                .ToList();
        }

        internal void SwitchRuleset(RuleBuilder2Ruleset ruleset)
        {
            if (ruleset == null)
            {
                return;
            }

            Ruleset = ruleset;
            RuleBuilder2RulesetStore.SetCurrent(BetterWorkTabMod.Settings, Ruleset, writeSettings: false);
            ActiveCard = GetVisibleCards().FirstOrDefault(card => !card.IsConfirmed) ?? GetVisibleCards().FirstOrDefault();
            ShowPreview = false;
            ShowMatchedPanel = false;
            SelectedWorkTabContext = null;
            PreviewedWorkTabContext = null;
            RefreshPreview();
        }

        internal void DuplicateCurrentRuleset()
        {
            RuleBuilder2Ruleset copy = Ruleset?.Copy();
            if (copy == null)
            {
                return;
            }

            Ruleset = copy;
            SaveRulesetIfPersistent(Ruleset);
            ActiveCard = GetVisibleCards().FirstOrDefault(card => !card.IsConfirmed) ?? GetVisibleCards().FirstOrDefault();
            ShowPreview = false;
            RefreshPreview();
        }

        internal void CopyDefaultRuleset()
        {
            WorkAssignmentRuleset classicDefault = BetterWorkTabMod.Settings.SavedRulesets?.FirstOrDefault(rs => rs?.IsDefault == true);
            if (classicDefault == null)
            {
                return;
            }

            Ruleset = RuleBuilder2ClassicRulesetTranslator.FromClassic(classicDefault);
            Ruleset.Source = RuleBuilder2SourceType.DefaultCopy;
            SaveRulesetIfPersistent(Ruleset);
            ActiveCard = GetVisibleCards().FirstOrDefault(card => !card.IsConfirmed) ?? GetVisibleCards().FirstOrDefault();
            ShowPreview = false;
            RefreshPreview();
        }

        private int GetNextRulesetSortOrder()
        {
            return (Ruleset.Cards?
                .Where(existing => existing != null)
                .Select(existing => existing.SortOrder)
                .DefaultIfEmpty(-1)
                .Max() ?? -1) + 1;
        }

        internal void SelectTarget(RuleBuilder2Card card, WorkTypeDef workType, WorkGiverDef workGiver, RuleBuilder2TargetSource source)
        {
            card.Target.WorkTypeDefName = workType?.defName ?? "";
            card.Target.WorkGiverDefName = workGiver?.defName ?? "";
            card.Target.DisplayLabel = BuildTargetLabel(workType, workGiver);
            card.Target.Source = source;
            card.NormalizeActionForTarget();
            tutorial.ObserveTargetSelected(workGiver != null);
            RefreshPreview();
        }

        internal void SetActionKind(RuleBuilder2Card card, RuleBuilder2ActionKind kind)
        {
            if (card?.Action == null || kind == RuleBuilder2ActionKind.FollowGlobal)
            {
                return;
            }

            card.Action.Kind = kind;
            card.NormalizeActionForTarget();
            card.Action.EnsureSchedule(card.Action.Priority);
            tutorial.ObserveActionEdited();
            RefreshPreview();
        }

        internal void RefreshPreview()
        {
            Preview = evaluator.Preview(Ruleset, ActiveCard);
        }

        internal RuleBuilder2PreviewResult PreviewPawn(RuleBuilder2Card card, Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return evaluator.PreviewPawn(card, pawn, workType, workGiver);
        }

        internal void AcceptWorkTabSelection(RuleBuilder2WorkTabSelection selection)
        {
            SelectedWorkTabContext = selection;
            PreviewedWorkTabContext = null;
            ShowMatchedPanel = selection.Pawn != null &&
                (BetterWorkTabMod.Settings?.ruleBuilder2ShowMatchedPanel ?? true);

            RuleBuilder2Card card = ActiveCard != null && Ruleset.Cards.Contains(ActiveCard) && !ActiveCard.IsConfirmed
                ? ActiveCard
                : AddRule();
            card.Target.WorkTypeDefName = selection.WorkType.defName;
            card.Target.WorkGiverDefName = selection.WorkGiver?.defName ?? "";
            card.Target.DisplayLabel = BuildTargetLabel(selection.WorkType, selection.WorkGiver);
            card.Target.Source = selection.Source;
            card.NormalizeActionForTarget();
            if (selection.Priority >= 0)
            {
                card.Action.Priority = RuleBuilder2PriorityRange.Clamp(selection.Priority);
            }

            ActiveCard = card;
            RefreshPreview();
            tutorial.ObserveTargetSelected(selection.WorkGiver != null);
        }

        internal void PreviewWorkTabSelection(RuleBuilder2WorkTabSelection selection)
        {
            PreviewedWorkTabContext = selection;
        }

        internal void ClearWorkTabPreview()
        {
            PreviewedWorkTabContext = null;
        }

        internal bool IsTargetSelected(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            RuleBuilder2Card card = ActiveCard;
            if (card?.Target == null || workType == null)
            {
                return false;
            }

            string activeWorkType = card.Target.WorkTypeDefName;
            string activeWorkGiver = card.Target.WorkGiverDefName ?? "";
            return activeWorkType == workType.defName &&
                   activeWorkGiver == (workGiver?.defName ?? "");
        }

    }
}
