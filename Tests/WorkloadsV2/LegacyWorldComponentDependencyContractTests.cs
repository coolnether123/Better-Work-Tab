using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class LegacyWorldComponentDependencyContractTests
    {
        private const string ConcreteComponent = "GameComponent_BWTWorldSettings";

        private static readonly HashSet<string> AllowedConsumers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Save compatibility requires the historical component type
                // to name itself. Runtime consumers have no exception.
                "Source/Features/Workloads/GameComponent_BWTWorldSettings.cs"
            };

        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Foundation", "GameState", "WorkTabPersistencePorts.cs"),
                "legacy world component dependency contracts");
            string sourceRoot = Path.Combine(root, "Source");
            string[] unexpected = Directory
                .GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains(ConcreteComponent))
                .Select(path => RelativeToRoot(root, path))
                .Where(path => !AllowedConsumers.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            TestAssert.True(
                unexpected.Length == 0,
                "ordinary domains must use WorkTabGameRoot.State ports instead of the save component. Unexpected consumers: " +
                string.Join(", ", unexpected));

            string ports = File.ReadAllText(Path.Combine(
                sourceRoot,
                "Foundation",
                "GameState",
                "WorkTabPersistencePorts.cs"));
            TestAssert.Contains(ports, "interface IWorkTabColumnOrderState",
                "column order must have its own persistence port");
            TestAssert.Contains(ports, "interface IWorkTabReassignmentState",
                "reassignment data must have its own persistence port");
            TestAssert.Contains(ports, "interface IWorkTabCustomLabelState",
                "custom labels must have their own persistence port");
            TestAssert.Contains(ports, "interface IWorkTabDividerState",
                "divider data must have its own persistence port");
            TestAssert.Contains(ports, "interface IWorkTabWorldSchemaState",
                "world schema version must have its own persistence port");
        }

        private static string Normalize(string path) => path.Replace('\\', '/');

        private static string RelativeToRoot(string root, string path)
        {
            string prefix = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            return Normalize(fullPath.Substring(prefix.Length));
        }
    }
}
