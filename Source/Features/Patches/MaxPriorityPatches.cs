using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Better_Work_Tab.Features.Patches
{
    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.ColorOfPriority))]
    internal class Patch_WidgetsWork_ColorOfPriority
    {
        // Patch WidgetsWork.ColorOfPriority(int priority) to support priorities greater than 4
        // line number: 54 in WidgetsWork.cs

        [HarmonyPostfix]
        public static void ColorOfPriorityPatch(Color __result, int prio)
        {
            int numPercent = prio / BetterWorkTabMod.Settings.maxPriorityInt;

            switch (numPercent)
            {
                case < 25:
                    __result = new Color(0f, 1f, 0f);
                    break;
                case <50:
                    __result = new Color(1f, 0.9f, 0.5f);
                    break;
                case < 75:
                    __result = new Color(0.8f, 0.7f, 0.5f);
                    break;
                case <= 100:
                    __result = new Color(0.74f, 0.74f, 0.74f);
                    break;
                default:
                    __result = Color.grey;
                    break;
            }
        }
    }

        // Transpile WidgetsWork.DrawWorkBoxFor() to support priorities greater than 4
        // line number: 71 in WidgetsWork.cs

        // Transpile PawnColumnWorker_WorkPriority.HeaderClicked(Rect headerRect, PawnTable table) to support priorities greater than 4
        // line number: 178 in PawnColumnWorker_WorkPriority.cs

        // Transpile Pawn_WorkSettings.SetPriority(WorkTypeDef w, int priority) to support priorities greater than 4
        // line number: 160 in Pawn_WorkSettings.cs

}
