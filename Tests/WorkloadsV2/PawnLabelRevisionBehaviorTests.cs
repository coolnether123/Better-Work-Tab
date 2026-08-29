using System;
using System.IO;
using Better_Work_Tab.UI.WorkGrid.Invalidation;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Executable checks for the label-revision boundary. The source assertions
    /// pin the Unity lifecycle wiring; the ledger checks exercise the production
    /// revision and sparse-target behavior without constructing RimWorld UI.
    /// </summary>
    internal static class PawnLabelRevisionBehaviorTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "MainTabWindow_BetterWork.cs"),
                "pawn-label revision behavior contracts");
            string window = Read(root, "Source", "UI", "MainTabWindow_BetterWork.cs");
            string audit = Read(
                root,
                "Source", "UI", "WorkGrid", "Invalidation", "WorkGridInvalidationAudit.cs");

            OpenBoundaryInvalidatesLabelsWithoutReleasingRows(window);
            CloseAndReopenKeepsTheResourceOwner(window);
            ExternalAuditIsSlowAndNarrow(audit);
            AuditBaselinesRemainIndependent(audit);
            SparsePriorityEditsRemainSparse();
        }

        private static void OpenBoundaryInvalidatesLabelsWithoutReleasingRows(string window)
        {
            string open = MemberBody(window, "public override void PreOpen()");
            TestAssert.Contains(
                open,
                "WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.PawnLabel);",
                "opening the cached tab must create an immediate label freshness boundary");
            TestAssert.False(
                open.IndexOf("WorkTabDirtyFlags.All", StringComparison.Ordinal) >= 0,
                "label freshness must not broad-invalidate the work tab");
            TestAssert.False(
                open.IndexOf("ReleaseRetainedResources", StringComparison.Ordinal) >= 0,
                "label freshness must preserve healthy retained rows");
        }

        private static void CloseAndReopenKeepsTheResourceOwner(string window)
        {
            string close = MemberBody(window, "public override void PreClose()");
            string postClose = MemberBody(window, "public override void PostClose()");
            TestAssert.False(
                close.IndexOf("ReleaseRetainedResources", StringComparison.Ordinal) >= 0,
                "ordinary close must not tear down retained GPU resources");
            TestAssert.False(
                postClose.IndexOf("ReleaseRetainedResources", StringComparison.Ordinal) >= 0,
                "ordinary close must leave the retained resource owner reusable");
            TestAssert.Contains(
                MemberBody(window, "private void ResetTransientWindowState()"),
                "_optimizedWorkGridRenderer.FinalizeTransientRenderState();",
                "close must still finish transient render state before reopening");

            var ledger = new WorkGridInvalidationLedger();
            WorkGridRevisionSet before = ledger.Current;
            ledger.Invalidate(WorkGridInvalidationCategory.PawnLabel);
            WorkGridRevisionSet after = ledger.Current;
            TestAssert.Equal(
                before.PawnLabel + 1,
                after.PawnLabel,
                "a close/reopen freshness event must advance the label revision");
            TestAssert.Equal(
                before.Priority,
                after.Priority,
                "a label freshness event must not invalidate priority cells");
        }

        private static void ExternalAuditIsSlowAndNarrow(string audit)
        {
            string poll = MemberBody(audit, "internal static void Poll(PawnTable table)");
            string labelPoll = MemberBody(
                audit,
                "private static void PollPawnLabelSignature(PawnTable table)");
            TestAssert.Contains(
                poll,
                "PollPawnLabelSignature(table);",
                "the compatibility audit must own the external-label backstop");
            TestAssert.Contains(
                labelPoll,
                "UnityEngine.Time.frameCount",
                "label auditing must continue while the game is paused");
            TestAssert.Contains(
                labelPoll,
                "PawnLabelAuditIntervalFrames",
                "external label detection must stay outside the repaint hot path");
            TestAssert.Contains(
                labelPoll,
                "WorkTabDirtyFlags.PawnLabel",
                "an external label write must invalidate only prepared labels");
            string signature = MemberBody(
                audit,
                "private static int ComputePawnLabelSignature(PawnTable table)");
            TestAssert.False(
                signature.IndexOf("PawnColorDatabase.Version", StringComparison.Ordinal) >= 0,
                "the slow audit must not duplicate the O(1) color-version owner");
            TestAssert.Contains(
                signature,
                "pawn.Name?.ToStringShort",
                "the backstop must still detect external pawn-name writes");
            TestAssert.Contains(
                signature,
                "pawn.story?.Title",
                "the backstop must still detect external title writes");
        }

        private static void AuditBaselinesRemainIndependent(string audit)
        {
            string labelPoll = MemberBody(
                audit,
                "private static void PollPawnLabelSignature(PawnTable table)");
            string regularPoll = MemberBody(audit, "internal static void Poll(PawnTable table)");
            TestAssert.Contains(
                labelPoll,
                "_lastPawnLabelAuditRevision = revisions.PawnLabel;",
                "label polling must publish its own revision baseline");
            TestAssert.Contains(
                labelPoll,
                "_hasPawnLabelAuditRevision = true;",
                "the label baseline must be initialized independently");
            TestAssert.False(
                labelPoll.IndexOf("_lastTrackedRevisions =", StringComparison.Ordinal) >= 0,
                "label polling must not mask unrelated regular-audit changes");
            TestAssert.Contains(
                regularPoll,
                "_lastTrackedRevisions = revisions;",
                "the regular audit must retain ownership of its own baseline");
        }

        private static void SparsePriorityEditsRemainSparse()
        {
            var ledger = new WorkGridInvalidationLedger();
            WorkGridRevisionSet before = ledger.Current;
            var key = new WorkGridPriorityKey(17, 42);
            ledger.InvalidatePriority(key);
            WorkGridRevisionSet after = ledger.Current;
            TestAssert.Equal(
                before.Priority + 1,
                after.Priority,
                "one priority edit must advance the priority revision once");
            TestAssert.Equal(
                before.PawnLabel,
                after.PawnLabel,
                "a sparse priority edit must not invalidate pawn labels");
            TestAssert.Equal(
                1,
                ledger.PriorityDirtyCount,
                "one priority edit must retain one exact dirty target");
            TestAssert.True(
                ledger.IsPriorityDirty(key),
                "the exact edited priority target must remain dirty until consumed");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int index = 0; index < parts.Length; index++)
            {
                path = Path.Combine(path, parts[index]);
            }

            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string MemberBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source member " + signature);
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "source member has no opening brace");
            int depth = 0;
            for (int index = open; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }

            throw new InvalidOperationException("source member has no closing brace");
        }
    }
}
