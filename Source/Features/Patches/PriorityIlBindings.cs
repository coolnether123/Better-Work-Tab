using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
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
}
