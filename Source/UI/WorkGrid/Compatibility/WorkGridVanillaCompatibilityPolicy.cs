using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
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
        private static readonly MethodBase[] FactionViewportScrollHooks =
        {
            AccessTools.Method(typeof(PawnColumnWorker_Icon), nameof(PawnColumnWorker_Icon.DoCell)),
            AccessTools.Method(typeof(PawnColumnWorker_Faction), "GetIconFor"),
            AccessTools.Method(typeof(PawnColumnWorker_Faction), "GetIconColor"),
            AccessTools.Method(typeof(PawnColumnWorker_Faction), "GetIconTip"),
            AccessTools.Method(typeof(PawnColumnWorker_Faction), "ClickedIcon"),
            AccessTools.Method(typeof(PawnColumnWorker_Icon), "PaintedIcon"),
            AccessTools.Method(typeof(PawnColumnWorker_Icon), "GetIconSize"),
            AccessTools.PropertyGetter(typeof(PawnColumnWorker_Icon), "Width"),
            AccessTools.PropertyGetter(typeof(PawnColumnWorker_Icon), "Padding")
        };
        private static readonly MethodBase[] CopyPasteViewportScrollHooks =
        {
            AccessTools.Method(
                typeof(PawnColumnWorker_CopyPasteWorkPriorities),
                nameof(PawnColumnWorker_CopyPasteWorkPriorities.DoCell)),
            AccessTools.Method(typeof(PawnColumnWorker_CopyPaste), nameof(PawnColumnWorker_CopyPaste.DoCell)),
            AccessTools.PropertyGetter(typeof(PawnColumnWorker_CopyPasteWorkPriorities), "AnythingInClipboard"),
            AccessTools.Method(typeof(CopyPasteUI), nameof(CopyPasteUI.DoCopyPasteButtons))
        };
        private static readonly MethodInfo CopyPasteAnythingInClipboardGetter =
            AccessTools.PropertyGetter(
                typeof(PawnColumnWorker_CopyPasteWorkPriorities),
                "AnythingInClipboard");
        private static readonly CopyPasteClipboardGetter CachedCopyPasteClipboardGetter =
            CreateCopyPasteClipboardGetter();
        private static readonly MethodBase[] RemainingSpaceViewportScrollHooks =
        {
            AccessTools.Method(
                typeof(PawnColumnWorker_RemainingSpace),
                nameof(PawnColumnWorker_RemainingSpace.DoCell))
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

        internal static bool CanSkipViewportScrollRowTraversal(
            IReadOnlyList<WorkTabLayoutColumn> columns,
            WorkGridSnapshot snapshot)
        {
            if (columns.Count != snapshot.Columns.Count ||
                !CanSnapshotVanillaPriorityCells())
            {
                return false;
            }

            for (int index = 0; index < columns.Count; index++)
            {
                PawnColumnDef column = columns[index].Column;
                WorkGridColumnEntry prepared = snapshot.Columns[index];
                switch (prepared.WorkerKind)
                {
                    case WorkGridColumnWorkerKind.WorkPriority:
                        // Snapshot construction already proved the vanilla priority
                        // hooks are unextended and that BWT owns this exact column.
                        continue;
                    case WorkGridColumnWorkerKind.SubWorkPriority:
                        if (!FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column))
                        {
                            continue;
                        }
                        return false;
                    case WorkGridColumnWorkerKind.PawnLabel:
                        if (CanPrepareVanillaLabelCells(column))
                        {
                            continue;
                        }
                        return false;
                    default:
                        if (IsWheelPassiveVanillaColumn(column))
                        {
                            continue;
                        }
                        return false;
                }
            }

            return true;
        }

        private static bool IsWheelPassiveVanillaColumn(PawnColumnDef column)
        {
            object worker = column?.Worker;
            switch (column?.defName)
            {
                case "Faction":
                    return worker?.GetType() == typeof(PawnColumnWorker_Faction) &&
                        HooksAreUnextended(FactionViewportScrollHooks);
                case "CopyPasteWorkPriorities":
                    return worker?.GetType() == typeof(PawnColumnWorker_CopyPasteWorkPriorities) &&
                        HooksAreUnextended(CopyPasteViewportScrollHooks);
                case "RemainingSpace":
                    return worker?.GetType() == typeof(PawnColumnWorker_RemainingSpace) &&
                        HooksAreUnextended(RemainingSpaceViewportScrollHooks);
                default:
                    return false;
            }
        }

        internal static bool CanPrepareCopyPasteWorkPriorities(PawnColumnDef column)
        {
            return column?.Worker?.GetType() == typeof(PawnColumnWorker_CopyPasteWorkPriorities) &&
                   CachedCopyPasteClipboardGetter != null &&
                   HooksAreUnextended(CopyPasteViewportScrollHooks);
        }

        internal static bool ReadCopyPasteClipboard(
            PawnColumnWorker_CopyPasteWorkPriorities worker)
        {
            return CachedCopyPasteClipboardGetter(worker);
        }

        private delegate bool CopyPasteClipboardGetter(
            PawnColumnWorker_CopyPasteWorkPriorities worker);

        private static CopyPasteClipboardGetter CreateCopyPasteClipboardGetter()
        {
            if (CopyPasteAnythingInClipboardGetter == null)
            {
                return null;
            }

            try
            {
                return (CopyPasteClipboardGetter)Delegate.CreateDelegate(
                    typeof(CopyPasteClipboardGetter),
                    CopyPasteAnythingInClipboardGetter);
            }
            catch (Exception)
            {
                // This protected vanilla getter is an optional compatibility seam.
                // An unfamiliar runtime keeps the native worker path instead.
                return null;
            }
        }

        private static bool HooksAreUnextended(IReadOnlyList<MethodBase> hooks)
        {
            for (int index = 0; index < hooks.Count; index++)
            {
                MethodBase hook = hooks[index];
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
