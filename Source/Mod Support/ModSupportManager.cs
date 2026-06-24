using Better_Work_Tab.Features.Rules;
using RimWorld;
using System.Collections.Generic;
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
                var matchedMod = ModListerCompat.GetActiveModWithIdentifier(module.PackageId);
                if (matchedMod != null)
                {
                    try
                    {
                        BetterWorkTabMod.DebugLog($"[ModSupport] Detected active mod: {module.DisplayName} ({module.PackageId}). Initializing module.", DebugFeature.ModSupport);
                        module.OnModsDetected(); // Perform one-time setup
                        _activeModules.Add(module);
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
