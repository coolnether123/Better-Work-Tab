using Better_Work_Tab.Features.Rules;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    [StaticConstructorOnStartup]
    public static class ModSupportManager
    {
        private static readonly List<IModSupportModule> _activeModules = new List<IModSupportModule>();

        // Register your specific support modules here
        private static readonly List<IModSupportModule> _allModules = new List<IModSupportModule>
        {
            new UsefulMarksSupport(),
            new VanillaSkillsExpandedSupport(),
            new DoOnceSupport(),
        };

        static ModSupportManager()
        {
            BetterWorkTabMod.DebugLog("[ModSupport] Initializing ModSupportManager...", DebugFeature.ModSupport);
            foreach (var module in _allModules)
            {
                if (IsModActive(module.PackageId))
                {
                    try
                    {
                        BetterWorkTabMod.DebugLog($"[ModSupport] Detected active mod: {module.DisplayName} ({module.PackageId}). Initializing module.", DebugFeature.ModSupport);
                        module.OnModsDetected(); // Perform one-time setup
                        _activeModules.Add(module);
                        if (module is IModSettingsContributor settingsContributor)
                        {
                            BWTModSettingsApi.RegisterContributor(settingsContributor);
                        }

                        BetterWorkTabMod.DebugLog($"[ModSupport] Registered module: {module.DisplayName}.", DebugFeature.ModSupport);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"[BWT][ModSupport] Failed to initialize module {module.DisplayName}: {ex.Message}");
                    }
                }
            }
            BetterWorkTabMod.DebugLog($"[ModSupport] ModSupportManager initialized. {_activeModules.Count} active modules.", DebugFeature.ModSupport);
        }

        // --- Public methods for your core BWT mod to call (the "one-liners") ---

        public static void EnsureInitialized()
        {
        }

        public static IReadOnlyList<string> GetActiveModuleNames()
        {
            var names = new List<string>(_activeModules.Select(module => module.DisplayName));
            if (FluffyWorkTabGateway.IsPresent) names.Add("Fluffy Work Tab");
            AddIfActive(names, "Chronos Pointer", "CoolNether123.ChronosPointer", "CoolNether123.ChronosPointer.Legacy");
            AddIfActive(names, "Clockwork", "jaskkro.workshift");
            AddIfActive(names, "Complex Jobs", "FrozenSnowFox.ComplexJobs");
            AddIfActive(names, "Work Manager", "lordkuper.workmanager");
            AddIfActive(names, "Multiplayer", "rwmt.multiplayer");
            return names.Distinct().ToArray();
        }

        private static void AddIfActive(List<string> names, string displayName, params string[] packageIds)
        {
            if (packageIds.Any(IsModActive))
            {
                names.Add(displayName);
            }
        }

        internal static bool IsModActive(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return false;
            }

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                string activePackageId = mods[i]?.PackageId;
                if (string.Equals(activePackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                    (activePackageId != null &&
                     activePackageId.StartsWith(packageId + "_", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        public static void OnPawnTableRefresh(PawnTable table)
        {
            for (int i = 0; i < _activeModules.Count; i++)
            {
                _activeModules[i].OnPawnTableRefresh(table);
            }
        }

        public static void OnPawnRowDrawn(Pawn pawn, Rect iconRect)
        {
            for (int i = 0; i < _activeModules.Count; i++)
            {
                _activeModules[i].OnPawnRowDrawn(pawn, iconRect);
            }
        }

        public static void OnRulesEvaluated(Pawn pawn, WorkAssignmentParameters currentParameters)
        {
            for (int i = 0; i < _activeModules.Count; i++)
            {
                _activeModules[i].OnRulesEvaluated(pawn, currentParameters);
            }
        }

        // Special helper for getting specific mod data (e.g., Useful Marks)
        public static UsefulMarksSupport.UsefulMarkInfo? GetUsefulMarkInfo(Pawn pawn)
        {
            foreach (var module in _activeModules)
            {
                if (module is UsefulMarksSupport usefulMarksSupportModule)
                {
                    return usefulMarksSupportModule.GetMarkInfo(pawn);
                }
            }
            return null; // No active UsefulMarksSupport module or no info found
        }
    }
}
