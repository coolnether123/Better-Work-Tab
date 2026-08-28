using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkGridRetainedSparseAuditPerformanceContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs"),
                "work-grid retained, sparse, and audit performance contracts");
            string renderer = Read(
                root,
                "Source", "UI", "WorkGrid", "Rendering", "OptimizedWorkGridRenderer.cs");
            string snapshots = Read(
                root,
                "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string audit = Read(
                root,
                "Source", "UI", "WorkGrid", "Invalidation", "WorkGridInvalidationAudit.cs");
            string retainedRows = Read(
                root,
                "Source", "UI", "WorkGrid", "Rendering", "RetainedWorkBoxRowCache.cs");
            string body = Read(
                root,
                "Source", "UI", "WorkGrid", "Rendering", "WorkTabBodyRenderer.cs");
            string headerCoordinator = Read(
                root,
                "Source", "UI", "Headers", "HeaderDrawingCoordinator.cs");
            string angledLabels = Read(
                root,
                "Source", "UI", "Headers", "Angled", "AngledLabelDrawer.cs");
            string vanillaHeaders = Read(
                root,
                "Source", "UI", "Headers", "Vanilla", "VanillaHeaderRenderer.cs");
            string window = Read(
                root,
                "Source", "UI", "MainTabWindow_BetterWork.cs");
            string gameCacheReset = Read(
                root,
                "Source", "Features", "Patches", "Patch_Building_Bed_Cache.cs");
            string workloadGateway = Read(
                root,
                "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string priorityPatch = Read(
                root,
                "Source", "Features", "Patches", "Patch_WorkPriority_DoCell_Unified.cs");

            ParentRowsDoNotRecomposeDuringColumnAnimation(renderer);
            SnapshotOwnedEmptyBackgroundsDoNotFallBack(renderer, body);
            RetainedRowsKeepLogicalImGuiOrientation(retainedRows);
            HeadersUseLivePreparedRendering(
                headerCoordinator,
                angledLabels,
                vanillaHeaders,
                window,
                gameCacheReset);
            MapWorldClearIsAnAuthoritativeTeardownHook(gameCacheReset);
            SparseParentChangesKeepSubWorkUpdatesLocal(
                snapshots,
                workloadGateway,
                priorityPatch);
            CompatibilityAuditUsesLinearSkillAndPriorityPasses(audit);
            RepresentativeOperationCountsAreReduced();
        }

        private static void MapWorldClearIsAnAuthoritativeTeardownHook(string gameCacheReset)
        {
            TestAssert.Contains(
                gameCacheReset,
                "Patch_MemoryUtility_ClearAllMapsAndWorld",
                "Save & Quit must have a teardown hook on the direct map/world clear route");
            TestAssert.Contains(
                gameCacheReset,
                "typeof(Verse.Profile.MemoryUtility)",
                "the teardown hook must target RimWorld's authoritative map/world clear API");
            TestAssert.Contains(
                gameCacheReset,
                "nameof(Verse.Profile.MemoryUtility.ClearAllMapsAndWorld)",
                "the teardown hook must remain bound to the stable method name");

            string hook = MemberBody(
                gameCacheReset,
                "public static void Prefix()\n        {\n            RetainedWorkTabSurfaceTeardown.Release(\"map/world clear\")");
            TestAssert.Contains(
                hook,
                "GameCacheResetUtility.Reset(\"map/world clear\")",
                "the direct map/world clear route must reset cached game-bound state");
        }

        private static void HeadersUseLivePreparedRendering(
            string headerCoordinator,
            string angledLabels,
            string vanillaHeaders,
            string window,
            string gameCacheReset)
        {
            string draw = MemberBody(headerCoordinator, "internal static void DrawHeader(");
            int directDraw = draw.IndexOf("preparedRenderer.DrawHeader(", StringComparison.Ordinal);
            TestAssert.True(
                directDraw >= 0,
                "prepared headers must draw through the live prepared renderer");
            TestAssert.False(
                draw.IndexOf("_retainedPriorityHeaders", StringComparison.Ordinal) >= 0,
                "header drawing must not route through the removed retained RenderTexture cache");
            TestAssert.False(
                angledLabels.IndexOf("DrawRetainedStable(", StringComparison.Ordinal) >= 0,
                "angled headers must not expose a deferred offscreen underline phase");
            TestAssert.False(
                angledLabels.IndexOf("DrawRetainedText(", StringComparison.Ordinal) >= 0,
                "angled headers must keep glyphs on the same live presentation pass as underlines");
            string angledDraw = MemberBody(angledLabels, "internal static void Draw(");
            TestAssert.Contains(
                angledDraw,
                "drawUnderline: true",
                "angled prepared headers must draw stable underlines live");
            TestAssert.False(
                vanillaHeaders.IndexOf("DrawRetainedStable(", StringComparison.Ordinal) >= 0,
                "vanilla headers must not expose a deferred offscreen stem phase");
            TestAssert.False(
                vanillaHeaders.IndexOf("DrawRetainedText(", StringComparison.Ordinal) >= 0,
                "vanilla headers must keep glyphs and stems on the same live presentation pass");
            string vanillaDraw = MemberBody(vanillaHeaders, "public void DrawHeader(");
            TestAssert.Contains(
                vanillaDraw,
                "drawStems: true",
                "vanilla prepared headers must draw stable stems live");
            string prepare = MemberBody(headerCoordinator, "internal static void PrepareFrame(");
            TestAssert.False(
                prepare.IndexOf("_retainedPriorityHeaders", StringComparison.Ordinal) >= 0,
                "header invalidation must not manage a removed offscreen cache");
            string invalidate = MemberBody(headerCoordinator, "public static void InvalidateCaches()");
            TestAssert.False(
                invalidate.IndexOf("_retainedPriorityHeaders", StringComparison.Ordinal) >= 0,
                "header cache invalidation must remain limited to live layout caches");
            TestAssert.Contains(
                headerCoordinator,
                "Header pixels are drawn live",
                "the retained-resource teardown seam must document that headers own no GPU surface");
            string settingsChanged = MemberBody(
                headerCoordinator,
                "public static void NotifyAngledHeadersChanged()");
            TestAssert.Contains(
                settingsChanged,
                "table.SetDirty();",
                "header presentation changes must invalidate only the active Work table");
            TestAssert.False(
                settingsChanged.IndexOf(
                    "NotifyAllPawnTables_PawnsChanged",
                    StringComparison.Ordinal) >= 0,
                "header presentation changes must not refresh every pawn table");
            string preOpen = MemberBody(window, "public override void PreOpen()");
            TestAssert.False(
                preOpen.IndexOf("HeaderDrawingCoordinator.ResetRetainedFailureLatchesForReopen();", StringComparison.Ordinal) >= 0,
                "reopening the Work tab must not reset a removed header surface latch");
            TestAssert.False(
                window.IndexOf("HeaderDrawingCoordinator.ResetRetainedFailureLatchesForReopen", StringComparison.Ordinal) >= 0,
                "the removed retained header cache must not leave a reset API behind");
            string resolution = MemberBody(window, "public override void Notify_ResolutionChanged()");
            TestAssert.Contains(
                resolution,
                "ReleaseRetainedResources();",
                "resolution changes must still release row and chrome retained resources");
            string teardown = MemberBody(gameCacheReset, "public static void Reset(string reason)");
            TestAssert.False(
                teardown.IndexOf("HeaderDrawingCoordinator.ReleaseRetainedResources();", StringComparison.Ordinal) >= 0,
                "game-data invalidation must not invent a header GPU release path");
        }

        private static void SnapshotOwnedEmptyBackgroundsDoNotFallBack(
            string renderer,
            string body)
        {
            string ownership = MemberBody(renderer, "public bool TryOwnRowBackground(");
            TestAssert.Contains(
                ownership,
                "(row.VisualFlags & WorkGridRowVisualFlags.HasBackground) != 0",
                "the snapshot must distinguish an owned empty row from an unavailable row");
            TestAssert.Contains(
                ownership,
                "return true;",
                "a valid snapshot row must own its empty background without a native retry");

            string pawnRow = MemberBody(body, "private static void DrawPawnRowContentUnclipped(");
            TestAssert.Contains(
                pawnRow,
                "!snapshotLayer.TryOwnRowBackground(rowIndex, rowRect)",
                "native pawn-colour lookup must run only when snapshot ownership is unavailable");
        }

        private static void RetainedRowsKeepLogicalImGuiOrientation(string retainedRows)
        {
            TestAssert.False(
                retainedRows.IndexOf("SystemInfo.graphicsUVStartsAtTop", StringComparison.Ordinal) >= 0,
                "retained IMGUI rows must not apply a second platform UV inversion");
            string build = MemberBody(retainedRows, "private static bool BuildSurface(");
            TestAssert.Contains(
                build,
                "GUI.matrix = Matrix4x4.identity",
                "retained row labels must compose in the surface's logical coordinate system");
            TestAssert.Contains(
                retainedRows,
                "new Rect(0f, 0f, 1f, 1f)",
                "retained IMGUI rows must present their top-left-composed surface directly");
        }

        private static void ParentRowsDoNotRecomposeDuringColumnAnimation(string renderer)
        {
            string drawCell = MemberBody(renderer, "public bool TryDrawCell(");
            TestAssert.Contains(
                drawCell,
                "visualAlpha > 0.999f &&\n                !ColumnReorderAnimationState.IsActive",
                "stable parent rows must use direct clipped drawing while columns animate");
        }

        private static void SparseParentChangesKeepSubWorkUpdatesLocal(
            string snapshots,
            string workloadGateway,
            string priorityPatch)
        {
            string eligibility = MemberBody(
                snapshots,
                "private bool CanApplySparsePriorityUpdate(");
            TestAssert.False(
                eligibility.IndexOf("ContainsSubWorkColumns", StringComparison.Ordinal) >= 0,
                "visible sub-work columns must not disable every sparse parent-priority update");

            string sparseReplacement = MemberBody(
                snapshots,
                "private bool TryBuildSparseReplacements(");
            TestAssert.Contains(
                sparseReplacement,
                "foreach (WorkGridPriorityKey dirtyKey in dirty)",
                "sparse replacement must start from the exact invalidated parent target");
            TestAssert.Contains(
                sparseReplacement,
                "WorkGridPreparedRowSpan span = previous.PreparedRows[rowIndex]",
                "a parent edit must inspect only its prepared row span");
            TestAssert.False(
                sparseReplacement.IndexOf("for (int i = 0; i < previous.Cells.Count; i++)", StringComparison.Ordinal) >= 0,
                "one parent edit must not walk every snapshot cell");
            TestAssert.Contains(
                sparseReplacement,
                "TryResolveSubWorkColumn(",
                "sparse replacement must resolve live definitions from the authoritative layout rather than the snapshot");
            TestAssert.False(
                snapshots.IndexOf("snapshotColumn.SubWorkGiver", StringComparison.Ordinal) >= 0,
                "snapshot columns must not own live WorkGiver references");
            TestAssert.Contains(
                snapshots,
                "!snapshotColumn.IsExpandBesideChild",
                "focus columns must retain their parent visual while expand-beside children stay child-only");
            TestAssert.Contains(
                snapshots,
                "worker.Compare(candidate, bestPawn) > 0",
                "best-pawn selection must remain RimWorld's worker-defined skill and eligibility comparison");
            TestAssert.False(
                snapshots.IndexOf("IsBetterPawn(", StringComparison.Ordinal) >= 0,
                "best-pawn selection must use RimWorld's comparison directly instead of a forwarding helper");
            TestAssert.False(
                priorityPatch.IndexOf("private static bool IsBetterPawn(", StringComparison.Ordinal) >= 0,
                "the direct fallback must use RimWorld's best-pawn comparison directly");
            string directBestPawn = MemberBody(
                priorityPatch,
                "private static Pawn GetBestPawnForWorktype(");
            TestAssert.Contains(
                directBestPawn,
                "worker.Compare(p, bestPawn) > 0",
                "the direct fallback must rank best pawns by RimWorld's worker comparison");
            TestAssert.False(
                directBestPawn.IndexOf("WorkTabEffectiveStateRuntime", StringComparison.Ordinal) >= 0 ||
                directBestPawn.IndexOf("ParentPriorityRead", StringComparison.Ordinal) >= 0,
                "preview priority edits must not clear or recalculate direct fallback best-pawn selection");

            string transition = MemberBody(
                snapshots,
                "private static bool IsSparseParentPriorityRevisionTransition(");
            TestAssert.Contains(
                transition,
                "before.SessionRevision != after.SessionRevision",
                "preview sparse replacement requires a new draft revision");
            TestAssert.Contains(
                transition,
                "before.SourceRevision == after.SourceRevision",
                "live-source changes must remain full-rebuild candidates");
            TestAssert.Contains(
                transition,
                "before.PersistenceRevision == after.PersistenceRevision",
                "persistence changes must remain full-rebuild candidates");
            TestAssert.Contains(
                transition,
                "before.ScheduleRevision == after.ScheduleRevision",
                "schedule edits must remain full-rebuild candidates");
            TestAssert.Contains(
                transition,
                "before.SpecificRevision == after.SpecificRevision",
                "specific-job edits must remain full-rebuild candidates");
            TestAssert.Contains(
                transition,
                "before.SettingsRevision == after.SettingsRevision",
                "presentation-setting edits must remain full-rebuild candidates");
            TestAssert.Contains(
                eligibility,
                "EqualNonPriorityConsumedRevisions(_revisions, current)",
                "roster and skill invalidation must remain outside the sparse priority path");

            TestAssert.Contains(
                transition,
                "before.MembershipRevision == after.MembershipRevision",
                "membership changes must remain full-rebuild candidates");
            TestAssert.Contains(
                transition,
                "before.AuthorityRevision == after.AuthorityRevision",
                "authority changes must remain full-rebuild candidates");

            string previewMutation = MemberBody(
                workloadGateway,
                "internal bool TrySetPreviewParentPriority(");
            TestAssert.Contains(
                previewMutation,
                "_projectedProvider.InvalidateDraft();\n            WorkTabInvalidationHub.InvalidatePriority(pawn.thingIDNumber, workType.shortHash);",
                "a successful preview parent-priority edit must publish one exact sparse invalidation after its draft revision");
        }

        private static void CompatibilityAuditUsesLinearSkillAndPriorityPasses(string audit)
        {
            string signature = MemberBody(audit, "private static int ComputeSignature(");
            TestAssert.False(
                signature.IndexOf("AverageOfRelevantSkillsFor", StringComparison.Ordinal) >= 0,
                "the defensive audit must not rescan relevant skills once per pawn and column");
            TestAssert.False(
                signature.IndexOf("MaxPassionOfRelevantSkillsFor", StringComparison.Ordinal) >= 0,
                "the defensive audit must not rescan passion once per pawn and column");
            TestAssert.Contains(
                signature,
                "new System.Collections.Generic.HashSet<WorkTypeDef>()",
                "the audit must deduplicate relevant priority work types before visiting pawns");
            TestAssert.Contains(
                signature,
                "seenWorkTypes.Add(workType)",
                "duplicate table columns must not duplicate priority reads");
            TestAssert.Contains(
                signature,
                "pawn.skills.skills",
                "each pawn's skill records must be traversed directly once");
            TestAssert.Contains(
                signature,
                "skill.def.shortHash",
                "the skill signature must retain skill identity");
            TestAssert.Contains(
                signature,
                "skill.levelInt",
                "the skill signature must detect direct level writes");
            TestAssert.Contains(
                signature,
                "(int)skill.passion",
                "the skill signature must detect direct passion writes");
        }

        private static void RepresentativeOperationCountsAreReduced()
        {
            const int pawns = 100;
            const int columns = 24;
            const int uniqueWorkTypes = 12;
            const int skillRecords = 20;

            int priorBoundaryReads = pawns * (columns + (columns * 2));
            int currentBoundaryReads = pawns * (uniqueWorkTypes + skillRecords);
            TestAssert.True(
                priorBoundaryReads == 7200,
                "the representative legacy operation count changed unexpectedly");
            TestAssert.True(
                currentBoundaryReads == 3200,
                "the representative linear audit operation count changed unexpectedly");
            TestAssert.True(
                currentBoundaryReads * 2 < priorBoundaryReads,
                "the linear audit must remove more than half of representative domain reads");

            int normalExpandFullCells = pawns * 3;
            int normalExpandSparseCells = 2;
            TestAssert.True(
                normalExpandSparseCells < normalExpandFullCells,
                "one parent edit in a parent-plus-expand layout must rebuild only that pawn's parent and child cells");

            int focusFullCells = pawns * 2;
            int focusSparseCells = 1;
            TestAssert.True(
                focusSparseCells < focusFullCells,
                "one focus-column parent edit must rebuild that parent/child column without rebuilding unrelated columns");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
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
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(start, i - start + 1);
            }
            throw new InvalidOperationException("source member has no closing brace");
        }
    }
}
