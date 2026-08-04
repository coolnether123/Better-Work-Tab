using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Feedback
{
    internal static class BWTBuildInfo
    {
        internal const string Build = "2.0.0-beta";

        // Only correct for whoever last edited it by hand, which is why the
        // packaged commit is preferred when one is present.
        private const string FallbackCommit = "ea3b7e5cae477f01c040c5a1c77a3abc92132149";
        private const string StampFileName = "BUILD.txt";
        private const string StampCommitPrefix = "commit:";

        private static string resolvedCommit;

        /// <summary>
        /// The commit a report should be attributed to.
        ///
        /// A hard-coded constant goes stale the moment anyone builds without
        /// editing it, and a report naming the wrong build is worse than one
        /// naming none: it sends triage at code the tester never ran. The
        /// packager stamps the real commit into BUILD.txt beside the mod, so that
        /// is read first and the constant is only a fallback for loose dev builds.
        /// </summary>
        internal static string SourceCommit
        {
            get
            {
                if (resolvedCommit != null)
                {
                    return resolvedCommit;
                }

                resolvedCommit = ReadStampedCommit() ?? FallbackCommit + " (unstamped build)";
                return resolvedCommit;
            }
        }

        private static string ReadStampedCommit()
        {
            try
            {
                ModContentPack content = LoadedModManager.ModHandles
                    .FirstOrDefault(handle => handle is BetterWorkTabMod)?.Content;
                if (content?.RootDir == null)
                {
                    return null;
                }

                string path = Path.Combine(content.RootDir, StampFileName);
                if (!File.Exists(path))
                {
                    return null;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith(StampCommitPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed.Substring(StampCommitPrefix.Length).Trim();
                        if (value.Length > 0)
                        {
                            return value;
                        }
                    }
                }
            }
            catch
            {
                // Reporting must never be the thing that breaks; an unreadable
                // stamp just falls back to the constant.
            }

            return null;
        }
    }

    /// <summary>
    /// The facts about a running build that a report quotes. Captured in one
    /// place so the portal, the report text and any future diagnostics all agree
    /// on what they are describing.
    /// </summary>
    internal sealed class BWTDiagnosticContext
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

        internal static BWTDiagnosticContext Capture(BetterWorkTabSettings settings)
        {
            WorkGridRendererDiagnosticSnapshot renderer = WorkGridRendererDiagnostics.Current;
            string fallback = renderer.FallbackReasons.Count == 0
                ? "none"
                : string.Join("; ", renderer.FallbackReasons.Select(reason =>
                    reason.Code + (string.IsNullOrWhiteSpace(reason.Detail) ? string.Empty : ": " + reason.Detail)).ToArray());
            return new BWTDiagnosticContext
            {
                RimWorldVersion = VersionControl.CurrentVersionStringWithRev,
                Build = BWTBuildInfo.Build,
                Commit = BWTBuildInfo.SourceCommit,
                Course = CourseName(settings),
                WorkTabInterface = FluffyWorkTabGateway.ExternalWorkTabOwnsWorkTab ? "Fluffy" : "Better Work Tab",
                SpecificJobPresentation = settings.subWorkDrilldownStyle.ToString(),
                Renderer = renderer.ActiveRendererId + " (selected " + renderer.SelectionMode + ")",
                RendererFallback = fallback,
                FluffyInstalled = FluffyWorkTabGateway.IsPresent,
                CompatibilityModules = JoinOrNone(ModSupportManager.GetActiveModuleNames()),
                MigratedFromPublic105 = settings.tutorialMigratedFromPublic105
            };
        }

        private static string CourseName(BetterWorkTabSettings settings)
        {
            return settings.selectedTutorialCourse == Tutorial.BWTTutorialCourse.WhatsNew20
                ? "What's new in 2.0"
                : "Full tutorial";
        }

        private static string JoinOrNone(IReadOnlyList<string> values)
        {
            return values == null || values.Count == 0 ? "none" : string.Join(", ", values.ToArray());
        }
    }
}
