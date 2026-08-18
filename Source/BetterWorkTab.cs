using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Migration;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.ModSupport.Mods.Clockwork;
using Better_Work_Tab.ModSupport.Mods.Spine;
using Better_Work_Tab.PawnOrganizer;
using HarmonyLib;
using Spine.Harmony.Infrastructure;
using Spine.UI.ColourPicker;
using System;
using UnityEngine;
using Verse;

/// <summary>
/// Main mod entry point for the Better Work Tab mod. This class serves as the primary initializer,
/// applying Harmony patches to extend the vanilla Work tab with features like skill overlays, pawn highlights,
/// drag-and-drop reordering for rows and columns, and rule-based automatic priority assignments.
/// Settings are loaded here, and default auto-assignment rulesets (e.g., prioritizing based on passions, skills) are queued.
/// Required load order: This mod loads after Harmony and RimWorld's core modules to ensure patches apply correctly.
/// </summary>
namespace Better_Work_Tab
{
    public class BetterWorkTabMod : Mod
    {
        /// <summary>
        /// Static reference to the global settings instance for the Better Work Tab mod.
        /// This allows easy access from patches, UI components, and features like skill overlays and assignment rules.
        /// Settings are automatically serialized/deserialized via Verse's ModSettings system.
        /// Defaults (e.g., enabled features, column order, default ruleset) are set during mod initialization.
        /// </summary>
        public static BetterWorkTabSettings Settings;

        public static void DebugLog(string message, DebugFeature feature = DebugFeature.General)
        {
            if (!(Settings?.enableDebugLogging ?? false))
            {
                return;
            }

            if (!Settings.debugFeatureToggles.TryGetValue(feature, out var enabled) || !enabled)
            {
                return;
            }

            Log.Message($"[BWT-{feature}] {message}");
        }

        /// <summary>
        /// Mod constructor invoked during game startup when the Better Work Tab mod is loaded.
        /// Applies all Harmony patches to relevant RimWorld classes (e.g., PawnTable_Work for row/column manipulation,
        /// MainTabWindow_Work for UI overlays, and work givers for execution order).
        /// Logs success or failure. Retrieves and initializes the mod settings,
        /// then queues the creation of default auto-assignment rulesets (e.g., "BWT Default" using passion/skill-based priorities)
        /// to run after long events like world loading.
        /// </summary>
        /// <param name="content">The mod's content pack, providing access to assets like textures and defs.</param>
        public BetterWorkTabMod(ModContentPack content) : base(content)
        {
            BetterWorkTabSettings settings = null;
            bool migratedSettings = false;
            BWT20SettingsMigration.BeginStartupSettingsLoad();
            try
            {
                settings = GetSettings<BetterWorkTabSettings>();
            }
            finally
            {
                migratedSettings = BWT20SettingsMigration.EndStartupSettingsLoad();
            }

            Settings = settings;
            if (migratedSettings)
            {
                // Persist the compatibility defaults and schema marker before the
                // player can close the first-launch prompt.
                Settings.Write();
            }

            Settings.NormalizePrioritySettings();
            Dialog_ColourPicker.ConfigureDebugLogger(message => DebugLog(message, DebugFeature.Layout));
            SpineCompatibilityGateway.Initialize();
            CompatibilityDiagnostics.ReportStartup(content);

            try
            {
                var harmony = new Harmony("Coolnether123.betterworktab");
                BwtRaisedPriorityInstallReport raisedPriorityReport =
                    BwtRaisedPriorityFeatureInstaller.InstallProduction(harmony);
                if (!raisedPriorityReport.FeatureActive)
                {
                    Log.Error("[Better Work Tab] Raised-priority feature was disabled fail-closed: " +
                        raisedPriorityReport.Format());
                }
                else if (raisedPriorityReport.FeatureGateState ==
                    BwtRaisedPriorityFeatureGateState.PreservedAfterRejectedReconfiguration)
                {
                    Log.Warning("[Better Work Tab] Raised-priority reconfiguration was rejected; " +
                        "the previous valid installation remains active: " +
                        raisedPriorityReport.Format());
                }
                else
                {
                    DebugLog("Raised-priority feature installed: " + raisedPriorityReport.State);
                }
                ClockworkCompatibility.Initialize(harmony);
                SleekWorkTabGateway.Initialize();
                FluffyWorkTabGateway.ApplyDesiredOwner();
                DebugLog("Harmony patched successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Better Work Tab] Harmony failed: {ex}");
            }


            LongEventHandler.ExecuteWhenFinished(Settings.InitializeRulesets);


            // Ensure game component exists and check for worklist
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                WorkColumnOrderManager.InitializeOnGameLoad();
                if (Current.Game != null)
                {
                    var component = Current.Game.GetComponent<GameComponent_BWTWorldSettings>();
                    if (component?.CurrentWorklist == null)
                    {
                        Log.Warning("[BetterWorkTab] No current worklist on startup. Create one in the Work tab.");
                    }
                }
            });




            //if (!Settings.firstTimeSetupDone)
            //{
            //    Log.Message("Setting up default rulesets for the first time.");
            //    LongEventHandler.ExecuteWhenFinished(Settings.CreateDefaultRulesets);
            //    BetterWorkTabMod.Settings.firstTimeSetupDone = true;
            //    BetterWorkTabMod.Settings.Write();
            //}
        }

        /// <summary>
        /// Overrides the base Mod method to provide the display name for this mod's settings section
        /// in RimWorld's options menu. Allows players to access toggles, rulesets, and UI customizations.
        /// </summary>
        /// <returns>A localized string representing the settings category name.</returns>
        public override string SettingsCategory() => "Better Work Tab";

        /// <summary>
        /// Overrides the base Mod method to draw the mod's custom settings interface.
        /// Delegates rendering to BetterWorkTabSettingsUI, which handles elements like feature toggles (e.g., skill overlay enabled),
        /// color schemes for highlights, management of auto-assignment rulesets, and workload calculations.
        /// Ensures the UI fits within the provided rectangle bounds for proper layout in the options window.
        /// </summary>
        /// <param name="inRect">The rectangular area in which to draw the settings contents.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            //Widgets.Label(inRect, "This is the widget.");
            //Widgets.Label(new Rect(inRect.center, new Vector2(50, 50)), "Yep it's in the middle.");
            UI.BetterWorkTabSettingsUI.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            Settings.NormalizePrioritySettings();
            Features.RaisedPriorityMaximum.PriorityAuthorityBroker.InvalidateCaches();
        }

        /// <summary>
        /// Monitors map changes and clears bed cache as needed.
        /// Prevents memory leaks from accumulating cached data for deleted maps.
        /// </summary>
        [StaticConstructorOnStartup]
        private static class MapChangeListener
        {
            static MapChangeListener()
            {
                if (!Prefs.DevMode)
                    return;

                // Optional: Register for map events if you want ultra-precise tracking
                // For now, cache self-cleans via invalidation, which is sufficient
            }
        }

    }

    public static class HighlightManager
    {
        public static WorkTypeDef WorkTypeToHighlight { get; private set; }

        public static void SetWorkTypeToHighlight(WorkTypeDef workType)
        {
            WorkTypeToHighlight = workType;
        }

        public static void ClearHighlight()
        {
            WorkTypeToHighlight = null;
        }
    }
}
