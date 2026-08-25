using System;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.UI;
using Verse;

namespace Better_Work_Tab.Features.Migration
{
    /// <summary>
    /// Owns the one-time 2.0 upgrade choice shown from a legacy colony's Work tab.
    /// </summary>
    internal static class BWT20UpgradePrompt
    {
        private static readonly OneTimePromptPresence Presence = new OneTimePromptPresence();

        internal static bool BlocksTutorialPresentation => Presence.IsPending;

        internal static void ShowIfNeeded(BetterWorkTabSettings settings)
        {
            IWorkTabWorldSchemaState worldSchema =
                WorkTabGameRoots.For(Current.Game)?.State.WorldSchema;
            if (settings == null ||
                worldSchema == null ||
                Presence.IsPending ||
                !BWT20UpgradePolicy.ShouldOfferUpgrade(
                    settings.v2UpgradePromptPending,
                    worldSchema.Version))
            {
                return;
            }

            Action startTutorial = () => Resolve(settings, worldSchema, startTutorial: true);
            Action keepSettings = () => Resolve(settings, worldSchema, startTutorial: false);
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
            Dialog_MessageBox dialog = new Dialog_MessageBox(
                "BWT_Upgrade20_PromptBody".Translate(),
                "BWT_Upgrade20_StartTutorial".Translate(),
                startTutorial,
                "BWT_Upgrade20_KeepSettings".Translate(),
                keepSettings,
                title: "BWT_Upgrade20_PromptTitle".Translate());
#else
            Dialog_MessageBox dialog = new Dialog_MessageBox(
                "BWT_Upgrade20_PromptBody".Translate(),
                "BWT_Upgrade20_StartTutorial".Translate(),
                startTutorial,
                "BWT_Upgrade20_KeepSettings".Translate(),
                keepSettings,
                title: "BWT_Upgrade20_PromptTitle".Translate(),
                acceptAction: startTutorial,
                cancelAction: keepSettings);
#endif
            Find.WindowStack.Add(dialog);
            Presence.Track(dialog);
        }

        private static void Resolve(
            BetterWorkTabSettings settings,
            IWorkTabWorldSchemaState worldSchema,
            bool startTutorial)
        {
            Presence.Clear();
            settings.v2UpgradePromptPending = false;
            worldSchema.Version = BWT20UpgradePolicy.CurrentWorldSchemaVersion;
            settings.showGeneralTutorial = startTutorial;
            settings.tutorialWelcomeCompleted = !startTutorial;
            settings.selectedTutorialCourse = BWTTutorialCourse.None;
            settings.tutorialFlowVersion = BWTGeneralTutorial.CurrentFlowVersion;
            settings.activeTutorialLessonId = string.Empty;
            settings.tutorialLessonPhase = 0;
            settings.Write();
        }
    }
}
