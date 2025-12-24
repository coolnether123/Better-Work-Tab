using Better_Work_Tab.Patches;
using HarmonyLib;
using RimWorld;
using Spine.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.ColorOfPriority))]
    internal class Patch_WidgetsWork_ColorOfPriority
    {
        // Patch WidgetsWork.ColorOfPriority(int priority) to support priorities greater than 4
        // line number: 54 in WidgetsWork.cs

        [HarmonyPostfix]
        public static void Postfix(ref Color __result, int prio)
        {
            if(prio == 0)
            {
                __result = Color.grey;
                return;
            }

            prio = (int)SpineUtils.Remap(prio, 1, BetterWorkTabMod.Settings.maxPriorityInt, 1, 4);
            switch (prio)
            {
                case 1:
                    __result = new Color(0f, 1f, 0f);
                    break;
                case 2:
                    __result = new Color(1f, 0.9f, 0.5f);
                    break;
                case 3:
                    __result = new Color(0.8f, 0.7f, 0.5f);
                    break;
                case 4:
                    __result = new Color(0.74f, 0.74f, 0.74f);
                    break;
                default:
                    __result = Color.grey;
                    break;
            }
        }
    }


    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker))]
    public static class Patch_WidgetsWork_TipForPawnWorker
    {
        // Patch WidgetsWork.TipForPawnWorker(Pawn pawn, WorkTypeDef workType) to support priorities greater than 4
        // line number: 180 in WidgetsWork.cs
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            //init the variable to hold the index of the opcode we want to start from
            var priorityStrIndex = -1;

            //convert the instructions to a list for easy manipulation
            var codes = new List<CodeInstruction>(instructions);

            //find the index of the opcode that loads the "Priority" string by checking each instruction's operand
            for (var i = 0; i < codes.Count; i++)
            {
                // Identify the next return instruction
                var strOperand = codes[i].operand as string;
                if (strOperand == "Priority")
                {
                    priorityStrIndex = i;
                    //Found it, so exit the loop
                    break;
                }

            }

            //If we found the index, we can now insert our new instructions
            if (priorityStrIndex > -1)
            {
                //adjust to get the index of where exactly we want to insert our new instructions
                var findPriorityOpcodeIndex = priorityStrIndex - 3;
                //setup the method we want to call to remap the priority value
                var remapMethod = AccessTools.Method(typeof(Patch_WidgetsWork_TipForPawnWorker), nameof(RemapPriority));

                //load the local variable so it can be used as an argument for our remap method
                codes.Insert(findPriorityOpcodeIndex + 1, new CodeInstruction(OpCodes.Ldloc_2));
                //call our remap method
                codes.Insert(findPriorityOpcodeIndex + 2, new CodeInstruction(OpCodes.Call, remapMethod));
                //store the result back into the local variable for RimWorld to use
                codes.Insert(findPriorityOpcodeIndex + 3, new CodeInstruction(OpCodes.Stloc_2));
            }
            //Return the modified instructions as an enumerable
            return codes.AsEnumerable();
        }

        // Remap the priority value from the extended range back to 0-4 for display purposes
        private static int RemapPriority(int priority)
        {
            // If priority is 0, return 0 directly because it means no work assigned
            if (priority == 0)
            {
                return 0;
            }
            //otherwise, remap the priority to 1-4
            return (int)SpineUtils.Remap(priority, 1, BetterWorkTabMod.Settings.maxPriorityInt, 1, 4);
        }

    }

    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
    public static class Patch_WidgetsWork_DrawWorkBoxFor
    {
        // Transpile WidgetsWork.DrawWorkBoxFor() to support priorities greater than 4
        // line number: 71 in WidgetsWork.cs

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            //init the variable to hold the index of the opcode we want to start from
            int[] getPriorityMethodIndexes = [-1,-1];

            //convert the instructions to a list for easy manipulation
            var codes = new List<CodeInstruction>(instructions);

            //find the index of the opcode we want to start from by checking each instruction's operand
            for (var i = 0; i < codes.Count; i++)
            {
                var operand = codes[i].operand as MethodInfo;
                if (operand == AccessTools.PropertyGetter(typeof(Event), "button"))
                {
                    if (getPriorityMethodIndexes[0] == -1)
                    {
                        Log.Message("found at: " + i.ToString());
                        getPriorityMethodIndexes[0] = i;
                    }
                    else
                    {
                        Log.Message("found second get priority method at index: " + i.ToString());
                        getPriorityMethodIndexes[1] = i;
                        break;
                        //Found both, so exit the loop
                    }
                }
            }
            if (getPriorityMethodIndexes[0] > -1 && getPriorityMethodIndexes[1] > -1)
            {
                Log.Message("first result: " + getPriorityMethodIndexes[0].ToString());
                Log.Message("second result: " + getPriorityMethodIndexes[1].ToString());

                int firstOpcodeIndex = getPriorityMethodIndexes[0] + 12;

                Log.Message("first result opcode before: " + codes[firstOpcodeIndex].ToString());
                Log.Message("maxPriorityInt: " + BetterWorkTabMod.Settings.maxPriorityInt.ToString());

                codes[firstOpcodeIndex] = new CodeInstruction(OpCodes.Ldc_I4_S, BetterWorkTabMod.Settings.maxPriorityInt);
                Log.Message("first result opcode after: " + codes[firstOpcodeIndex].ToString());

                codes[getPriorityMethodIndexes[1] + 11] = new CodeInstruction(OpCodes.Ldc_I4_S, BetterWorkTabMod.Settings.maxPriorityInt);

            }


            return codes.AsEnumerable();
        }

    }


    // Transpile PawnColumnWorker_WorkPriority.HeaderClicked(Rect headerRect, PawnTable table) to support priorities greater than 4
    // line number: 178 in PawnColumnWorker_WorkPriority.cs

    // Transpile Pawn_WorkSettings.SetPriority(WorkTypeDef w, int priority) to support priorities greater than 4
    // line number: 160 in Pawn_WorkSettings.cs



}
