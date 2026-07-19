using System;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.Workloads;
using Verse;

namespace Better_Work_Tab.Features.Migration
{
    /// <summary>
    /// Owns the one-time 2.0 upgrade choice shown from a legacy colony's Work tab.
    /// </summary>
    internal static class BWT20UpgradePrompt
    {
        private static bool promptQueued;

        internal static void ShowIfNeeded(
            BetterWorkTabSettings settings,
            GameComponent_BWTWorldSettings worldSettings)
        {
            if (settings == null ||
                worldSettings == null ||
                promptQueued ||
                !BWT20UpgradePolicy.ShouldOfferUpgrade(
                    settings.v2UpgradePromptPending,
                    worldSettings.BWTWorldSchemaVersion))
            {
                return;
            }

            promptQueued = true;
            Action startTutorial = () => Resolve(settings, worldSettings, startTutorial: true);
            Action keepSettings = () => Resolve(settings, worldSettings, startTutorial: false);
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
            Find.WindowStack.Add(new Dialog_MessageBox(
                "BWT_Upgrade20_PromptBody".Translate(),
                "BWT_Upgrade20_StartTutorial".Translate(),
                startTutorial,
                "BWT_Upgrade20_KeepSettings".Translate(),
                keepSettings,
                title: "BWT_Upgrade20_PromptTitle".Translate()));
#else
            Find.WindowStack.Add(new Dialog_MessageBox(
                "BWT_Upgrade20_PromptBody".Translate(),
                "BWT_Upgrade20_StartTutorial".Translate(),
                startTutorial,
                "BWT_Upgrade20_KeepSettings".Translate(),
                keepSettings,
                title: "BWT_Upgrade20_PromptTitle".Translate(),
                acceptAction: startTutorial,
                cancelAction: keepSettings));
#endif
        }

        private static void Resolve(
            BetterWorkTabSettings settings,
            GameComponent_BWTWorldSettings worldSettings,
            bool startTutorial)
        {
            promptQueued = false;
            settings.v2UpgradePromptPending = false;
            worldSettings.BWTWorldSchemaVersion = BWT20UpgradePolicy.CurrentWorldSchemaVersion;
            settings.showGeneralTutorial = startTutorial;
            settings.tutorialWelcomeCompleted = true;
            settings.tutorialFlowVersion = BWTGeneralTutorial.CurrentFlowVersion;
            settings.activeTutorialLessonId = string.Empty;
            settings.tutorialLessonPhase = 0;
            settings.Write();
        }
    }
}
