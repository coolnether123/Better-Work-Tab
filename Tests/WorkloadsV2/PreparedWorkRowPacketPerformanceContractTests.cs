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
            string surface = Read(root, "Source", "UI", "WorkGrid", "Rendering", "WorkGridDrawingSurface.cs");
            string compatibility = Read(root, "Source", "UI", "WorkGrid", "Compatibility", "WorkGridVanillaCompatibilityPolicy.cs");
            string retained = Read(root, "Source", "UI", "WorkGrid", "Rendering", "RetainedWorkBoxRowCache.cs");
            string preparedBox = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkBoxRenderer.cs");
            string subWork = Read(root, "Source", "UI", "WorkGiverReassignments", "WorkGiverPriorityBoxRenderer.cs");
            string snapshot = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshot.cs");
            string provider = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string window = Read(root, "Source", "UI", "MainTabWindow_BetterWork.cs");

            StableRepaintUsesOrderedRowCommands(body, optimized, subWork);
            OptimizedInputPolicyEliminatesRedundantWheelDispatch(
                body,
                surface,
                optimized,
                compatibility);
            PacketBuilderConsumesPreparedStateOnly(packet);
            RetainedPriorityGlyphsStayOnTheLivePath(preparedBox, packet, optimized);
            PreparedRowsRejectStalePawns(optimized);
            SparseUpdatesAdvanceOnlyDirtyRows(snapshot, provider);
            RetainedHitsUsePrecomputedBoundsAndFingerprint(retained);
            RetainedFailuresStayRevisionScoped(retained, preparedBox);
            ResourceOwnershipIsBounded(retained, optimized, window);
            ReopenRetriesOnlyRevisionScopedRowFailures(retained, optimized, window);
            RepresentativeStableWorkIsRemoved();
        }

        private static void StableRepaintUsesOrderedRowCommands(
            string body,
            string optimized,
            string subWork)
        {
            string drawRow = MemberBody(body, "private static void DrawPawnRow(");
            TestAssert.Contains(drawRow, "TryDrawPreparedPawnRow(", "row drawing must prefer the prepared packet path");
            TestAssert.Contains(drawRow, "DrawDirectPawnRow(", "row drawing must retain the direct fallback path");
            TestAssert.Contains(body, "Event.current.type != EventType.Repaint", "row packets must be Repaint-only");
            TestAssert.Contains(body, "preparedLayer.TryGetPreparedRow", "stable rows must ask the optimized layer for a packet");
            TestAssert.Contains(body, "packet.Commands", "the body must traverse ordered packet commands instead of all columns");
            TestAssert.Contains(body, "PreparedWorkRowCommandKind.RetainedRun", "retained runs must remain ordered with native columns");
            string preparedRow = MemberBody(body, "private static bool TryDrawPreparedPawnRow(");
            TestAssert.False(
                preparedRow.IndexOf("columnIndex < 0", StringComparison.Ordinal) >= 0,
                "retained hits must trust producer-validated commands instead of silently drawing a partial row");
            TestAssert.Contains(body, "snapshotLayer?.BeginRow();", "direct drawing must preserve snapshot row ownership");
            TestAssert.Contains(body, "snapshotLayer?.EndRow();", "legacy drawing must close snapshot row ownership");
            TestAssert.Contains(body, "snapshotLayer.ShouldVisitCell", "direct drawing must preserve cell culling");
            TestAssert.Contains(body, "snapshotLayer.TryDrawCell", "direct drawing must preserve snapshot cell dispatch");
            TestAssert.Contains(body, "DrawNativePawnCell(", "unsupported cells must retain their native worker path");

            string eligibility = MemberBody(optimized, "public bool TryGetPreparedRow(");
            TestAssert.Contains(eligibility, "ColumnReorderAnimationState.IsActive", "column animation must use direct clipped rendering");
            TestAssert.Contains(eligibility, "DividerCollapseAnimationState.HasActiveAnimations", "collapsing rows must use direct clipped rendering");
            TestAssert.Contains(eligibility, "DividerInsertionAnimationState.HasActiveAnimations", "inserted rows must use direct clipped rendering");
            TestAssert.Contains(eligibility, "SubWorkDrilldownState.IsTransitioning", "sub-work transitions must use direct clipped rendering");
            TestAssert.Contains(eligibility, "!_hasMatchingLayoutRevision", "stale pass geometry must use direct clipped rendering");
            TestAssert.Contains(eligibility, "!_liveReferenceTopologyValid", "incomplete live interaction topology must use direct clipped rendering");
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

        private static void OptimizedInputPolicyEliminatesRedundantWheelDispatch(
            string body,
            string surface,
            string optimized,
            string compatibility)
        {
            string drawRows = MemberBody(body, "internal void DrawRows(");
            int beginScroll = drawRows.IndexOf("Widgets.BeginScrollView", StringComparison.Ordinal);
            int traversalGuard = drawRows.IndexOf(
                "traversalPolicy.ShouldTraverseRows(Event.current)",
                StringComparison.Ordinal);
            int drawPhases = drawRows.IndexOf("DrawOrderedPhases(in pass)", StringComparison.Ordinal);

            TestAssert.True(beginScroll >= 0, "the body must let the scroll view process input");
            TestAssert.True(
                traversalGuard > beginScroll && traversalGuard < drawPhases,
                "optimized wheel events must stop before visible-row and cell dispatch");
            TestAssert.Contains(
                drawRows,
                "finally\n            {\n                Widgets.EndScrollView();",
                "the wheel-event fast path must still balance the scroll-view scope");
            TestAssert.Contains(
                surface,
                "interface IWorkGridRowEventTraversalPolicy",
                "row traversal suppression must be an explicit optimized-layer capability");

            string policy = MemberBody(optimized, "public bool ShouldTraverseRows(");
            TestAssert.Contains(policy, "EventType.ScrollWheel", "the optimized policy must identify wheel events explicitly");
            TestAssert.Contains(policy, "currentEvent.rawType == EventType.ScrollWheel", "a consumed wheel must remain distinguishable from other Used events");
            TestAssert.Contains(policy, "_delegateSleekPriorityCells", "Sleek must retain native wheel dispatch");
            TestAssert.Contains(policy, "_delegateScheduleCells", "open schedules must retain native wheel dispatch");
            TestAssert.Contains(
                compatibility,
                "CanSkipViewportScrollRowTraversal(",
                "the optimized layer must prove its topology is safe before skipping wheel dispatch");
            TestAssert.Contains(compatibility, "!CanSnapshotVanillaPriorityCells()", "priority-worker patch ownership must be revalidated for each wheel event");
            TestAssert.Contains(compatibility, "HooksAreUnextended(", "externally patched worker call chains must fail closed");
            TestAssert.Contains(compatibility, "typeof(CopyPasteUI)", "copy/paste wheel passivity must include its invoked UI hook");
            TestAssert.Contains(compatibility, "typeof(PawnColumnWorker_Icon)", "faction wheel passivity must include its inherited icon worker hooks");
            TestAssert.Contains(compatibility, "FluffyWorkTabGateway.IsFluffyWorkGiverColumn", "Fluffy-owned workers must fail closed");
            TestAssert.Contains(compatibility, "default:\n                    return false;", "unknown native workers must fail closed");
        }

        private static void PacketBuilderConsumesPreparedStateOnly(string packet)
        {
            TestAssert.Contains(packet, "Snapshot.Cells[cellIndex]", "packets must consume immutable prepared cells");
            TestAssert.Contains(packet, "WorkGridSubWorkVisualState presentation = cell.SubWork", "sub-work packets must consume prepared child scalars");
            TestAssert.Contains(packet, "PreparedWorkRowCommandKind.NativeColumn", "unsupported workers must remain sparse native commands");
            TestAssert.Contains(packet, "PreparedWorkRowCommandKind.RetainedRun", "prepared cells must be grouped into retained runs");
            TestAssert.Contains(packet, "PreparedColumnDisposition.Hidden", "hidden focus-view parents must remain distinct from native fallback");
            TestAssert.Contains(packet, "request.Geometry.Columns[columnIndex]", "packet geometry must come from the finished pass geometry");
            TestAssert.Contains(packet, "return delegateScheduleCells", "every sub-work cell must delegate while the live schedule owner is open");
            string complete = MemberBody(packet, "internal PreparedWorkRowPacket Complete(");
            TestAssert.Contains(
                complete,
                "HasValidCommandTopology(",
                "the packet producer must validate the complete command stream before caching it");
            TestAssert.False(packet.IndexOf("ParentPriorityRead", StringComparison.Ordinal) >= 0, "packet construction must not read live parent priorities");
            TestAssert.False(packet.IndexOf("AverageOfRelevantSkillsFor", StringComparison.Ordinal) >= 0, "packet construction must not read pawn skills");
            TestAssert.False(packet.IndexOf("WorkGiverCellPresentationCache.Resolve", StringComparison.Ordinal) >= 0, "packet construction must not resolve live sub-work presentation");
            TestAssert.False(packet.IndexOf("SubWorkDrilldownState", StringComparison.Ordinal) >= 0, "packet construction must not query live transition state");
            TestAssert.False(packet.IndexOf("Text.CalcSize", StringComparison.Ordinal) >= 0, "packet construction must not measure labels from ambient GUI state");
            TestAssert.False(packet.IndexOf("Workload", StringComparison.Ordinal) >= 0, "packet construction must not read workload domains");
        }

        private static void RetainedPriorityGlyphsStayOnTheLivePath(
            string preparedBox,
            string packet,
            string optimized)
        {
            TestAssert.Contains(
                preparedBox,
                "internal const float PriorityLabelOutset = 3f;",
                "native priority label geometry must keep the vanilla three-pixel outset");
            TestAssert.Contains(
                preparedBox,
                "internal const float LowSkillWarningOutset = 2f;",
                "native warning geometry must keep the vanilla two-pixel outset");
            string retained = MemberBody(preparedBox, "internal static bool DrawRetained(");
            TestAssert.False(
                retained.IndexOf("Widgets.Label", StringComparison.Ordinal) >= 0,
                "transparent retained surfaces must not compose manual priority glyphs");
            TestAssert.False(
                retained.IndexOf("DrawPriorityLabel(", StringComparison.Ordinal) >= 0,
                "retained work boxes must leave manual priority glyphs to the live pass");

            string live = MemberBody(
                preparedBox,
                "internal static void DrawLivePriorityLabel(");
            TestAssert.False(
                live.IndexOf("CharacterInfo", StringComparison.Ordinal) >= 0,
                "retained priority glyphs must not manually reconstruct font metrics");
            TestAssert.False(
                live.IndexOf("GL.QUADS", StringComparison.Ordinal) >= 0,
                "retained priority glyphs must not use a second custom rasterizer");
            TestAssert.Contains(
                live,
                "DrawPriorityLabel(",
                "manual priority glyphs must use the shared direct label helper on screen");
            TestAssert.Contains(
                live,
                "Text.Anchor = TextAnchor.MiddleCenter",
                "live priority glyphs must keep vanilla centered text alignment");
            TestAssert.Contains(
                live,
                "compactText",
                "live priority glyphs must preserve the compact sub-work font decision");
            string hasLabel = MemberBody(preparedBox, "internal static bool HasPriorityLabel(");
            TestAssert.Contains(
                hasLabel,
                "WorkCellVisualFlags.Disabled",
                "disabled cells must not reintroduce a numeral that direct drawing omits");

            string direct = MemberBody(preparedBox, "private static void DrawForeground(");
            TestAssert.Contains(
                direct,
                "DrawPriorityLabel(",
                "direct and retained live priority glyphs must share one label helper");
            TestAssert.Contains(
                preparedBox,
                "boxRect.ContractedBy(-PriorityLabelOutset)",
                "all priority glyphs must keep the vanilla three-pixel label geometry");

            TestAssert.Contains(
                packet,
                "LivePrioritySlotIndexes",
                "prepared runs must publish sparse live-priority slot indexes");
            string run = MemberBody(packet, "internal PreparedWorkRowRun(");
            TestAssert.Contains(
                run,
                "livePrioritySlotIndexes",
                "prepared run packets must carry the live-priority index set");
            string preparedDraw = MemberBody(optimized, "public void DrawPreparedRun(");
            int retainedDraw = preparedDraw.IndexOf("_retainedRows.TryDraw(", StringComparison.Ordinal);
            int liveDraw = preparedDraw.IndexOf("DrawPreparedRunPriorityLabels(", StringComparison.Ordinal);
            TestAssert.True(
                retainedDraw >= 0 && liveDraw > retainedDraw,
                "prepared rows must draw live numerals only after retained presentation succeeds");
            TestAssert.Contains(
                preparedDraw,
                "if (!retained)\n            {\n                DrawPreparedRunDirect(",
                "retained failure must use the direct path instead of a second live-label pass");
            string directFallback = MemberBody(optimized, "private void DrawPreparedRunDirect(");
            TestAssert.False(
                directFallback.IndexOf("DrawPreparedRunPriorityLabels(", StringComparison.Ordinal) >= 0,
                "direct prepared-row fallback must draw each manual numeral through DrawInBatch exactly once");
            string flush = MemberBody(optimized, "private void FlushRetainedCells(");
            TestAssert.Contains(
                flush,
                "if (retained)\n            {\n                DrawPendingPriorityLabels();",
                "batched retained cells must draw live numerals only on the retained-success path");
            TestAssert.Contains(
                flush,
                "if (!retained)\n                {\n                    Text.Font = GameFont.Medium;",
                "batched retained failure must keep the existing direct cell path");

            string stable = MemberBody(preparedBox, "internal static bool DrawRetained(");
            TestAssert.Contains(
                stable,
                "out RetainedWorkBoxDrawFailure failure",
                "retained composition must report resource readiness separately from permanent unsupported state");
            TestAssert.Contains(
                stable,
                "failure = RetainedWorkBoxDrawFailure.ResourceUnavailable",
                "retained composition must classify unavailable texture/material resources");
            TestAssert.Contains(
                stable,
                "WidgetsWork.WorkBoxOverlay_Warning",
                "retained cells must keep the authoritative low-skill warning pixel");
            TestAssert.Contains(
                stable,
                "WidgetsWork.WorkBoxOverlay_PreceptWarning",
                "retained cells must keep the authoritative ideology warning pixel");
            string dynamic = MemberBody(
                preparedBox,
                "internal static void DrawDynamicOverlays(");
            TestAssert.False(
                dynamic.IndexOf("WorkBoxOverlay_Warning", StringComparison.Ordinal) >= 0 ||
                dynamic.IndexOf("WorkBoxOverlay_PreceptWarning", StringComparison.Ordinal) >= 0,
                "dynamic overlays must not draw a second warning texture over retained cells");
        }

        private static void PreparedRowsRejectStalePawns(string optimized)
        {
            string matching = MemberBody(optimized, "private bool HasMatchingLiveRow(");
            TestAssert.Contains(
                matching,
                "IsLiveRenderablePawn(live.Pawn)",
                "prepared row topology must reject a pawn that is no longer renderable");
            string livePawn = MemberBody(optimized, "private bool TryGetLivePawn(");
            TestAssert.Contains(
                livePawn,
                "IsLiveRenderablePawn(pawn)",
                "live interaction lookup must reject stale pawn rows before optimized drawing");
            string eligibility = MemberBody(optimized, "private static bool IsLiveRenderablePawn(");
            TestAssert.Contains(eligibility, "!pawn.Dead", "dead pawns must use authoritative fallback drawing");
            TestAssert.Contains(eligibility, "!pawn.Destroyed", "destroyed pawns must use authoritative fallback drawing");
            TestAssert.Contains(eligibility, "pawn.workSettings != null", "pawns without work settings must use fallback drawing");
            TestAssert.Contains(eligibility, "pawn.workSettings.EverWork", "ineligible pawns must use authoritative fallback drawing");
        }

        private static void SparseUpdatesAdvanceOnlyDirtyRows(string snapshot, string provider)
        {
            TestAssert.Contains(snapshot, "WorkGridPreparedRowSpan", "snapshots must publish prepared row spans");
            TestAssert.Contains(snapshot, "internal long TopologyRevision", "snapshots must distinguish topology from content changes");
            TestAssert.Contains(provider, "previous.PreparedRows.WithReplacements", "unaffected row spans must be retained");
            TestAssert.Contains(provider, "previous.TopologyRevision", "sparse updates must preserve cell topology");
            TestAssert.Contains(provider, "FindBestPawnId(table, workType, worker)", "priority updates must refresh comparison-dependent best-pawn identity");
            TestAssert.Contains(provider, "changedBestPawnIds", "old and new best-pawn rows must receive row-local invalidation");
        }

        private static void RetainedHitsUsePrecomputedBoundsAndFingerprint(string retained)
        {
            string prepared = MemberBody(retained, "internal PreparedRun(Cell[] cells)");
            TestAssert.Contains(prepared, "Bounds = GetBounds(Cells)", "run bounds must be computed once during packet preparation");
            TestAssert.Contains(prepared, "StaticFingerprint = GetStaticFingerprint(Cells)", "cell fingerprints must be computed once during packet preparation");
            string bounds = MemberBody(retained, "private static Rect GetBounds(");
            TestAssert.Contains(bounds, "float stableOutset", "prepared bounds must account for stable visuals that extend beyond cell boxes");
            TestAssert.Contains(bounds, "GetStableVisualOutset", "stable bounds must derive extent from prepared visual state");
            TestAssert.Contains(bounds, "bounds.ExpandedBy(stableOutset)", "retained surfaces must include the full stable visual extent");
            string stableOutset = MemberBody(retained, "private static float GetStableVisualOutset(");
            TestAssert.False(stableOutset.IndexOf("PriorityLabelOutset", StringComparison.Ordinal) >= 0, "live priority numerals must not expand retained surface bounds");
            TestAssert.Contains(stableOutset, "PreparedWorkBoxRenderer.LowSkillWarningOutset", "warning bounds must use the native warning outset");
            string fingerprint = MemberBody(retained, "private static ulong GetFingerprint(");
            TestAssert.False(fingerprint.IndexOf("for (", StringComparison.Ordinal) >= 0, "steady retained hits must not rescan cells");
            TestAssert.Contains(fingerprint, "staticFingerprint", "steady fingerprints must mix the packet fingerprint in O(1)");
            TestAssert.False(fingerprint.IndexOf("Text.CurFontStyle", StringComparison.Ordinal) >= 0, "steady hits must not depend on ambient GUI font state");
            string staticFingerprint = MemberBody(retained, "private static ulong GetStaticFingerprint(");
            TestAssert.False(staticFingerprint.IndexOf("visual.Priority", StringComparison.Ordinal) >= 0, "manual priority values must not rebuild texture-only retained surfaces");
            TestAssert.False(staticFingerprint.IndexOf("PriorityColor", StringComparison.Ordinal) >= 0, "manual priority colors belong to the live label pass");
            TestAssert.False(staticFingerprint.IndexOf("CompactText", StringComparison.Ordinal) >= 0, "font-size metadata must not rebuild texture-only retained surfaces");
            TestAssert.Contains(staticFingerprint, "retainedCheck", "retained fingerprints must preserve checkbox visibility changes");
            string buildSurface = MemberBody(retained, "private static bool BuildSurface(");
            TestAssert.False(buildSurface.IndexOf("Widgets.Label", StringComparison.Ordinal) >= 0, "retained surface composition must remain texture-only");
            TestAssert.False(buildSurface.IndexOf("Text.Font", StringComparison.Ordinal) >= 0, "retained surface composition must not prepare unused glyph state");
            int groupEnd = buildSurface.IndexOf("GUI.EndGroup();", StringComparison.Ordinal);
            int flush = buildSurface.IndexOf("GL.Flush();", StringComparison.Ordinal);
            TestAssert.True(
                groupEnd >= 0 && flush > groupEnd,
                "texture-only retained composition must flush its queued draw stream before restoring the render target");
            TestAssert.True(
                flush == buildSurface.LastIndexOf("GL.Flush();", StringComparison.Ordinal),
                "retained row composition must flush once per surface, not once per cell");
        }

        private static void RetainedFailuresStayRevisionScoped(
            string retained,
            string preparedBox)
        {
            TestAssert.Contains(
                preparedBox,
                "RetainedWorkBoxDrawFailure.ResourceUnavailable",
                "resource readiness failures must be classified explicitly");
            TestAssert.Contains(
                retained,
                "RetainedWorkBoxDrawFailure.Unsupported",
                "permanent fallback must require an explicit unsupported result");
            TestAssert.Contains(
                retained,
                "private void HandleCompositionException(",
                "retained exceptions must be classified at the cache boundary");
            TestAssert.Contains(
                retained,
                "if (exception is NotSupportedException)",
                "only a proven unsupported composition exception may disable the cache permanently");
            TestAssert.Contains(
                retained,
                "LatchResourceFailure(\n                    renderResourcesRevision",
                "transient composition failures must latch to the current resource revision");

            string rebuild = MemberBody(retained, "private bool TryRebuildSurfaceIfChanged(");
            TestAssert.Contains(
                rebuild,
                "failure == RetainedWorkBoxDrawFailure.Unsupported",
                "surface rebuilds must distinguish unsupported composition from resource readiness");
            TestAssert.Contains(
                rebuild,
                "LatchResourceFailure(",
                "surface rebuild resource failures must use the revision latch");
            string build = MemberBody(retained, "private static bool BuildSurface(");
            TestAssert.Contains(
                build,
                "catch (NotSupportedException)",
                "surface composition must only classify an explicit unsupported API as permanent");

            string preparedDraw = MemberBody(
                retained,
                "internal bool TryDraw(\n            PreparedRun run,");
            TestAssert.Contains(
                preparedDraw,
                "HandleCompositionException(renderResourcesRevision, exception)",
                "prepared-row exceptions must not permanently disable on an unclassified transient failure");
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
            TestAssert.Contains(release, "FinalizeTransientRenderState();", "resource release must first restore any active retained render target");
            TestAssert.Contains(release, "_retainedRows.Dispose();", "the optimized renderer must release every retained row surface");
            string resolution = MemberBody(window, "public override void Notify_ResolutionChanged()");
            TestAssert.Contains(
                resolution,
                "ReleaseRetainedResources();",
                "resolution changes must release retained row surfaces");
            string close = MemberBody(window, "private void ResetTransientWindowState()");
            TestAssert.Contains(
                close,
                "_optimizedWorkGridRenderer.FinalizeTransientRenderState();",
                "ordinary Work-tab close must finalize transient row rendering");
        }

        private static void ReopenRetriesOnlyRevisionScopedRowFailures(
            string retained,
            string optimized,
            string window)
        {
            string retryLatch = MemberBody(
                retained,
                "internal void ResetResourceFailureLatchForReopen()");
            TestAssert.Contains(
                retryLatch,
                "_failedRenderResourcesRevision = int.MinValue;",
                "a new Work-tab open must retry only a revision-scoped row allocation failure");
            TestAssert.False(
                retryLatch.IndexOf("_disabled", StringComparison.Ordinal) >= 0,
                "a composition failure must keep the permanent direct-render fallback latched");

            string rendererRetry = MemberBody(
                optimized,
                "internal void ResetRetainedRowResourceFailureLatchForReopen()");
            TestAssert.Contains(
                rendererRetry,
                "_retainedRows.ResetResourceFailureLatchForReopen();",
                "the renderer must keep row-cache retry policy at its ownership boundary");

            string reopen = MemberBody(window, "public override void PreOpen()");
            TestAssert.Contains(
                reopen,
                "_optimizedWorkGridRenderer.ResetRetainedRowResourceFailureLatchForReopen();",
                "reopening the cached Work tab must restore allocation retries without releasing healthy rows");
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
