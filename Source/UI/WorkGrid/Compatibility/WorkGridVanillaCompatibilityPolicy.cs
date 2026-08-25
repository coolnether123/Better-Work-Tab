using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Compatibility
{
    /// <summary>
    /// Limits snapshot rendering to vanilla cells whose normal render hooks have not been
    /// extended by another assembly. Everything else continues through the worker itself.
    /// </summary>
    internal static class WorkGridVanillaCompatibilityPolicy
    {
        private static readonly Assembly BetterWorkTabAssembly = typeof(BetterWorkTabMod).Assembly;
        private const string FluffyHarmonyOwner = "fluffy.worktab";
        private const string DubsProfilerHarmonyOwner = "Dubwise.DubsProfiler";
        private static readonly MethodBase[] ReplacedVanillaHooks =
        {
            AccessTools.Method(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell)),
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor)),
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxBackground)),
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.ColorOfPriority))
        };
        private static readonly MethodBase[] ReplacedVanillaLabelHooks =
        {
            AccessTools.Method(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell)),
            AccessTools.Method(typeof(PawnColumnWorker_Label), "GetLabel", new[] { typeof(Pawn) })
        };

        internal static bool CanSnapshotVanillaPriorityCells()
        {
            for (int index = 0; index < ReplacedVanillaHooks.Length; index++)
            {
                MethodBase hook = ReplacedVanillaHooks[index];
                if (hook == null || HasExternalPatch(Harmony.GetPatchInfo(hook)))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool CanSnapshotPriorityColumn(PawnColumnDef column)
        {
            object worker = column?.Worker;
            if (worker != null && worker.GetType() == typeof(PawnColumnWorker_WorkPriority))
            {
                return true;
            }

            return FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab &&
                   FluffyWorkTabGateway.IsFluffyColumn(column) &&
                   !FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column);
        }

        internal static bool CanPrepareVanillaLabelCells(PawnColumnDef column)
        {
            if (column?.Worker?.GetType() != typeof(PawnColumnWorker_Label))
            {
                return false;
            }

            for (int index = 0; index < ReplacedVanillaLabelHooks.Length; index++)
            {
                MethodBase hook = ReplacedVanillaLabelHooks[index];
                if (hook == null || HasExternalPatch(Harmony.GetPatchInfo(hook)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasExternalPatch(HarmonyLib.Patches patches)
        {
            return patches != null &&
                   (ContainsExternalPatch(patches.Prefixes) ||
                    ContainsExternalPatch(patches.Postfixes) ||
                    ContainsExternalPatch(patches.Transpilers) ||
                    ContainsExternalPatch(patches.Finalizers));
        }

        private static bool ContainsExternalPatch(IReadOnlyCollection<HarmonyLib.Patch> patches)
        {
            if (patches == null)
            {
                return false;
            }

            foreach (HarmonyLib.Patch patch in patches)
            {
                MethodInfo patchMethod = patch?.PatchMethod;
                Assembly patchAssembly = patchMethod?.DeclaringType?.Assembly;
                bool knownFluffyAdapterPatch =
                    string.Equals(patch?.owner, FluffyHarmonyOwner, System.StringComparison.OrdinalIgnoreCase) &&
                    FluffyWorkTabGateway.IsPresent &&
                    FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab;
                bool observationalProfilerPatch = string.Equals(
                    patch?.owner,
                    DubsProfilerHarmonyOwner,
                    System.StringComparison.OrdinalIgnoreCase);
                if (!knownFluffyAdapterPatch &&
                    !observationalProfilerPatch &&
                    patchAssembly != BetterWorkTabAssembly)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
