using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
            typeof(WorkPrioritySystem),
            nameof(WorkPrioritySystem.GetTooltipPriority),
            new[] { typeof(Pawn_WorkSettings), typeof(WorkTypeDef) });

        internal static readonly MethodInfo GetMaxPriority = AccessTools.Method(
            typeof(WorkPrioritySystem),
            nameof(WorkPrioritySystem.GetMaxPriority));

        internal static readonly MethodInfo GetDefaultEnabledPriority = AccessTools.Method(
            typeof(WorkPrioritySystem),
            nameof(WorkPrioritySystem.GetDefaultEnabledPriority));

        internal static readonly MethodInfo SetPriority = AccessTools.Method(
            typeof(Pawn_WorkSettings),
            nameof(Pawn_WorkSettings.SetPriority),
            new[] { typeof(WorkTypeDef), typeof(int) });
    }

    /// <summary>
    /// Locates the specific IL instructions that enforce RimWorld's vanilla max priority.
    /// </summary>
    internal static class PriorityTranspilerPatterns
    {
        /// <summary>
        /// Finds the constant used by RimWorld's decrement wraparound path.
        /// The matched sequence is:
        /// GetPriority, subtract 1, store to a local, test that local against 0, and if it is negative load 4.
        /// The returned index is the final 4 load, which is the instruction replaced with GetMaxPriority().
        /// </summary>
        internal static int FindPriorityWrapUnderflowIndex(List<CodeInstruction> codes, int startIndex)
        {
            int index = FindPattern(
                codes,
                startIndex,
                (list, i) => i + 7 < list.Count &&
                             list[i].Calls(PriorityIl.GetPriority) &&
                             list[i + 1].LoadsConstant(1) &&
                             list[i + 2].opcode == OpCodes.Sub &&
                             IsStoreLocal(list[i + 3]) &&
                             IsLoadLocal(list[i + 4]) &&
                             list[i + 5].LoadsConstant(0) &&
                             IsBranch(list[i + 6], OpCodes.Bge, OpCodes.Bge_S) &&
                             IsMaxPriorityCeilingInstruction(list[i + 7]),
                i => i + 7);

            if (index >= 0)
            {
                return index;
            }

            return FindPattern(
                codes,
                startIndex,
                (list, i) => i >= 3 &&
                             i + 1 < list.Count &&
                             IsLoadLocal(list[i - 3]) &&
                             list[i - 2].LoadsConstant(0) &&
                             IsBranch(list[i - 1], OpCodes.Bge, OpCodes.Bge_S) &&
                             IsMaxPriorityCeilingInstruction(list[i]) &&
                             IsStoreLocal(list[i + 1]),
                i => i);
        }

        /// <summary>
        /// Finds the constant used by RimWorld's increment wraparound path.
        /// The matched sequence is:
        /// GetPriority, add 1, store to a local, compare that local against 4, and if it exceeds the limit load 0.
        /// The returned index is the comparison's 4 load, which is the instruction replaced with GetMaxPriority().
        /// </summary>
        internal static int FindPriorityWrapOverflowIndex(List<CodeInstruction> codes, int startIndex)
        {
            int index = FindPattern(
                codes,
                startIndex,
                (list, i) => i + 7 < list.Count &&
                             list[i].Calls(PriorityIl.GetPriority) &&
                             list[i + 1].LoadsConstant(1) &&
                             list[i + 2].opcode == OpCodes.Add &&
                             IsStoreLocal(list[i + 3]) &&
                             IsLoadLocal(list[i + 4]) &&
                             IsMaxPriorityCeilingInstruction(list[i + 5]) &&
                             IsBranch(list[i + 6], OpCodes.Ble, OpCodes.Ble_S) &&
                             list[i + 7].LoadsConstant(0),
                i => i + 5);

            if (index >= 0)
            {
                return index;
            }

            return FindPattern(
                codes,
                startIndex,
                (list, i) => i >= 1 &&
                             i + 1 < list.Count &&
                             IsLoadLocal(list[i - 1]) &&
                             IsMaxPriorityCeilingInstruction(list[i]) &&
                             IsBranch(list[i + 1], OpCodes.Ble, OpCodes.Ble_S),
                i => i);
        }

        /// <summary>
        /// Finds the upper-bound check inside Pawn_WorkSettings.SetPriority.
        /// </summary>
        internal static int FindSetPriorityUpperBoundIndex(List<CodeInstruction> codes)
        {
            return FindPattern(
                codes,
                0,
                (list, i) => i + 5 < list.Count &&
                             IsLoadArgument(list[i], 2) &&
                             list[i + 1].LoadsConstant(0) &&
                             IsBranch(list[i + 2], OpCodes.Blt, OpCodes.Blt_S) &&
                             IsLoadArgument(list[i + 3], 2) &&
                             IsMaxPriorityCeilingInstruction(list[i + 4]) &&
                             IsBranch(list[i + 5], OpCodes.Ble, OpCodes.Ble_S),
                i => i + 4);
        }

        /// <summary>
        /// Replaces a vanilla constant load with a call that returns the configured max priority.
        /// </summary>
        internal static void ReplaceWithMaxPriorityCall(List<CodeInstruction> codes, int index)
        {
            CodeInstruction source = codes[index];
            var replacement = new CodeInstruction(OpCodes.Call, PriorityIl.GetMaxPriority);
            replacement.labels.AddRange(source.labels);
            replacement.blocks.AddRange(source.blocks);
            codes[index] = replacement;
        }

        internal static void ReplaceWorkBoxDefaultEnabledPriority(List<CodeInstruction> codes)
        {
            for (int i = 1; i < codes.Count; i++)
            {
                if (!Calls(codes[i], PriorityIl.SetPriority) ||
                    !codes[i - 1].LoadsConstant(PriorityConstants.VanillaDefaultEnabled))
                {
                    continue;
                }

                CodeInstruction source = codes[i - 1];
                var replacement = new CodeInstruction(OpCodes.Call, PriorityIl.GetDefaultEnabledPriority);
                replacement.labels.AddRange(source.labels);
                replacement.blocks.AddRange(source.blocks);
                codes[i - 1] = replacement;
                return;
            }
        }

        internal static void WarnPatternMiss(MethodBase original, string patternDescription)
        {
            BetterWorkTabMod.DebugLog("Skipping max-priority patch for " +
                $"{original?.DeclaringType?.Name}.{original?.Name}: unable to locate {patternDescription}. " +
                "The Work tab will keep vanilla priority wrap behavior for that method.",
                DebugFeature.General);
        }

        private static int FindPattern(List<CodeInstruction> codes, int startIndex, Func<List<CodeInstruction>, int, bool> predicate, Func<int, int> resultSelector)
        {
            for (int i = Math.Max(0, startIndex); i < codes.Count; i++)
            {
                if (predicate(codes, i))
                {
                    return resultSelector(i);
                }
            }

            return -1;
        }

        private static bool IsBranch(CodeInstruction instruction, OpCode longForm, OpCode shortForm)
        {
            return instruction.opcode == longForm || instruction.opcode == shortForm;
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            return instruction != null &&
                   method != null &&
                   (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                   (instruction.operand as MethodInfo) == method;
        }

        private static bool IsMaxPriorityCeilingInstruction(CodeInstruction instruction)
        {
            return instruction != null &&
                   (instruction.LoadsConstant(PriorityConstants.VanillaMax) ||
                    CallsCompatibleExternalMaxPriorityProvider(instruction));
        }

        private static bool CallsCompatibleExternalMaxPriorityProvider(CodeInstruction instruction)
        {
            var method = instruction?.operand as MethodInfo;
            if (method == null ||
                (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt))
            {
                return false;
            }

            string declaringType = method.DeclaringType?.FullName;
            return string.Equals(declaringType, "PriorityMod.Tools.PatchHook", StringComparison.Ordinal) &&
                   (string.Equals(method.Name, "GetMaximumPriority", StringComparison.Ordinal) ||
                    string.Equals(method.Name, "GetMaxPriority", StringComparison.Ordinal));
        }

        private static bool IsLoadLocal(CodeInstruction instruction)
        {
            return instruction.opcode.Name.StartsWith("ldloc", StringComparison.Ordinal);
        }

        private static bool IsStoreLocal(CodeInstruction instruction)
        {
            return instruction.opcode.Name.StartsWith("stloc", StringComparison.Ordinal);
        }

        private static bool IsLoadArgument(CodeInstruction instruction, int argumentIndex)
        {
            return argumentIndex switch
            {
                0 => instruction.opcode == OpCodes.Ldarg_0,
                1 => instruction.opcode == OpCodes.Ldarg_1,
                2 => instruction.opcode == OpCodes.Ldarg_2 ||
                     (instruction.opcode == OpCodes.Ldarg_S &&
                      ((instruction.operand is byte byteIndex && byteIndex == 2) ||
                       (instruction.operand is ushort shortIndex && shortIndex == 2))),
                3 => instruction.opcode == OpCodes.Ldarg_3,
                _ => false
            };
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
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return;
            }

            __result = WorkPrioritySystem.GetPriorityColor(prio);
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
            return FluentTranspiler.Execute(instructions, original, null, t =>
            {
                t.MatchCall(PriorityIl.GetPriority)
                 .AssertValid()
                 .ReplaceWithCall(typeof(WorkPrioritySystem), nameof(WorkPrioritySystem.GetTooltipPriority), new[] { typeof(Pawn_WorkSettings), typeof(WorkTypeDef) });
            });
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
            var codes = new List<CodeInstruction>(instructions);
            int leftWrapIndex = PriorityTranspilerPatterns.FindPriorityWrapUnderflowIndex(codes, 0);
            int rightWrapIndex = PriorityTranspilerPatterns.FindPriorityWrapOverflowIndex(codes, leftWrapIndex + 1);

            if (leftWrapIndex < 0 || rightWrapIndex < 0)
            {
                PriorityTranspilerPatterns.WarnPatternMiss(original, "work-box priority wrap checks");
                return codes;
            }

            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, leftWrapIndex);
            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, rightWrapIndex);
            PriorityTranspilerPatterns.ReplaceWorkBoxDefaultEnabledPriority(codes);
            return codes;
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.HeaderClicked))]
    internal static class Patch_PawnColumnWorker_WorkPriority_HeaderClicked
    {
        /// <summary>
        /// Replaces the two wraparound constants used by the header bulk-edit controls.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = new List<CodeInstruction>(instructions);
            int leftWrapIndex = PriorityTranspilerPatterns.FindPriorityWrapUnderflowIndex(codes, 0);
            int rightWrapIndex = PriorityTranspilerPatterns.FindPriorityWrapOverflowIndex(codes, leftWrapIndex + 1);

            if (leftWrapIndex < 0 || rightWrapIndex < 0)
            {
                PriorityTranspilerPatterns.WarnPatternMiss(original, "header priority wrap checks");
                return codes;
            }

            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, leftWrapIndex);
            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, rightWrapIndex);
            return codes;
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
            var codes = new List<CodeInstruction>(instructions);
            int upperBoundIndex = PriorityTranspilerPatterns.FindSetPriorityUpperBoundIndex(codes);

            if (upperBoundIndex < 0)
            {
                PriorityTranspilerPatterns.WarnPatternMiss(original, "SetPriority upper-bound check");
                return codes;
            }

            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, upperBoundIndex);
            return codes;
        }
    }
}
