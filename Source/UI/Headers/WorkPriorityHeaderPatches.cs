using HarmonyLib;
using ModAPI.Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI.Headers.Vanilla;
using Better_Work_Tab.UI.Headers.Angled;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Harmony patches for PawnColumnWorker_WorkPriority to inject custom header rendering and interactions.
    /// Supports both Vanilla (staggered) and Angled header styles.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Patch
    {
        /// <summary>
        /// The work type currently being hovered, as detected by the active controller.
        /// </summary>
        public static WorkTypeDef HoveredWorkType => HeaderInputController.HoveredWorkType;

        /// <summary>
        /// Checks if the currently open tab is a Work tab (vanilla or BWT).
        /// Returns false for other tabs like MechTab to avoid interference.
        /// </summary>
        public static bool IsWorkTab()
        {
            if (BetterWorkTabMod.Settings == null) return false;
            var windowStack = Find.WindowStack;
            if (windowStack == null) return false;
            
            var windows = windowStack.Windows;
            if (windows == null) return false;

            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] is MainTabWindow_Work)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Prefix patch that redirects standard header rendering to the custom system.
        /// </summary>
        /// <returns>False to skip the original vanilla method, True to allow it (fallback).</returns>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            // Only apply BWT patches to the Work tab (vanilla or BWT), not other tabs like MechTab
            if (!IsWorkTab())
                return true;

            try
            {
                // Update shared input cache once per frame
                HeaderInputController.UpdateCache(Event.current);

                var settings = BetterWorkTabMod.Settings;
                if (settings == null) return true;

                var workType = __instance?.def?.workType;
                if (workType == null) return false;

                bool enableAngled = settings.enableAngledHeaders;

                if (enableAngled)
                {
                     // Angled Mode: Always take over execution if enabled
                     return AngledHeaderController.DoHeader(__instance, rect, table); // Returns false to skip vanilla
                }
                else
                {
                     // Vanilla Mode: Only intervenes if columns are moved; otherwise, permits vanilla execution
                     return VanillaHeaderController.DoHeader(__instance, rect, table);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BWT] WorkPriority header failed: {ex}");
                return true; // Fallback to vanilla on error
            }
        }

        /// <summary>
        /// Postfix patch (currently empty, kept for structural symmetry).
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            // Logic handled in Prefix
        }
    }

    /// <summary>
    /// Patches the header height calculation to accommodate staggered or angled labels.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.GetMinHeaderHeight))]
    public static class Patch_PawnColumnWorker_WorkPriority_GetMinHeaderHeight
    {
        /// <summary>
        /// Postfix that expands the header height if needed by the active layout strategy.
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, PawnTable table, ref int __result)
        {
            // Only apply to the Work tab (vanilla or BWT), not other tabs like MechTab
            if (!PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab())
                return;

            var settings = BetterWorkTabMod.Settings;
            if (settings == null) return;

            bool enableAngled = settings.enableAngledHeaders;

            if (enableAngled)
            {
                AngledHeaderController.CalculateMinHeaderHeight(table, ref __result);
            }
            else
            {
                VanillaHeaderController.CalculateMinHeaderHeight(table, ref __result);
            }
        }
    }

    /// <summary>
    /// Transpiler: injects a condition before the vanilla header highlight draw to skip
    /// the vanilla highlight if:
    /// - Angled headers are enabled AND
    /// - We're in a PawnColumnWorker_WorkPriority (our custom header handler)
    /// 
    /// Result: Custom hover highlights override vanilla defaults, preventing artifacts 
    /// where both vanilla and BWT stagger systems attempt to render simultaneously.
    ///
    /// IL Pseudo-code:
    ///   if (!Settings.enableAngledHeaders) goto do_highlight;
    ///   if (!(this is PawnColumnWorker_WorkPriority)) goto do_highlight;
    ///   goto skip_highlight;
    ///   run_original:
    ///     Widgets.DrawHighlight(rect);
    ///   skip_original:
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader))]
    public static class Patch_PawnColumnWorker_DoHeader_DisableHighlight
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
        {
            var drawHighlightMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlight), new[] { typeof(Rect) });
            var workPriorityWorkerType = typeof(PawnColumnWorker_WorkPriority);
            var settingsField = AccessTools.Field(typeof(BetterWorkTabMod), nameof(BetterWorkTabMod.Settings));
            var angledHeadersField = AccessTools.Field(typeof(BetterWorkTabSettings), nameof(BetterWorkTabSettings.enableAngledHeaders));
            var isWorkTabMethod = AccessTools.Method(
                typeof(PawnColumnWorker_WorkPriority_DoHeader_Patch),
                nameof(PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab));

            return FluentTranspilerExecution.ExecuteOrOriginal(
                instructions,
                original,
                il,
                transpiler =>
                {
                    FluentReplacementResult result = transpiler.BeforeCall(drawHighlightMethod)
                                                               .IncludingPreviousInstruction()
                                                               .SkipOriginalWhen(
                                                                   guard => guard
                                                                       .RequireStaticFieldNotNull(settingsField)
                                                                       .RequireStaticFieldInstanceFieldTrue(settingsField, angledHeadersField)
                                                                       .RequireCallTrue(isWorkTabMethod)
                                                                       .SkipIfThisIs(workPriorityWorkerType),
                                                                   "Skip vanilla work-priority header highlight");

                    if (result == FluentReplacementResult.NoMatch)
                    {
                        throw new InvalidOperationException("expected Widgets.DrawHighlight call was not found");
                    }
                },
                (codes, method, exception) => ReturnOriginalWithWarning(codes, method, exception));
        }

        private static IEnumerable<CodeInstruction> ReturnOriginalWithWarning(
            List<CodeInstruction> codes,
            MethodBase original,
            Exception exception)
        {
            string methodName = original?.DeclaringType != null
                ? $"{original.DeclaringType.FullName}.{original.Name}"
                : original?.Name ?? "<unknown method>";

            string message =
                $"[BWT] Header highlight transpiler skipped for {methodName}: " +
                $"{exception.GetType().Name}: {exception.Message}. Leaving the original IL unchanged.";

            Log.WarningOnce(message, message.GetHashCode());
            return codes;
        }
    }
}
