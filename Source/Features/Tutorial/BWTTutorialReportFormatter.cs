using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal static class BWTBuildInfo
    {
        internal const string Build = "2.0.0-beta";
        internal const string SourceCommit = "ea3b7e5cae477f01c040c5a1c77a3abc92132149";
    }

    internal sealed class BWTTutorialDiagnosticContext
    {
        internal string RimWorldVersion;
        internal string Build;
        internal string Commit;
        internal string Course;
        internal string WorkTabInterface;
        internal string SpecificJobPresentation;
        internal string Renderer;
        internal string RendererFallback;
        internal bool FluffyInstalled;
        internal string CompatibilityModules;
        internal bool MigratedFromPublic105;
    }

    /// <summary>Pure report composition. It performs no storage or transmission.</summary>
    internal static class BWTTutorialReportFormatter
    {
        internal const int DiscordTargetLength = 1800;

        internal static string FormatDiscord(BetterWorkTabSettings settings)
        {
            BWTTutorialDiagnosticContext context = CaptureContext(settings);
            var report = new StringBuilder();
            report.AppendLine("BWT 2.0 beta tutorial report");
            AppendCompactDiagnostics(report, context);
            AppendCompactProgress(report, settings);
            AppendCompactResponses(report, settings, DiscordTargetLength);
            AppendOptional(report, "Overall: ", settings.tutorialOverallFeedback, 320);
            return Clamp(report.ToString().TrimEnd(), DiscordTargetLength);
        }

        internal static string FormatFull(BetterWorkTabSettings settings)
        {
            BWTTutorialDiagnosticContext context = CaptureContext(settings);
            var report = new StringBuilder();
            report.AppendLine("Better Work Tab 2.0 beta tutorial report");
            report.AppendLine("RimWorld: " + context.RimWorldVersion);
            report.AppendLine("BWT: " + context.Build + " (commit " + context.Commit + ")");
            report.AppendLine("Course: " + context.Course);
            report.AppendLine("Work-tab interface: " + context.WorkTabInterface);
            report.AppendLine("Specific-job presentation: " + context.SpecificJobPresentation);
            report.AppendLine("Renderer: " + context.Renderer);
            report.AppendLine("Renderer fallback: " + context.RendererFallback);
            report.AppendLine("Fluffy installed: " + YesNo(context.FluffyInstalled));
            report.AppendLine("Active BWT compatibility modules: " + context.CompatibilityModules);
            report.AppendLine("Migrated from public 1.0.5: " + YesNo(context.MigratedFromPublic105));
            report.AppendLine();

            foreach (BWTTutorialLessonDefinition lesson in BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                string state = settings.completedTutorialLessonIds.Contains(lesson.Id)
                    ? "Completed"
                    : settings.skippedTutorialLessonIds.Contains(lesson.Id) ? "Skipped" : "Not finished";
                report.AppendLine(lesson.Id + " — " + state);
                BWTTutorialLessonFeedback response = BWTTutorialFeedbackStore.Find(settings, lesson.Id);
                if (response == null || !response.HasResponse)
                {
                    continue;
                }

                if (response.lessonValue != BWTTutorialLessonFeedbackValue.Unanswered)
                    report.AppendLine("  Lesson: " + Friendly(response.lessonValue));
                if (response.behaviorValue != BWTTutorialBehaviorFeedbackValue.Unanswered)
                    report.AppendLine("  Behaved as explained: " + Friendly(response.behaviorValue));
                if (!string.IsNullOrWhiteSpace(response.note))
                {
                    report.AppendLine("  Note: " + OneLine(response.note));
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.tutorialOverallFeedback))
            {
                report.AppendLine();
                report.AppendLine("Overall feedback:");
                report.AppendLine(settings.tutorialOverallFeedback.Trim());
            }

            return report.ToString().TrimEnd();
        }

        internal static BWTTutorialDiagnosticContext CaptureContext(BetterWorkTabSettings settings)
        {
            WorkGridRendererDiagnosticSnapshot renderer = WorkGridRendererDiagnostics.Current;
            string fallback = renderer.FallbackReasons.Count == 0
                ? "none"
                : string.Join("; ", renderer.FallbackReasons.Select(reason =>
                    reason.Code + (string.IsNullOrWhiteSpace(reason.Detail) ? string.Empty : ": " + reason.Detail)));
            return new BWTTutorialDiagnosticContext
            {
                RimWorldVersion = VersionControl.CurrentVersionStringWithRev,
                Build = BWTBuildInfo.Build,
                Commit = BWTBuildInfo.SourceCommit,
                Course = CourseName(settings.selectedTutorialCourse),
                WorkTabInterface = FluffyWorkTabGateway.ExternalWorkTabOwnsWorkTab ? "Fluffy" : "Better Work Tab",
                SpecificJobPresentation = settings.subWorkDrilldownStyle.ToString(),
                Renderer = renderer.ActiveRendererId + " (selected " + renderer.SelectionMode + ")",
                RendererFallback = fallback,
                FluffyInstalled = FluffyWorkTabGateway.IsPresent,
                CompatibilityModules = JoinOrNone(ModSupportManager.GetActiveModuleNames()),
                MigratedFromPublic105 = settings.tutorialMigratedFromPublic105
            };
        }

        private static void AppendCompactDiagnostics(StringBuilder report, BWTTutorialDiagnosticContext context)
        {
            report.AppendLine("RW " + context.RimWorldVersion + " | BWT " + context.Build + " @ " + context.Commit);
            report.AppendLine("Course " + context.Course + " | UI " + context.WorkTabInterface +
                              " | jobs " + context.SpecificJobPresentation);
            report.AppendLine("Renderer " + context.Renderer + " | fallback " + context.RendererFallback);
            report.AppendLine("Fluffy " + YesNo(context.FluffyInstalled) + " | compat " + context.CompatibilityModules +
                              " | from 1.0.5 " + YesNo(context.MigratedFromPublic105));
        }

        private static void AppendCompactProgress(StringBuilder report, BetterWorkTabSettings settings)
        {
            var completed = BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse)
                .Where(lesson => settings.completedTutorialLessonIds.Contains(lesson.Id))
                .Select(lesson => lesson.Id).ToArray();
            var skipped = BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse)
                .Where(lesson => settings.skippedTutorialLessonIds.Contains(lesson.Id))
                .Select(lesson => lesson.Id).ToArray();
            if (completed.Length > 0) report.AppendLine("Done: " + string.Join(", ", completed));
            if (skipped.Length > 0) report.AppendLine("Skipped: " + string.Join(", ", skipped));
        }

        private static void AppendCompactResponses(StringBuilder report, BetterWorkTabSettings settings, int limit)
        {
            foreach (BWTTutorialLessonFeedback response in settings.tutorialLessonFeedback.Where(item => item.HasResponse))
            {
                var fields = new List<string>();
                if (response.lessonValue != BWTTutorialLessonFeedbackValue.Unanswered)
                    fields.Add("lesson=" + Friendly(response.lessonValue));
                if (response.behaviorValue != BWTTutorialBehaviorFeedbackValue.Unanswered)
                    fields.Add("behavior=" + Friendly(response.behaviorValue));
                if (!string.IsNullOrWhiteSpace(response.note))
                {
                    fields.Add("note=" + Shorten(OneLine(response.note), 150));
                }
                string line = response.lessonId + ": " + string.Join(", ", fields);
                if (report.Length + line.Length + 1 >= limit - 360) break;
                report.AppendLine(line);
            }
        }

        private static void AppendOptional(StringBuilder report, string prefix, string value, int max)
        {
            if (!string.IsNullOrWhiteSpace(value)) report.AppendLine(prefix + Shorten(OneLine(value), max));
        }

        private static string Friendly(BWTTutorialLessonFeedbackValue value)
        {
            switch (value)
            {
                case BWTTutorialLessonFeedbackValue.Keep: return "Keep";
                case BWTTutorialLessonFeedbackValue.Revise: return "Keep, revise";
                case BWTTutorialLessonFeedbackValue.NoLesson: return "No lesson needed";
                default: return "Unanswered";
            }
        }

        private static string Friendly(BWTTutorialBehaviorFeedbackValue value)
        {
            switch (value)
            {
                case BWTTutorialBehaviorFeedbackValue.Yes: return "Yes";
                case BWTTutorialBehaviorFeedbackValue.No: return "No";
                case BWTTutorialBehaviorFeedbackValue.NotSure: return "Not sure";
                default: return "Unanswered";
            }
        }

        private static string CourseName(BWTTutorialCourse course)
        {
            return course == BWTTutorialCourse.WhatsNew20 ? "What's new in 2.0" : "Full tutorial";
        }

        private static string JoinOrNone(IReadOnlyList<string> values) =>
            values == null || values.Count == 0 ? "none" : string.Join(", ", values);
        private static string YesNo(bool value) => value ? "yes" : "no";
        private static string OneLine(string value) =>
            (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        private static string Shorten(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 1)) + "…";
        private static string Clamp(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 24)) + "\n[report shortened]";
    }
}
