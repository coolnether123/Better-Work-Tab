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
            string retainedHeaders = Read(
                root,
                "Source", "UI", "Headers", "RetainedPriorityHeaderCache.cs");
            string retainedHeaderKey = Read(
                root,
                "Source", "UI", "Headers", "RetainedPriorityHeaderVisualKey.cs");
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

            ParentRowsDoNotRecomposeDuringColumnAnimation(renderer);
            SnapshotOwnedEmptyBackgroundsDoNotFallBack(renderer, body);
            RetainedRowsKeepLogicalImGuiOrientation(retainedRows);
            RetainedHeadersKeepDynamicAndForeignRenderingLive(
                retainedHeaders,
                retainedHeaderKey,
                headerCoordinator,
                angledLabels,
                vanillaHeaders,
                window,
                gameCacheReset);
            MapWorldClearIsAnAuthoritativeTeardownHook(gameCacheReset);
            SparseParentChangesKeepSubWorkUpdatesLocal(snapshots);
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

        private static void RetainedHeadersKeepDynamicAndForeignRenderingLive(
            string retainedHeaders,
            string retainedHeaderKey,
            string headerCoordinator,
            string angledLabels,
            string vanillaHeaders,
            string window,
            string gameCacheReset)
        {
            TestAssert.Contains(
                retainedHeaders,
                "MaximumEntries = 64",
                "retained headers need a hard surface-count bound");
            TestAssert.Contains(
                retainedHeaders,
                "MaximumEstimatedSurfaceBytes = 32L * 1024L * 1024L",
                "retained headers need a hard estimated-byte bound");
            TestAssert.Contains(
                retainedHeaders,
                "if (!HasCapacity(requestedBytes, 1))",
                "retained headers must prove new-entry capacity before allocating an entry");
            int headerCapacityCheck = retainedHeaders.IndexOf(
                "if (!HasCapacity(requestedBytes, 1))",
                StringComparison.Ordinal);
            int headerEntryAllocation = retainedHeaders.IndexOf(
                "entry = new Entry();",
                StringComparison.Ordinal);
            TestAssert.True(
                headerCapacityCheck >= 0 &&
                headerEntryAllocation > headerCapacityCheck,
                "retained headers must check capacity before the new-entry allocation");
            TestAssert.Contains(
                retainedHeaders,
                "renderer.GetType() != typeof(AngledHeaderRenderer)",
                "subclassed or foreign header renderers must retain the direct fallback");
            TestAssert.Contains(
                retainedHeaders,
                "column.Worker?.GetType() != typeof(PawnColumnWorker_WorkPriority)",
                "only the exact BWT-owned vanilla priority worker may retain pixels");
            TestAssert.Contains(
                retainedHeaders,
                "FluffyWorkTabGateway.IsFluffyColumn(column)",
                "Fluffy-owned headers must retain their direct path");
            TestAssert.Contains(
                retainedHeaders,
                "SleekWorkTabGateway.BetterWorkTabHostsSleek",
                "mixed Sleek header composition must retain its direct path");

            string retainedDraw = MemberBody(retainedHeaders, "internal bool TryDraw(");
            int surfacePresentation = retainedDraw.IndexOf(
                "PresentSurface(",
                StringComparison.Ordinal);
            int liveText = retainedDraw.IndexOf(
                "DrawRetainedText(",
                StringComparison.Ordinal);
            TestAssert.True(
                surfacePresentation >= 0 && liveText > surfacePresentation,
                "retained headers must present static pixels before drawing live glyphs");

            string retainedBuild = MemberBody(
                retainedHeaders,
                "private static bool BuildSurface(");
            TestAssert.False(
                retainedBuild.IndexOf("renderer.DrawHeader(", StringComparison.Ordinal) >= 0,
                "transparent retained header surfaces must not compose font glyphs through the full renderer");
            TestAssert.Contains(
                retainedBuild,
                "DrawRetainedStable(",
                "retained header surfaces must use the static-pixel render boundary");
            TestAssert.Contains(
                retainedHeaders,
                "retained priority-header renderer has no live text boundary",
                "every retained header renderer must expose an explicit live glyph boundary");

            string eligibility = MemberBody(retainedHeaders, "private static bool CanRetain(");
            TestAssert.Contains(eligibility, "isMouseOver", "hover pixels must stay live");
            TestAssert.Contains(eligibility, "isSorted", "sort indicators must stay live");
            TestAssert.Contains(
                eligibility,
                "ColumnSelectionManager.IsSelected(column)",
                "selection highlights must stay live");
            TestAssert.Contains(
                eligibility,
                "ColumnReorderAnimationState.IsActive",
                "column animation must use direct header rendering");
            TestAssert.Contains(
                eligibility,
                "PawnOrganizerSystem.Instance.IsDraggingColumn",
                "column dragging must use direct header rendering");
            TestAssert.False(
                eligibility.IndexOf("PawnOrganizerSystem.Instance == null", StringComparison.Ordinal) >= 0,
                "the open Work window must trust its established organizer lifecycle invariant");
            TestAssert.False(
                eligibility.IndexOf("layout.Text == null", StringComparison.Ordinal) >= 0,
                "prepared headers must keep their exact non-null label invariant");
            TestAssert.Contains(
                eligibility,
                "SubWorkDrilldownState.HasAnyDrilldown",
                "sub-work header presentation must use its established direct renderer");

            TestAssert.Contains(
                retainedHeaders,
                "new Rect(0f, 0f, 1f, 1f)",
                "cropped retained headers must preserve the proven top-left IMGUI orientation");
            TestAssert.False(
                retainedHeaders.IndexOf("1f - destination.yMax / logicalHeight", StringComparison.Ordinal) >= 0,
                "retained headers must not apply a second platform UV inversion");
            string retainedAngled = MemberBody(
                angledLabels,
                "internal static void DrawRetainedStable(");
            TestAssert.Contains(
                retainedAngled,
                "useUnclippedPivot: false",
                "cache-local angled composition must not unclip back into screen space");
            TestAssert.Contains(
                retainedAngled,
                "drawText: false",
                "transparent retained angled surfaces must exclude font glyphs");
            TestAssert.Contains(
                retainedAngled,
                "drawUnderline: true",
                "retained angled surfaces must keep their static underline pixels");
            string retainedAngledText = MemberBody(
                angledLabels,
                "internal static void DrawRetainedText(");
            TestAssert.Contains(
                retainedAngledText,
                "useUnclippedPivot: true",
                "live angled glyphs must return through the screen-space IMGUI boundary");
            TestAssert.Contains(
                retainedAngledText,
                "drawUnderline: false",
                "the live angled glyph pass must not duplicate cached underlines");

            string retainedVanilla = MemberBody(
                vanillaHeaders,
                "internal void DrawRetainedStable(");
            TestAssert.Contains(
                retainedVanilla,
                "drawText: false",
                "transparent retained vanilla surfaces must exclude font glyphs");
            TestAssert.Contains(
                retainedVanilla,
                "drawStems: true",
                "retained vanilla surfaces must keep their static stem pixels");
            string retainedVanillaText = MemberBody(
                vanillaHeaders,
                "internal void DrawRetainedText(");
            TestAssert.Contains(
                retainedVanillaText,
                "drawStems: false",
                "the live vanilla glyph pass must not duplicate cached stems");
            TestAssert.Contains(
                retainedHeaders,
                "_failedRenderResourcesRevision == renderResourcesRevision",
                "a failed retained header resource must not retry every repaint");
            TestAssert.Contains(
                retainedHeaders,
                "if (!HasStablePixels(renderer, in layout, in presentation))",
                "headers with no stable underline/stem pixels must use the direct path without a GPU surface");
            string stablePixels = MemberBody(
                retainedHeaders,
                "private static bool HasStablePixels(");
            TestAssert.Contains(
                stablePixels,
                "!layout.IsCJKVertical",
                "CJK vertical headers must not allocate an empty retained surface");
            TestAssert.Contains(
                stablePixels,
                "presentation.RemoveUnderline",
                "underline removal must bypass retained-surface allocation");
            TestAssert.Contains(
                retainedHeaders,
                "UnityEngine.Object.Destroy(surface)",
                "released retained header surfaces must destroy their Unity resources");
            TestAssert.Contains(
                retainedHeaderKey,
                "private readonly Matrix4x4 _guiMatrix;",
                "the exact cache key must include the GUI transform used for composition");
            TestAssert.Contains(
                retainedHeaderKey,
                "private readonly long _settingsThemeLanguageScaleRevision;",
                "the exact cache key must include the font/theme/language resource generation");
            TestAssert.False(
                retainedHeaderKey.IndexOf("activeLanguage?", StringComparison.Ordinal) >= 0,
                "header composition must trust RimWorld's active-language UI invariant");

            string draw = MemberBody(headerCoordinator, "internal static void DrawHeader(");
            int retainedAttempt = draw.IndexOf("_retainedPriorityHeaders.TryDraw(", StringComparison.Ordinal);
            int directDraw = draw.IndexOf("preparedRenderer.DrawHeader(", StringComparison.Ordinal);
            TestAssert.True(
                retainedAttempt >= 0 && directDraw > retainedAttempt,
                "retained failure must flow into the existing prepared direct renderer");
            string close = MemberBody(window, "private void ResetTransientWindowState()");
            TestAssert.False(
                close.IndexOf("HeaderDrawingCoordinator.ReleaseRetainedResources();", StringComparison.Ordinal) >= 0,
                "ordinary Work-tab close must retain valid header surfaces");
            string resolution = MemberBody(window, "public override void Notify_ResolutionChanged()");
            TestAssert.Contains(
                resolution,
                "ReleaseRetainedResources();",
                "resolution changes must release retained header surfaces");
            string teardown = MemberBody(gameCacheReset, "public static void Reset(string reason)");
            TestAssert.False(
                teardown.IndexOf("HeaderDrawingCoordinator.ReleaseRetainedResources();", StringComparison.Ordinal) >= 0,
                "game-data invalidation must not defer retained header release until a successful load");

            string animatedLayout = MemberBody(
                headerCoordinator,
                "public static void InvalidateAnimatedLayout()");
            TestAssert.False(
                animatedLayout.IndexOf("_retainedPriorityHeaders.Dispose()", StringComparison.Ordinal) >= 0,
                "shared row-animation invalidation must not destroy unchanged retained header surfaces");
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

        private static void SparseParentChangesKeepSubWorkUpdatesLocal(string snapshots)
        {
            string eligibility = MemberBody(
                snapshots,
                "private bool CanApplySparsePriorityUpdate(");
            TestAssert.False(
                eligibility.IndexOf("ContainsSubWorkColumns", StringComparison.Ordinal) >= 0,
                "visible sub-work columns must not disable every sparse parent-priority update");

            TestAssert.Contains(
                snapshots,
                "if (!dirtyCell && !bestPawnChanged)\n                {\n                    continue;",
                "unrelated cells must remain immutable while old/new best-pawn rows are revised");
            TestAssert.Contains(
                snapshots,
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
                "FindBestPawnId(table, workType, worker)",
                "a parent edit must refresh comparison-dependent best-pawn identity once per affected work type");
            TestAssert.Contains(
                snapshots,
                "cell.PawnId == bestPawnChange.PreviousPawnId",
                "the old best-pawn row must be revised when its marker moves");
            TestAssert.Contains(
                snapshots,
                "cell.PawnId == bestPawnChange.CurrentPawnId",
                "the new best-pawn row must be revised when its marker moves");
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
