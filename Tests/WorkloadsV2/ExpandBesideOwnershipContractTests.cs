using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Guards the ownership boundary for BWT's built-in expand-beside presentation.
    /// The optional Fluffy gateway may handle only external reflection and coexistence.
    /// </summary>
    internal static class ExpandBesideOwnershipContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "WorkGiverReassignments", "BwtExpandBesideColumns.cs"),
                "expand-beside ownership contracts");
            string nativeColumns = Read(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "BwtExpandBesideColumns.cs");
            string nativeWorker = Read(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "PawnColumnWorker_BwtSubWorkPriority.cs");
            string state = Read(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "SubWorkDrilldownState.cs");
            string layout = Read(root, "Source", "PawnOrganizer", "Layout", "WorkTabLayoutController.cs");
            string header = Read(root, "Source", "UI", "Headers", "WorkTabHeaderRenderer.cs");
            string input = Read(root, "Source", "UI", "WorkGiverReassignments", "SubWorkInteractionController.cs");
            string registry = Read(root, "Source", "UI", "Settings", "BWTSettingsRegistry.cs");
            string gateway = Read(
                root,
                "Source",
                "Mod Support",
                "Mods",
                "Fluffy WorkTab",
                "FluffyWorkTabGateway.cs");

            NativeColumnsAreIndependentOfExternalFluffy(nativeColumns, nativeWorker);
            PersistedExpandBesideRemainsTheEffectiveStyle(state);
            LayoutAndHeadersUseTheNativeColumnOwner(layout, header);
            ExpandBesideSupportsTheSameMouseExitGesture(input);
            SettingsAndGatewayDoNotRetainTheNativeColumnGate(registry, gateway);
        }

        private static void NativeColumnsAreIndependentOfExternalFluffy(
            string nativeColumns,
            string nativeWorker)
        {
            TestAssert.Contains(
                nativeColumns,
                "internal static bool CanBuild",
                "BWT must own an explicit native expand-beside health boundary");
            TestAssert.Contains(
                nativeColumns,
                "TryBuildColumnSpecs(",
                "BWT must build its own expand-beside column specifications");
            TestAssert.Contains(
                nativeColumns,
                "TryGetWorkGiver(",
                "BWT must retain native child-to-work-giver identity");
            TestAssert.False(
                nativeColumns.IndexOf("FluffyWorkTabGateway", StringComparison.Ordinal) >= 0,
                "native expand-beside columns must not depend on the optional Fluffy gateway");
            TestAssert.False(
                nativeColumns.IndexOf("enableFluffyStyleFeatures", StringComparison.Ordinal) >= 0,
                "native expand-beside availability must not be gated by Fluffy-style feature settings");
            TestAssert.Contains(
                nativeWorker,
                "BwtExpandBesideColumns.TryGetWorkGiver(def)",
                "the BWT child worker must sort through the native column identity map");
        }

        private static void PersistedExpandBesideRemainsTheEffectiveStyle(string state)
        {
            string effective = MemberBody(state, "internal static BetterWorkTabSettings.SubWorkDrilldownStyle EffectiveDrilldownStyle()");
            TestAssert.Contains(
                effective,
                "SubWorkDrilldownStyle.NotChosen",
                "only an undecided specific-job style may fall back to Focus view");
            TestAssert.False(
                effective.IndexOf("FluffyWorkTabGateway", StringComparison.Ordinal) >= 0,
                "the effective persisted view style must not be rewritten by external-mod compatibility");

            string toggle = MemberBody(state, "internal static void ToggleExpandBeside(WorkTypeDef workType)");
            TestAssert.Contains(
                toggle,
                "BwtExpandBesideColumns.CanBuild",
                "expand-beside entry must use BWT's own health boundary");
            TestAssert.False(
                toggle.IndexOf("Enter(workType)", StringComparison.Ordinal) >= 0,
                "a failed native expand-beside capability must not silently enter Focus view");
        }

        private static void LayoutAndHeadersUseTheNativeColumnOwner(string layout, string header)
        {
            TestAssert.Contains(
                layout,
                "BwtExpandBesideColumns.TryBuildColumnSpecs(",
                "layout must insert BWT-native expand-beside child columns");
            TestAssert.Contains(
                layout,
                "BwtExpandBesideColumns.TryGetWorkGiver(childColumns[slot])",
                "layout must retain the BWT-native child work-giver identity");
            TestAssert.Contains(
                header,
                "BwtExpandBesideColumns.GetHeaderLaneRect(",
                "header geometry must use BWT-native expand-beside column ownership");
            TestAssert.Contains(
                header,
                "BwtExpandBesideColumns.IsNativeColumn(column.Column)",
                "header rendering must recognize BWT-native child columns independently of Fluffy");
        }

        private static void ExpandBesideSupportsTheSameMouseExitGesture(string input)
        {
            string exit = MemberBody(input, "internal bool TryHandleSubWorkExitGesture(in WorkTabView view)");
            TestAssert.Contains(
                exit,
                "SubWorkDrilldownState.HasAnyDrilldown",
                "the exit gesture must cover both Focus and Expand-beside state");
            TestAssert.False(
                exit.IndexOf("if (!SubWorkDrilldownState.IsActive)", StringComparison.Ordinal) >= 0,
                "the mouse exit gesture must not exclude an active expand-beside presentation");
        }

        private static void SettingsAndGatewayDoNotRetainTheNativeColumnGate(string registry, string gateway)
        {
            TestAssert.Contains(
                registry,
                "BwtExpandBesideColumns.CanBuild && UsesExpandBesideDrilldown()",
                "the expand-beside settings suppression must use BWT's native capability");
            TestAssert.False(
                gateway.IndexOf("CanHostFluffySubWorkColumns", StringComparison.Ordinal) >= 0,
                "the external Fluffy gateway must not own BWT-native expand-beside capability");
            TestAssert.False(
                gateway.IndexOf("NativeHostedWorkGivers", StringComparison.Ordinal) >= 0,
                "the external Fluffy gateway must not retain BWT-native child identity");
            TestAssert.Contains(
                gateway,
                "PrepareExternalFluffyDraw(",
                "external Fluffy drawing must remain an explicitly named compatibility path");
        }

        private static string Read(string root, params string[] segments)
        {
            return File.ReadAllText(Path.Combine(Combine(root, segments)));
        }

        private static string Combine(string root, string[] segments)
        {
            string path = root;
            for (int i = 0; i < segments.Length; i++)
            {
                path = Path.Combine(path, segments[i]);
            }

            return path;
        }

        private static string MemberBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "expected member was not found: " + signature);
            int openBrace = source.IndexOf('{', start);
            TestAssert.True(openBrace >= 0, "expected member body was not found: " + signature);
            int depth = 0;
            for (int i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}' && --depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("expected member body was not closed: " + signature);
        }
    }
}
