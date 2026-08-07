using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// Answers "has this player already done this?" for the lessons whose
    /// feature leaves a trace behind.
    ///
    /// Better Work Tab has been installed for years, so most people meeting the
    /// 2.0 tour are not new to it. Teaching somebody with eight dividers and a
    /// reordered header row how to add a divider and drag a column wastes their
    /// time and makes the tour look like it has not looked at their colony. So
    /// before the tour offers anything it reads what is already there.
    ///
    /// Two rules keep this honest:
    ///
    /// Evidence has to mean the action, not merely allow it. Dividers exist
    /// because somebody inserted one; a column sits off its baseline because
    /// somebody dragged it. Where a feature leaves nothing behind -- holding
    /// Shift to read skill numbers, selecting headers to group them -- there is
    /// no test here at all and the lesson is offered normally. Guessing would
    /// hide a lesson from somebody who has never seen the feature, which is a
    /// far worse failure than offering one they did not need.
    ///
    /// And a detection is recorded, not recomputed. Evidence lives in the save;
    /// tutorial progress lives in the settings and outlives any one colony.
    /// Deriving this fresh each time would un-know a feature the moment the
    /// player started a new game.
    /// </summary>
    internal static class BWTTutorialPriorUse
    {
        // Scanning walks the colony and the work table, so it is throttled
        // rather than run per frame. Detection only ever adds, so a late scan
        // costs the player nothing beyond seeing one lesson offered briefly.
        private const float ScanIntervalSeconds = 2f;

        private static float lastScanAt = float.NegativeInfinity;

        /// <summary>
        /// Records any feature the player is already using. Safe to call often;
        /// throttled internally. Returns true when something new was found.
        /// </summary>
        internal static bool Scan(BetterWorkTabSettings settings, bool force = false)
        {
            if (settings == null)
            {
                return false;
            }

            float now = Time.realtimeSinceStartup;
            if (!force && now - lastScanAt < ScanIntervalSeconds)
            {
                return false;
            }

            lastScanAt = now;
            settings.tutorialLessonIdsAlreadyUsed ??= new List<string>();

            bool added = false;
            foreach (string lessonId in Detect())
            {
                if (!settings.tutorialLessonIdsAlreadyUsed.Contains(lessonId))
                {
                    settings.tutorialLessonIdsAlreadyUsed.Add(lessonId);
                    added = true;
                }
            }

            if (added)
            {
                settings.Write();
            }

            return added;
        }

        /// <summary>Forgets every recorded detection, for the tutorial reset path.</summary>
        internal static void Clear(BetterWorkTabSettings settings)
        {
            settings?.tutorialLessonIdsAlreadyUsed?.Clear();
            lastScanAt = float.NegativeInfinity;
        }

        internal static bool IsAlreadyUsed(BetterWorkTabSettings settings, string lessonId)
        {
            return !string.IsNullOrEmpty(lessonId) &&
                   settings?.tutorialLessonIdsAlreadyUsed != null &&
                   settings.tutorialLessonIdsAlreadyUsed.Contains(lessonId);
        }

        /// <summary>
        /// The lessons this colony shows evidence for, right now.
        ///
        /// Every probe is wrapped: these read live game state during a Work-tab
        /// pass, and a tour that throws is worse than a tour that offers a
        /// lesson the player did not need.
        /// </summary>
        internal static IEnumerable<string> Detect()
        {
            var found = new List<string>();
            if (Current.ProgramState != ProgramState.Playing)
            {
                return found;
            }

            bool dividers = Probe(HasDividers);
            bool appearance = Probe(HasCustomPawnAppearance);

            Add(found, BWTTutorialLessonCatalog.PawnDivider, dividers);
            Add(found, BWTTutorialLessonCatalog.PawnAppearance, appearance);

            // Both of those are reached only through the right-click menu, so
            // either one proves the player has found it.
            Add(found, BWTTutorialLessonCatalog.PawnMenu, dividers || appearance);

            Add(found, BWTTutorialLessonCatalog.HeaderReorder, Probe(HasReorderedColumns));
            Add(found, BWTTutorialLessonCatalog.PrioritySchedule, Probe(HasSchedules));
            Add(found, BWTTutorialLessonCatalog.HeaderSubWork, Probe(HasSubWorkOverrides));
            Add(found, BWTTutorialLessonCatalog.RuleBuilder2, Probe(HasBuiltRules));
            Add(found, BWTTutorialLessonCatalog.PriorityRange, Probe(HasWidenedPriorityRange));
            return found;
        }

        private static void Add(ICollection<string> found, string lessonId, bool detected)
        {
            if (detected)
            {
                found.Add(lessonId);
            }
        }

        private static bool Probe(Func<bool> test)
        {
            try
            {
                return test();
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[BWT] Tutorial prior-use probe failed: " + ex.Message,
                    DebugFeature.General);
                return false;
            }
        }

        private static bool HasDividers()
        {
            IReadOnlyList<WorkTabLayoutRow> rows = PawnOrganizerSystem.Instance?.Layout?.Rows;
            if (rows == null)
            {
                return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Divider != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A recoloured row or a retitled colonist. Both are set from the same
        /// menu and the lesson teaches both, so either one settles it.
        /// </summary>
        private static bool HasCustomPawnAppearance()
        {
            if (PawnColorDatabase.GetColors()?.Count > 0)
            {
                return true;
            }

            List<Pawn> colonists = Find.CurrentMap?.mapPawns?.FreeColonists;
            if (colonists == null)
            {
                return false;
            }

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                string current = PawnTitleUtility.GetCurrentTitle(pawn);
                if (!current.NullOrEmpty() &&
                    !string.Equals(current, PawnTitleUtility.GetDefaultTitle(pawn), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Any Work column sitting off the position it started this save in.
        /// The baseline is captured per save, so this reports a drag the player
        /// performed rather than a mod load order that happens to differ.
        /// </summary>
        private static bool HasReorderedColumns()
        {
            GameComponent_BWTWorldSettings worldSettings =
                Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (worldSettings == null)
            {
                return false;
            }

            List<string> baseline = ColumnBaselineManager.GetBaselineOrder(worldSettings);
            List<string> current = ColumnBaselineManager.CaptureCurrentOrder();
            if (baseline.Count == 0 || current.Count == 0)
            {
                return false;
            }

            // Compared position by position among the columns both lists know
            // about. A work type added or removed by another mod shifts every
            // index after it, which a straight index equality would report as a
            // drag the player never made.
            List<string> shared = current.Where(baseline.Contains).ToList();
            List<string> expected = baseline.Where(current.Contains).ToList();
            for (int i = 0; i < shared.Count && i < expected.Count; i++)
            {
                if (!string.Equals(shared[i], expected[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSchedules()
        {
            return TimePriorityService.HasAnySchedule();
        }

        private static bool HasSubWorkOverrides()
        {
            return Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?
                .WorkGiverReassignments?.HasAnyData() ?? false;
        }

        /// <summary>
        /// A confirmed rule, not merely a saved ruleset. The store seeds a blank
        /// one on first open, so counting rulesets would credit the player for
        /// opening the window once.
        /// </summary>
        private static bool HasBuiltRules()
        {
            return HasBuiltRuleBuilder2Rules() || HasBuiltClassicRules();
        }

        internal static bool HasBuiltRuleBuilder2Rules()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            // Same trap as the classic side, one layer along: opening Rule
            // Builder 2 for the first time seeds it with a copy of the shipped
            // classic default, whose rules arrive already confirmed. Read the
            // serialized collection directly because the store accessor calls
            // Ensure(), which can seed and normalize settings as a side effect.
            List<RuleBuilder2Ruleset> saved = settings.SavedRuleBuilder2Rulesets;
            return saved != null &&
                   saved.Any(set => set?.Cards != null &&
                                    set.Source != RuleBuilder2SourceType.DefaultCopy &&
                                    set.Cards.Any(card => card != null && card.IsConfirmed));
        }

        internal static bool HasBuiltClassicRules()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            // Classic counts too -- somebody with years of classic rulesets
            // knows perfectly well what a rule is.
            //
            // But the shipped defaults are five rulesets carrying nine rules
            // between them, so "has rules" credits every new player and hides
            // this lesson from exactly the people who need it. Only rulesets the
            // player made count, which is what IsDefault marks.
            return settings.SavedRulesets != null &&
                   settings.SavedRulesets.Any(set =>
                       set != null && !set.IsDefault && set.Rules != null && set.Rules.Count > 0);
        }

        /// <summary>
        /// The priority-range lesson routes into settings rather than the tab,
        /// so its evidence is the setting itself: a range that is no longer the
        /// shipped default is one the player went and changed.
        /// </summary>
        private static bool HasWidenedPriorityRange()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null && settings.maxPriorityInt != DefaultSettings.maxPriority;
        }
    }
}
