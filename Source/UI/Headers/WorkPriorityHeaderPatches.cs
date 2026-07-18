using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI.Headers.Vanilla;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Spine.Profiling;

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
        /// <remarks>
        /// This is a rendering question, not a priority-data question. When Fluffy Work Tab owns the
        /// Work tab it swaps in its own <c>WorkTab.MainTabWindow_WorkTab</c>, which does not derive
        /// from <see cref="MainTabWindow_Work"/>, so the window scan below already excludes it.
        /// Gating this on priority authority would silently disable Better Work Tab's headers
        /// whenever Fluffy happened to be backing the numbers.
        /// </remarks>
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
            if (SpineTiming.Enabled)
            {
                return SpineTiming.Time("Harmony.WorkPriority.DoHeader.Prefix", () => PrefixProfiled(__instance, rect, table));
            }

            return PrefixProfiled(__instance, rect, table);
        }

        private static bool PrefixProfiled(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
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

                // Fluffy work-giver columns derive from PawnColumnWorker_WorkPriority but carry their
                // parent's workType. Drawing a Better Work Tab header here would label every sub-work
                // column with the work type. Let Fluffy's own worker and transpiler render them.
                if (FluffyWorkTabGateway.IsFluffyWorkGiverColumn(__instance.def) &&
                    !FluffyWorkTabGateway.IsHostedFluffyWorkGiverColumn(__instance.def))
                {
                    return true;
                }

                if (SubWorkDrilldownState.IsBlankWorkColumn(__instance.def))
                {
                    return false;
                }

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

            // Fluffy sizes its own work-giver column headers.
            if (FluffyWorkTabGateway.IsFluffyWorkGiverColumn(__instance?.def) &&
                !FluffyWorkTabGateway.IsHostedFluffyWorkGiverColumn(__instance?.def))
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
    /// Transpiler: Injects a condition before Widgets.DrawHighlightIfMouseover to skip 
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
    ///   do_highlight:
    ///     Widgets.DrawHighlightIfMouseover(rect);
    ///   skip_highlight:
    /// </summary>
#if !v1_0
    [HarmonyPatch(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader))]
    public static class Patch_PawnColumnWorker_DoHeader_DisableHighlight
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var drawHighlightMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlightIfMouseover));
            var workPriorityWorkerType = typeof(PawnColumnWorker_WorkPriority);

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(drawHighlightMethod))
                {
                    var labelDoHighlight = il.DefineLabel();
                    var labelSkipHighlight = il.DefineLabel();

                    if (i + 1 < codes.Count)
                        codes[i + 1].labels.Add(labelSkipHighlight);

                    int insertIndex = i;
                    if (i > 0 && (codes[i - 1].opcode == OpCodes.Ldarg_1 || codes[i - 1].opcode == OpCodes.Ldloc_0))
                    {
                        insertIndex = i - 1;
                    }

                    var newCodes = new List<CodeInstruction>();

                    // if (Settings == null) goto do_highlight;
                    newCodes.Add(new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(BetterWorkTabMod), nameof(BetterWorkTabMod.Settings))));
                    newCodes.Add(new CodeInstruction(OpCodes.Brfalse, labelDoHighlight));

                    // if (!Settings.enableAngledHeaders) goto do_highlight;
                    newCodes.Add(new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(BetterWorkTabMod), nameof(BetterWorkTabMod.Settings))));
                    newCodes.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(BetterWorkTabSettings), nameof(BetterWorkTabSettings.enableAngledHeaders))));
                    newCodes.Add(new CodeInstruction(OpCodes.Brfalse, labelDoHighlight));

                    // if (!PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab()) goto do_highlight;
                    newCodes.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PawnColumnWorker_WorkPriority_DoHeader_Patch), nameof(PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab))));
                    newCodes.Add(new CodeInstruction(OpCodes.Brfalse, labelDoHighlight));

                    // if (this is PawnColumnWorker_WorkPriority) goto skip_highlight;
                    newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    newCodes.Add(new CodeInstruction(OpCodes.Isinst, workPriorityWorkerType));
                    newCodes.Add(new CodeInstruction(OpCodes.Brtrue, labelSkipHighlight));

                    codes[insertIndex].labels.Add(labelDoHighlight);
                    codes.InsertRange(insertIndex, newCodes);

                    break;
                }
            }
            return codes;
        }
    }
#endif
}
