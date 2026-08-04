using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Spine.UI.Tutorial;

namespace Better_Work_Tab.Features.Tutorial
{
    internal enum BWTTutorialCourse
    {
        None,
        WhatsNew20,
        Full
    }

    [Flags]
    internal enum BWTTutorialCourseMembership
    {
        None = 0,
        WhatsNew20 = 1,
        Full = 2
    }

    internal enum BWTTutorialLessonRoute
    {
        WorkTab,
        PrioritySettings,
        RuleBuilder2,
        FluffyCoexistence
    }

    internal sealed class BWTTutorialLessonDefinition
    {
        internal BWTTutorialLessonDefinition(
            string id,
            string versionIntroduced,
            string[] categories,
            BWTTutorialCourseMembership courses,
            Func<bool> isAvailable,
            string feedbackLabelKey,
            TutorialHubAnchor anchor,
            string localizationStem,
            BWTTutorialLessonRoute route = BWTTutorialLessonRoute.WorkTab)
        {
            Id = id;
            VersionIntroduced = versionIntroduced;
            Categories = categories;
            Courses = courses;
            IsAvailable = isAvailable;
            FeedbackLabelKey = feedbackLabelKey;
            Anchor = anchor;
            LocalizationStem = localizationStem;
            Route = route;
        }

        internal string Id { get; }
        internal string VersionIntroduced { get; }
        internal IReadOnlyList<string> Categories { get; }
        internal BWTTutorialCourseMembership Courses { get; }
        internal Func<bool> IsAvailable { get; }
        internal string FeedbackLabelKey { get; }
        internal TutorialHubAnchor Anchor { get; }
        internal string LocalizationStem { get; }
        internal BWTTutorialLessonRoute Route { get; }

        internal bool BelongsTo(BWTTutorialCourse course)
        {
            BWTTutorialCourseMembership membership = course == BWTTutorialCourse.WhatsNew20
                ? BWTTutorialCourseMembership.WhatsNew20
                : BWTTutorialCourseMembership.Full;
            return (Courses & membership) != 0 && (IsAvailable?.Invoke() ?? true);
        }
    }

    /// <summary>
    /// Canonical lesson metadata. Course screens project this catalog; lesson
    /// execution never owns a second copy of labels, IDs, or availability.
    /// </summary>
    internal static class BWTTutorialLessonCatalog
    {
        // Retained so an in-progress lesson from an older build can be retired
        // without changing the persisted tutorial ID contract.
        internal const string RetiredPriorityChange = "priority.change";
        internal const string PrioritySkill = "priority.skill";
        internal const string PrioritySchedule = "priority.schedule";
        internal const string PriorityRange = "priority.range";
        internal const string PawnMenu = "pawn.menu";
        internal const string PawnDivider = "pawn.divider";
        internal const string PawnAppearance = "pawn.appearance";
        internal const string HeaderReorder = "header.reorder";
        internal const string HeaderGroup = "header.group";
        internal const string HeaderSubWork = "header.subwork";
        internal const string RuleBuilder2 = "rules.builder2";
        internal const string FluffyCoexistence = "compat.fluffy-coexistence";

        private const BWTTutorialCourseMembership Both =
            BWTTutorialCourseMembership.WhatsNew20 | BWTTutorialCourseMembership.Full;

        private static readonly IReadOnlyList<BWTTutorialLessonDefinition> Lessons =
            new[]
            {
                // Every lesson teaches a feature the player is free to switch
                // off, including while the tour is open. Courses project this
                // catalog on every read, so an availability test here withdraws a
                // lesson the moment its feature is disabled instead of leaving
                // one whose instruction can never be satisfied.
                Lesson(PrioritySkill, "1.0.5", "Priorities", BWTTutorialCourseMembership.Full,
                    "PrioritySkill", TutorialHubAnchor.PriorityCell,
                    isAvailable: () => Enabled(s => s.enableSkillOverlayFeature, DefaultSettings.enableSkillOverlayFeature)),
                Lesson(PrioritySchedule, "2.0", "Priorities|Schedules", Both,
                    "PrioritySchedule", TutorialHubAnchor.PriorityCell,
                    isAvailable: () => Enabled(s => s.enableTimePrioritySchedules, DefaultSettings.enableTimePrioritySchedules)),
                Lesson(PriorityRange, "2.0", "Priorities|Configuration", Both,
                    "PriorityRange", TutorialHubAnchor.PriorityCell,
                    route: BWTTutorialLessonRoute.PrioritySettings),
                Lesson(PawnMenu, "1.0.5", "Pawn rows", BWTTutorialCourseMembership.Full,
                    "PawnMenu", TutorialHubAnchor.PawnName,
                    isAvailable: () => Enabled(s => s.enableContextMenuOnRightClick, DefaultSettings.enableContextMenuOnRightClick)),
                // Dividers are added from the pawn context menu, so this lesson
                // needs both the menu and the dividers themselves.
                Lesson(PawnDivider, "1.0.5", "Pawn rows|Organization", BWTTutorialCourseMembership.Full,
                    "PawnDivider", TutorialHubAnchor.PawnName,
                    isAvailable: () => Enabled(s => s.enableContextMenuOnRightClick, DefaultSettings.enableContextMenuOnRightClick) &&
                                       Enabled(s => s.enableDividers, DefaultSettings.enableDividers)),
                Lesson(PawnAppearance, "1.0.5", "Pawn rows|Appearance", BWTTutorialCourseMembership.Full,
                    "PawnAppearance", TutorialHubAnchor.PawnName,
                    isAvailable: () => Enabled(s => s.enableContextMenuOnRightClick, DefaultSettings.enableContextMenuOnRightClick)),
                Lesson(HeaderReorder, "2.0", "Work order|Layout", Both,
                    "HeaderReorder", TutorialHubAnchor.WorkHeader,
                    isAvailable: () => Enabled(s => s.enableDragDropReordering, DefaultSettings.enableDragDropReordering)),
                Lesson(HeaderGroup, "1.0.5", "Work order|Layout", BWTTutorialCourseMembership.Full,
                    "HeaderGroup", TutorialHubAnchor.WorkHeader,
                    isAvailable: () => Enabled(s => s.enableColumnGrouping, DefaultSettings.enableColumnGrouping)),
                Lesson(HeaderSubWork, "2.0", "Specific jobs|Priorities", Both,
                    "HeaderSubWork", TutorialHubAnchor.WorkHeader,
                    isAvailable: () => Enabled(s => s.enableSubWorkDrilldown, DefaultSettings.enableSubWorkDrilldown)),
                // Rule Builder 2.0 is switched on by the lesson's own route, so
                // it stays offered even when the player has not enabled it yet.
                Lesson(RuleBuilder2, "2.0", "Automation|Rules", Both,
                    "RuleBuilder2", TutorialHubAnchor.PriorityCell,
                    route: BWTTutorialLessonRoute.RuleBuilder2),
                new BWTTutorialLessonDefinition(
                    FluffyCoexistence,
                    "2.0",
                    Split("Compatibility|Specific jobs"),
                    Both,
                    () => FluffyWorkTabGateway.IsPresent,
                    "BWT_Tutorial_FluffyCoexistence_Feedback",
                    TutorialHubAnchor.WorkHeader,
                    "FluffyCoexistence",
                    BWTTutorialLessonRoute.FluffyCoexistence)
            };

        internal static IReadOnlyList<BWTTutorialLessonDefinition> All => Lessons;

        internal static BWTTutorialLessonDefinition Find(string id)
        {
            return Lessons.FirstOrDefault(lesson => string.Equals(lesson.Id, id, StringComparison.Ordinal));
        }

        internal static IEnumerable<BWTTutorialLessonDefinition> ForCourse(BWTTutorialCourse course)
        {
            BWTTutorialCourse effective = course == BWTTutorialCourse.None
                ? BWTTutorialCourse.Full
                : course;
            return Lessons.Where(lesson => lesson.BelongsTo(effective));
        }

        private static BWTTutorialLessonDefinition Lesson(
            string id,
            string version,
            string categories,
            BWTTutorialCourseMembership courses,
            string stem,
            TutorialHubAnchor anchor,
            BWTTutorialLessonRoute route = BWTTutorialLessonRoute.WorkTab,
            Func<bool> isAvailable = null)
        {
            return new BWTTutorialLessonDefinition(
                id,
                version,
                Split(categories),
                courses,
                isAvailable ?? (() => true),
                "BWT_Tutorial_" + stem + "_Feedback",
                anchor,
                stem,
                route);
        }

        /// <summary>
        /// Reads a feature toggle, falling back to its default when settings are
        /// not loaded yet, so availability never throws during startup.
        /// </summary>
        private static bool Enabled(Func<BetterWorkTabSettings, bool> read, bool fallback)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings == null ? fallback : read(settings);
        }

        private static string[] Split(string value)
        {
            return value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
