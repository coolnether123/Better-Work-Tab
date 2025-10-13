using HarmonyLib;
using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab
{
    public class BetterWorkTabMod : Mod
    {
        public static BetterWorkTabSettings Settings;

        public BetterWorkTabMod(ModContentPack content) : base(content)
        {
            try
            {
                new Harmony("Coolnether123.betterworktab").PatchAll();
                Log.Message("[Better Work Tab] Harmony patched successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Better Work Tab] Harmony failed: {ex}");
            }

            Settings = GetSettings<BetterWorkTabSettings>();

            if (!Settings.firstTimeSetupDone)
            {
                Log.Message("Setting up default rulesets for the first time.");
                LongEventHandler.ExecuteWhenFinished(Settings.CreateDefaultRulesets);
                BetterWorkTabMod.Settings.firstTimeSetupDone = true;
                BetterWorkTabMod.Settings.Write();
            }
        }

        public override string SettingsCategory() => "Better Work Tab";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            //Widgets.Label(inRect, "This is the widget.");
            //Widgets.Label(new Rect(inRect.center, new Vector2(50, 50)), "Yep it's in the middle.");
            UI.BetterWorkTabSettingsUI.DoSettingsWindowContents(inRect, Settings);
        }

    }
}