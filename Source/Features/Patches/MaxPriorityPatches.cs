using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ModAPI.Harmony;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Shared method handles used by the max-priority patches.
    /// </summary>
    internal static class PriorityIl
    {
        internal static readonly MethodInfo GetPriority = AccessTools.Method(
            typeof(Pawn_WorkSettings),
            nameof(Pawn_WorkSettings.GetPriority),
            new[] { typeof(WorkTypeDef) });

        internal static readonly MethodInfo GetTooltipPriority = AccessTools.Method(
            typeof(MaxPriorityLogic),
            nameof(MaxPriorityLogic.GetTooltipPriority),
            new[] { typeof(Pawn_WorkSettings), typeof(WorkTypeDef) });

        internal static readonly MethodInfo GetMaxPriority = AccessTools.Method(
            typeof(MaxPriorityLogic),
            nameof(MaxPriorityLogic.GetMaxPriority));
    }

    /// <summary>
    /// Centralized rules for extended manual priorities.
    /// </summary>
    internal static class MaxPriorityLogic
    {
        /// <summary>
        /// Returns the configured upper bound for manual priorities.
        /// </summary>
        internal static int GetMaxPriority()
        {
            return PriorityAuthority.GetEffectiveMaxPriority();
        }

        /// <summary>
        /// Returns the default priority used when enabling a work type outside manual priorities.
        /// Uses vanilla's normal priority as a baseline, clamped to the configured range.
        /// </summary>
        internal static int GetDefaultEnabledPriority()
        {
            return Mathf.Clamp(3, 1, GetMaxPriority());
        }

        /// <summary>
        /// Maps extended priorities back into RimWorld's tooltip display range.
        /// </summary>
        internal static int MapPriorityToVanillaDisplay(int priority)
        {
            if (priority <= 0)
            {
                return 0;
            }

            int maxPriority = GetMaxPriority();
            if (maxPriority <= 1)
            {
                return 1;
            }

            return Mathf.Clamp((int)Math.Round(Spine.Utils.SpineUtils.Remap(priority, 1, maxPriority, 1, 4)), 1, 4);
        }

        /// <summary>
        /// Supplies the tooltip priority after remapping it into the vanilla display range.
        /// </summary>
        internal static int GetTooltipPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType)
        {
            if (workSettings == null || workType == null)
            {
                return 0;
            }

            return MapPriorityToVanillaDisplay(workSettings.GetPriority(workType));
        }

        /// <summary>
        /// Returns the color used for a manual priority value.
        /// </summary>
        internal static Color GetPriorityColor(int priority)
        {
            if (priority <= 0)
            {
                return Color.grey;
            }

            int percentage = (int)(((float)priority / GetMaxPriority()) * 100f);

            if (percentage <= BetterWorkTabMod.Settings.priorityColorPercentage_Green)
            {
                return new Color(0f, 1f, 0f);
            }

            if (percentage <= BetterWorkTabMod.Settings.priorityColorPercentage_Yellow)
            {
                return new Color(1f, 0.9f, 0.5f);
            }

            if (percentage <= BetterWorkTabMod.Settings.priorityColorPercentage_Tan)
            {
                return new Color(0.8f, 0.7f, 0.5f);
            }

            return new Color(0.74f, 0.74f, 0.74f);
        }
    }

    /// <summary>
    /// BWT-specific compatibility rules for other max-priority providers.
    /// </summary>
    internal static class PriorityTranspilerPatterns
    {
        private const string ExternalMaxPriorityPatchLogKeyPrefix = "BWT.MaxPriority.ExternalProvider.";
        private static readonly HashSet<string> LoggedExternalMaxPriorityProviders = new HashSet<string>();

        internal static bool IsExternalMaxPriorityProvider(MethodInfo method)
        {
            string declaringType = method.DeclaringType?.FullName ?? string.Empty;
            return method.Name == "GetMaximumPriority" &&
                   declaringType.IndexOf("PriorityMod", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void LogExternalMaxPriorityProviderReplacement(MethodBase original)
        {
            string methodName = original?.DeclaringType != null
                ? $"{original.DeclaringType.FullName}.{original.Name}"
                : original?.Name ?? "<unknown method>";

            string logKey = ExternalMaxPriorityPatchLogKeyPrefix + methodName;
            if (LoggedExternalMaxPriorityProviders.Add(logKey))
            {
                Log.Message($"[BWT] Another mod patched max priorities in {methodName}; Better Work Tab patched after it and now owns the final max-priority provider.");
            }
        }
    }

    /// <summary>
    /// BWT-specific max-priority edits expressed through the generic FluentTranspiler API.
    /// </summary>
    internal static class MaxPriorityFluentPatches
    {
        internal static void ReplaceTooltipPriorityLookup(FluentTranspiler transpiler)
        {
            transpiler.MatchCall(PriorityIl.GetPriority)
                      .AssertValid()
                      .ReplaceWithCall(PriorityIl.GetTooltipPriority);
        }

        internal static void ReplaceWorkBoxWrapConstants(FluentTranspiler transpiler, MethodBase original)
        {
            FluentWrapBoundsReplacementResult result = transpiler.ForCallResult(PriorityIl.GetPriority)
                                                                 .AsWrappedRange(0, 4)
                                                                 .ReplaceUpperBoundWithCallOrCompatibleProvider(
                                                                     PriorityIl.GetMaxPriority,
                                                                     PriorityTranspilerPatterns.IsExternalMaxPriorityProvider,
                                                                     "External max-priority provider replacement");

            if (result.ReplacedFallbackCall)
            {
                PriorityTranspilerPatterns.LogExternalMaxPriorityProviderReplacement(original);
            }

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"expected wrap patterns were not found ({result}, instructions={CountInstructions(transpiler)})");
            }
        }

        internal static void ReplaceSetPriorityUpperBound(FluentTranspiler transpiler, MethodBase original)
        {
            FluentReplacementResult result = transpiler.ForArgument(2)
                                                       .InRangeCheck(0, 4)
                                                       .ReplaceUpperBoundWithCallOrCompatibleProvider(
                                                           PriorityIl.GetMaxPriority,
                                                           PriorityTranspilerPatterns.IsExternalMaxPriorityProvider,
                                                           "External max-priority provider replacement");

            LogExternalProviderReplacementIfNeeded(result, original);

            if (result != FluentReplacementResult.NoMatch)
            {
                return;
            }

            throw new InvalidOperationException(
                $"expected upper-bound pattern was not found (instructions={CountInstructions(transpiler)})");
        }

        private static void LogExternalProviderReplacementIfNeeded(FluentReplacementResult result, MethodBase original)
        {
            if (result == FluentReplacementResult.FallbackCallReplaced)
            {
                PriorityTranspilerPatterns.LogExternalMaxPriorityProviderReplacement(original);
            }
        }

        private static int CountInstructions(FluentTranspiler transpiler)
        {
            int count = 0;
            foreach (CodeInstruction _ in transpiler.Instructions())
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Shared fail-safe logging for priority transpilers.
    /// </summary>
    internal static class PriorityTranspilerDiagnostics
    {
        internal static IEnumerable<CodeInstruction> ExecuteWithWarningFallback(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            string patchName,
            Action<FluentTranspiler> patch)
        {
            return FluentTranspilerExecution.ExecuteOrOriginal(
                instructions,
                original,
                null,
                patch,
                (codes, method, exception) => ReturnOriginalWithWarning(codes, method, patchName, exception));
        }

        internal static IEnumerable<CodeInstruction> ReturnOriginalWithWarning(
            List<CodeInstruction> codes,
            MethodBase original,
            string patchName,
            string reason)
        {
            string methodName = original?.DeclaringType != null
                ? $"{original.DeclaringType.FullName}.{original.Name}"
                : original?.Name ?? "<unknown method>";

            string message =
                $"[BWT] Max-priority transpiler '{patchName}' skipped for {methodName}: {reason}. " +
                "Leaving the original IL unchanged to preserve compatibility.";

            Log.WarningOnce(message, message.GetHashCode());
            return codes;
        }

        internal static IEnumerable<CodeInstruction> ReturnOriginalWithWarning(
            List<CodeInstruction> codes,
            MethodBase original,
            string patchName,
            Exception exception)
        {
            return ReturnOriginalWithWarning(
                codes,
                original,
                patchName,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.ColorOfPriority))]
    internal static class Patch_WidgetsWork_ColorOfPriority
    {
        /// <summary>
        /// Extends RimWorld's priority color calculation beyond the vanilla 1..4 range.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(ref Color __result, int prio)
        {
            __result = MaxPriorityLogic.GetPriorityColor(prio);
        }
    }

    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker))]
    internal static class Patch_WidgetsWork_TipForPawnWorker
    {
        /// <summary>
        /// Redirects the tooltip priority lookup so tooltips keep using RimWorld's translated
        /// 1..4 labels even when the stored priority exceeds 4.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return PriorityTranspilerDiagnostics.ExecuteWithWarningFallback(
                instructions,
                original,
                nameof(Patch_WidgetsWork_TipForPawnWorker),
                MaxPriorityFluentPatches.ReplaceTooltipPriorityLookup);
        }
    }

    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
    internal static class Patch_WidgetsWork_DrawWorkBoxFor
    {
        /// <summary>
        /// Replaces the two wraparound constants used by the work-cell click handlers.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return PriorityTranspilerDiagnostics.ExecuteWithWarningFallback(
                instructions,
                original,
                nameof(Patch_WidgetsWork_DrawWorkBoxFor),
                transpiler => MaxPriorityFluentPatches.ReplaceWorkBoxWrapConstants(transpiler, original));
        }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    internal static class Patch_Pawn_WorkSettings_SetPriority
    {
        /// <summary>
        /// Replaces RimWorld's validation ceiling so values above 4 remain valid.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return PriorityTranspilerDiagnostics.ExecuteWithWarningFallback(
                instructions,
                original,
                nameof(Patch_Pawn_WorkSettings_SetPriority),
                transpiler => MaxPriorityFluentPatches.ReplaceSetPriorityUpperBound(transpiler, original));
        }
    }
}
