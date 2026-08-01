using System.Collections.Generic;

namespace Better_Work_Tab.Features.Tutorial
{
    internal static class BWTTutorialProgressMigration
    {
        internal const int CurrentSchemaVersion = 1;

        internal static void Apply(BetterWorkTabSettings settings, bool migratedFromPublic105)
        {
            if (settings == null)
            {
                return;
            }

            settings.completedTutorialLessonIds ??= new List<string>();
            settings.skippedTutorialLessonIds ??= new List<string>();
            settings.tutorialLessonFeedback ??= new List<BWTTutorialLessonFeedback>();
            settings.tutorialOverallFeedback ??= string.Empty;

            if (settings.activeTutorialLessonId == BWTTutorialLessonCatalog.RetiredPriorityChange)
            {
                // Changing a priority is vanilla Work-tab behavior, so this
                // lesson no longer belongs in Better Work Tab's feature tour.
                settings.activeTutorialLessonId = string.Empty;
                settings.tutorialLessonPhase = 0;
            }

            if (settings.tutorialProgressSchemaVersion < CurrentSchemaVersion)
            {
                // No pre-public 2.0 step/index format is carried forward. The
                // public beta starts from catalog IDs and preserves them thereafter.
                settings.completedTutorialLessonIds.Clear();
                settings.skippedTutorialLessonIds.Clear();
                settings.tutorialLessonFeedback.Clear();
                settings.tutorialOverallFeedback = string.Empty;
                settings.activeTutorialLessonId = string.Empty;
                settings.tutorialLessonPhase = 0;
                settings.selectedTutorialCourse = BWTTutorialCourse.None;
                if (settings.showGeneralTutorial)
                {
                    settings.tutorialWelcomeCompleted = false;
                }
            }

            settings.tutorialMigratedFromPublic105 |= migratedFromPublic105;
            settings.tutorialProgressSchemaVersion = CurrentSchemaVersion;
            settings.tutorialFlowVersion = BWTGeneralTutorial.CurrentFlowVersion;
        }
    }
}
