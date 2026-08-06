using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.RuleBuilderV2;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.RuleBuilder
{
    /// <summary>
    /// Central boundary between Better Work Tab's general UI and the rule builders.
    ///
    /// Callers outside the rule-builder folders should come here instead of reaching
    /// into Window_RuleBuilder2, Window_RulesetBuilder, RuleBuilder2WorkTabBridge, or
    /// RuleBuilder2ApplyService directly. That keeps the Work tab and footer buttons
    /// stable while the classic builder and Rule Builder 2 can evolve independently.
    /// </summary>
    internal static class RuleBuilderGateway
    {
        static RuleBuilderGateway()
        {
            RuleBuilder2Evaluator.RegisterCurrentPawnOrderProvider(GetWorkTabOrderedPawns);
        }

        internal static void EnsureRuleBuilder2ServicesRegistered()
        {
        }

        /// <summary>
        /// Footer-facing operations: labels, apply behavior, and menu construction.
        /// These methods choose between classic rulesets and Rule Builder 2 according
        /// to the active setting, so the footer does not need to know either backend.
        /// </summary>
        internal static string CurrentRulesetLabel()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return "BWT_NoRuleset".Translate();
            }

            if (UseRuleBuilder2(settings))
            {
                return RuleBuilder2RulesetStore.Current(settings)?.Name ?? "BWT_NoRuleset".Translate();
            }

            return settings.CurrentRuleset?.Name ?? "BWT_NoRuleset".Translate();
        }

        /// <summary>
        /// Whether <see cref="CurrentRulesetLabel"/> is naming a real ruleset
        /// rather than the "nothing selected" placeholder. The footer needs to
        /// tell those apart: one is a name to apply, the other is an invitation.
        /// </summary>
        internal static bool HasCurrentRuleset()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            return UseRuleBuilder2(settings)
                ? RuleBuilder2RulesetStore.Current(settings) != null
                : settings.CurrentRuleset != null;
        }

        internal static void ApplyCurrentRuleset()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (UseRuleBuilder2(settings))
            {
                ApplyRuleBuilder2Ruleset(RuleBuilder2RulesetStore.Current(settings));
                return;
            }

            ApplyClassicRuleset(settings.CurrentRuleset, settings);
        }

        internal static List<FloatMenuOption> BuildRulesetMenuOptions()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            var options = new List<FloatMenuOption>();
            if (settings == null)
            {
                options.Add(new FloatMenuOption("BWT_NoRuleset".Translate(), null));
                return options;
            }

            if (UseRuleBuilder2(settings))
            {
                AddRuleBuilder2Rulesets(settings, options);
            }
            else
            {
                AddClassicRulesets(settings, options);
            }

            AddManagementOptions(settings, options);
            return options;
        }

        /// <summary>
        /// Work-tab-facing operations for selecting and previewing targets.
        /// Rule Builder 2's window bridge is deliberately hidden behind these methods
        /// so MainTabWindow_BetterWork only speaks in Work tab concepts.
        /// </summary>
        internal static bool IsRuleBuilder2ListeningToWorkTab => RuleBuilder2WorkTabBridge.IsOpen;

        internal static void SelectPriorityCellForRuleBuilder2(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds)
        {
            RuleBuilder2WorkTabBridge.SelectTarget(
                workType,
                workGiver,
                pawn,
                priority,
                bounds,
                RuleBuilder2TargetSource.PriorityCell);
        }

        internal static void SelectHeaderForRuleBuilder2(WorkTypeDef workType, WorkGiverDef workGiver, Rect bounds)
        {
            RuleBuilder2WorkTabBridge.SelectTarget(
                workType,
                workGiver,
                null,
                -1,
                bounds,
                RuleBuilder2TargetSource.WorkTabClick);
        }

        internal static void PreviewPriorityCellForRuleBuilder2(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds)
        {
            RuleBuilder2WorkTabBridge.PreviewTarget(
                workType,
                workGiver,
                pawn,
                priority,
                bounds,
                RuleBuilder2TargetSource.PriorityCell);
        }

        internal static void PreviewHeaderForRuleBuilder2(WorkTypeDef workType, WorkGiverDef workGiver, Rect bounds)
        {
            RuleBuilder2WorkTabBridge.PreviewTarget(
                workType,
                workGiver,
                null,
                -1,
                bounds,
                RuleBuilder2TargetSource.WorkTabClick);
        }

        internal static bool RuleBuilder2BlocksWorkTabHover()
        {
            return RuleBuilder2WorkTabBridge.BlocksWorkTabHover();
        }

        internal static bool TryGetRuleBuilder2SelectionTransitionOffset(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out Vector2 offset)
        {
            return RuleBuilder2WorkTabBridge.TryGetSelectionTransitionOffset(workType, workGiver, out offset);
        }

        internal static void ClearRuleBuilder2WorkTabPreview()
        {
            RuleBuilder2WorkTabBridge.ClearPreview();
        }

        internal static bool IsRuleBuilder2WorkTabOpen()
        {
            return Find.MainTabsRoot?.OpenTab?.TabWindow is Better_Work_Tab.UI.MainTabWindow_BetterWork;
        }

        internal static bool FlashRuleBuilder2Target(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return RuleBuilder2WorkTabBridge.FlashTarget(workType, workGiver);
        }

        internal static bool ShouldHighlightRuleBuilder2Target(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return RuleBuilder2WorkTabBridge.ShouldHighlight(workType, workGiver);
        }

        internal static bool ShouldHighlightRuleBuilder2Pawn(Pawn pawn)
        {
            return RuleBuilder2WorkTabBridge.ShouldHighlightPawn(pawn);
        }

        private static List<Pawn> GetWorkTabOrderedPawns()
        {
            List<Pawn> currentMapPawns = GetCurrentMapFreeColonistsInWorkTabOrder();
            var rows = PawnOrganizerSystem.Instance?.Layout?.Rows;
            if (rows == null || rows.Count == 0)
            {
                return currentMapPawns;
            }

            var allowed = new HashSet<Pawn>(currentMapPawns);
            var seen = new HashSet<Pawn>();
            var ordered = rows
                .OrderBy(row => row.VisualIndex)
                .Select(row => row.Pawn)
                .Where(pawn => pawn != null && allowed.Contains(pawn) && seen.Add(pawn))
                .ToList();

            if (ordered.Count == 0)
            {
                return currentMapPawns;
            }

            foreach (Pawn pawn in currentMapPawns)
            {
                if (seen.Add(pawn))
                {
                    ordered.Add(pawn);
                }
            }

            return ordered;
        }

        private static List<Pawn> GetCurrentMapFreeColonistsInWorkTabOrder()
        {
            return Find.CurrentMap?.mapPawns?.FreeColonists?
                .Where(pawn => pawn != null && !pawn.Dead)
                .OrderBy(RowOrderUtility.GetPawnRowOrder)
                .ThenBy(pawn => pawn.LabelShortCap)
                .Distinct()
                .ToList() ?? new List<Pawn>();
        }

        private static void AddRuleBuilder2Rulesets(BetterWorkTabSettings settings, List<FloatMenuOption> options)
        {
            List<RuleBuilder2Ruleset> rulesets = RuleBuilder2RulesetStore.Saved(settings);
            if (rulesets.Count == 0)
            {
                options.Add(new FloatMenuOption("BWT_NoRuleset".Translate(), null));
                return;
            }

            foreach (RuleBuilder2Ruleset ruleset in rulesets)
            {
                RuleBuilder2Ruleset local = ruleset;
                options.Add(new FloatMenuOption(local.Name, () =>
                {
                    RuleBuilder2RulesetStore.SetCurrent(settings, local);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }));
            }
        }

        private static void AddClassicRulesets(BetterWorkTabSettings settings, List<FloatMenuOption> options)
        {
            foreach (WorkAssignmentRuleset ruleset in settings.SavedRulesets ?? new List<WorkAssignmentRuleset>())
            {
                WorkAssignmentRuleset local = ruleset;
                options.Add(new FloatMenuOption(local.Name, () =>
                {
                    settings.SetCurrentRuleset(local);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }));
            }
        }

        private static void AddManagementOptions(BetterWorkTabSettings settings, List<FloatMenuOption> options)
        {
            BetterWorkTabSettings.RulesetViewMode mode = settings.rulesetViewMode;
            if (mode == BetterWorkTabSettings.RulesetViewMode.Regular ||
                mode == BetterWorkTabSettings.RulesetViewMode.Both)
            {
                options.Add(new FloatMenuOption("BWT_RuleBuilder_OpenBuilder".Translate(), OpenPreferredBuilder));
                options.Add(new FloatMenuOption("BWT_RuleBuilder2_OpenClassic".Translate(), OpenClassicBuilder));
            }

            if (mode == BetterWorkTabSettings.RulesetViewMode.Raw ||
                mode == BetterWorkTabSettings.RulesetViewMode.Both)
            {
                string label = mode == BetterWorkTabSettings.RulesetViewMode.Raw
                    ? "BWT_RuleBuilder_ManageRulesets".Translate()
                    : "BWT_RuleBuilder_ManageRulesets".Translate() + " (Raw)";

                options.Add(new FloatMenuOption(label, () =>
                {
                    Find.WindowStack.Add(new Window_RulesManager());
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }));
            }
        }

        private static void OpenPreferredBuilder()
        {
            if (UseRuleBuilder2(BetterWorkTabMod.Settings))
            {
                Find.WindowStack.Add(new Window_RuleBuilder2());
            }
            else
            {
                Find.WindowStack.Add(new Window_RulesetBuilder());
            }

            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        internal static void OpenRuleBuilder2Tutorial()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                settings.useRuleBuilder2 = true;
                settings.Write();
            }

            // Asked for deliberately from a lesson, so the concept is put back
            // even for a player who dismissed it long ago. Opening the builder
            // below would otherwise teach nothing to anyone who already knows.
            Features.Tutorial.BWTConcepts.Replay(Features.Tutorial.BWTConceptDefOf.BWT_WorkRules);
            Find.WindowStack.Add(new Window_RuleBuilder2());
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void OpenClassicBuilder()
        {
            Find.WindowStack.Add(new Window_RulesetBuilder());
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void ApplyRuleBuilder2Ruleset(RuleBuilder2Ruleset ruleset)
        {
            if (ruleset == null)
            {
                return;
            }

            new RuleBuilder2ApplyService().Apply(ruleset, out List<string> warnings);
            if (warnings.Count > 0)
            {
                Log.Warning("[BWT] Rule Builder 2.0 apply warnings from footer button:\n" + string.Join("\n", warnings.ToArray()));
            }
        }

        private static void ApplyClassicRuleset(WorkAssignmentRuleset ruleset, BetterWorkTabSettings settings)
        {
            if (ruleset == null)
            {
                return;
            }

            System.Action applyAction = () =>
            {
                if (ruleset.ResetBeforeApplying)
                {
                    WorkAssignmentRuleset.SetAllToZero();
                }

                ruleset.ApplyAutoAssignments();
            };

            if (settings.warnOnApplyRuleset)
            {
                Find.WindowStack.Add(new Dialog_WarningWithCheckbox(
                    "Applying this will reset the current work tab priority configuration. Continue?",
                    "Apply ruleset?",
                    applyAction,
                    value =>
                    {
                        settings.warnOnApplyRuleset = !value;
                        settings.Write();
                    }));
                return;
            }

            applyAction();
        }

        private static bool UseRuleBuilder2(BetterWorkTabSettings settings)
        {
            return settings?.useRuleBuilder2 ?? DefaultSettings.useRuleBuilder2;
        }
    }
}
