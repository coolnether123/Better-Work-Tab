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
            RetainedTransparentForegroundStaysOnTheLivePath(preparedBox, packet, optimized);
            PreparedRowsRejectStalePawns(optimized);
            SparseUpdatesAdvanceOnlyDirtyRows(snapshot, provider);
            RetainedHitsUsePrecomputedBoundsAndFingerprint(retained, preparedBox);
            RetainedCompositionRestoresRenderState(retained, preparedBox);
            RetainedPresentationKeepsOwnerClipForAllVerticalPositions(retained, optimized);
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

        private static void RetainedTransparentForegroundStaysOnTheLivePath(
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
            TestAssert.False(
                retained.IndexOf("PassionWorkbox", StringComparison.Ordinal) >= 0,
                "transparent passion icons must not be composed into a retained surface");

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

            string liveWarning = MemberBody(
                preparedBox,
                "internal static void DrawLiveLowSkillWarning(");
            TestAssert.Contains(
                liveWarning,
                "GUI.DrawTexture(",
                "transparent warning borders must use the shared live IMGUI texture path");
            TestAssert.Contains(
                liveWarning,
                "boxRect.ContractedBy(-LowSkillWarningOutset)",
                "live warning borders must keep the vanilla two-pixel geometry");
            string hasWarning = MemberBody(
                preparedBox,
                "internal static bool HasLiveLowSkillWarning(");
            TestAssert.Contains(
                hasWarning,
                "WorkCellVisualFlags.Disabled",
                "disabled cells must not reintroduce a low-skill warning that direct drawing omits");

            string livePassion = MemberBody(
                preparedBox,
                "internal static void DrawLivePassionIcon(");
            TestAssert.Contains(
                livePassion,
                "0.4f * visualAlpha",
                "live passion icons must retain the direct semi-transparent tint");
            TestAssert.Contains(
                livePassion,
                "DrawPassionIcon(boxRect, visual);",
                "live passion icons must share the direct icon geometry and texture choice");
            string hasPassion = MemberBody(
                preparedBox,
                "internal static bool HasLivePassionIcon(");
            TestAssert.Contains(
                hasPassion,
                "WorkCellVisualFlags.Disabled",
                "disabled cells must not reintroduce a passion icon that direct drawing omits");
            string foreground = MemberBody(
                preparedBox,
                "internal static void DrawLiveForeground(");
            int warning = foreground.IndexOf("DrawLiveLowSkillWarning(", StringComparison.Ordinal);
            int passion = foreground.IndexOf("DrawLivePassionIcon(", StringComparison.Ordinal);
            int priority = foreground.IndexOf("DrawLivePriorityLabel(", StringComparison.Ordinal);
            TestAssert.True(
                warning >= 0 && passion > warning && priority > passion,
                "live foreground must retain the direct warning, passion, then priority ordering");

            TestAssert.Contains(
                packet,
                "LiveForegroundSlotIndexes",
                "prepared runs must publish one sparse live-foreground index set");
            string run = MemberBody(packet, "internal PreparedWorkRowRun(");
            TestAssert.Contains(
                run,
                "liveForegroundSlotIndexes",
                "prepared run packets must carry the live-foreground index set");
            TestAssert.Contains(
                packet,
                "HasLiveForeground(",
                "packet construction must select transparent foreground slots without a draw-time cell scan");
            TestAssert.Contains(
                run,
                "liveForegroundSlotIndexes",
                "prepared run packets must carry only the union index set");
            string preparedDraw = MemberBody(optimized, "public void DrawPreparedRun(");
            int retainedDraw = preparedDraw.IndexOf("_retainedRows.TryDraw(", StringComparison.Ordinal);
            int liveDraw = preparedDraw.IndexOf("DrawPreparedRunLiveForeground(", StringComparison.Ordinal);
            TestAssert.True(
                retainedDraw >= 0 && liveDraw > retainedDraw,
                "prepared rows must draw live transparent foreground only after retained presentation succeeds");
            TestAssert.Contains(
                preparedDraw,
                "if (!retained)\n            {\n                DrawPreparedRunDirect(",
                "retained failure must use the direct path instead of a second live-label pass");
            string directFallback = MemberBody(optimized, "private void DrawPreparedRunDirect(");
            TestAssert.False(
                directFallback.IndexOf("DrawPreparedRunLiveForeground(", StringComparison.Ordinal) >= 0,
                "direct prepared-row fallback must draw each transparent foreground pixel through DrawInBatch exactly once");
            TestAssert.False(
                directFallback.IndexOf("DrawLivePassionIcon(", StringComparison.Ordinal) >= 0,
                "direct prepared-row fallback must not add a second passion icon pass");
            string flush = MemberBody(optimized, "private void FlushRetainedCells(");
            TestAssert.Contains(
                flush,
                "if (retained)\n                {\n                    PreparedWorkBoxRenderer.DrawLiveForeground(",
                "batched retained cells must draw live transparent foreground only on the retained-success path");
            TestAssert.False(
                flush.IndexOf("_pendingLowSkillWarnings", StringComparison.Ordinal) >= 0,
                "batched retained cells must not allocate a second warning-only traversal");
            TestAssert.Contains(
                flush,
                "else\n                {\n                    Text.Font = GameFont.Medium;",
                "batched retained failure must keep the existing direct cell path");

            string stable = MemberBody(preparedBox, "internal static bool DrawRetained(");
            TestAssert.Contains(
                stable,
                "out RetainedWorkBoxDrawFailure failure",
                "retained composition must report resource readiness separately from permanent unsupported state");
            TestAssert.Contains(
                stable,
                "failure = RetainedWorkBoxDrawFailure.ResourceUnavailable",
                "retained composition must classify unavailable texture resources");
            TestAssert.False(
                stable.IndexOf("WidgetsWork.WorkBoxOverlay_Warning", StringComparison.Ordinal) >= 0,
                "transparent low-skill warning borders must not be composed into a retained surface");
            TestAssert.False(
                stable.IndexOf("PassionWorkbox", StringComparison.Ordinal) >= 0,
                "transparent passion icons must not be composed into a retained surface");
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

        private static void RetainedCompositionRestoresRenderState(
            string retained,
            string preparedBox)
        {
            string build = MemberBody(retained, "private static bool BuildSurface(");
            int target = build.IndexOf("RenderTexture.active = surface;", StringComparison.Ordinal);
            int configureSrgb = build.IndexOf(
                "PreparedWorkBoxRenderer.ConfigureSrgbWriteForSrgbTarget();",
                target,
                StringComparison.Ordinal);
            int viewport = build.IndexOf(
                "GL.Viewport(new Rect(0f, 0f, surface.width, surface.height));",
                StringComparison.Ordinal);
            int restoreTarget = build.IndexOf("RenderTexture.active = previous;", StringComparison.Ordinal);
            int restoreViewport = build.IndexOf("previousViewportWidth", restoreTarget, StringComparison.Ordinal);
            TestAssert.True(
                target >= 0 && configureSrgb > target && viewport > configureSrgb,
                "retained composition must establish sRGB output and the offscreen viewport after binding its target");
            TestAssert.True(
                restoreTarget > viewport && restoreViewport > restoreTarget,
                "retained composition must restore the caller target viewport after emitting quads");
            TestAssert.Contains(
                build,
                "GL.sRGBWrite",
                "retained composition must preserve the caller's sRGB write state");
            TestAssert.Contains(
                build,
                "GL.InvalidateState();",
                "retained composition must invalidate Unity's cached GL state after direct emission");

            string sentinel = MemberBody(
                preparedBox,
                "internal static bool TryValidateRetainedComposition(");
            int sentinelTarget = sentinel.IndexOf("RenderTexture.active = surface;", StringComparison.Ordinal);
            int sentinelConfigureSrgb = sentinel.IndexOf(
                "ConfigureSrgbWriteForSrgbTarget();",
                sentinelTarget,
                StringComparison.Ordinal);
            TestAssert.True(
                sentinelTarget >= 0 && sentinelConfigureSrgb > sentinelTarget,
                "the capability sentinel must establish the same sRGB target policy after binding its target");
            TestAssert.Contains(
                sentinel,
                "GL.Viewport(new Rect(0f, 0f, surface.width, surface.height));",
                "the capability sentinel must use the same explicit offscreen viewport as rows");
            string srgbPolicy = MemberBody(
                preparedBox,
                "internal static void ConfigureSrgbWriteForSrgbTarget()");
            TestAssert.Contains(
                srgbPolicy,
                "QualitySettings.activeColorSpace == ColorSpace.Linear",
                "retained sRGB targets must derive write conversion from the project color space, not caller GL state");
            TestAssert.Contains(
                sentinel,
                "Texture2D.whiteTexture",
                "the capability sentinel must exercise the immediate textured-quad path");
            int readback = sentinel.IndexOf("ReadPixels(", StringComparison.Ordinal);
            TestAssert.True(readback >= 0, "the capability sentinel must verify a written pixel");
            TestAssert.False(
                sentinel.IndexOf("ReadPixels(", readback + 1, StringComparison.Ordinal) >= 0,
                "the capability sentinel must perform at most one synchronous readback");
            string capability = MemberBody(
                retained,
                "private bool TryEnsureCompositionCapability(");
            TestAssert.Contains(
                capability,
                "if (_compositionCapabilityRevision == renderResourcesRevision)",
                "retained composition capability must be checked at most once per resource revision");
            TestAssert.Contains(
                capability,
                "PreparedWorkBoxRenderer.TryValidateRetainedComposition(",
                "the row cache must own the once-per-revision capability latch");
            TestAssert.False(
                build.IndexOf("ReadPixels(", StringComparison.Ordinal) >= 0,
                "row rebuilds must not perform synchronous readbacks");

            string release = MemberBody(
                preparedBox,
                "internal static void ReleaseRetainedResources()");
            TestAssert.Contains(
                release,
                "_retainedMaterial = null;",
                "full retained-resource teardown must detach the shared material before destruction");
            TestAssert.Contains(
                release,
                "UnityEngine.Object.Destroy(retainedMaterial)",
                "full retained-resource teardown must destroy the shared material");
            TestAssert.Contains(
                retained,
                "PreparedWorkBoxRenderer.ReleaseRetainedResources();",
                "row-cache disposal must own the shared material teardown boundary");
        }

        private static void RetainedPresentationKeepsOwnerClipForAllVerticalPositions(
            string retained,
            string optimized)
        {
            TestAssert.Contains(
                retained,
                "above, within,\n    /// or below the viewport",
                "retained rows must document owner clipping for top, middle, and bottom positions");
            string preparedDraw = MemberBody(
                retained,
                "internal bool TryDraw(\n            PreparedRun run,");
            TestAssert.Contains(
                preparedDraw,
                "destination.y += rowOffsetY;",
                "retained presentation must preserve the live row offset for every clipped position");
            string present = MemberBody(retained, "private static void PresentSurface(");
            TestAssert.Contains(
                present,
                "GUI.DrawTextureWithTexCoords(",
                "the owning IMGUI clip must receive the complete retained destination");
            TestAssert.False(
                present.IndexOf("Mathf.Clamp", StringComparison.Ordinal) >= 0 ||
                present.IndexOf("GUI.BeginGroup", StringComparison.Ordinal) >= 0,
                "retained presentation must not replace the owner's top/middle/bottom clip");
            TestAssert.Contains(
                optimized,
                "DrawPreparedRunDirect(packet, run, rowOffsetY, baseColor);",
                "every retained clip position must retain the direct fallback path");
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

        private static void RetainedHitsUsePrecomputedBoundsAndFingerprint(
            string retained,
            string preparedBox)
        {
            string prepared = MemberBody(retained, "internal PreparedRun(Cell[] cells)");
            TestAssert.Contains(prepared, "Bounds = GetBounds(Cells)", "run bounds must be computed once during packet preparation");
            TestAssert.Contains(prepared, "StaticFingerprint = GetStaticFingerprint(Cells)", "cell fingerprints must be computed once during packet preparation");
            string bounds = MemberBody(retained, "private static Rect GetBounds(");
            TestAssert.False(
                bounds.IndexOf("ExpandedBy", StringComparison.Ordinal) >= 0,
                "live warning borders must not enlarge retained work-box surfaces");
            TestAssert.False(
                retained.IndexOf("GetStableVisualOutset", StringComparison.Ordinal) >= 0,
                "no live visual may contribute unused retained-surface padding");
            string fingerprint = MemberBody(retained, "private static ulong GetFingerprint(");
            TestAssert.False(fingerprint.IndexOf("for (", StringComparison.Ordinal) >= 0, "steady retained hits must not rescan cells");
            TestAssert.Contains(fingerprint, "staticFingerprint", "steady fingerprints must mix the packet fingerprint in O(1)");
            TestAssert.False(fingerprint.IndexOf("Text.CurFontStyle", StringComparison.Ordinal) >= 0, "steady hits must not depend on ambient GUI font state");
            string staticFingerprint = MemberBody(retained, "private static ulong GetStaticFingerprint(");
            TestAssert.False(staticFingerprint.IndexOf("visual.Priority", StringComparison.Ordinal) >= 0, "manual priority values must not rebuild texture-only retained surfaces");
            TestAssert.False(staticFingerprint.IndexOf("visual.Passion", StringComparison.Ordinal) >= 0, "live passion icons must not rebuild texture-only retained surfaces");
            TestAssert.False(staticFingerprint.IndexOf("PriorityColor", StringComparison.Ordinal) >= 0, "manual priority colors belong to the live label pass");
            TestAssert.False(staticFingerprint.IndexOf("CompactText", StringComparison.Ordinal) >= 0, "font-size metadata must not rebuild texture-only retained surfaces");
            TestAssert.Contains(
                staticFingerprint,
                "WorkCellVisualFlags.LowSkillWarning",
                "live warning state must not rebuild the texture-only retained surface");
            TestAssert.Contains(staticFingerprint, "retainedCheck", "retained fingerprints must preserve checkbox visibility changes");
            string buildSurface = MemberBody(retained, "private static bool BuildSurface(");
            TestAssert.False(buildSurface.IndexOf("Widgets.Label", StringComparison.Ordinal) >= 0, "retained surface composition must remain texture-only");
            TestAssert.False(buildSurface.IndexOf("Text.Font", StringComparison.Ordinal) >= 0, "retained surface composition must not prepare unused glyph state");
            TestAssert.Contains(
                buildSurface,
                "GL.LoadPixelMatrix(0f, bounds.width, bounds.height, 0f);",
                "retained surface composition must establish its own pixel matrix");
            TestAssert.False(
                buildSurface.IndexOf("GUI.BeginGroup", StringComparison.Ordinal) >= 0 ||
                buildSurface.IndexOf("GUI.EndGroup", StringComparison.Ordinal) >= 0,
                "retained surface composition must not enqueue IMGUI group work against the temporary target");
            TestAssert.False(
                buildSurface.IndexOf("GL.Flush();", StringComparison.Ordinal) >= 0,
                "the immediate retained texture path must not depend on a manual GL flush");
            TestAssert.False(
                retained.IndexOf("Graphics.DrawTexture", StringComparison.Ordinal) >= 0,
                "retained work boxes must not enqueue graphics draws against a temporary target");
            string retainedTexture = MemberBody(preparedBox, "private static bool DrawRetainedTexture(");
            TestAssert.Contains(
                retainedTexture,
                "material.SetTexture(\"_MainTex\", texture);",
                "retained work boxes must bind each source texture before immediate emission");
            TestAssert.Contains(
                retainedTexture,
                "material.SetColor(\"_Color\", color);",
                "retained work boxes must preserve direct-path tint during immediate emission");
            TestAssert.Contains(
                retainedTexture,
                "material.SetPass(0)",
                "retained work boxes must bind a render pass before immediate emission");
            TestAssert.Contains(
                retainedTexture,
                "GL.Begin(GL.QUADS)",
                "retained work boxes must emit immediate textured quads");
            TestAssert.Contains(
                retainedTexture,
                "GL.TexCoord2(0f, 1f)",
                "retained work boxes must explicitly orient texture coordinates for the top-left pixel matrix");
            TestAssert.Contains(
                preparedBox,
                "ShaderDatabase.Transparent",
                "retained work boxes must use RimWorld's transparent texture shader");
            TestAssert.Contains(
                retainedTexture,
                "texture == null",
                "missing retained textures must use the direct fallback path");
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
