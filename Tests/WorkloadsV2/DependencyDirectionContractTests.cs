using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class DependencyDirectionContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "Application", "WorkTabAtomicMutationPlan.cs"),
                "dependency direction contracts");

            LeaseIsNeutral(root);
            CanonicalizationIsNeutral(root);
            SpecificJobStorageIsAdapterOwned(root);
            AmbientApplicationEdgesAreExplicit(root);
        }

        private static void LeaseIsNeutral(string root)
        {
            string leasePath = Path.Combine(
                root,
                "Source",
                "Foundation",
                "Transactions",
                "WorkTabMutationLease.cs");
            TestAssert.True(File.Exists(leasePath),
                "the compound mutation lease must have a neutral owner");
            string lease = File.ReadAllText(leasePath);
            TestAssert.Contains(
                lease,
                "namespace Better_Work_Tab.Foundation.Transactions",
                "the mutation lease must not be owned by a feature application namespace");
            TestAssert.False(
                File.Exists(Path.Combine(
                    root,
                    "Source",
                    "Features",
                    "Application",
                    "WorkTabMutationAuthorization.cs")),
                "the former application-owned lease file must not remain authoritative");
        }

        private static void CanonicalizationIsNeutral(string root)
        {
            string canonical = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Foundation",
                "Canonicalization",
                "DeterministicCanonical.cs"));
            string workloadFacade = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "Workloads",
                "V2",
                "WorkloadCanonical.cs"));
            string reassignment = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "WorkGiverReassignmentData.cs"));

            TestAssert.Contains(canonical, "internal static class DeterministicCanonical",
                "neutral canonicalization must own deterministic encoding and hashing");
            TestAssert.Contains(workloadFacade, "DeterministicCanonical.Fingerprint(canonical)",
                "the workload compatibility facade must delegate fingerprinting");
            TestAssert.Contains(reassignment, "using Better_Work_Tab.Foundation.Canonicalization;",
                "specific-job persistence must depend on neutral canonicalization");
            TestAssert.False(
                reassignment.IndexOf("Features.Workloads", StringComparison.Ordinal) >= 0,
                "specific-job persistence must not depend on the workload feature");
        }

        private static void SpecificJobStorageIsAdapterOwned(string root)
        {
            string plan = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "Application",
                "WorkTabAtomicMutationPlan.cs"));
            string adapter = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "SpecificJobPriorityAuthorityAdapter.cs"));

            TestAssert.False(
                plan.IndexOf("Sleek", StringComparison.OrdinalIgnoreCase) >= 0,
                "the atomic plan must not import or name an optional specific-job provider");
            TestAssert.Contains(plan, "SpecificJobPriorityAuthorityAdapter.Capture(pawn, workGiver)",
                "the atomic plan must capture specific-job storage through the neutral seam");
            TestAssert.Contains(adapter, "internal static SpecificJobPriorityStorageSnapshot Capture",
                "the storage adapter must own authority selection and capture");
            TestAssert.Contains(adapter, "internal static bool TryRead(",
                "the storage adapter must own provider-specific reads");
            TestAssert.Contains(adapter, "internal static bool TryWriteExternal(",
                "the storage adapter must own provider-specific writes");
            TestAssert.Contains(adapter, "internal static bool TryRestoreExternal(",
                "the storage adapter must own provider-specific restoration");
        }

        private static void AmbientApplicationEdgesAreExplicit(string root)
        {
            string sourceRoot = Path.Combine(root, "Source");
            var allowedEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "API/TimePriorityApi.cs",
                "Features/Patches/Patch_WorkPriority_DoCell_Unified.cs",
                "Features/TimePriority/FluffyTimeScheduleAssigner.cs",
                "Features/TimePriority/TimePriorityService.cs",
                "Features/Workloads/V2/Runtime/Workload2Backend.cs",
                "Mod Support/Mods/Sleek Work Priorities/SleekWorkTabPriorityHandoff.cs",
                "UI/Headers/Angled/AngledHeaderInteraction.cs",
                "UI/Schedule/ScheduleProjection.cs",
                "UI/Settings/BWTWorkTabContextSettingsRouter.cs",
                "UI/WorkGiverReassignments/WorkGiverPriorityBoxRenderer.cs",
                "UI/WorkGrid/Invalidation/WorkGridInvalidationAudit.cs",
                "UI/WorkGrid/Projection/WorkTabEffectiveStateRuntime.cs"
            };

            string[] actualEdges = Directory.GetFiles(
                    sourceRoot,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("WorkTabApplication.Current"))
                .Select(path => path.Substring(sourceRoot.Length + 1).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            TestAssert.Equal(
                string.Join("|", allowedEdges.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)),
                string.Join("|", actualEdges),
                "ambient application lookup must remain limited to API, Harmony, MP, compatibility, and static UI edges");

            string mainWindow = File.ReadAllText(Path.Combine(
                sourceRoot,
                "UI",
                "MainTabWindow_BetterWork.cs"));
            string commands = File.ReadAllText(Path.Combine(
                sourceRoot,
                "UI",
                "WorkGrid",
                "Commands",
                "WorkPriorityCommandGateway.cs"));
            string drag = File.ReadAllText(Path.Combine(
                sourceRoot,
                "UI",
                "WorkGiverReassignments",
                "WorkGiverDragHandler.cs"));

            TestAssert.Contains(
                mainWindow,
                "_application = WorkTabGameRoots.For(Current.Game)?.Application;",
                "the Work-tab window must bind commands from the per-game composition root");
            TestAssert.False(
                commands.Contains("WorkTabApplication.Current"),
                "the normal WorkGrid command adapter must receive its application dependency");
            TestAssert.False(
                drag.Contains("WorkTabApplication.Current"),
                "the owned sub-work drag controller must receive its application dependency");
        }
    }
}
