using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
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
        FluffyWorkTab,
        SleekWorkPriorities,
        BetterWorkTabWithSleekWorkPriorities
    }

    internal static class FluffyWorkTabCoexistence
    {
        internal const string SimulateFluffyFlag = "bwt-simulate-fluffy-worktab";

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

        internal static bool BetterWorkTabOwnsWorkTab =>
            (!IsFluffyWorkTabPresent ||
             BetterWorkTabMod.Settings?.preferredWorkTabOwner != WorkTabOwnerPreference.FluffyWorkTab ||
             !TryGetFluffyWorkTabWindowType(out _)) &&
            (!SleekWorkTabGateway.IsPresent ||
             BetterWorkTabMod.Settings?.preferredWorkTabOwner != WorkTabOwnerPreference.SleekWorkPriorities);

        internal static bool FluffyOwnsWorkTab =>
            IsFluffyWorkTabPresent &&
            BetterWorkTabMod.Settings?.preferredWorkTabOwner == WorkTabOwnerPreference.FluffyWorkTab &&
            TryGetFluffyWorkTabWindowType(out _);

        internal static bool SleekOwnsWorkTab =>
            SleekWorkTabGateway.IsPresent &&
            BetterWorkTabMod.Settings?.preferredWorkTabOwner == WorkTabOwnerPreference.SleekWorkPriorities;

        internal static bool BetterWorkTabHostsSleek =>
            SleekWorkTabGateway.IsPresent &&
            BetterWorkTabMod.Settings?.preferredWorkTabOwner == WorkTabOwnerPreference.BetterWorkTabWithSleekWorkPriorities;

        internal static bool ExternalWorkTabOwnsWorkTab => FluffyOwnsWorkTab || SleekOwnsWorkTab;

        internal static bool ShouldRunBetterWorkTabFeatures => BetterWorkTabOwnsWorkTab;

        internal static bool IsKnownFluffyPackageId(string packageId)
        {
            return FluffyWorkTabIdentity.IsKnownPackageId(packageId);
        }

        /// <summary>
        /// A newly detected Sleek installation starts in the mixed host. The
        /// explicit-selection bit keeps a later BWT-only choice from being
        /// silently changed back on the next compatibility reconciliation.
        /// </summary>
        internal static void ApplyDefaultExternalCompatibility()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null ||
                settings.workTabOwnerSelectionMade ||
                settings.preferredWorkTabOwner != WorkTabOwnerPreference.BetterWorkTab ||
                !SleekWorkTabGateway.IsPresent)
            {
                return;
            }

            settings.preferredWorkTabOwner = WorkTabOwnerPreference.BetterWorkTabWithSleekWorkPriorities;
            settings.Write();
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
                if (Current.ProgramState == ProgramState.Playing)
                {
                    SleekWorkTabGateway.ReconcileCompatibility();
                }
                return;
            }

            Type desired = typeof(UI.MainTabWindow_BetterWork);
            if (FluffyOwnsWorkTab &&
                TryGetFluffyWorkTabWindowType(out Type fluffyType))
            {
                desired = fluffyType;
            }
            else if (SleekOwnsWorkTab)
            {
                // Sleek owns the vanilla MainTabWindow_Work and gates its full replacement on the
                // same owner decision. BWT remains loaded for a reversible handoff and shared data.
                desired = typeof(MainTabWindow_Work);
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
            if (Current.ProgramState == ProgramState.Playing)
            {
                SleekWorkTabGateway.ReconcileCompatibility();
            }
        }

        internal static void SwitchToBetterWorkTab()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.workTabOwnerSelectionMade = true;
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

            settings.workTabOwnerSelectionMade = true;
            settings.preferredWorkTabOwner = WorkTabOwnerPreference.FluffyWorkTab;
            settings.Write();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            ApplyDesiredOwner(reopenIfOpen: true);
        }

        internal static void SwitchToBetterWorkTabWithSleek()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !SleekWorkTabGateway.IsPresent)
            {
                return;
            }

            settings.workTabOwnerSelectionMade = true;
            settings.preferredWorkTabOwner = WorkTabOwnerPreference.BetterWorkTabWithSleekWorkPriorities;
            settings.Write();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            ApplyDesiredOwner(reopenIfOpen: true);
        }

        internal static void SwitchToSleekWorkPriorities()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !SleekWorkTabGateway.IsPresent)
            {
                return;
            }

            settings.workTabOwnerSelectionMade = true;
            settings.preferredWorkTabOwner = WorkTabOwnerPreference.SleekWorkPriorities;
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

        /// <summary>
        /// Rechecks optional Fluffy identity once the post-load assembly set is complete. Normal
        /// IsPresent reads remain cached so closed-tab UI paths do not become a polling mechanism.
        /// </summary>
        internal static void ReconcileDetection()
        {
            if (_detected == true && (_fluffyWorkTabWindowType != null || IsSimulated))
            {
                return;
            }

            _detected = null;
            _detectedPackageId = null;
            _fluffyWorkTabWindowType = null;
            EnsureDetected();
        }

        private static MainTabsRoot TryGetMainTabsRoot()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return null;
            }

            try
            {
                return Find.MainTabsRoot;
            }
            catch (Exception exception) when (
                exception is NullReferenceException ||
                exception is InvalidCastException)
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
