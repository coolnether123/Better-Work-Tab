using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using HarmonyLib;
using RimWorld;

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
        private static readonly MethodBase[] ReplacedVanillaHooks =
        {
            AccessTools.Method(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell)),
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor)),
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxBackground)),
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.ColorOfPriority))
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
                if (!knownFluffyAdapterPatch && patchAssembly != BetterWorkTabAssembly)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
