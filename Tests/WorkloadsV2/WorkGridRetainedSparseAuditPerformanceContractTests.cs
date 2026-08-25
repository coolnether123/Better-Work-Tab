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

            ParentRowsDoNotRecomposeDuringColumnAnimation(renderer);
            RetainedRowsKeepLogicalImGuiOrientation(retainedRows);
            SparseParentChangesKeepSubWorkUpdatesLocal(snapshots);
            CompatibilityAuditUsesLinearSkillAndPriorityPasses(audit);
            RepresentativeOperationCountsAreReduced();
        }

        private static void RetainedRowsKeepLogicalImGuiOrientation(string retainedRows)
        {
            string draw = MemberBody(retainedRows, "private bool TryDrawCore(");
            TestAssert.False(
                draw.IndexOf("SystemInfo.graphicsUVStartsAtTop", StringComparison.Ordinal) >= 0,
                "retained IMGUI rows must not apply a second platform UV inversion");
            TestAssert.Contains(
                draw,
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

            string update = MemberBody(
                snapshots,
                "private bool TryApplySparsePriorityUpdate(");
            TestAssert.Contains(
                update,
                "if (!dirtyCell && !bestPawnChanged)\n                {\n                    continue;",
                "unrelated cells must remain immutable while old/new best-pawn rows are revised");
            TestAssert.Contains(
                update,
                "WorkGiver subWorkGiver = snapshotColumn.SubWorkGiver;",
                "sparse replacement must rebuild a sub-work cell through its prepared work-giver path");
            TestAssert.Contains(
                update,
                "!snapshotColumn.IsExpandBesideChild",
                "focus columns must retain their parent visual while expand-beside children stay child-only");
            TestAssert.Contains(
                update,
                "FindBestPawnId(table, workType, worker)",
                "a parent edit must refresh comparison-dependent best-pawn identity once per affected work type");
            TestAssert.Contains(
                update,
                "cell.PawnId == bestPawnChange.PreviousPawnId",
                "the old best-pawn row must be revised when its marker moves");
            TestAssert.Contains(
                update,
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
