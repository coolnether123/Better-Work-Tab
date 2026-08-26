using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkGridSnapshotIsolationContractTests
    {
        internal static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshot.cs"),
                "work-grid snapshot isolation contracts");
            string snapshot = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshot.cs");
            string provider = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string packet = Read(root, "Source", "UI", "WorkGrid", "Rendering", "PreparedWorkRowPacket.cs");
            string renderer = Read(root, "Source", "UI", "WorkGrid", "Rendering", "OptimizedWorkGridRenderer.cs");
            string retained = Read(root, "Source", "UI", "WorkGrid", "Rendering", "RetainedWorkBoxRowCache.cs");
            string drawing = Read(root, "Source", "UI", "WorkGrid", "Rendering", "WorkGridDrawingSurface.cs");
            string facade = Read(root, "Source", "UI", "WorkGrid", "Rendering", "WorkGridRendererFacade.cs");
            string contracts = Read(root, "Source", "UI", "WorkGrid", "Contracts", "WorkGridRenderContracts.cs");

            SnapshotOwnsOnlyImmutablePresentation(root, snapshot, contracts, drawing, facade);
            RevisionGranularityMatchesIndependentConsumers(snapshot, provider, retained);
            PacketCompilationUsesPreparedTopologyAndGeometry(packet, provider);
            LiveInteractionReferencesStayOutsideTheSnapshot(snapshot, renderer);
        }

        private static void SnapshotOwnsOnlyImmutablePresentation(
            string root,
            string snapshot,
            string contracts,
            string drawing,
            string facade)
        {
            TestAssert.Contains(snapshot, "internal sealed class WorkGridSnapshot",
                "prepared snapshot state is an internal renderer implementation boundary");
            TestAssert.Contains(snapshot, "internal readonly struct WorkCellVisualState",
                "cell presentation must remain internal, immutable, and scalar-only");
            TestAssert.False(snapshot.IndexOf("public sealed class WorkGridSnapshot", StringComparison.Ordinal) >= 0,
                "the prepared snapshot is not an external renderer API");
            TestAssert.Contains(contracts, "internal WorkGridSnapshot Snapshot { get; }",
                "the public pass view must not expose the internal prepared snapshot");
            TestAssert.Contains(drawing, "internal interface IWorkGridSnapshotLayer",
                "the optimized snapshot drawing layer is a BWT implementation detail");
            TestAssert.Contains(drawing, "internal interface IWorkGridDrawingSurface",
                "the BWT drawing surface must not expose its internal snapshot layer through public API");
            TestAssert.Contains(facade, "internal WorkGridRendererFacade(",
                "the public renderer facade must not expose its BWT-owned drawing surface constructor");
            TestAssert.False(
                File.Exists(Path.Combine(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshot.LegacyApi.cs")),
                "removed live-reference snapshot accessors must not return through a compatibility shim");
            TestAssert.False(snapshot.IndexOf("Pawn Pawn", StringComparison.Ordinal) >= 0,
                "snapshot values must not retain live pawns");
            TestAssert.False(snapshot.IndexOf("WorkTypeDef", StringComparison.Ordinal) >= 0,
                "snapshot values must not retain live work-type definitions");
            TestAssert.False(snapshot.IndexOf("WorkGiverCellPresentationCache", StringComparison.Ordinal) >= 0,
                "snapshot values must not retain cache-owned presentation objects");
            TestAssert.False(snapshot.IndexOf("SubWorkGiver", StringComparison.Ordinal) >= 0,
                "snapshot columns must not retain live WorkGiver objects");
            TestAssert.Contains(snapshot, "WorkGridSubWorkVisualState SubWork",
                "sub-work cells must carry only their derived immutable visual state");

            TestAssert.False(snapshot.IndexOf("rows ??", StringComparison.Ordinal) >= 0,
                "the internal producer contract must not hide a null rows invariant violation");
            TestAssert.False(snapshot.IndexOf("columns ??", StringComparison.Ordinal) >= 0,
                "the internal producer contract must not hide a null columns invariant violation");
            TestAssert.False(snapshot.IndexOf("cells ??", StringComparison.Ordinal) >= 0,
                "the internal producer contract must not hide a null cells invariant violation");
            TestAssert.False(snapshot.IndexOf("preparedRows ??", StringComparison.Ordinal) >= 0,
                "the internal producer contract must not hide a null prepared-row invariant violation");
            TestAssert.False(snapshot.IndexOf("pawnLabels ??", StringComparison.Ordinal) >= 0,
                "the internal producer contract must not hide a null pawn-label invariant violation");
        }

        private static void RevisionGranularityMatchesIndependentConsumers(
            string snapshot,
            string provider,
            string retained)
        {
            TestAssert.False(snapshot.IndexOf("WorkGridRevisionSet Revisions", StringComparison.Ordinal) >= 0,
                "consumed provider revisions must not be copied into an unread snapshot property");
            TestAssert.False(snapshot.IndexOf("internal long Revision { get; }", StringComparison.Ordinal) >= 0,
                "diagnostic publication sequence must remain with the provider rather than masquerade as renderer state");
            TestAssert.False(snapshot.IndexOf("UiScaleRevision", StringComparison.Ordinal) >= 0,
                "UI scale must not pretend to have an independent retained-cache consumer");
            TestAssert.False(snapshot.IndexOf("FontThemeRevision", StringComparison.Ordinal) >= 0,
                "theme and font state must not pretend to have an independent retained-cache consumer");
            TestAssert.False(snapshot.IndexOf("PriorityRangeRevision", StringComparison.Ordinal) >= 0,
                "priority range must not pretend to have an independent retained-cache consumer");
            TestAssert.Contains(snapshot, "WorkGridRetainedVisualKey",
                "the retained cache needs an exact value key for its shared presentation inputs");
            TestAssert.Contains(snapshot, "internal int UiScaleMilli { get; }",
                "the exact retained key must carry UI scale independently");
            TestAssert.Contains(snapshot, "internal long SettingsThemeLanguageScaleRevision { get; }",
                "the exact retained key must carry the full presentation revision");
            TestAssert.Contains(snapshot, "internal int MaximumPriority { get; }",
                "the exact retained key must carry priority range independently");
            TestAssert.False(snapshot.IndexOf("RetainedVisualRevision", StringComparison.Ordinal) >= 0,
                "a pre-hashed presentation signature must not masquerade as a collision-free revision");
            TestAssert.Contains(retained, "entry.RetainedVisualKey.Equals(retainedVisualKey)",
                "retained reuse must compare the exact visual key independently of its fingerprint");
            TestAssert.False(snapshot.IndexOf("RetainedCapacityBytes", StringComparison.Ordinal) >= 0,
                "provider storage diagnostics are not work-grid presentation state");
            TestAssert.False(snapshot.IndexOf("ManualPriorities", StringComparison.Ordinal) >= 0,
                "unused global mode configuration must not be carried by every snapshot");
            TestAssert.False(snapshot.IndexOf("MaxPriority", StringComparison.Ordinal) >= 0,
                "unused global priority configuration must not be carried by every snapshot");
            TestAssert.Contains(provider, "_preparedCapacityBytes",
                "prepared-storage capacity remains available to diagnostics at its owner");
            TestAssert.False(retained.IndexOf("WorkGridSnapshot snapshot", StringComparison.Ordinal) >= 0,
                "the retained cache must receive its explicit revision inputs rather than the whole snapshot");
        }

        private static void PacketCompilationUsesPreparedTopologyAndGeometry(
            string packet,
            string provider)
        {
            TestAssert.Contains(packet, "request.Snapshot.CellIndexes[lookupIndex]",
                "the snapshot provider must publish the dense dispatch topology");
            TestAssert.Contains(provider, "private void BuildCellIndexes()",
                "the producer must build dispatch indexes with the topology it publishes");
            TestAssert.Contains(packet, "request.Geometry.Columns[columnIndex]",
                "packet compilation must use pass-stable geometry rather than live layout columns");
            TestAssert.Contains(packet, "Geometry.Revision == Snapshot.LayoutRevision",
                "same-sized stale geometry must not be combined with snapshot presentation state");
            TestAssert.False(packet.IndexOf("LayoutColumns", StringComparison.Ordinal) >= 0,
                "packet compilation must not receive a second live column topology");
            TestAssert.False(packet.IndexOf("SubWorkDrilldownState", StringComparison.Ordinal) >= 0,
                "transitional UI state must be screened before packet compilation");
            TestAssert.False(packet.IndexOf("Text.CalcSize", StringComparison.Ordinal) >= 0,
                "packet compilation must not measure unchanged label text");
        }

        private static void LiveInteractionReferencesStayOutsideTheSnapshot(
            string snapshot,
            string renderer)
        {
            TestAssert.Contains(renderer, "TryGetLiveCellReferences(",
                "hover and native fallback must use a distinct live-reference boundary");
            TestAssert.Contains(renderer, "_currentLayoutRows",
                "live pawn references must come from the pass-captured layout");
            TestAssert.Contains(renderer, "_liveWorkTypesByColumn",
                "live definition references must be captured separately from presentation state");
            TestAssert.Contains(renderer, "fail closed if topology no longer matches",
                "the reference boundary must document its stale-topology behavior");
            TestAssert.Contains(renderer, "_liveReferenceTopologyValid",
                "prepared rendering must prove its separate live-reference topology before ownership");
            TestAssert.Contains(renderer, "!ReferenceEquals(_liveReferenceLayout, context.Layout)",
                "a reused snapshot must refresh live references when the layout owner changes");
            TestAssert.Contains(renderer, "_liveReferenceLayoutRevision != context.Layout?.LayoutRevision",
                "a reused snapshot must refresh live references when the layout revision changes");
            TestAssert.Contains(renderer, "!ReferenceEquals(_currentGeometry, context.Geometry)",
                "a replacement finished-geometry owner must invalidate packets even when its numeric revision is reused");
            TestAssert.Contains(renderer, "topologyChanged || liveLayoutChanged || passGeometryChanged",
                "cached packet rectangles must be discarded when their layout or geometry owner changes");
            TestAssert.Contains(renderer, "HasMatchingLiveRows(snapshot)",
                "live row identity must be validated when a prepared topology is accepted");
            TestAssert.Contains(renderer, "!TryGetLivePawn(rowIndex, row.PawnId, out _)",
                "each prepared row must cheaply revalidate its pawn before retained pixels are drawn");
            TestAssert.Contains(renderer, "context.HasMatchingLayoutRevision",
                "the optimized renderer must reject stale snapshot/geometry pairs at selection");
            string background = MemberBody(renderer, "public bool TryOwnRowBackground(");
            TestAssert.Contains(background, "!_hasMatchingLayoutRevision",
                "direct row-background ownership must reject stale pass geometry");
            TestAssert.Contains(background, "!_liveReferenceTopologyValid",
                "direct row-background ownership must reject mismatched live rows");
            TestAssert.Contains(background, "!HasMatchingLiveRow(rowIndex, row)",
                "row-background ownership must revalidate the current row before suppressing native drawing");
            string cell = MemberBody(renderer, "public bool TryDrawCell(");
            TestAssert.Contains(cell, "!_hasMatchingLayoutRevision",
                "direct cell ownership must reject stale pass geometry");
            TestAssert.Contains(cell, "!_liveReferenceTopologyValid",
                "direct cell ownership must reject mismatched live references");
            string culling = MemberBody(renderer, "public bool ShouldVisitCell(");
            TestAssert.Contains(culling, "return true;",
                "stale snapshot ranges must not cull native fallback cells");
            TestAssert.Contains(culling, "!HasMatchingLiveRow(rowIndex, _snapshot.Rows[rowIndex])",
                "cell culling must fail open to native traversal when the current row identity is stale");
            string traversal = MemberBody(renderer, "public bool ShouldTraverseRows(");
            TestAssert.Contains(traversal, "!_hasMatchingLayoutRevision",
                "scroll traversal may be skipped only for matching pass geometry");
            TestAssert.Contains(traversal, "!_liveReferenceTopologyValid",
                "scroll traversal may be skipped only for validated live topology");
            TestAssert.False(snapshot.IndexOf("using RimWorld", StringComparison.Ordinal) >= 0,
                "the snapshot definition must not require RimWorld entity types");
            TestAssert.False(snapshot.IndexOf("using Verse", StringComparison.Ordinal) >= 0,
                "the snapshot definition must not require Verse entity types");
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
                if (source[index] == '{') depth++;
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
            throw new InvalidOperationException("source member has no closing brace");
        }
    }
}
