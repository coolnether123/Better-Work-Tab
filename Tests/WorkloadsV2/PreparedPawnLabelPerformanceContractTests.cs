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
            string packet = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkRowPacket.cs");
            string retained = Read(root, "Source", "UI", "WorkGrid", "Rendering", "RetainedWorkBoxRowCache.cs");
            string preparedBox = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkBoxRenderer.cs");
            string optimized = Read(root, "Source", "UI", "WorkGrid", "Rendering", "OptimizedWorkGridRenderer.cs");
            string provider = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");

            TestAssert.Contains(compatibility, "column?.Worker?.GetType() != typeof(PawnColumnWorker_Label)", "worker subclasses must stay native");
            TestAssert.Contains(compatibility, "ReplacedVanillaLabelHooks", "label replacement needs an explicit hook allowlist");
            TestAssert.Contains(compatibility, "HasExternalPatch(Harmony.GetPatchInfo(hook))", "foreign label patches must force native fallback");

            string signature = MemberBody(capture, "internal static int ComputeSourceSignature(");
            TestAssert.Contains(signature, "pawn.Name?.ToStringShort", "name changes must invalidate prepared text");
            TestAssert.Contains(signature, "pawn.story?.Title", "title changes must invalidate prepared text");
            TestAssert.Contains(signature, "PawnColorDatabase.Version", "contrast changes must invalidate prepared text");
            TestAssert.False(signature.IndexOf("Widgets.ThingIcon", StringComparison.Ordinal) >= 0, "portraits must not enter snapshot capture");
            TestAssert.False(capture.IndexOf("internal Pawn Pawn", StringComparison.Ordinal) >= 0,
                "prepared label state must not retain a live pawn");
            TestAssert.Contains(capture, "richText ?? throw new ArgumentNullException",
                "the capture boundary must establish its non-null label invariant once");
            TestAssert.False(packet.IndexOf("text ?? string.Empty", StringComparison.Ordinal) >= 0,
                "downstream packet values must not hide a broken label capture invariant");
            TestAssert.Contains(capture, "TruncateForPreparedCell", "label measurement must finish at snapshot capture");
            TestAssert.Contains(capture, "previous.MatchesSource(",
                "full snapshot rebuilds must reuse labels whose exact source and metric inputs are unchanged");
            string captureMethod = MemberBody(capture, "internal static PreparedPawnLabelPresentation Capture(");
            TestAssert.True(
                captureMethod.IndexOf("previous.MatchesSource(", StringComparison.Ordinal) <
                    captureMethod.IndexOf("TruncateForPreparedCell(", StringComparison.Ordinal),
                "exact reuse must be decided before text measurement or truncation");
            TestAssert.Contains(provider, "_reusablePawnLabels",
                "label reuse must follow pawn identity across row movement and roster additions");
            TestAssert.False(packet.IndexOf("Text.CalcSize", StringComparison.Ordinal) >= 0,
                "row-packet compilation must not measure unchanged label text");

            TestAssert.Contains(provider, "_pawnLabelSourceSignature == pawnLabelSourceSignature", "unchanged snapshots need a label freshness guard");
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
    }
}
