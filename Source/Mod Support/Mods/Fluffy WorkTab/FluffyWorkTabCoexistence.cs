using System;
using System.Collections.Generic;
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
            if (reopenIfOpen)
            {
                mainTabsRoot = TryGetMainTabsRoot();
                workTabOpen = mainTabsRoot?.OpenTab == work;
            }

            if (work.tabWindowClass != desired)
            {
                work.tabWindowClass = desired;
                work.Notify_ClearingAllMapsMemory();
            }

            if (workTabOpen && mainTabsRoot != null)
            {
                mainTabsRoot.SetCurrentTab(null, false);
                mainTabsRoot.SetCurrentTab(work, false);
            }
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
}
