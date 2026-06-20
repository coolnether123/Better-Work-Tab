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
                             list[i + 7].LoadsConstant(4),
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
                             list[i].LoadsConstant(4) &&
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
                             list[i + 5].LoadsConstant(4) &&
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
                             list[i].LoadsConstant(4) &&
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
                             list[i + 4].LoadsConstant(4) &&
                             IsBranch(list[i + 5], OpCodes.Ble, OpCodes.Ble_S),
                i => i + 4);
        }

        /// <summary>
        /// Replaces a vanilla constant load with a call that returns the configured max priority.
        /// </summary>
        internal static void ReplaceWithMaxPriorityCall(List<CodeInstruction> codes, int index)
        {
            codes[index] = new CodeInstruction(OpCodes.Call, PriorityIl.GetMaxPriority);
        }

        internal static bool TryReplacePriorityWrapChecks(List<CodeInstruction> codes)
        {
            int leftWrapIndex = FindPriorityWrapUnderflowIndex(codes, 0);
            int rightWrapIndex = FindPriorityWrapOverflowIndex(codes, leftWrapIndex + 1);

            if (leftWrapIndex < 0 || rightWrapIndex < 0)
            {
                return false;
            }

            ReplaceWithMaxPriorityCall(codes, leftWrapIndex);
            ReplaceWithMaxPriorityCall(codes, rightWrapIndex);
            return true;
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

    [HarmonyPatch(typeof(WidgetsWork), "ColorOfPriority")]
    internal static class Patch_WidgetsWork_ColorOfPriority
    {
        /// <summary>
        /// Extends RimWorld's priority color calculation beyond the vanilla 1..4 range.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(ref Color __result, int prio)
        {
            __result = WorkPrioritySystem.GetPriorityColor(prio);
        }
    }

#if !(v1_0 || v0_19)
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
#endif

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

            if (!PriorityTranspilerPatterns.TryReplacePriorityWrapChecks(codes))
            {
                throw new InvalidOperationException($"Unable to locate work-box priority wrap checks in {original?.DeclaringType?.Name}.{original?.Name}.");
            }

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

            if (!PriorityTranspilerPatterns.TryReplacePriorityWrapChecks(codes))
            {
                BetterWorkTabMod.DebugLog(
                    $"Skipped optional header priority wrap patch for {original?.DeclaringType?.Name}.{original?.Name}; RimWorld version does not match the expected vanilla header-click IL.",
                    DebugFeature.General);
                return codes;
            }

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
                throw new InvalidOperationException($"Unable to locate the SetPriority upper-bound check in {original?.DeclaringType?.Name}.{original?.Name}.");
            }

            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, upperBoundIndex);
            return codes;
        }
    }
}
