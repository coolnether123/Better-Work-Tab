using System;
using System.Collections.Generic;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    internal static class CompatibilityDiagnostics
    {
        private static readonly KnownConflict[] HardConflicts =
        {
            new KnownConflict("Compact Work Tab", "mlie.compactworktab"),
        };

        private static readonly string[] CooperativeWorkTabExtensions =
        {
            "spacemoth.mechtab",
        };

        private static bool reported;

        public static void ReportStartup(ModContentPack betterWorkTab)
        {
            if (reported)
            {
                return;
            }

            reported = true;
            ReportHardConflicts();
            ReportPossibleWorkTabReplacements(betterWorkTab);
            ReportKnownMultiplayerLoadOrderRisk(betterWorkTab);
        }

        private static void ReportHardConflicts()
        {
            for (int i = 0; i < HardConflicts.Length; i++)
            {
                KnownConflict conflict = HardConflicts[i];
                if (ModLister.GetActiveModWithIdentifier(conflict.PackageId, ignorePostfix: true) == null)
                {
                    continue;
                }

                Log.ErrorOnce(
                    $"[Better Work Tab] Incompatible mod active: {conflict.DisplayName} ({conflict.PackageId}). " +
                    "Both mods replace the Work tab UI; load order cannot make this reliable. Disable one of them before reporting BWT UI or multiplayer issues.",
                    74239101 + i);
            }
        }

        private static void ReportPossibleWorkTabReplacements(ModContentPack betterWorkTab)
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (IsBetterWorkTab(mod, betterWorkTab) ||
                    IsKnownHardConflict(mod) ||
                    IsKnownCooperativeExtension(mod) ||
                    FluffyWorkTabGateway.IsKnownPackageId(mod.PackageId))
                {
                    continue;
                }

                if (ContainsIgnoreCase(mod.Name, "work tab") || ContainsIgnoreCase(mod.PackageId, "worktab"))
                {
                    Log.WarningOnce(
                        $"[Better Work Tab] Possible Work tab UI mod also active: {mod.Name} ({mod.PackageId}). " +
                        "If it changes the vanilla Work tab, run only one Work tab replacement at a time.",
                        74239200 + i);
                }
            }
        }

        private static void ReportKnownMultiplayerLoadOrderRisk(ModContentPack betterWorkTab)
        {
            if (ModLister.GetActiveModWithIdentifier("rwmt.multiplayer", ignorePostfix: true) == null ||
                ModLister.GetActiveModWithIdentifier("unlimitedhugs.hugslib", ignorePostfix: true) == null)
            {
                return;
            }

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            int bwtIndex = IndexOf(mods, betterWorkTab);
            int lastRimhammerIndex = -1;
            ModContentPack lastRimhammer = null;

            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (ContainsIgnoreCase(mod.Name, "rimhammer") || ContainsIgnoreCase(mod.PackageId, "rimhammer"))
                {
                    lastRimhammerIndex = i;
                    lastRimhammer = mod;
                }
            }

            if (lastRimhammerIndex < 0 || bwtIndex < 0 || bwtIndex > lastRimhammerIndex)
            {
                return;
            }

            Log.WarningOnce(
                $"[Better Work Tab] Multiplayer + HugsLib + Rimhammer detected, with BWT loading before {lastRimhammer.Name} ({lastRimhammer.PackageId}). " +
                "There is a historical report of this stack locking the host map render during new-colony startup. Put Better Work Tab after the Rimhammer mods and include Player.log if it still reproduces.",
                74239300);
        }

        private static bool IsKnownHardConflict(ModContentPack mod)
        {
            for (int i = 0; i < HardConflicts.Length; i++)
            {
                if (EqualsIgnoreCase(mod.PackageId, HardConflicts[i].PackageId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownCooperativeExtension(ModContentPack mod)
        {
            for (int i = 0; i < CooperativeWorkTabExtensions.Length; i++)
            {
                if (EqualsIgnoreCase(mod.PackageId, CooperativeWorkTabExtensions[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBetterWorkTab(ModContentPack mod, ModContentPack betterWorkTab)
        {
            return ReferenceEquals(mod, betterWorkTab) || EqualsIgnoreCase(mod.PackageId, "coolnether123.betterworktab");
        }

        private static int IndexOf(List<ModContentPack> mods, ModContentPack target)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                if (ReferenceEquals(mods[i], target) || IsBetterWorkTab(mods[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct KnownConflict
        {
            public KnownConflict(string displayName, string packageId)
            {
                DisplayName = displayName;
                PackageId = packageId;
            }

            public string DisplayName { get; }
            public string PackageId { get; }
        }
    }
}
