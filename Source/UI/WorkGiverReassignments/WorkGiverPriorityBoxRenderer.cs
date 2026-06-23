using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Renders priority boxes for WorkGivers with vanilla-style appearance and behavior.
    /// </summary>
    internal static class WorkGiverPriorityBoxRenderer
    {
        public static void DrawPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            if (wg?.def == null)
            {
                return;
            }

            int defaultPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);

            if (pawn != null &&
                !pawn.WorkTypeIsDisabled(workType) &&
                defaultPriority <= WorkPrioritySystem.DisabledPriority)
            {
                DrawInheritedDisabledPriorityBox(workType, pawn, boxRect);
                TooltipHandler.TipRegion(boxRect, WorkTypeCompat.WorkGiverLabelCap(wg.def));
                return;
            }

            int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, wg.def, defaultPriority);

            if (pawn != null)
            {
                DrawPawnPriorityBox(wg, workType, pawn, boxRect, workGiverPriority);
            }
            else
            {
                DrawGlobalPriorityBox(wg, boxRect, workGiverPriority);
            }

            TooltipHandler.TipRegion(boxRect, WorkTypeCompat.WorkGiverLabelCap(wg.def));
        }

        private static void DrawPawnPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect, int workGiverPriority)
        {
            if (DrawPawnWorkBoxContents(boxRect, pawn, workType, workGiverPriority, IsIncapable(pawn, wg)))
            {
                HandlePriorityClick(pawn.thingIDNumber, wg.def, boxRect, workGiverPriority);
            }
        }

        private static void DrawGlobalPriorityBox(WorkGiver wg, Rect boxRect, int workGiverPriority)
        {
            if (BetterWorkTabMod.Settings?.useVanillaSubWorkGlobalPriorityBoxes == true)
            {
                DrawVanillaGlobalPriorityBoxContents(boxRect, workGiverPriority);
            }
            else
            {
                DrawPriorityBoxContents(boxRect, workGiverPriority, false);
            }

            HandlePriorityClick(-1, wg.def, boxRect, workGiverPriority);
        }

        private static void DrawVanillaGlobalPriorityBoxContents(Rect boxRect, int priority)
        {
            priority = WorkPrioritySystem.ClampPriority(priority);
            Texture2D bgTex = priority == WorkPrioritySystem.DisabledPriority
                ? WorkGiverPriorityBoxCompatibility.WorkBoxBGTexBad
                : WorkGiverPriorityBoxCompatibility.WorkBoxBGTexMid;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            WorkGiverPriorityBoxCompatibility.DrawWorkBoxTexture(
                boxRect,
                bgTex,
                priority == WorkPrioritySystem.DisabledPriority);

            if (Find.PlaySettings.useWorkPriorities)
            {
                if (priority > WorkPrioritySystem.DisabledPriority)
                {
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                    Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                }
            }
            else if (priority > WorkPrioritySystem.DisabledPriority)
            {
                WorkGiverPriorityBoxCompatibility.DrawCheck(boxRect);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static void DrawPriorityBoxContents(Rect boxRect, int priority, bool incapable)
        {
            priority = WorkPrioritySystem.ClampPriority(priority);
            Texture2D bgTex = priority == WorkPrioritySystem.DisabledPriority
                ? WorkGiverPriorityBoxCompatibility.WorkBoxBGTexBad
                : WorkGiverPriorityBoxCompatibility.WorkBoxBGTexMid;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            if (incapable)
            {
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            WorkGiverPriorityBoxCompatibility.DrawWorkBoxTexture(
                boxRect,
                bgTex,
                priority == WorkPrioritySystem.DisabledPriority);
            GUI.color = oldColor;

            if (priority > 0)
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static bool DrawPawnWorkBoxContents(Rect boxRect, Pawn pawn, WorkTypeDef workType, int priority, bool incapable)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            if (pawn.WorkTypeIsDisabled(workType))
            {
                int minAgeRequired;
                if (WorkGiverPriorityBoxCompatibility.IsWorkTypeDisabledByAge(pawn, workType, out minAgeRequired))
                {
                    if (Event.current.type == EventType.MouseDown && Mouse.IsOver(boxRect))
                    {
                        string message = "MessageWorkTypeDisabledAge".Translate(
                            pawn,
                            WorkGiverPriorityBoxCompatibility.AgeBiologicalYears(pawn),
                            WorkTypeCompat.LabelShort(workType),
                            minAgeRequired);
#if vAlpha4
                        MessageCompat.Message(message, MessageTypeDefOf.RejectInput, false);
#else
                        MessageCompat.Message(message, pawn, MessageTypeDefOf.RejectInput, false);
#endif
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        Event.current.Use();
                    }

                    WorkGiverPriorityBoxCompatibility.DrawWorkBoxTexture(
                        boxRect,
                        WorkGiverPriorityBoxCompatibility.WorkBoxBGTexAgeDisabled,
                        true);
                }

                return false;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            if (incapable)
            {
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            WorkGiverPriorityBoxCompatibility.DrawWorkBoxBackground(boxRect, pawn, workType);
            GUI.color = oldColor;

            if (Find.PlaySettings.useWorkPriorities)
            {
                if (priority > WorkPrioritySystem.DisabledPriority)
                {
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                    Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                }
            }
            else if (priority > WorkPrioritySystem.DisabledPriority)
            {
                WorkGiverPriorityBoxCompatibility.DrawCheck(boxRect);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            return true;
        }

        private static void DrawInheritedDisabledPriorityBox(WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            GUI.color = new Color(0.52f, 0.52f, 0.52f, 0.82f);
            WorkGiverPriorityBoxCompatibility.DrawWorkBoxTexture(
                boxRect,
                WorkGiverPriorityBoxCompatibility.WorkBoxBGTexBad,
                true);
            GUI.color = new Color(0.18f, 0.18f, 0.18f, 0.42f);
            WidgetsCompat.DrawBoxSolid(boxRect, GUI.color);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            HandleInheritedDisabledClick(pawn, workType, boxRect);
        }

        private static void HandleInheritedDisabledClick(Pawn pawn, WorkTypeDef workType, Rect boxRect)
        {
            Event evt = Event.current;
            if (evt == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel) ||
                !Mouse.IsOver(boxRect) ||
                BetterWorkTabLocalState.IsHeaderDragging ||
                SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button != 0 && evt.button != 1)
            {
                return;
            }

            if (evt.type == EventType.ScrollWheel && !(BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false))
            {
                return;
            }

            WorkGiverReassignmentManager.EnableParentAndClearSubOverridesSynced(pawn.thingIDNumber, workType.defName);
            UISoundCompat.CheckboxTurnedOn.PlayOneShotOnCamera();
            evt.Use();
        }

        private static void HandlePriorityClick(int pawnId, WorkGiverDef workGiverDef, Rect boxRect, int currentPriority)
        {
            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return;
            }

            if (SubWorkDrilldownInput.MatchesGesture(Event.current))
            {
                return;
            }

            if (!Mouse.IsOver(boxRect))
            {
                return;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown)
            {
                int newPriority = GetNextPriority(currentPriority, evt.button);

                if (newPriority != currentPriority)
                {
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(pawnId, workGiverDef.defName, newPriority);
                    UISoundCompat.DragSlider.PlayOneShotOnCamera();
                }

                evt.Use();
                return;
            }

            if ((BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false) && evt.type == EventType.ScrollWheel)
            {
                int direction = evt.delta.y > 0f ? -1 : 1;
                int newPriority = Find.PlaySettings.useWorkPriorities
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(currentPriority, direction)
                    : ToggleNonManualPriority(currentPriority);

                if (newPriority != currentPriority)
                {
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(pawnId, workGiverDef.defName, newPriority);
                    UISoundCompat.DragSlider.PlayOneShotOnCamera();
                }

                evt.Use();
            }
        }

        private static int GetNextPriority(int currentPriority, int button)
        {
            if (Find.PlaySettings.useWorkPriorities)
            {
                return WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, button);
            }

            if (button != 0)
            {
                return WorkPrioritySystem.ClampPriority(currentPriority);
            }

            return currentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
        }

        private static int ToggleNonManualPriority(int currentPriority)
        {
            return currentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
        }

        private static bool IsIncapable(Pawn pawn, WorkGiver wg)
        {
#if vAlpha4
            return false;
#else
            if (wg.def.requiredCapacities == null) return false;

            foreach (var cap in wg.def.requiredCapacities)
            {
                if (!pawn.health.capacities.CapableOf(cap))
                {
                    return true;
                }
            }

            return false;
#endif
        }

    }

    internal static class WorkGiverPriorityBoxCompatibility
    {
        private const BindingFlags TextureFieldFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo IsWorkTypeDisabledByAgeMethod = typeof(Pawn).GetMethod(
            "IsWorkTypeDisabledByAge",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(WorkTypeDef), typeof(int).MakeByRefType() },
            null);

        private static readonly FieldInfo WorkBoxBGTexBadField = FindTextureField("WorkBoxBGTex_Bad");
        private static readonly FieldInfo WorkBoxBGTexMidField = FindTextureField("WorkBoxBGTex_Mid");
        private static readonly FieldInfo WorkBoxBGTexExcellentField = FindTextureField("WorkBoxBGTex_Excellent");
        private static readonly FieldInfo WorkBoxBGTexAgeDisabledField = FindTextureField("WorkBoxBGTex_AgeDisabled");
        private static readonly FieldInfo WorkBoxBGTexAwfulField = FindTextureField("WorkBoxBGTex_Awful");
        private static readonly FieldInfo WorkBoxCheckField = FindTextureField("WorkBoxCheckTex");
        private static readonly FieldInfo PassionMinorField = FindTextureField("PassionWorkboxMinorIcon");
        private static readonly FieldInfo PassionMajorField = FindTextureField("PassionWorkboxMajorIcon");

        internal static Texture2D WorkBoxBGTexBad => GetTexture(WorkBoxBGTexBadField);

        internal static Texture2D WorkBoxBGTexMid => GetTexture(WorkBoxBGTexMidField);

        internal static Texture2D WorkBoxBGTexAgeDisabled
        {
            get
            {
                return GetTexture(WorkBoxBGTexAgeDisabledField) ?? WorkBoxBGTexBad;
            }
        }

        private static Texture2D WorkBoxBGTexExcellent => GetTexture(WorkBoxBGTexExcellentField);

        private static Texture2D WorkBoxCheckTex => GetTexture(WorkBoxCheckField);

        private static Texture2D PassionMinorTex => GetTexture(PassionMinorField);

        private static Texture2D PassionMajorTex => GetTexture(PassionMajorField);

        internal static bool IsWorkTypeDisabledByAge(Pawn pawn, WorkTypeDef workType, out int minAgeRequired)
        {
            minAgeRequired = 0;
            if (pawn == null || workType == null || IsWorkTypeDisabledByAgeMethod == null)
            {
                return false;
            }

            try
            {
                object[] args = { workType, minAgeRequired };
                bool result = (bool)IsWorkTypeDisabledByAgeMethod.Invoke(pawn, args);
                minAgeRequired = args[1] is int value ? value : 0;
                return result;
            }
            catch
            {
                return false;
            }
        }

        internal static int AgeBiologicalYears(Pawn pawn)
        {
#if vAlpha4
            return 0;
#else
            return pawn?.ageTracker?.AgeBiologicalYears ?? 0;
#endif
        }

        internal static void DrawWorkBoxTexture(Rect rect, Texture2D texture, bool disabledFallback)
        {
            if (texture != null)
            {
                GUI.DrawTexture(rect, texture);
                return;
            }

            Color fallbackColor = disabledFallback
                ? new Color(0.28f, 0.28f, 0.28f, GUI.color.a)
                : new Color(0.38f, 0.38f, 0.38f, GUI.color.a);
            WidgetsCompat.DrawBoxSolid(rect, fallbackColor);
        }

        internal static void DrawCheck(Rect rect)
        {
            Texture2D check = WorkBoxCheckTex;
            if (check != null)
            {
                GUI.DrawTexture(rect, check);
                return;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = WorkPrioritySystem.GetPriorityColor(WorkPrioritySystem.GetDefaultEnabledPriority());
            Widgets.Label(rect, "X");

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        internal static void DrawWorkBoxBackground(Rect rect, Pawn pawn, WorkTypeDef workType)
        {
            SkillRecord skill = GetFirstRelevantSkill(pawn, workType);
            Texture2D background = GetLegacyWorkBoxBackground(skill);

            DrawWorkBoxTexture(rect, background, false);

            if (skill == null)
            {
                return;
            }

            if (skill.passion == Passion.Minor && PassionMinorTex != null)
            {
                GUI.DrawTexture(rect, PassionMinorTex);
            }
            else if (skill.passion == Passion.Major && PassionMajorTex != null)
            {
                GUI.DrawTexture(rect, PassionMajorTex);
            }
        }

        private static SkillRecord GetFirstRelevantSkill(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null || workType?.relevantSkills == null || workType.relevantSkills.Count == 0)
            {
                return null;
            }

            return pawn.skills.GetSkill(workType.relevantSkills[0]);
        }

        private static Texture2D GetLegacyWorkBoxBackground(SkillRecord skill)
        {
            if (skill == null)
            {
                return WorkBoxBGTexMid;
            }

            int level = SkillCompat.Level(skill);
            if (level <= 3)
            {
                return WorkBoxBGTexAwful;
            }

            if (level <= 7)
            {
                return WorkBoxBGTexBad;
            }

            if (level <= 13)
            {
                return WorkBoxBGTexMid;
            }

            return WorkBoxBGTexExcellent ?? WorkBoxBGTexMid;
        }

        private static Texture2D WorkBoxBGTexAwful
        {
            get
            {
                return GetTexture(WorkBoxBGTexAwfulField) ?? WorkBoxBGTexBad;
            }
        }

        private static FieldInfo FindTextureField(string name)
        {
            return typeof(WidgetsWork).GetField(name, TextureFieldFlags);
        }

        private static Texture2D GetTexture(FieldInfo field)
        {
            if (field == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(null) as Texture2D;
            }
            catch
            {
                return null;
            }
        }
    }
}
