using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal static class BWTTutorialUserContext
    {
        internal static string BuildSkillNumberTutorialBody(WorkTypeDef workType)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            string body = "BWT_Tutorial_PrioritySkill_Action".Translate();
            bool smallerNumbersCanAppear = settings != null &&
                settings.enableSkillOverlayFeature &&
                workType?.relevantSkills != null &&
                workType.relevantSkills.Count > 0 &&
                (settings.ShowUIMode_ShowSmallSkillNumbers == BetterWorkTabSettings.ShowUIMode.Always ||
                 settings.ShowUIMode_ShowSmallSkillNumbers == BetterWorkTabSettings.ShowUIMode.Shifted);
            if (smallerNumbersCanAppear)
            {
                body += "\n\n" + "BWT_Tutorial_PrioritySkill_SecondaryVisible".Translate();
            }

            return body;
        }
    }
}
