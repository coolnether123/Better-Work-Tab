using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab
{
    // ----------------------------------------------------------------------------
    // Mod entry point: runs Harmony.PatchAll() on load.
    // ----------------------------------------------------------------------------
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

            // This initializes mod settings during startup to ensure they're available throughout the game session
            Settings = GetSettings<BetterWorkTabSettings>();
        }

        public override string SettingsCategory() => "Better Work Tab";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var s = Settings;

            var l = new Listing_Standard { ColumnWidth = inRect.width / 2f - 12f };
            l.Begin(inRect);

            l.CheckboxLabeled("Enable skill overlay feature", ref s.enableSkillOverlayFeature,
                "If disabled, overlay-related patches do nothing.");

            l.GapLine();
            l.Label("Default starting priority (0 = don't change, 1..4 = force):");
            IntAdjust(ref s.defaultStartingPriority, 0, 4, l.GetRect(24f));

            l.GapLine();
            l.Label("Core 'Always X' Rule");
            l.CheckboxLabeled("Enable", ref s.rule_CoreAlwaysPriorityEnabled);
            l.Label("Priority for core/extra: " + s.rule_CoreAlwaysPriorityValue);
            s.rule_CoreAlwaysPriorityValue = Mathf.Clamp(
                Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_CoreAlwaysPriorityValue, 1, 4, middleAlignment: true)),
                1, 4);

            l.CheckboxLabeled("Firefighter", ref s.core_Firefighter);
            l.CheckboxLabeled("Patient", ref s.core_Patient);
            l.CheckboxLabeled("BedRest", ref s.core_BedRest);
            l.CheckboxLabeled("Basic", ref s.core_Basic);

            l.Gap();
            l.Label("Additional WorkTypeDef defNames (CSV):");

            l.NewColumn();

            l.CheckboxLabeled("Enable Auto-Assign feature", ref s.enableAutoAssignFeature,
                "If disabled, the 'Auto Assign Work' button does nothing.");

            l.GapLine();
            l.Label("Best Doctors Rule");
            l.CheckboxLabeled("Enable", ref s.rule_BestDoctorsEnabled);
            l.Label("Priority for best doctors: " + s.rule_BestDoctorsPriority);
            s.rule_BestDoctorsPriority = Mathf.Clamp(
                Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_BestDoctorsPriority, 1, 4, middleAlignment: true)),
                1, 4);

            l.GapLine();
            l.Label("Childcare Rule");
            l.CheckboxLabeled("Enable", ref s.rule_ChildcareEnabled);
            l.Label("Priority for eligible pawns: " + s.rule_ChildcarePriority);
            s.rule_ChildcarePriority = Mathf.Clamp(
                Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_ChildcarePriority, 1, 4, middleAlignment: true)),
                1, 4);

            l.GapLine();
            l.Label("Passion Overrides (0 = disabled / don't change)");
            l.CheckboxLabeled("Enable passion overrides", ref s.rule_PassionOverrideEnabled,
                "Map each passion level to a fixed priority. 0 = leave as is.");
            DrawPassionField(l, "None", ref s.passion_None);
            DrawPassionField(l, "Minor", ref s.passion_Minor);
            DrawPassionField(l, "Major", ref s.passion_Major);

            l.End();
        }

        // This provides increment/decrement buttons for integer settings to make precise adjustment easier than sliders
        private static void IntAdjust(ref int val, int min, int max, Rect row)
        {
            var minus = new Rect(row.x, row.y, 24f, 24f);
            var label = new Rect(row.x + 28f, row.y, 64f, 24f);
            var plus = new Rect(row.x + 96f, row.y, 24f, 24f);

            if (Widgets.ButtonText(minus, "–")) val = Mathf.Max(min, val - 1);
            Widgets.Label(label, val.ToString());
            if (Widgets.ButtonText(plus, "+")) val = Mathf.Min(max, val + 1);
        }

        // This creates a labeled slider for passion priority settings with tooltip explaining the 0-4 range
        private static void DrawPassionField(Listing_Standard l, string label, ref int value)
        {
            var row = l.GetRect(24f);
            Widgets.Label(new Rect(row.x, row.y, 90f, 24f), label);
            var sliderRect = new Rect(row.x + 92f, row.y + 2f, row.width - 92f, 20f);
            var cur = Mathf.Clamp(value, 0, 4);
            int v = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, cur, 0, 4, middleAlignment: true));
            TooltipHandler.TipRegion(sliderRect, "0 = don't change\n1..4 = set passion to this priority");
            value = Mathf.Clamp(v, 0, 4);
        }
    }

    // Global overlay state. Toggle is in the Work tab header.
    public static class SkillOverlayState
    {
        /// <summary>
        /// When true, the priority cell is replaced with the skill level (0–20).
        /// </summary>
        public static bool ShowSkills = false;

        /// <summary>
        /// Returns true if the overlay should render right now (global toggle OR Shift held).
        /// </summary>
        public static bool ActiveNow
        {
            get
            {
                bool shift = Event.current != null && Event.current.shift;
                return ShowSkills || shift;
            }
        }
    }

    // Custom work box drawer that preserves all vanilla visuals except priority number.
    public static class CustomWorkBoxDrawer
    {
        /// <summary>
        /// Draws a work box with vanilla visuals (background, passion flames, incapable tint)
        /// but WITHOUT the priority number or click handling.
        /// </summary>
        public static void DrawWorkBoxForSkillOverlay(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        {
            if (p.WorkTypeIsDisabled(wType))
            {
                // This handles age-disabled work types by showing the vanilla age restriction texture and message
                int minAgeRequired;
                if (!p.IsWorkTypeDisabledByAge(wType, out minAgeRequired))
                    return;

                Rect rect = new Rect(x, y, 25f, 25f);

                // This preserves the vanilla age restriction feedback when clicking on age-disabled work
                if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                {
                    Messages.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired), p, MessageTypeDefOf.RejectInput, false);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                var tex = (Texture2D)AccessTools.Field(typeof(WidgetsWork), "WorkBoxBGTex_AgeDisabled").GetValue(null);
                GUI.DrawTexture(rect, tex);
            }
            else
            {
                Rect rect = new Rect(x, y, 25f, 25f);

                // This applies the same red tint that vanilla uses for incapable work types to maintain visual consistency
                if (incapableBecauseOfCapacities)
                    GUI.color = new Color(1f, 0.3f, 0.3f);

                // This draws the work box background including passion flame effects exactly like vanilla does
                var method = typeof(WidgetsWork).GetMethod("DrawWorkBoxBackground", BindingFlags.NonPublic | BindingFlags.Static);
                method.Invoke(null, new object[] { rect, p, wType });

                // This resets the GUI color after drawing the background to prevent affecting other UI elements
                GUI.color = Color.white;

                // This intentionally skips priority number drawing and click handling because the mod will overlay skill levels instead
            }
        }
    }

    // Patch: add ONE checkbox to the top of the Work tab near "Manual priorities".
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class Patch_WorkTab_AddSingleToggle
    {
        public static void Postfix(Rect rect)
        {
            // This only draws UI during actual rendering passes to avoid layout calculation issues
            if (Event.current == null || Event.current.type == EventType.Layout) return;

            // This positions the mod's toggle next to the vanilla "Manual priorities" checkbox at (5,5,140,30)
            Rect toggleRect = new Rect(150f, 5f, 230f, 30f);

            bool show = SkillOverlayState.ShowSkills;
            Widgets.CheckboxLabeled(toggleRect, "Show skill levels (0\u201320)", ref show);

            if (show != SkillOverlayState.ShowSkills)
            {
                SkillOverlayState.ShowSkills = show;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                // This doesn't force table refresh because the work tab repaints continuously anyway
            }

            AutoWorkAssigner.DrawButton(rect);
        }
    }

    // Patch: Replace the priority number inside the vanilla box with the skill level.
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_ReplaceNumber
    {
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            if (!SkillOverlayState.ActiveNow)
                return true;

            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return false;

            var wt = __instance.def.workType;
            if (wt == null || pawn.WorkTypeIsDisabled(wt))
                return false;

            bool incapable = IsIncapableOfWholeWorkType(pawn, wt);

            float x = rect.x + ((rect.width - 25f) / 2f);
            float y = rect.y + 2.5f;
            Rect boxRect = new Rect(x, y, 25f, 25f);

            if (Event.current.type == EventType.Repaint)
            {
                CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(x, y, pawn, wt, incapable);
            }

            // This calculates the average skill level across all skills relevant to this work type
            int level = 0;
            if (pawn.skills != null)
            {
                float avg = pawn.skills.AverageOfRelevantSkillsFor(wt);
                level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
            }

            var oldF = Text.Font;
            var oldA = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);

            Widgets.Label(boxRect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldF;
            Text.Anchor = oldA;

            // This preserves the vanilla tooltip functionality so players can still see work type details
            TooltipHandler.TipRegion(boxRect,
                () => WidgetsWork.TipForPawnWorker(pawn, wt, incapable),
                pawn.thingIDNumber ^ wt.GetHashCode());

            return false;
        }

        // This determines if a pawn is incapable of a work type by checking if they can do at least one work giver
        private static bool IsIncapableOfWholeWorkType(Pawn p, WorkTypeDef work)
        {
            for (int i = 0; i < work.workGiversByPriority.Count; i++)
            {
                bool canDoThisGiver = true;
                var reqs = work.workGiversByPriority[i].requiredCapacities;
                for (int j = 0; j < reqs.Count; j++)
                {
                    if (!p.health.capacities.CapableOf(reqs[j]))
                    {
                        canDoThisGiver = false;
                        break;
                    }
                }
                if (canDoThisGiver)
                    return false;
            }
            return true;
        }

        // This provides color coding for skill levels to make them easier to read at a glance
        private static Color ColorForSkillLevel(int level)
        {
            if (level <= 3) return new Color(0.82f, 0.25f, 0.25f);  // Red for very low skills
            if (level <= 9) return new Color(0.95f, 0.75f, 0.20f);  // Orange for low skills
            if (level <= 15) return new Color(0.95f, 0.95f, 0.95f); // White for good skills
            return new Color(0.35f, 0.85f, 0.35f);                  // Green for excellent skills
        }
    }
}