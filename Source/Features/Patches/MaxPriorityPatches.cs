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
            return Math.Max(1, BetterWorkTabMod.Settings.maxPriorityInt);
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

            if (percentage < BetterWorkTabMod.Settings.priorityColorPercentage_Green)
            {
                return new Color(0f, 1f, 0f);
            }

            if (percentage < BetterWorkTabMod.Settings.priorityColorPercentage_Yellow)
            {
                return new Color(1f, 0.9f, 0.5f);
            }

            if (percentage < BetterWorkTabMod.Settings.priorityColorPercentage_Tan)
            {
                return new Color(0.8f, 0.7f, 0.5f);
            }

            return new Color(0.74f, 0.74f, 0.74f);
        }
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
            return FindPattern(
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
        }

        /// <summary>
        /// Finds the constant used by RimWorld's increment wraparound path.
        /// The matched sequence is:
        /// GetPriority, add 1, store to a local, compare that local against 4, and if it exceeds the limit load 0.
        /// The returned index is the comparison's 4 load, which is the instruction replaced with GetMaxPriority().
        /// </summary>
        internal static int FindPriorityWrapOverflowIndex(List<CodeInstruction> codes, int startIndex)
        {
            return FindPattern(
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
            return FluentTranspiler.Execute(instructions, original, null, t =>
            {
                t.MatchCall(PriorityIl.GetPriority)
                 .AssertValid()
                 .ReplaceWithCall(typeof(MaxPriorityLogic), nameof(MaxPriorityLogic.GetTooltipPriority), new[] { typeof(Pawn_WorkSettings), typeof(WorkTypeDef) });
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
                throw new InvalidOperationException($"Unable to locate work-box priority wrap checks in {original?.DeclaringType?.Name}.{original?.Name}.");
            }

            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, leftWrapIndex);
            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, rightWrapIndex);
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
                throw new InvalidOperationException($"Unable to locate header priority wrap checks in {original?.DeclaringType?.Name}.{original?.Name}.");
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
                throw new InvalidOperationException($"Unable to locate the SetPriority upper-bound check in {original?.DeclaringType?.Name}.{original?.Name}.");
            }

            PriorityTranspilerPatterns.ReplaceWithMaxPriorityCall(codes, upperBoundIndex);
            return codes;
        }
    }
}
