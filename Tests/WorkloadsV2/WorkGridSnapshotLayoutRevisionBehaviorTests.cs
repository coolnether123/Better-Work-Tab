using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Exercises the snapshot cache contract at the same scalar boundaries used
    /// by the production provider, then locks the production gate placement.
    /// This keeps the layout-transition case executable without constructing
    /// RimWorld's UI or allocating a colony-sized snapshot.
    /// </summary>
    internal static class WorkGridSnapshotLayoutRevisionBehaviorTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs"),
                "work-grid snapshot layout-revision behavior");
            string provider = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string contracts = Read(root, "Source", "UI", "WorkGrid", "Contracts", "WorkGridRenderContracts.cs");
            string layout = Read(root, "Source", "PawnOrganizer", "Layout", "WorkTabLayoutController.cs");
            string window = Read(root, "Source", "UI", "MainTabWindow_BetterWork.cs");
            string renderer = Read(root, "Source", "UI", "WorkGrid", "Rendering", "OptimizedWorkGridRenderer.cs");

            LayoutRevisionChangeForcesBuildAndCoherentSnapshot();
            UnchangedLayoutReusesThePreparedSnapshot();
            ProviderUsesTheRevisionGateForEveryReusePath(provider);
            ViewAndLayoutPublishOneRevision(contracts, layout, window);
            RendererOnlyConsumesMatchingPassState(renderer);
        }

        private static void LayoutRevisionChangeForcesBuildAndCoherentSnapshot()
        {
            var cache = new SnapshotCacheProbe();
            SnapshotCacheProbe.Snapshot first = cache.Prepare(
                layoutRevision: 12,
                geometryRevision: 12,
                topologyRevision: 4,
                effectiveStateRevision: 9,
                layoutSignature: 77);

            // Tutorial strip reservation changes geometry/layout revision while
            // topology, effective state, and the topology signature stay fixed.
            SnapshotCacheProbe.Snapshot rebuilt = cache.Prepare(
                layoutRevision: 13,
                geometryRevision: 13,
                topologyRevision: 4,
                effectiveStateRevision: 9,
                layoutSignature: 77);

            TestAssert.False(
                ReferenceEquals(first, rebuilt),
                "a layout revision change must build a new prepared snapshot even when topology is unchanged");
            TestAssert.Equal(
                13,
                rebuilt.LayoutRevision,
                "the rebuilt snapshot must carry the current layout revision");
            TestAssert.Equal(
                rebuilt.LayoutRevision,
                rebuilt.GeometryRevision,
                "the rebuilt snapshot must be coherent with the current geometry revision");
            TestAssert.Equal(
                2,
                cache.BuildCount,
                "a layout revision transition must take the full snapshot build path once");
        }

        private static void UnchangedLayoutReusesThePreparedSnapshot()
        {
            var cache = new SnapshotCacheProbe();
            SnapshotCacheProbe.Snapshot first = cache.Prepare(
                layoutRevision: 21,
                geometryRevision: 21,
                topologyRevision: 8,
                effectiveStateRevision: 14,
                layoutSignature: 101);
            SnapshotCacheProbe.Snapshot reused = cache.Prepare(
                layoutRevision: 21,
                geometryRevision: 21,
                topologyRevision: 8,
                effectiveStateRevision: 14,
                layoutSignature: 101);

            TestAssert.True(
                ReferenceEquals(first, reused),
                "an unchanged layout must retain the O(1) prepared snapshot cache hit");
            TestAssert.Equal(
                1,
                cache.BuildCount,
                "an unchanged layout must not rebuild the prepared snapshot");
        }

        private static void ProviderUsesTheRevisionGateForEveryReusePath(string provider)
        {
            string prepare = MemberBody(provider, "internal WorkGridSnapshot Prepare(");
            string sparse = MemberBody(provider, "private bool CanApplySparsePriorityUpdate(");
            string gate = MemberBody(provider, "private bool HasMatchingCurrentLayoutRevision(");

            TestAssert.Contains(
                prepare,
                "ReferenceEquals(_layout, layout) &&\n                HasMatchingCurrentLayoutRevision(layout)",
                "the clean same-layout cache hit must validate the current geometry revision");
            TestAssert.Contains(
                prepare,
                "if (HasMatchingCurrentLayoutRevision(layout) &&",
                "the topology-signature fallback must validate the current geometry revision");
            TestAssert.Contains(
                sparse,
                "return HasMatchingCurrentLayoutRevision(layout) &&",
                "the sparse priority path must not publish across a layout rebuild");
            TestAssert.Contains(
                gate,
                "snapshot.LayoutRevision == layout.LayoutRevision",
                "the shared cache gate must compare the snapshot with the layout");
            TestAssert.Contains(
                gate,
                "layout.GeometrySnapshot.Revision == layout.LayoutRevision",
                "the shared cache gate must compare geometry with the layout");
            TestAssert.False(
                gate.IndexOf("ComputeLayoutSignature", StringComparison.Ordinal) >= 0,
                "the revision gate must remain constant-time and must not scan topology");
        }

        private static void ViewAndLayoutPublishOneRevision(
            string contracts,
            string layout,
            string window)
        {
            string view = MemberBody(contracts, "public readonly struct WorkTabView");
            string shouldRebuild = MemberBody(layout, "private bool ShouldRebuild(");
            string rebuild = MemberBody(layout, "public void Rebuild(");
            string advance = MemberBody(window, "private void AdvanceFrameState()");

            TestAssert.Contains(
                view,
                "Layout.LayoutRevision == Geometry.Revision",
                "the pass view must require layout and geometry revisions to agree");
            TestAssert.Contains(
                view,
                "(Snapshot == null || Snapshot.LayoutRevision == Geometry.Revision)",
                "the pass view must reject a snapshot from an older geometry revision");
            TestAssert.Contains(
                shouldRebuild,
                "WorkGridLayoutMetrics.TutorialPinnedHeight",
                "tutorial strip geometry changes must invalidate the layout owner");
            TestAssert.Contains(
                rebuild,
                "_layoutRevision++",
                "a rebuilt layout must publish a new revision");
            TestAssert.Contains(
                rebuild,
                "PublishGeometrySnapshot();",
                "the rebuilt layout must publish geometry for that same revision");
            TestAssert.Contains(
                advance,
                "BWTWorkTabTutorial.RefreshStripReservation();",
                "tutorial reservation must be latched before layout preparation");
        }

        private static void RendererOnlyConsumesMatchingPassState(string renderer)
        {
            string availability = MemberBody(renderer, "public bool IsAvailable(");
            string prepare = MemberBody(renderer, "public void Prepare(");

            TestAssert.Contains(
                availability,
                "context.HasMatchingLayoutRevision",
                "the optimized renderer must reject an incoherent snapshot/geometry pair");
            TestAssert.Contains(
                prepare,
                "_hasMatchingLayoutRevision = context.HasMatchingLayoutRevision",
                "renderer preparation must retain the pass coherence result");
            TestAssert.Contains(
                renderer,
                "_snapshot.LayoutRevision",
                "retained rendering must use the snapshot revision that passed the gate");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int index = 0; index < parts.Length; index++)
            {
                path = Path.Combine(path, parts[index]);
            }

            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
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

        private sealed class SnapshotCacheProbe
        {
            private Snapshot _current;

            internal int BuildCount { get; private set; }

            internal Snapshot Prepare(
                int layoutRevision,
                int geometryRevision,
                long topologyRevision,
                long effectiveStateRevision,
                int layoutSignature)
            {
                if (_current != null &&
                    _current.LayoutRevision == layoutRevision &&
                    _current.LayoutRevision == geometryRevision &&
                    _current.TopologyRevision == topologyRevision &&
                    _current.EffectiveStateRevision == effectiveStateRevision &&
                    _current.LayoutSignature == layoutSignature)
                {
                    return _current;
                }

                BuildCount++;
                _current = new Snapshot(
                    layoutRevision,
                    geometryRevision,
                    topologyRevision,
                    effectiveStateRevision,
                    layoutSignature);
                return _current;
            }

            internal sealed class Snapshot
            {
                internal Snapshot(
                    int layoutRevision,
                    int geometryRevision,
                    long topologyRevision,
                    long effectiveStateRevision,
                    int layoutSignature)
                {
                    LayoutRevision = layoutRevision;
                    GeometryRevision = geometryRevision;
                    TopologyRevision = topologyRevision;
                    EffectiveStateRevision = effectiveStateRevision;
                    LayoutSignature = layoutSignature;
                }

                internal int LayoutRevision { get; }
                internal int GeometryRevision { get; }
                internal long TopologyRevision { get; }
                internal long EffectiveStateRevision { get; }
                internal int LayoutSignature { get; }
            }
        }
    }
}
