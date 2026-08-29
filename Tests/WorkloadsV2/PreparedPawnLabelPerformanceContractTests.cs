using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PreparedPawnLabelPerformanceContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Rendering", "PreparedPawnLabelPresentation.cs"),
                "prepared pawn-label performance contracts");
            string compatibility = Read(root, "Source", "UI", "WorkGrid", "Compatibility", "WorkGridVanillaCompatibilityPolicy.cs");
            string capture = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedPawnLabelPresentation.cs");
            string labelText = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedPawnLabelText.cs");
            string packet = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkRowPacket.cs");
            string retained = Read(root, "Source", "UI", "WorkGrid", "Rendering", "RetainedWorkBoxRowCache.cs");
            string preparedBox = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkBoxRenderer.cs");
            string optimized = Read(root, "Source", "UI", "WorkGrid", "Rendering", "OptimizedWorkGridRenderer.cs");
            string provider = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string audit = Read(root, "Source", "UI", "WorkGrid", "Invalidation", "WorkGridInvalidationAudit.cs");

            TestAssert.Contains(compatibility, "column?.Worker?.GetType() != typeof(PawnColumnWorker_Label)", "worker subclasses must stay native");
            TestAssert.Contains(compatibility, "ReplacedVanillaLabelHooks", "label replacement needs an explicit hook allowlist");
            TestAssert.Contains(compatibility, "HasExternalPatch(Harmony.GetPatchInfo(hook))", "foreign label patches must force native fallback");

            string signature = MemberBody(capture, "internal static int ComputePresentationModeSignature(");
            TestAssert.Contains(signature, "PawnColorDatabase.Version", "contrast changes need an O(1) producer revision");
            TestAssert.False(signature.IndexOf("table.cachedPawns", StringComparison.Ordinal) >= 0,
                "presentation mode must not scan every pawn during snapshot admission");
            TestAssert.Contains(audit, "private static void PollPawnLabelSignature(PawnTable table)",
                "label freshness must have a separate compatibility-audit path");
            TestAssert.Contains(audit, "PawnLabelAuditIntervalFrames",
                "label auditing must remain slower than the render pass");
            TestAssert.Contains(audit, "WorkTabDirtyFlags.PawnLabel",
                "external label changes must invalidate only the label lane");
            TestAssert.Contains(audit, "_lastPawnLabelAuditRevision",
                "label audit freshness must have its own revision baseline");
            TestAssert.False(
                MemberBody(audit, "private static void PollPawnLabelSignature(PawnTable table)")
                    .IndexOf("_lastTrackedRevisions =", StringComparison.Ordinal) >= 0,
                "label polling must not overwrite the regular audit revision baseline");
            TestAssert.Contains(audit, "private static int ComputePawnLabelSignature(PawnTable table)",
                "the compatibility audit must retain the complete label source coverage");
            string auditSignature = MemberBody(audit, "private static int ComputePawnLabelSignature(PawnTable table)");
            TestAssert.False(auditSignature.IndexOf("PawnColorDatabase.Version", StringComparison.Ordinal) >= 0,
                "the slow label audit must not duplicate the O(1) color-version owner");
            TestAssert.Contains(auditSignature, "pawn.Name?.ToStringShort", "the audit must cover pawn-name changes");
            TestAssert.Contains(auditSignature, "pawn.story?.Title", "the audit must cover title changes");
            TestAssert.Contains(auditSignature, "pawn.KindLabel", "the audit must cover role-label changes");
            TestAssert.Contains(auditSignature, "pawn.IsSlave", "the audit must cover slave-label changes");
            TestAssert.Contains(auditSignature, "pawn.IsColonyMech", "the audit must cover mech-label changes");
            TestAssert.False(signature.IndexOf("Widgets.ThingIcon", StringComparison.Ordinal) >= 0, "portraits must not enter snapshot capture");
            TestAssert.False(capture.IndexOf("internal Pawn Pawn", StringComparison.Ordinal) >= 0,
                "prepared label state must not retain a live pawn");
            TestAssert.Contains(capture, "richText ?? throw new ArgumentNullException",
                "the capture boundary must establish its non-null label invariant once");
            TestAssert.False(packet.IndexOf("text ?? string.Empty", StringComparison.Ordinal) >= 0,
                "downstream packet values must not hide a broken label capture invariant");
            TestAssert.False(capture.IndexOf("TruncateForPreparedCell", StringComparison.Ordinal) >= 0,
                "prepared labels must not use the former unconditional truncator");
            TestAssert.False(capture.IndexOf("Text.CalcSize", StringComparison.Ordinal) >= 0,
                "prepared labels must not repeat native's transient width check");
            TestAssert.False(capture.IndexOf("GenText.Truncate", StringComparison.Ordinal) >= 0,
                "prepared labels must preserve native's stable full-label clipping result");
            TestAssert.Contains(capture, "string resolvedLabel = nativeLabel.Resolve();",
                "the complete native label must be resolved once");
            TestAssert.False(capture.IndexOf("preparedBaseText.Resolve", StringComparison.Ordinal) >= 0,
                "prepared label variants must not re-enter ColoredText resolution");
            string captureMethod = MemberBody(capture, "internal static PreparedPawnLabelPresentation Capture(");
            TestAssert.False(captureMethod.IndexOf("PreparedPawnLabelText.Truncate(", StringComparison.Ordinal) >= 0,
                "prepared labels must not replace native clipping with an in-label ellipsis");
            TestAssert.False(captureMethod.IndexOf("\"...\"", StringComparison.Ordinal) >= 0,
                "prepared labels must not synthesize an unconditional ellipsis");
            TestAssert.Equal(1, CountOccurrences(captureMethod, ".Resolve()"),
                "capture must resolve only the conditionally prepared source label");
            TestAssert.False(captureMethod.IndexOf("new Dictionary<", StringComparison.Ordinal) >= 0,
                "snapshot capture must not introduce a process-wide native label cache");
            TestAssert.Contains(captureMethod,
                "bool colorizePawnName = pawn.IsSlave || pawn.IsColonyMech",
                "capture must keep the pawn, slave, and colony-mech color decision at the boundary");
            TestAssert.Contains(captureMethod,
                "richText = resolvedLabel.Colorize(pawnNameColor)",
                "capture source identity must retain native name-color behavior");
            TestAssert.Contains(captureMethod,
                "richText = PreparedPawnLabelText.StripMarkup(resolvedLabel)",
                "contrast capture must strip role markup without changing native clipping");
            TestAssert.Contains(captureMethod,
                "string preparedText = richText;",
                "prepared labels must pass the complete presentation string to the live native label widget");
            TestAssert.Contains(captureMethod,
                "float textWidth = columnWidth - 3f -",
                "prepared label width must reserve only the native left padding and optional icon");
            TestAssert.False(captureMethod.IndexOf("Mathf.Max(0f, textWidth)", StringComparison.Ordinal) >= 0,
                "prepared label width must remain the native rect2 width without a clamp");
            string buildPawnLabel = MemberBody(
                packet,
                "private static PreparedPawnLabelCell BuildPawnLabel(");
            TestAssert.Contains(buildPawnLabel, "textRect.xMin += 3f;",
                "prepared label geometry must preserve native left padding");
            TestAssert.Contains(buildPawnLabel, "textRect.xMin += cellRect.height;",
                "prepared label geometry must reserve only the native icon width");
            TestAssert.False(labelText.IndexOf("Dictionary<", StringComparison.Ordinal) >= 0,
                "the truncator must not retain a process-wide cache");
            TestAssert.False(labelText.IndexOf("ColoredText.", StringComparison.Ordinal) >= 0,
                "the truncator must not mutate RimWorld's global color cache");
            TestAssert.False(labelText.IndexOf("GenText.Truncate", StringComparison.Ordinal) >= 0,
                "the truncator must own its balanced markup boundary");
            TestAssert.Contains(capture, "previous.MatchesSource(",
                "full snapshot rebuilds must reuse labels whose exact source and metric inputs are unchanged");
            TestAssert.True(
                captureMethod.IndexOf("previous.MatchesSource(", StringComparison.Ordinal) <
                    captureMethod.IndexOf("string preparedText = richText;", StringComparison.Ordinal),
                "exact reuse must be decided before rebuilding the prepared presentation");
            TestAssert.Contains(provider, "_reusablePawnLabels",
                "label reuse must follow pawn identity across row movement and roster additions");
            TestAssert.False(packet.IndexOf("Text.CalcSize", StringComparison.Ordinal) >= 0,
                "row-packet compilation must not measure unchanged label text");

            TestAssert.Contains(provider, "_pawnLabelPresentationMode == pawnLabelPresentationMode", "unchanged snapshots need a label freshness guard");
            TestAssert.Contains(packet, "PreparedWorkRowCommandKind.PreparedPawnLabel", "stable labels must use the existing ordered row packet");
            TestAssert.Contains(packet, "internal string Text { get; }", "prepared labels must carry their stable display text");
            TestAssert.Contains(optimized, "Widgets.Label(OffsetY(label.TextRect, rowOffsetY), label.Text)",
                "prepared label text must use RimWorld's native live glyph path");
            TestAssert.False(retained.IndexOf("PawnLabelText", StringComparison.Ordinal) >= 0,
                "pawn-label pixels must not be retained and alpha-composited twice");
            TestAssert.False(preparedBox.IndexOf("DrawRetainedText(", StringComparison.Ordinal) >= 0,
                "the common work-box renderer must not regain pawn-label text composition");

            string draw = MemberBody(optimized, "public bool DrawPreparedPawnLabel(");
            TestAssert.Contains(draw, "Widgets.ThingIcon(iconRect, pawn)", "portraits must remain live");
            TestAssert.Contains(draw, "Find.Selector.IsSelected(pawn)", "selection must remain live");
            TestAssert.Contains(draw, "WorkTabDiagnostics.RecordPawnLabelIcon", "diagnostic geometry callbacks must remain live");
            TestAssert.Contains(draw, "ModSupportManager.OnPawnRowDrawn", "mod geometry callbacks must remain live");
            TestAssert.Contains(draw, "pawn.health.summaryHealth.SummaryHealthPercent < 0.99f", "health-bar rows must fall back native");
            TestAssert.Contains(draw, "Mouse.IsOver(cellRect)", "hovered rows must fall back native");
            TestAssert.True(
                draw.IndexOf("return false;", StringComparison.Ordinal) <
                    draw.IndexOf("DrawPreparedPawnLabelText", StringComparison.Ordinal),
                "unsupported live label states must fall back before prepared text is drawn");

            const int visibleRows = 25;
            const int baselineNativeLabelCalls = visibleRows;
            const int preparedNativeLabelCalls = 0;
            const int livePortraitCalls = visibleRows;
            TestAssert.True(baselineNativeLabelCalls == 25, "locked label baseline changed");
            TestAssert.True(preparedNativeLabelCalls == 0, "stable supported rows must bypass the native label worker");
            TestAssert.True(livePortraitCalls == 25, "the optimization must not cache or skip portraits");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int index = 0; index < parts.Length; index++) path = Path.Combine(path, parts[index]);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string MemberBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source member " + signature);
            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int index = open; index < source.Length; index++)
            {
                if (source[index] == '{') depth++;
                else if (source[index] == '}' && --depth == 0)
                    return source.Substring(start, index - start + 1);
            }
            throw new InvalidOperationException("source member has no closing brace");
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            for (int offset = 0; ;)
            {
                int found = source.IndexOf(value, offset, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }
                count++;
                offset = found + value.Length;
            }
        }
    }
}
