using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    internal static class CompatibilityDiagnostics
    {
        private static readonly KnownConflict[] HardConflicts =
        {
            new KnownConflict("Fluffy Work Tab", "fluffy.worktab"),
            new KnownConflict("Compact Work Tab", "mlie.compactworktab"),
        };

        private static bool reported;
        private static readonly HashSet<int> ReportedWarnings = new HashSet<int>();

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
                if (GetActiveModWithIdentifier(conflict.PackageId) == null)
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
                if (IsBetterWorkTab(mod, betterWorkTab) || IsKnownHardConflict(mod))
                {
                    continue;
                }

                string modPackageId = GetPackageId(mod);
                if (ContainsIgnoreCase(mod.Name, "work tab") || ContainsIgnoreCase(modPackageId, "worktab"))
                {
                    WarningOnce(
                        $"[Better Work Tab] Possible Work tab UI mod also active: {mod.Name} ({modPackageId}). " +
                        "If it changes the vanilla Work tab, run only one Work tab replacement at a time.",
                        74239200 + i);
                }
            }
        }

        private static void ReportKnownMultiplayerLoadOrderRisk(ModContentPack betterWorkTab)
        {
            if (GetActiveModWithIdentifier("rwmt.multiplayer") == null ||
                GetActiveModWithIdentifier("unlimitedhugs.hugslib") == null)
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
                string modPackageId = GetPackageId(mod);
                if (ContainsIgnoreCase(mod.Name, "rimhammer") || ContainsIgnoreCase(modPackageId, "rimhammer"))
                {
                    lastRimhammerIndex = i;
                    lastRimhammer = mod;
                }
            }

            if (lastRimhammerIndex < 0 || bwtIndex < 0 || bwtIndex > lastRimhammerIndex)
            {
                return;
            }

            WarningOnce(
                $"[Better Work Tab] Multiplayer + HugsLib + Rimhammer detected, with BWT loading before {lastRimhammer.Name} ({GetPackageId(lastRimhammer)}). " +
                "There is a historical report of this stack locking the host map render during new-colony startup. Put Better Work Tab after the Rimhammer mods and include Player.log if it still reproduces.",
                74239300);
        }

        private static ModContentPack GetActiveModWithIdentifier(string packageId)
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                string activePackageId = GetPackageId(mod);
                if (EqualsIgnoreCase(activePackageId, packageId) ||
                    StartsWithIgnoreCase(activePackageId, packageId + "_"))
                {
                    return mod;
                }
            }

            return null;
        }

        private static void WarningOnce(string message, int key)
        {
            if (!ReportedWarnings.Add(key))
            {
                return;
            }

            Log.Warning(message);
        }

        private static bool IsKnownHardConflict(ModContentPack mod)
        {
            for (int i = 0; i < HardConflicts.Length; i++)
            {
                if (EqualsIgnoreCase(GetPackageId(mod), HardConflicts[i].PackageId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBetterWorkTab(ModContentPack mod, ModContentPack betterWorkTab)
        {
            return ReferenceEquals(mod, betterWorkTab) || EqualsIgnoreCase(GetPackageId(mod), "coolnether123.betterworktab");
        }

        private static string GetPackageId(ModContentPack mod)
        {
            if (mod == null)
            {
                return string.Empty;
            }

            Type type = mod.GetType();
            PropertyInfo property = type.GetProperty("PackageId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                object value = property.GetValue(mod, null);
                if (value is string packageId && !string.IsNullOrEmpty(packageId))
                {
                    return packageId;
                }
            }

            FieldInfo field = type.GetField("packageId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                object value = field.GetValue(mod);
                if (value is string packageId && !string.IsNullOrEmpty(packageId))
                {
                    return packageId;
                }
            }

            return mod.Name ?? string.Empty;
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

        private static bool StartsWithIgnoreCase(string value, string fragment)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.StartsWith(fragment, StringComparison.OrdinalIgnoreCase);
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
