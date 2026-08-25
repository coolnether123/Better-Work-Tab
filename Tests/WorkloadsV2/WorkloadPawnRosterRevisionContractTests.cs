using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Source contracts for the RimWorld-bound roster cache. The deterministic
    /// suite cannot construct Verse pawn registries, so these checks pin the
    /// boundary that keeps global roster scans out of IMGUI frame preparation.
    /// </summary>
    internal static class WorkloadPawnRosterRevisionContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Workloads", "WorkloadPawnRosterCache.cs"),
                "workload pawn-roster revision contracts");
            string gateway = Read(
                root,
                "Source",
                "UI",
                "Workloads",
                "WorkloadGateway.cs");
            string roster = Read(
                root,
                "Source",
                "UI",
                "Workloads",
                "WorkloadPawnRosterCache.cs");
            string liveAdapter = Read(
                root,
                "Source",
                "UI",
                "Workloads",
                "Projection",
                "BwtLiveWorkTabEffectiveStateAdapter.cs");
            string pawnTablePatch = Read(
                root,
                "Source",
                "Features",
                "Patches",
                "Patch_PawnTable_RecacheIfDirty.cs");
            string worldComponent = Read(
                root,
                "Source",
                "Features",
                "Workloads",
                "GameComponent_BWTWorldSettings.cs");

            PreviewReadsUseTheRosterRevision(gateway);
            CandidateSemanticsStayAtTheRosterBoundary(roster);
            ExistingPawnTableInvalidationAdvancesTheRevision(roster, pawnTablePatch);
            AuditRunsFromTheExistingWorldComponent(roster, worldComponent);
            PawnResolutionUsesTheSharedIndex(gateway, liveAdapter, roster);
        }

        private static void PreviewReadsUseTheRosterRevision(string gateway)
        {
            string revision = MethodBody(
                gateway,
                "private long ComputeAvailablePawnSetRevision()");
            TestAssert.Contains(
                revision,
                "return WorkloadPawnRosterCache.CurrentRevision;",
                "preview frame preparation must read the event-driven roster revision");
            TestAssert.False(
                revision.IndexOf("PawnsFinder", StringComparison.Ordinal) >= 0,
                "preview frame preparation must not scan the global pawn population");
            TestAssert.False(
                revision.IndexOf("Time.frameCount", StringComparison.Ordinal) >= 0,
                "roster freshness must not be scoped to one Unity frame");

            string candidates = MethodBody(
                gateway,
                "private IReadOnlyList<PawnScopeCandidate> BuildAvailableCandidates()");
            TestAssert.Contains(
                candidates,
                "return WorkloadPawnRosterCache.GetCandidates();",
                "membership classification must consume the revision-owned candidate snapshot");
        }

        private static void CandidateSemanticsStayAtTheRosterBoundary(string roster)
        {
            TestAssert.Contains(
                roster,
                "PawnsFinder.AllMapsWorldAndTemporary_Alive",
                "the optimized roster must preserve the original candidate universe");
            TestAssert.Contains(
                roster,
                "WorkTabEffectiveStateIds.ForPawn(pawn)",
                "candidate identity must remain the effective-state pawn key");
            TestAssert.Contains(
                roster,
                "bool isCurrentMap = pawn.Map == currentMap;",
                "candidate scope must retain current-map membership");
            TestAssert.Contains(
                roster,
                "pawn.IsColonist",
                "candidate scope must retain colonist membership");
            TestAssert.Contains(
                roster,
                "isCurrentMap && pawn.IsFreeColonist",
                "free-colonist membership must remain restricted to the current map");
        }

        private static void ExistingPawnTableInvalidationAdvancesTheRevision(
            string roster,
            string pawnTablePatch)
        {
            TestAssert.Contains(
                pawnTablePatch,
                "UI.Workloads.WorkloadPawnRosterCache.NotifyRosterChanged();",
                "the existing vanilla pawn-table notification must advance the workload roster revision");
            TestAssert.False(
                roster.IndexOf("[HarmonyPatch]", StringComparison.Ordinal) >= 0,
                "the roster cache must not create a parallel Harmony lifecycle subsystem");
            TestAssert.False(
                roster.IndexOf("GameComponent_WorkloadPawnRosterAudit", StringComparison.Ordinal) >= 0,
                "the roster cache must not create a parallel game component");
        }

        private static void AuditRunsFromTheExistingWorldComponent(
            string roster,
            string worldComponent)
        {
            TestAssert.Contains(
                roster,
                "internal const int AuditIntervalTicks = 250;",
                "the defensive audit must remain infrequent and explicit");
            string update = MethodBody(worldComponent, "public override void GameComponentUpdate()");
            TestAssert.Contains(
                update,
                "WorkloadPreviewController.Current?.IsActive == true",
                "the defensive audit must stay dormant without an active preview");
            TestAssert.Contains(
                update,
                "WorkloadPawnRosterCache.AuditIfDue(Find.TickManager.TicksGame);",
                "the existing world-settings component must run the audit outside IMGUI");

            string currentRevision = PropertyBody(roster, "internal static long CurrentRevision");
            TestAssert.False(
                currentRevision.IndexOf("ComputeCandidateSignature", StringComparison.Ordinal) >= 0,
                "a per-frame revision read must remain O(1)");
        }

        private static void PawnResolutionUsesTheSharedIndex(
            string gateway,
            string liveAdapter,
            string roster)
        {
            TestAssert.Contains(
                roster,
                "private static readonly Dictionary<int, Pawn> PawnsByThingId",
                "the roster owner must retain one pawn-ID index per revision");
            TestAssert.Contains(
                gateway,
                "return WorkloadPawnRosterCache.ResolvePawn(thingId);",
                "the workload controller must use the shared pawn index");
            TestAssert.Contains(
                liveAdapter,
                "return WorkloadPawnRosterCache.ResolvePawn(thingId);",
                "the live effective-state adapter must use the shared pawn index");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
            }

            return File.ReadAllText(path);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source method " + signature);
            return BalancedBlock(source, start);
        }

        private static string PropertyBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source property " + signature);
            return BalancedBlock(source, start);
        }

        private static string BalancedBlock(string source, int start)
        {
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "source member has no opening brace");
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(start, i - start + 1);
                    }
                }
            }

            throw new InvalidOperationException("source member has no closing brace");
        }
    }
}
