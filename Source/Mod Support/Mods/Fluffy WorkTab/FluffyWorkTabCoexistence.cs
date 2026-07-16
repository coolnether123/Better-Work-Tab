using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.UI;
using HarmonyLib;
using RimWorld;
using Verse;
using BwtMainButtonDefOf = Better_Work_Tab.Patches.MainButtonDefOf;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    public enum WorkTabOwnerPreference
    {
        BetterWorkTab,
        FluffyWorkTab
    }

    internal static class FluffyWorkTabCoexistence
    {
        internal const string SimulateFluffyFlag = "bwt-simulate-fluffy-worktab";

        private static readonly HashSet<string> KnownPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "fluffy.worktab",
                "fluffy.worktab.continued",
                "arof.fluffy.worktab.continued"
            };

        private const string FluffyMainTabWindowTypeName = "WorkTab.MainTabWindow_WorkTab";
        private const string FluffyControllerTypeName = "WorkTab.Controller";
        private static Type _fluffyWorkTabWindowType;
        private static bool? _detected;
        private static string _detectedPackageId;

        internal static bool IsSimulated => GenCommandLine.CommandLineArgPassed(SimulateFluffyFlag);

        internal static bool IsFluffyWorkTabPresent
        {
            get
            {
                EnsureDetected();
                return _detected == true;
            }
        }

        internal static string DetectedPackageId
        {
            get
            {
                EnsureDetected();
                return _detectedPackageId;
            }
        }

        internal static bool BetterWorkTabOwnsWorkTab => !IsFluffyWorkTabPresent ||
            BetterWorkTabMod.Settings?.preferredWorkTabOwner != WorkTabOwnerPreference.FluffyWorkTab ||
            !TryGetFluffyWorkTabWindowType(out _);

        internal static bool FluffyOwnsWorkTab => IsFluffyWorkTabPresent && !BetterWorkTabOwnsWorkTab;

        internal static bool ShouldRunBetterWorkTabFeatures => BetterWorkTabOwnsWorkTab;

        internal static bool IsKnownFluffyPackageId(string packageId)
        {
            return !packageId.NullOrEmpty() && KnownPackageIds.Contains(packageId);
        }

        internal static void ApplyColumnVisibility()
        {
            FluffyWorkTabColumnVisibility.Apply();
        }

        internal static void ApplyDesiredOwner(bool reopenIfOpen = false)
        {
            MainButtonDef work = BwtMainButtonDefOf.Work;
            if (work == null)
            {
                return;
            }

            Type desired = typeof(UI.MainTabWindow_BetterWork);
            if (IsFluffyWorkTabPresent &&
                BetterWorkTabMod.Settings?.preferredWorkTabOwner == WorkTabOwnerPreference.FluffyWorkTab &&
                TryGetFluffyWorkTabWindowType(out Type fluffyType))
            {
                desired = fluffyType;
            }

            MainTabsRoot mainTabsRoot = null;
            bool workTabOpen = false;
            MainTabWindow openWorkTabWindow = null;
            if (reopenIfOpen)
            {
                mainTabsRoot = TryGetMainTabsRoot();
                var windowStack = Find.WindowStack;
                var windows = windowStack?.Windows;
                for (int i = 0; windows != null && i < windows.Count; i++)
                {
                    if (windows[i] is MainTabWindow mainTabWindow && mainTabWindow.def == work)
                    {
                        openWorkTabWindow = mainTabWindow;
                        workTabOpen = true;
                        break;
                    }
                }
            }

            bool classChanged = work.tabWindowClass != desired;
            bool openWindowMismatch = workTabOpen &&
                openWorkTabWindow != null &&
                openWorkTabWindow.GetType() != desired;

            if (classChanged || openWindowMismatch)
            {
                if (workTabOpen && openWorkTabWindow != null)
                {
                    Find.WindowStack.TryRemove(openWorkTabWindow, doCloseSound: false);
                }

                work.tabWindowClass = desired;
                work.Notify_ClearingAllMapsMemory();

                if (workTabOpen && mainTabsRoot != null)
                {
                    Find.WindowStack.Add(work.TabWindow);
                }
            }

            ApplyColumnVisibility();
        }

        internal static void SwitchToBetterWorkTab()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.preferredWorkTabOwner = WorkTabOwnerPreference.BetterWorkTab;
            settings.Write();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            ApplyDesiredOwner(reopenIfOpen: true);
        }

        internal static void SwitchToFluffyWorkTab()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !IsFluffyWorkTabPresent)
            {
                return;
            }

            settings.preferredWorkTabOwner = WorkTabOwnerPreference.FluffyWorkTab;
            settings.Write();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            ApplyDesiredOwner(reopenIfOpen: true);
        }

        internal static bool TryGetFluffyWorkTabWindowType(out Type type)
        {
            EnsureDetected();
            type = _fluffyWorkTabWindowType;
            return type != null;
        }

        private static MainTabsRoot TryGetMainTabsRoot()
        {
            try
            {
                return Find.MainTabsRoot;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        private static void EnsureDetected()
        {
            if (_detected.HasValue)
            {
                return;
            }

            _detected = false;
            _detectedPackageId = null;
            _fluffyWorkTabWindowType = AccessTools.TypeByName(FluffyMainTabWindowTypeName);

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; mods != null && i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (mod != null && IsKnownFluffyPackageId(mod.PackageId))
                {
                    _detected = true;
                    _detectedPackageId = mod.PackageId;
                    break;
                }
            }

            if (_fluffyWorkTabWindowType != null || AccessTools.TypeByName(FluffyControllerTypeName) != null)
            {
                _detected = true;
                if (_detectedPackageId.NullOrEmpty())
                {
                    _detectedPackageId = "type probe";
                }
            }

            if (IsSimulated)
            {
                _detected = true;
                _detectedPackageId = "simulated";
                if (_fluffyWorkTabWindowType == null)
                {
                    _fluffyWorkTabWindowType = typeof(MainTabWindow_Work);
                }
            }
        }

        [HarmonyPatch(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PreResolve))]
        private static class Patch_DefGenerator_GenerateImpliedDefs_PreResolve_WorkTabOwner
        {
            [HarmonyAfter(new[] { "fluffy.worktab" })]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                ApplyDesiredOwner();
            }
        }
    }

    internal static class FluffyWorkTabColumnVisibility
    {
        private static readonly HashSet<string> FluffyWidgetColumnDefNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Mood",
                "Job",
                "CopyPasteDetailedWorkPriorities",
                "Favourite"
            };

        private static readonly List<ColumnSnapshot> CapturedColumns = new List<ColumnSnapshot>();

        internal static void Apply()
        {
            PawnTableDef workTable = PawnTableDefOf.Work;
            if (workTable?.columns == null)
            {
                return;
            }

            CaptureColumns(workTable);

            bool changed = ShouldShowColumns()
                ? RestoreColumns(workTable)
                : HideColumns(workTable);

            if (changed)
            {
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }
        }

        private static bool ShouldShowColumns()
        {
            if (!FluffyWorkTabGateway.IsPresent ||
                !FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab)
            {
                return true;
            }

            return BetterWorkTabMod.Settings?.showExternalWorkTabColumns ??
                DefaultSettings.showExternalWorkTabColumns;
        }

        private static void CaptureColumns(PawnTableDef workTable)
        {
            for (int i = 0; i < workTable.columns.Count; i++)
            {
                PawnColumnDef column = workTable.columns[i];
                if (!IsFluffyWidgetColumn(column) || IsCaptured(column))
                {
                    continue;
                }

                CapturedColumns.Add(new ColumnSnapshot(column, i));
            }
        }

        private static bool HideColumns(PawnTableDef workTable)
        {
            bool changed = false;
            for (int i = workTable.columns.Count - 1; i >= 0; i--)
            {
                if (IsFluffyWidgetColumn(workTable.columns[i]))
                {
                    workTable.columns.RemoveAt(i);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool RestoreColumns(PawnTableDef workTable)
        {
            bool changed = false;
            for (int i = 0; i < CapturedColumns.Count; i++)
            {
                ColumnSnapshot snapshot = CapturedColumns[i];
                if (workTable.columns.Contains(snapshot.Column))
                {
                    continue;
                }

                int index = Math.Min(snapshot.Index, workTable.columns.Count);
                workTable.columns.Insert(index, snapshot.Column);
                changed = true;
            }

            return changed;
        }

        private static bool IsFluffyWidgetColumn(PawnColumnDef column)
        {
            return column?.defName != null &&
                column.workType == null &&
                FluffyWidgetColumnDefNames.Contains(column.defName) &&
                column.Worker?.GetType().FullName?.StartsWith("WorkTab.PawnColumnWorker_", StringComparison.Ordinal) == true;
        }

        private static bool IsCaptured(PawnColumnDef column)
        {
            for (int i = 0; i < CapturedColumns.Count; i++)
            {
                if (CapturedColumns[i].Column == column)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class ColumnSnapshot
        {
            internal ColumnSnapshot(PawnColumnDef column, int index)
            {
                Column = column;
                Index = index;
            }

            internal PawnColumnDef Column { get; }
            internal int Index { get; }
        }
    }
}
