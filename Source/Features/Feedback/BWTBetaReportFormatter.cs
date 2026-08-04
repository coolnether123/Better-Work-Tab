using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Better_Work_Tab.Features.Tutorial;
using Verse;

namespace Better_Work_Tab.Features.Feedback
{
    /// <summary>
    /// Pure report composition for the 2.0 beta portal. It performs no storage
    /// and no transmission; the player copies what it produces.
    ///
    /// Two shapes, because they are read by different people in different
    /// places. The Discord one has to survive a chat message, so it drops
    /// everything that is not a lead. The full one is what gets pasted into an
    /// issue, so it carries the mod list and the settings diff — the two things
    /// that decide whether a report can be reproduced.
    /// </summary>
    internal static class BWTBetaReportFormatter
    {
        internal const int DiscordTargetLength = 1800;

        internal static string FormatFull(BetterWorkTabSettings settings)
        {
            var report = new StringBuilder();
            report.AppendLine("Better Work Tab 2.0 beta feedback");
            AppendEnvironment(report, settings);
            AppendRatings(report, settings, int.MaxValue, false);
            AppendProblems(report, settings, int.MaxValue);
            AppendTutorial(report, settings);
            AppendOverall(report, settings, int.MaxValue);
            AppendModList(report);
            AppendChangedSettings(report, settings, BWTBetaEnvironmentReport.SettingsDiffCap);
            return report.ToString().TrimEnd();
        }

        internal static string FormatDiscord(BetterWorkTabSettings settings)
        {
            var report = new StringBuilder();
            report.AppendLine("BWT 2.0 beta feedback");
            AppendEnvironmentCompact(report, settings);

            // Problems first: in a chat message they are the part somebody can
            // act on, and the part most likely to be cut by the length clamp if
            // it goes last.
            AppendProblems(report, settings, 6);
            AppendRatings(report, settings, int.MaxValue, true);
            AppendOverall(report, settings, 400);
            AppendChangedSettings(report, settings, 12);
            return Clamp(report.ToString().TrimEnd(), DiscordTargetLength);
        }

        private static void AppendEnvironment(StringBuilder report, BetterWorkTabSettings settings)
        {
            BWTDiagnosticContext context = BWTDiagnosticContext.Capture(settings);
            foreach (BWTBetaEnvironmentReport.Line line in BWTBetaEnvironmentReport.Summary())
            {
                report.AppendLine(line.Label + ": " + line.Value);
            }

            report.AppendLine("Renderer fallback: " + context.RendererFallback);
            report.AppendLine("Course: " + context.Course);
            report.AppendLine("Migrated from public 1.0.5: " + (context.MigratedFromPublic105 ? "yes" : "no"));
            if (!string.IsNullOrWhiteSpace(settings?.betaTesterHandle))
            {
                report.AppendLine("Tester: " + OneLine(settings.betaTesterHandle));
            }

            report.AppendLine();
        }

        private static void AppendEnvironmentCompact(StringBuilder report, BetterWorkTabSettings settings)
        {
            BWTDiagnosticContext context = BWTDiagnosticContext.Capture(settings);
            report.AppendLine("RW " + context.RimWorldVersion + " | BWT " + context.Build + " @ " + Short(context.Commit, 10));

            // The header line already carries the build and the game version, so
            // the two lines that repeat them are dropped rather than printed twice.
            string buildLabel = "BWT_Beta_Env_Build".Translate();
            string versionLabel = "BWT_Beta_Env_RimWorld".Translate();
            foreach (BWTBetaEnvironmentReport.Line line in BWTBetaEnvironmentReport.Summary())
            {
                if (string.Equals(line.Label, buildLabel, StringComparison.Ordinal) ||
                    string.Equals(line.Label, versionLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                report.AppendLine(line.Label + ": " + line.Value);
            }

            if (!string.IsNullOrWhiteSpace(settings?.betaTesterHandle))
            {
                report.AppendLine("Tester: " + OneLine(settings.betaTesterHandle));
            }

            report.AppendLine();
        }

        private static void AppendRatings(
            StringBuilder report,
            BetterWorkTabSettings settings,
            int limit,
            bool problemsOnly)
        {
            BWTBetaFeedbackStore.Ensure(settings);
            var answered = settings.betaFeatureRatings
                .Where(rating => rating.HasResponse)
                .Where(rating => !problemsOnly ||
                                 rating.verdict == BWTFeatureVerdict.Rough ||
                                 rating.verdict == BWTFeatureVerdict.Broken ||
                                 !string.IsNullOrWhiteSpace(rating.note))
                .Take(limit)
                .ToList();
            if (answered.Count == 0)
            {
                return;
            }

            report.AppendLine(problemsOnly ? "Rated rough or broken:" : "How 2.0 is going:");
            foreach (BWTFeatureRating rating in answered)
            {
                string label = BWTBetaFeatureCatalog.AreaLabel(rating.featureId);
                report.AppendLine("  " + label + " — " + Friendly(rating.verdict));
                if (!string.IsNullOrWhiteSpace(rating.note))
                {
                    report.AppendLine("    " + Short(OneLine(rating.note), 240));
                }
            }

            report.AppendLine();
        }

        private static void AppendProblems(StringBuilder report, BetterWorkTabSettings settings, int limit)
        {
            BWTBetaFeedbackStore.Ensure(settings);
            var problems = settings.betaProblemReports.Where(problem => problem.HasResponse).ToList();
            if (problems.Count == 0)
            {
                return;
            }

            report.AppendLine("Problems reported (" + problems.Count + "):");
            for (int i = 0; i < problems.Count && i < limit; i++)
            {
                BWTProblemReport problem = problems[i];
                report.AppendLine("  " + (i + 1) + ". [" + Friendly(problem.severity) + "] " +
                                  BWTBetaFeatureCatalog.AreaLabel(problem.areaId));
                report.AppendLine("     " + Short(OneLine(problem.text), 500));
            }

            if (problems.Count > limit)
            {
                report.AppendLine("  (+" + (problems.Count - limit) + " more in the full report)");
            }

            report.AppendLine();
        }

        private static void AppendTutorial(StringBuilder report, BetterWorkTabSettings settings)
        {
            BWTTutorialFeedbackStore.Ensure(settings);
            var lessons = BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse).ToList();
            bool anyProgress = lessons.Any(lesson =>
                settings.completedTutorialLessonIds.Contains(lesson.Id) ||
                settings.skippedTutorialLessonIds.Contains(lesson.Id));
            if (!anyProgress && !settings.tutorialLessonFeedback.Any(item => item.HasResponse))
            {
                return;
            }

            report.AppendLine("Tutorial lessons:");
            foreach (BWTTutorialLessonDefinition lesson in lessons)
            {
                string state = settings.completedTutorialLessonIds.Contains(lesson.Id)
                    ? "Completed"
                    : settings.skippedTutorialLessonIds.Contains(lesson.Id) ? "Skipped" : "Not finished";
                report.AppendLine("  " + lesson.Id + " — " + state);
                BWTTutorialLessonFeedback response = BWTTutorialFeedbackStore.Find(settings, lesson.Id);
                if (response == null || !response.HasResponse)
                {
                    continue;
                }

                if (response.lessonValue != BWTTutorialLessonFeedbackValue.Unanswered)
                {
                    report.AppendLine("    Lesson: " + response.lessonValue);
                }

                if (response.behaviorValue != BWTTutorialBehaviorFeedbackValue.Unanswered)
                {
                    report.AppendLine("    Behaved as explained: " + response.behaviorValue);
                }

                if (!string.IsNullOrWhiteSpace(response.note))
                {
                    report.AppendLine("    Note: " + OneLine(response.note));
                }
            }

            report.AppendLine();
        }

        private static void AppendOverall(StringBuilder report, BetterWorkTabSettings settings, int max)
        {
            BWTBetaFeedbackStore.Ensure(settings);
            string text = settings.betaOverallFeedback;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            report.AppendLine("Anything else:");
            report.AppendLine("  " + Short(max == int.MaxValue ? text.Trim() : OneLine(text), max));
            report.AppendLine();
        }

        private static void AppendModList(StringBuilder report)
        {
            List<string> mods = BWTBetaEnvironmentReport.ActiveMods();
            report.AppendLine("Active mods (" + mods.Count + "), in load order:");
            for (int i = 0; i < mods.Count; i++)
            {
                report.AppendLine("  " + (i + 1) + ". " + mods[i]);
            }

            report.AppendLine();
        }

        private static void AppendChangedSettings(StringBuilder report, BetterWorkTabSettings settings, int limit)
        {
            List<string> changed = BWTBetaEnvironmentReport.ChangedSettings(settings);
            if (changed.Count == 0)
            {
                report.AppendLine("Better Work Tab settings: all at defaults.");
                report.AppendLine();
                return;
            }

            report.AppendLine("Better Work Tab settings changed from default (" + changed.Count + "):");
            for (int i = 0; i < changed.Count && i < limit; i++)
            {
                report.AppendLine("  " + changed[i]);
            }

            if (changed.Count > limit)
            {
                report.AppendLine("  (+" + (changed.Count - limit) + " more)");
            }

            report.AppendLine();
        }

        private static string Friendly(BWTFeatureVerdict verdict)
        {
            switch (verdict)
            {
                case BWTFeatureVerdict.Great: return "Great";
                case BWTFeatureVerdict.Works: return "Works";
                case BWTFeatureVerdict.Rough: return "Rough";
                case BWTFeatureVerdict.Broken: return "Broken";
                case BWTFeatureVerdict.NotUsed: return "Didn't use";
                default: return "Unanswered";
            }
        }

        private static string Friendly(BWTProblemSeverity severity)
        {
            switch (severity)
            {
                case BWTProblemSeverity.Cosmetic: return "Cosmetic";
                case BWTProblemSeverity.Annoying: return "Annoying";
                case BWTProblemSeverity.Blocking: return "Blocking";
                case BWTProblemSeverity.Crash: return "Crash or error";
                default: return "Unrated";
            }
        }

        private static string OneLine(string value) =>
            (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static string Short(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 1)) + "…";

        private static string Clamp(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 24)) + "\n[report shortened]";
    }
}
