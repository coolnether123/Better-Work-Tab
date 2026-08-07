using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.UI.Settings;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// What Better Work Tab could still do for a player who has switched parts
    /// of it off.
    ///
    /// The tour only ever offers lessons for features that are turned on, which
    /// is right -- an instruction nobody can follow is worse than no lesson.
    /// But it leaves somebody running a trimmed-down configuration finishing a
    /// three-lesson tour convinced that is the whole mod. So when everything
    /// available is settled, the tour names what is switched off and offers to
    /// go and show them, rather than quietly ending early.
    ///
    /// Only features gated by a setting appear here. A lesson withheld because
    /// another mod is absent is not something the player can act on, so
    /// offering it would be a dead end.
    /// </summary>
    internal static class BWTTutorialFeatureDiscovery
    {
        /// <summary>
        /// Lessons whose feature is switched off but could be switched on.
        /// </summary>
        internal static IReadOnlyList<BWTTutorialLessonDefinition> FindDisabled(BWTTutorialCourse course)
        {
            return BWTTutorialLessonCatalog.All
                .Where(lesson => lesson.SettingIds.Count > 0)
                .Where(lesson => lesson.InCourse(course))
                .Where(lesson => !(lesson.IsAvailable?.Invoke() ?? true))
                .ToList();
        }

        /// <summary>
        /// How many features the tour could still offer, or zero when there is
        /// nothing to offer or the player has lessons left they can do now.
        ///
        /// One call rather than a "should I?" plus a count, because this sits on
        /// the band's per-frame path and each answer walks the catalog.
        /// </summary>
        internal static int CountOffer(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return 0;
            }

            int disabled = FindDisabled(settings.selectedTutorialCourse).Count;
            return disabled > 0 && AllAvailableSettled(settings) ? disabled : 0;
        }

        internal static bool AllAvailableSettled(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            BWTTutorialProgressSnapshot progress = BWTTutorialProgressSnapshot.For(settings);
            return BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse).All(
                lesson => progress.IsSettled(lesson.Id) ||
                          (settings.skippedTutorialLessonIds?.Contains(lesson.Id) ?? false));
        }

        /// <summary>
        /// The settings the disabled lessons live behind, as one focus request.
        /// Filtering to exactly these turns the settings window into the list of
        /// what is switched off, with each row highlighted where it sits.
        /// </summary>
        internal static BWTSettingsFocusRequest BuildFocusRequest(BWTTutorialCourse course)
        {
            IReadOnlyList<BWTTutorialLessonDefinition> disabled = FindDisabled(course);
            if (disabled.Count == 0)
            {
                return null;
            }

            List<string> settingIds = ExpandWithAncestors(
                disabled.SelectMany(lesson => lesson.SettingIds));

            return new BWTSettingsFocusRequest(
                "BWT_Tutorial_Discover_Filter".Translate(),
                "BWT_Tutorial_Discover_FilterTooltip".Translate(),
                settingIds.FirstOrDefault(),
                false,
                settingIds);
        }

        /// <summary>
        /// Adds each setting's ancestors to the list.
        ///
        /// The settings list is a tree, and filtering to a child alone hides
        /// it: the row has no parent to sit under. "Column grouping" lives
        /// under drag and drop, so a filter naming only the grouping toggle
        /// opened a window listing one of the two switched-off features and
        /// silently dropping the other.
        ///
        /// Walked from the registry rather than written out per lesson, so
        /// moving a setting deeper in the tree cannot quietly un-list it.
        /// </summary>
        private static List<string> ExpandWithAncestors(IEnumerable<string> settingIds)
        {
            BWTSettingsRegistry.EnsureInitialized();
            Dictionary<string, string> parents = BWTSettingsRegistry.Definitions
                .Where(def => def != null && !string.IsNullOrEmpty(def.Id))
                .GroupBy(def => def.Id)
                .ToDictionary(group => group.Key, group => group.First().ParentId);

            var expanded = new List<string>();
            foreach (string settingId in settingIds)
            {
                string current = settingId;

                // Bounded by the number of settings, so a parent cycle in the
                // registry cannot hang the Work tab.
                int guard = 0;
                while (!string.IsNullOrEmpty(current) &&
                       guard++ <= parents.Count &&
                       !expanded.Contains(current))
                {
                    expanded.Add(current);
                    parents.TryGetValue(current, out current);
                }
            }

            return expanded;
        }

        /// <summary>The feature names behind the button, for its tooltip.</summary>
        internal static string DescribeDisabled(BWTTutorialCourse course)
        {
            IReadOnlyList<BWTTutorialLessonDefinition> disabled = FindDisabled(course);
            if (disabled.Count == 0)
            {
                return string.Empty;
            }

            string names = string.Join(
                "\n",
                disabled
                    .Select(lesson => "- " + ("BWT_Tutorial_" + lesson.LocalizationStem + "_Label").Translate())
                    .ToArray());
            return "BWT_Tutorial_Discover_Tooltip".Translate(disabled.Count) + "\n\n" + names;
        }
    }
}
