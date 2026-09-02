using System;
using System.Collections.Generic;
using System.IO;
using Better_Work_Tab.UI;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class ChromePresentationCacheTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Chrome", "WorkTabChrome.cs"),
                "chrome presentation cache contracts");
            string chrome = Read(root, "Source", "UI", "Chrome", "WorkTabChrome.cs");
            string retainedRows = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Rendering",
                "RetainedWorkBoxRowCache.cs");
            string footerCache = Read(
                root,
                "Source",
                "UI",
                "Chrome",
                "FooterInstructionTextCache.cs");
            string header = Read(root, "Source", "UI", "HeaderButtons.cs");
            ManualInputAndOverlaysRemainLive(chrome);
            RetainedResourceReleaseContinuesAfterFailures();
            SurfaceReleaseAlwaysAttemptsDestroy(retainedRows);
            FooterAndCounterPathsAvoidStableAllocations(chrome, footerCache);
            SelectorAndTooltipCachesRemainBounded(header);
        }

        private static void RetainedResourceReleaseContinuesAfterFailures()
        {
            var releaseOrder = new List<string>();
            var reportedGroups = new List<string>();

            RetainedResourceReleaseSequence.Release(
                () =>
                {
                    releaseOrder.Add("rows");
                    throw new InvalidOperationException("row device lost");
                },
                () =>
                {
                    releaseOrder.Add("headers");
                    throw new InvalidOperationException("header device lost");
                },
                (group, exception) => reportedGroups.Add(group + ":" + exception.GetType().Name));

            TestAssert.Sequence(
                new[] { "rows", "headers" },
                releaseOrder,
                "each retained owner must be attempted after an earlier release fails");
            TestAssert.Sequence(
                new[]
                {
                    "retained work-grid rows:InvalidOperationException",
                    "retained priority headers:InvalidOperationException"
                },
                reportedGroups,
                "each failed owner must report without suppressing a later release");
        }

        private static void SurfaceReleaseAlwaysAttemptsDestroy(string retainedRows)
        {
            AssertReleaseAttemptsDestroy(
                MemberBody(retainedRows, "private void ReleaseSurface(Entry entry)"),
                "retained work-grid rows");
        }

        private static void AssertReleaseAttemptsDestroy(string release, string surfaceOwner)
        {
            int releaseCall = release.IndexOf("surface.Release();", StringComparison.Ordinal);
            int finallyBlock = release.IndexOf("finally", StringComparison.Ordinal);
            int destroyCall = release.IndexOf("UnityEngine.Object.Destroy(surface);", StringComparison.Ordinal);
            TestAssert.True(
                releaseCall >= 0 && finallyBlock > releaseCall && destroyCall > finallyBlock,
                surfaceOwner + " must destroy a surface even when Unity release throws");
        }

        private static void ManualInputAndOverlaysRemainLive(string source)
        {
            string draw = MemberBody(source, "private void DrawManualPrioritiesCheckbox()");
            TestAssert.Contains(
                draw,
                "ParentPriorityRead.GetObservedManualModeForDisplay",
                "manual chrome must keep preview-aware state reads live");
            TestAssert.Contains(
                draw,
                "WorkTabEffectiveStateRuntime.TrySetManualMode",
                "manual chrome must keep mutation ownership live");
            TestAssert.Contains(
                draw,
                "DrawManualModeInspectionIndicator(rect)",
                "manual inspection highlighting must remain outside retained pixels");
            TestAssert.Contains(
                draw,
                "UIHighlighter.HighlightOpportunity",
                "tutorial/highlighter state must remain live");
            TestAssert.Contains(
                draw,
                "DrawManualPrioritiesCheckboxDirect(rect, requestedEnabled)",
                "manual checkbox label and control must stay on the live presentation pass");
            TestAssert.False(
                draw.IndexOf("retainedPresentation", StringComparison.Ordinal) >= 0,
                "manual chrome must not branch on an invalid retained surface result");

            string input = MemberBody(source, "private static void HandleManualPrioritiesCheckboxInput(");
            TestAssert.Contains(
                input,
                "Widgets.ToggleInvisibleDraggable",
                "manual hit testing must remain a live invisible toggle");
            TestAssert.Contains(
                source,
                "DrawManualPrioritiesHelp(rect, maxPriority)",
                "manual help text must stay on the live presentation pass");
        }

        private static void FooterAndCounterPathsAvoidStableAllocations(
            string source,
            string footerCache)
        {
            string footer = MemberBody(source, "private void DrawBottomRightButtons(");
            TestAssert.False(
                footer.IndexOf("new List<string>", StringComparison.Ordinal) >= 0,
                "stable footer repaint must not allocate a List<string>");
            TestAssert.False(
                footer.IndexOf("string.Join", StringComparison.Ordinal) >= 0,
                "stable footer repaint must not join instruction strings");
            TestAssert.Contains(
                footer,
                "_footerInstructionTextCache.GetInstructionText",
                "footer text must pass through its bounded presentation cache");
            TestAssert.Contains(
                footer,
                "TryGetSubWorkExitTarget",
                "footer sub-work hit testing must remain live");
            TestAssert.Contains(
                footer,
                "TryGetSubWorkOpenTarget",
                "footer sub-work open hit testing must remain live");

            string counters = MemberBody(source, "internal void DrawBottomCounters(");
            TestAssert.False(
                counters.IndexOf(".Translate(", StringComparison.Ordinal) >= 0,
                "counter repaint must use cached translated labels");
            TestAssert.False(
                counters.IndexOf("Text.CalcSize", StringComparison.Ordinal) >= 0,
                "counter repaint must use cached label width");

            TestAssert.False(
                footerCache.IndexOf("new List<string>", StringComparison.Ordinal) >= 0,
                "footer cache must not allocate a list while composing stable instructions");
            TestAssert.False(
                footerCache.IndexOf("string.Join", StringComparison.Ordinal) >= 0,
                "footer cache must compose at most one bounded string without string.Join");
        }

        private static void SelectorAndTooltipCachesRemainBounded(string source)
        {
            string selector = MemberBody(source, "internal static float MeasureSelectorWidth(");
            TestAssert.Contains(
                selector,
                "_selectorWidthSlotAValid",
                "selector width cache must have a bounded first slot");
            TestAssert.Contains(
                selector,
                "_selectorWidthSlotBValid",
                "selector width cache must have a bounded second slot");
            TestAssert.Contains(
                selector,
                "WorkTabPresentationRevision.Current",
                "selector widths must invalidate on presentation revision");
            TestAssert.Contains(
                selector,
                "BWTBottomBarSelector.MeasureWidth(value)",
                "selector width measurement must remain the original miss path");

            string top = MemberBody(source, "public static void DrawTopRightFluffyStyle(");
            TestAssert.Contains(
                top,
                "ParentPriorityRead.GetObservedManualModeForDisplay",
                "top-button state reads must remain live");
            TestAssert.Contains(
                top,
                "EnsureTopTooltipCache",
                "top-button tooltip translation must be cached by state");

            string ruleset = MemberBody(source, "private static void DrawAutoAssignGroup(");
            TestAssert.Contains(
                ruleset,
                "RuleBuilderGateway.CurrentRulesetLabel()",
                "ruleset label reads must remain live");
            TestAssert.Contains(
                ruleset,
                "RuleBuilderGateway.HasCurrentRuleset()",
                "ruleset presence reads must remain live");
            TestAssert.Contains(
                ruleset,
                "EnsureRulesetTooltipCache",
                "ruleset tooltip translations must be cached by label and state");
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
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }

            throw new InvalidOperationException("source member has no closing brace");
        }
    }
}
