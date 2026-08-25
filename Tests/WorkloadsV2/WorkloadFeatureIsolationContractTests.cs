using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkloadFeatureIsolationContractTests
    {
        private static readonly string[] NormalFeatureRoots =
        {
            "Source/Foundation",
            "Source/Features/Application",
            "Source/Features/Rules",
            "Source/Features/TimePriority",
            "Source/Features/WorkGiverReassignments",
            "Source/UI/WorkGrid",
            "Source/UI/Settings"
        };

        private static readonly string[] ForbiddenNormalFeatureTokens =
        {
            "using Better_Work_Tab.Features.Workloads",
            "using Better_Work_Tab.UI.Workloads",
            "WorkloadProjectionRuntime",
            "WorkloadGateway",
            "WorkloadSession",
            "Workload2Backend",
            "GameComponent_BWTWorldSettings"
        };

        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "Application", "WorkTabApplication.cs"),
                "workload feature isolation contracts");

            NormalFeaturesDoNotReferenceWorkloads(root);
            ApplicationContractsAreProviderNeutral(root);
            WorkGridRuntimeAndRendererArePreviewNeutral(root);
            HeaderHostIsOptionalFeatureNeutral(root);
            WorkloadBackendUsesOnlyDomainPorts(root);
            WorldComponentIsOnlyItsSaveShell(root);
        }

        private static void HeaderHostIsOptionalFeatureNeutral(string root)
        {
            string host = Read(root, "Source", "UI", "HeaderButtons.cs");
            string contracts = Read(root, "Source", "UI", "HeaderFooterFeatureContracts.cs");
            string neutral = host + "\n" + contracts;
            foreach (string token in new[]
            {
                "Workload",
                "Features.Workloads",
                "UI.Workloads",
                "WorkloadGateway",
                "WorkloadPreviewController"
            })
            {
                TestAssert.False(
                    neutral.IndexOf(token, StringComparison.Ordinal) >= 0,
                    "the header host must know only the optional footer contract; found " + token);
            }

            TestAssert.Contains(
                contracts,
                "interface IHeaderFooterFeature",
                "the header host must expose one typed optional-feature boundary");
            TestAssert.Contains(
                host,
                "HeaderFooterFeatureRegistry.Current?.Draw(rects)",
                "the header host must delegate optional drawing without feature lookup in its hot path");
        }

        private static void NormalFeaturesDoNotReferenceWorkloads(string root)
        {
            var violations = new List<string>();
            for (int directoryIndex = 0;
                 directoryIndex < NormalFeatureRoots.Length;
                 directoryIndex++)
            {
                string directory = Path.Combine(
                    root,
                    NormalFeatureRoots[directoryIndex].Replace('/', Path.DirectorySeparatorChar));
                foreach (string path in Directory.GetFiles(
                             directory,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    string source = File.ReadAllText(path);
                    for (int tokenIndex = 0;
                         tokenIndex < ForbiddenNormalFeatureTokens.Length;
                         tokenIndex++)
                    {
                        string token = ForbiddenNormalFeatureTokens[tokenIndex];
                        if (source.IndexOf(token, StringComparison.Ordinal) >= 0)
                        {
                            violations.Add(Relative(root, path) + " -> " + token);
                        }
                    }
                }
            }

            TestAssert.True(
                violations.Count == 0,
                "normal BWT features must compile and define behavior without Workloads: " +
                string.Join(", ", violations.OrderBy(value => value, StringComparer.Ordinal)));
        }

        private static void ApplicationContractsAreProviderNeutral(string root)
        {
            string application = Read(root, "Source", "Features", "Application", "WorkTabApplication.cs");
            string staged = Read(root, "Source", "Features", "Application", "WorkTabStagedMutation.cs");
            string atomic = Read(root, "Source", "Features", "Application", "WorkTabAtomicMutation.cs");
            string contracts = application + "\n" + staged + "\n" + atomic;
            foreach (string token in new[]
            {
                "Workload",
                "SleekWorkTabGateway",
                "FluffyWorkTabGateway",
                "WorkGiverReassignmentManager.SpecificPriorityBatchEntry",
                "WorkGiverReassignmentManager.SpecificOrderBatchEntry",
                "WorkGiverReassignmentManager.SpecificJobBatchRollback"
            })
            {
                TestAssert.False(
                    contracts.IndexOf(token, StringComparison.Ordinal) >= 0,
                    "application and staged contracts must not expose optional/provider implementation token " + token);
            }

            TestAssert.Contains(
                staged,
                "IWorkTabSpecificJobRollbackReceipt",
                "the staged receipt must retain one opaque specific-job rollback capability");
            TestAssert.Contains(
                staged,
                "WorkTabStagedSpecificPriority",
                "specific priority staging must use an application-owned value");
            TestAssert.Contains(
                staged,
                "WorkTabStagedSpecificOrder",
                "specific order staging must use an application-owned value");
        }

        private static void WorkGridRuntimeAndRendererArePreviewNeutral(string root)
        {
            string runtime = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Projection",
                "WorkTabEffectiveStateRuntime.cs");
            string renderingRoot = Path.Combine(root, "Source", "UI", "WorkGrid", "Rendering");
            string rendering = string.Join(
                "\n",
                Directory.GetFiles(renderingRoot, "*.cs", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(File.ReadAllText));

            foreach (string token in new[]
            {
                "Workload",
                "WorkloadProjectionRuntime",
                "WorkloadGateway",
                "Features.Workloads",
                "UI.Workloads"
            })
            {
                TestAssert.False(
                    runtime.IndexOf(token, StringComparison.Ordinal) >= 0,
                    "effective-state runtime must not name Workload implementation token " + token);
                TestAssert.False(
                    rendering.IndexOf(token, StringComparison.Ordinal) >= 0,
                    "renderer must consume only captured neutral preview state; found " + token);
            }

            TestAssert.Contains(
                rendering,
                "InspectionHighlightsEnabled",
                "renderer highlight policy must be captured by the preview view");
            TestAssert.Contains(
                rendering,
                "InspectionOpacity",
                "renderer opacity must be captured by the preview view");
        }

        private static void WorkloadBackendUsesOnlyDomainPorts(string root)
        {
            string backend = Read(
                root,
                "Source",
                "Features",
                "Workloads",
                "V2",
                "Runtime",
                "Workload2Backend.cs");
            foreach (string token in new[]
            {
                "WorkGiverReassignmentManager",
                "WorkPrioritySystem",
                "PriorityAuthorityBroker",
                "TimePriorityService",
                "GameComponent_BWTWorldSettings"
            })
            {
                TestAssert.False(
                    backend.IndexOf(token, StringComparison.Ordinal) >= 0,
                    "Workload2Backend must use neutral domain/world ports instead of " + token);
            }

            TestAssert.Contains(backend, "IWorkTabPriorityCapturePort",
                "Workload baseline capture must use the neutral priority port");
            TestAssert.Contains(backend, "IWorkTabScheduleCapturePort",
                "Workload baseline capture must use the neutral schedule port");
            TestAssert.Contains(backend, "IWorkTabSpecificJobCapturePort",
                "Workload baseline capture must use the neutral specific-job port");
        }

        private static void WorldComponentIsOnlyItsSaveShell(string root)
        {
            string componentPath = Path.Combine(
                root,
                "Source",
                "Features",
                "Workloads",
                "GameComponent_BWTWorldSettings.cs");
            string[] consumers = Directory.GetFiles(
                    Path.Combine(root, "Source"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(componentPath),
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => File.ReadAllText(path).IndexOf(
                    "GameComponent_BWTWorldSettings",
                    StringComparison.Ordinal) >= 0)
                .Select(path => Relative(root, path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            TestAssert.True(
                consumers.Length == 0,
                "runtime features must use WorkTabGameRoot or a narrow feature state port, not the save shell: " +
                string.Join(", ", consumers));
        }

        private static string Read(string root, params string[] parts) =>
            File.ReadAllText(parts.Aggregate(root, Path.Combine));

        private static string Relative(string root, string path)
        {
            string prefix = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).Substring(prefix.Length).Replace('\\', '/');
        }
    }
}
