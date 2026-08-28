using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkloadPerformanceInstrumentationContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Workloads", "WorkloadGateway.cs"),
                "workload performance instrumentation contracts");
            string backend = Read(
                root,
                "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string layout = Read(
                root,
                "Source", "PawnOrganizer", "Layout", "WorkTabLayoutController.cs");
            string audit = Read(
                root,
                "Source", "UI", "WorkGrid", "Invalidation", "WorkGridInvalidationAudit.cs");
            string timing = Read(
                root,
                "Source", "Spine", "Profiling", "SpineTiming.cs");

            BaselineCaptureProfilesAggregateCounts(backend);
            LayoutChecksAreAttributed(layout);
            InvalidationAuditIsAggregateAndGated(audit);
            RecorderFailuresCannotChangeMeasuredBehavior(timing);
        }

        private static void BaselineCaptureProfilesAggregateCounts(string backend)
        {
            string capture = MemberBody(
                backend,
                "internal WorkloadOperationResult<WorkloadLiveBaselineCapture> CaptureLiveBaselineCapture(");
            int workTypeRead = capture.IndexOf(
                "List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;",
                StringComparison.Ordinal);
            int pawnLoop = capture.IndexOf(
                "foreach (Pawn pawn in runtime.Pawns.Values)",
                StringComparison.Ordinal);
            int profileScope = capture.IndexOf(
                "long profileScope = Stopwatch.GetTimestamp();",
                pawnLoop,
                StringComparison.Ordinal);

            TestAssert.True(
                workTypeRead >= 0 && pawnLoop > workTypeRead,
                "baseline capture must read the work-type list before the pawn loop");
            TestAssert.Equal(
                1,
                Count(capture, "AllDefsListForReading"),
                "baseline capture must not reread the work-type list per pawn");
            TestAssert.True(profileScope > pawnLoop, "baseline pawn loop must remain measurable");
            string pawnLoopBody = capture.Substring(pawnLoop, profileScope - pawnLoop);
            TestAssert.False(
                pawnLoopBody.IndexOf("SpineTiming.Time", StringComparison.Ordinal) >= 0,
                "baseline profiling must remain aggregate rather than per-pawn");
            TestAssert.Contains(capture, "counts=pawns:", "baseline profiling must report pawn count");
            TestAssert.Contains(capture, ",workTypes:", "baseline profiling must report work-type count");
            TestAssert.Contains(capture, ",workGivers:", "baseline profiling must report work-giver count");
            TestAssert.Contains(capture, ",runtimePriorities:", "baseline profiling must label runtime priorities");
            TestAssert.Contains(capture, ",livePriorities:", "baseline profiling must label live priorities");
            TestAssert.Contains(capture, ",manualModes:", "baseline profiling must label manual modes");
            TestAssert.Contains(capture, ",specificOverrides:", "baseline profiling must label specific overrides");
            TestAssert.Contains(capture, ",specificOrder:", "baseline profiling must label specific order entries");
            TestAssert.Equal(
                1,
                Count(capture, "Log.Message("),
                "baseline profiling must emit one aggregate record, not per-entry logs");
        }

        private static void LayoutChecksAreAttributed(string layout)
        {
            string rebuild = MemberBody(layout, "public void Rebuild(");
            string shouldRebuild = MemberBody(layout, "private bool ShouldRebuild(");
            string[] sections =
            {
                "WorkTab.Layout.RebuildBody",
                "WorkTab.Layout.ShouldRebuild.ColumnSignature",
                "WorkTab.Layout.ShouldRebuild.HiddenWorktypesSignature",
                "WorkTab.Layout.ShouldRebuild.DividerAnimationSignature",
                "WorkTab.Layout.ShouldRebuild.SubWorkSettingsSignature",
                "WorkTab.Layout.ShouldRebuild.PawnDisplayOrder",
                "WorkTab.Layout.ShouldRebuild.DividerCollapseState"
            };

            for (int i = 0; i < sections.Length; i++)
            {
                TestAssert.Contains(
                    layout,
                    sections[i],
                    "layout instrumentation must expose " + sections[i]);
            }

            TestAssert.Contains(
                rebuild,
                "SpineTiming.Enabled",
                "layout rebuild timing must be gated");
            TestAssert.Contains(
                rebuild,
                "WorkTab.Layout.RebuildBody",
                "rebuild count must come from the actual build body");
            TestAssert.False(
                layout.IndexOf("TimeSafely", StringComparison.Ordinal) >= 0,
                "layout code must rely on the profiler boundary instead of duplicating recovery wrappers");
            TestAssert.Contains(
                shouldRebuild,
                "HasPawnDisplayOrderChanged(snapshot)",
                "pawn-order attribution must retain the existing scan");
            TestAssert.Contains(
                shouldRebuild,
                "HasDividerCollapseStateChanged(snapshot)",
                "divider attribution must retain the existing scan");
        }

        private static void InvalidationAuditIsAggregateAndGated(string audit)
        {
            string roster = MemberBody(audit, "internal static void PollRoster(PawnTable table)");
            string poll = MemberBody(audit, "internal static void Poll(PawnTable table)");
            string compute = MemberBody(audit, "private static int ComputeSignature(PawnTable table)");
            string rosterSignature = MemberBody(
                audit,
                "private static int RosterSignature(System.Collections.Generic.IEnumerable<Pawn> pawns)");

            TestAssert.Contains(
                roster,
                "WorkTab.InvalidationAudit.RosterSignature",
                "roster audit timing must be aggregate");
            TestAssert.Contains(
                poll,
                "WorkTab.InvalidationAudit.Reconcile",
                "direct-mutation audit timing must be aggregate");
            TestAssert.Contains(
                poll,
                "WorkTab.InvalidationAudit.ComputeSignature",
                "signature audit timing must be aggregate");
            TestAssert.Contains(
                poll,
                "SpineTiming.Enabled",
                "invalidation audit timing must be gated");
            TestAssert.False(
                audit.IndexOf("TimeSafely", StringComparison.Ordinal) >= 0,
                "audit code must rely on the profiler boundary instead of duplicating recovery wrappers");
            TestAssert.False(
                compute.IndexOf("SpineTiming.Time", StringComparison.Ordinal) >= 0,
                "signature timing must not run inside the pawn/work-type loops");
            TestAssert.False(
                rosterSignature.IndexOf("SpineTiming.Time", StringComparison.Ordinal) >= 0,
                "roster timing must not run inside the pawn loop");
        }

        private static void RecorderFailuresCannotChangeMeasuredBehavior(string timing)
        {
            TestAssert.Contains(
                timing,
                "RecordSafely(name, Stopwatch.GetTimestamp() - start);",
                "both timing wrappers must use the observational recorder boundary");
            TestAssert.Equal(
                2,
                Count(timing, "RecordSafely(name, Stopwatch.GetTimestamp() - start);"),
                "action and value timing must share the recorder boundary");
            TestAssert.Contains(
                timing,
                "catch (Exception exception) when (!IsFatal(exception))",
                "non-fatal recorder failures must not change measured behavior");
            TestAssert.Contains(
                timing,
                "discard this profiling run",
                "reports must disclose missing timing samples");
        }

        private static int Count(string source, string value)
        {
            int count = 0;
            int start = 0;
            while (source != null && value != null)
            {
                int next = source.IndexOf(value, start, StringComparison.Ordinal);
                if (next < 0) break;
                count++;
                start = next + value.Length;
            }

            return count;
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
            }

            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string MemberBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source member " + signature);
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "source member has no opening brace " + signature);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(start, i - start + 1);
            }

            throw new InvalidOperationException("source member has no closing brace " + signature);
        }
    }
}
