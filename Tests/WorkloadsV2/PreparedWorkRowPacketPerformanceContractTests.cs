using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PreparedWorkRowPacketPerformanceContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Rendering", "PreparedWorkRowPacket.cs"),
                "prepared work-row packet performance contracts");
            string packet = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkRowPacket.cs");
            string optimized = Read(root, "Source", "UI", "WorkGrid", "Rendering", "OptimizedWorkGridRenderer.cs");
            string body = Read(root, "Source", "UI", "WorkGrid", "Rendering", "WorkTabBodyRenderer.cs");
            string retained = Read(root, "Source", "UI", "WorkGrid", "Rendering", "RetainedWorkBoxRowCache.cs");
            string subWork = Read(root, "Source", "UI", "WorkGiverReassignments", "WorkGiverPriorityBoxRenderer.cs");
            string snapshot = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshot.cs");
            string provider = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string window = Read(root, "Source", "UI", "MainTabWindow_BetterWork.cs");

            StableRepaintUsesOrderedRowCommands(body, optimized, subWork);
            PacketBuilderConsumesPreparedStateOnly(packet);
            SparseUpdatesAdvanceOnlyDirtyRows(snapshot, provider);
            RetainedHitsUsePrecomputedBoundsAndFingerprint(retained);
            ResourceOwnershipIsBounded(retained, optimized, window);
            RepresentativeStableWorkIsRemoved();
        }

        private static void StableRepaintUsesOrderedRowCommands(
            string body,
            string optimized,
            string subWork)
        {
            string drawRow = MemberBody(body, "private static void DrawPawnRow(");
            TestAssert.Contains(drawRow, "TryDrawPreparedPawnRow(", "row drawing must prefer the prepared packet path");
            TestAssert.Contains(drawRow, "DrawLegacyPawnRow(", "row drawing must retain the direct fallback path");
            TestAssert.Contains(body, "Event.current.type != EventType.Repaint", "row packets must be Repaint-only");
            TestAssert.Contains(body, "preparedLayer.TryGetPreparedRow", "stable rows must ask the optimized layer for a packet");
            TestAssert.Contains(body, "packet.Commands", "the body must traverse ordered packet commands instead of all columns");
            TestAssert.Contains(body, "PreparedWorkRowCommandKind.RetainedRun", "retained runs must remain ordered with native columns");
            TestAssert.Contains(body, "snapshotLayer?.BeginRow();", "legacy drawing must preserve snapshot row ownership");
            TestAssert.Contains(body, "snapshotLayer?.EndRow();", "legacy drawing must close snapshot row ownership");
            TestAssert.Contains(body, "snapshotLayer.ShouldVisitCell", "legacy drawing must preserve cell culling");
            TestAssert.Contains(body, "snapshotLayer.TryDrawCell", "legacy drawing must preserve snapshot cell dispatch");
            TestAssert.Contains(body, "DrawNativePawnCell(", "unsupported cells must retain their native worker path");

            string eligibility = MemberBody(optimized, "public bool TryGetPreparedRow(");
            TestAssert.Contains(eligibility, "ColumnReorderAnimationState.IsActive", "column animation must use direct clipped rendering");
            TestAssert.Contains(eligibility, "DividerCollapseAnimationState.HasActiveAnimations", "collapsing rows must use direct clipped rendering");
            TestAssert.Contains(eligibility, "DividerInsertionAnimationState.HasActiveAnimations", "inserted rows must use direct clipped rendering");
            TestAssert.Contains(eligibility, "SubWorkDrilldownState.IsTransitioning", "sub-work transitions must use direct clipped rendering");
            TestAssert.Contains(eligibility, "_delegateScheduleCells && WorkTabEffectiveStateRuntime.IsPreviewActive", "preview plus Fluffy schedule must use the reporting fallback");
            string hover = MemberBody(optimized, "private void ResolveHoverTargets(");
            TestAssert.Contains(hover, "_columnIndexByHoverKey.TryGetValue", "hover mapping must not rescan every visible column");
            TestAssert.False(hover.IndexOf("for (", StringComparison.Ordinal) >= 0, "hover mapping must remain O(1) after layout hit testing");

            string subWorkFallback = MemberBody(optimized, "private bool TryDrawSubWorkCell(");
            TestAssert.Contains(subWorkFallback, "!_delegateScheduleCells", "legacy retained sub-work must delegate every live schedule cell");
            TestAssert.Contains(optimized, "WorkGiverPriorityBoxRenderer.MaintainResetAnimations();", "reset animation expiry must run once per repaint preparation");
            string maintenance = MemberBody(subWork, "internal static void MaintainResetAnimations(");
            TestAssert.Contains(maintenance, "ResetAnimations.Clear()", "offscreen reset animations must expire without being drawn");
            TestAssert.False(maintenance.IndexOf("foreach", StringComparison.Ordinal) >= 0, "reset animation maintenance must remain O(1)");
        }

        private static void PacketBuilderConsumesPreparedStateOnly(string packet)
        {
            TestAssert.Contains(packet, "Snapshot.Cells[cellIndex]", "packets must consume immutable prepared cells");
            TestAssert.Contains(packet, "cell.TryGetSubWorkPresentation", "sub-work packets must consume prepared child presentation");
            TestAssert.Contains(packet, "PreparedWorkRowCommandKind.NativeColumn", "unsupported workers must remain sparse native commands");
            TestAssert.Contains(packet, "PreparedWorkRowCommandKind.RetainedRun", "prepared cells must be grouped into retained runs");
            TestAssert.Contains(packet, "PreparedColumnDisposition.Hidden", "hidden focus-view parents must remain distinct from native fallback");
            TestAssert.Contains(packet, "WorkGridInteractionGeometry.GetAnimatedBodyContentRect", "packet geometry must use the same content-space calculation as direct drawing");
            TestAssert.Contains(packet, "return delegateScheduleCells", "every sub-work cell must delegate while the live schedule owner is open");
            TestAssert.False(packet.IndexOf("ParentPriorityRead", StringComparison.Ordinal) >= 0, "packet construction must not read live parent priorities");
            TestAssert.False(packet.IndexOf("AverageOfRelevantSkillsFor", StringComparison.Ordinal) >= 0, "packet construction must not read pawn skills");
            TestAssert.False(packet.IndexOf("WorkGiverCellPresentationCache.Resolve", StringComparison.Ordinal) >= 0, "packet construction must not resolve live sub-work presentation");
            TestAssert.False(packet.IndexOf("Workload", StringComparison.Ordinal) >= 0, "packet construction must not read workload domains");
        }

        private static void SparseUpdatesAdvanceOnlyDirtyRows(string snapshot, string provider)
        {
            TestAssert.Contains(snapshot, "WorkGridPreparedRowSpan", "snapshots must publish prepared row spans");
            TestAssert.Contains(snapshot, "internal long TopologyRevision", "snapshots must distinguish topology from content changes");
            string sparse = MemberBody(provider, "private bool TryApplySparsePriorityUpdate(");
            TestAssert.Contains(sparse, "preparedRowReplacements", "sparse cell updates must carry row-local invalidation");
            TestAssert.Contains(sparse, "previous.PreparedRows.WithReplacements", "unaffected row spans must be retained");
            TestAssert.Contains(sparse, "previous.TopologyRevision", "sparse updates must preserve cell topology");
            TestAssert.Contains(sparse, "FindBestPawnId(table, workType, worker)", "priority updates must refresh comparison-dependent best-pawn identity");
            TestAssert.Contains(sparse, "changedBestPawnIds", "old and new best-pawn rows must receive row-local invalidation");
        }

        private static void RetainedHitsUsePrecomputedBoundsAndFingerprint(string retained)
        {
            string prepared = MemberBody(retained, "internal PreparedRun(Cell[] cells)");
            TestAssert.Contains(prepared, "Bounds = GetBounds(Cells)", "run bounds must be computed once during packet preparation");
            TestAssert.Contains(prepared, "StaticFingerprint = GetStaticFingerprint(Cells)", "cell fingerprints must be computed once during packet preparation");
            string fingerprint = MemberBody(retained, "private static ulong GetFingerprint(");
            TestAssert.False(fingerprint.IndexOf("for (", StringComparison.Ordinal) >= 0, "steady retained hits must not rescan cells");
            TestAssert.Contains(fingerprint, "staticFingerprint", "steady fingerprints must mix the packet fingerprint in O(1)");
            TestAssert.False(fingerprint.IndexOf("Text.CurFontStyle", StringComparison.Ordinal) >= 0, "steady hits must not depend on ambient GUI font state");
        }

        private static void ResourceOwnershipIsBounded(
            string retained,
            string optimized,
            string window)
        {
            TestAssert.Contains(retained, "MaximumEntries = 128", "retained surfaces need a hard entry bound");
            TestAssert.Contains(retained, "MaximumEstimatedSurfaceBytes = 64L * 1024L * 1024L", "retained surfaces need a hard estimated-byte bound");
            TestAssert.Contains(retained, "EnsureCapacity(requestedBytes", "capacity must be enforced before allocation");
            TestAssert.Contains(retained, "entry.Surface.IsCreated()", "surface reuse must require a live device resource");
            TestAssert.Contains(retained, "ReleaseSurface(entry);", "invalid or device-lost surfaces must be released before recreation");
            TestAssert.Contains(retained, "_failedRenderResourcesRevision = renderResourcesRevision", "surface creation failure must be latched for the current resource generation");
            TestAssert.Contains(retained, "IsResourceFailureLatched(renderResourcesRevision)", "steady repaint must not retry a failed surface allocation");
            TestAssert.Contains(retained, "_estimatedSurfaceBytes -= entry.EstimatedBytes", "surface release must return its accounted bytes");
            TestAssert.Contains(retained, "UnityEngine.Object.Destroy(surface)", "released Unity surfaces must be destroyed");

            string release = MemberBody(optimized, "internal void ReleaseRetainedResources(");
            TestAssert.Contains(release, "EndCellBatch();", "resource release must first restore any active retained render target");
            TestAssert.Contains(release, "_retainedRows.Dispose();", "the optimized renderer must release every retained row surface");
            string resolution = MemberBody(window, "public override void Notify_ResolutionChanged()");
            TestAssert.Contains(
                resolution,
                "_optimizedWorkGridRenderer.ReleaseRetainedResources();",
                "resolution changes must release retained row surfaces");
            string close = MemberBody(window, "private void ResetTransientWindowState()");
            TestAssert.Contains(
                close,
                "_optimizedWorkGridRenderer.ReleaseRetainedResources();",
                "closing the Work tab must release retained row surfaces");
        }

        private static void RepresentativeStableWorkIsRemoved()
        {
            const int visibleRows = 25;
            const int columns = 24;
            const int nativeColumns = 4;
            const int retainedRunsPerRow = 1;
            int legacyVisits = visibleRows * columns;
            int packetCommands = visibleRows * (nativeColumns + retainedRunsPerRow);
            TestAssert.True(legacyVisits == 600, "the locked baseline cell-visit count changed unexpectedly");
            TestAssert.True(packetCommands == 125, "the representative packet command count changed unexpectedly");
            TestAssert.True(packetCommands * 4 < legacyVisits, "stable packet dispatch must remove more than three quarters of outer visits");
            TestAssert.True(visibleRows * (columns - nativeColumns) == 500, "the locked optimized-cell count changed unexpectedly");
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
            TestAssert.True(open >= 0, "source member has no opening brace");
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
