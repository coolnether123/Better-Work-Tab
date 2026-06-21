using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    public class PawnWorkload : IExposable
    {
        public PawnWorkload(Pawn pawn)
        {
            owningPawn = pawn;
            Copy();
        }
        public PawnWorkload() { }



        Pawn owningPawn;
        Dictionary<WorkTypeDef, int> Priorities;

        public void Apply()
        {
            Paste();
        }


        void Copy()
        {
            if(Priorities == null)
                Priorities = new Dictionary<WorkTypeDef, int>();
            //Log.Message("----------Saving Values-------");
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                WorkTypeDef worktype = w;
                int priority = Better_Work_Tab.PawnCompat.WorkSettings(owningPawn).GetPriority(w);
                //Log.Message("Saved " + owningPawn.Name + "'s " + worktype.defName + " priority of " + priority);
                Priorities.Add(worktype, priority);
                //Log.Message("Added " + owningPawn.Name + "'s " + Priorities.Last().Key + " priority of " + Priorities.Last().Value + " to Priorities");
            }
            //Log.Message("----------Stored Values-------");
            foreach (var kvp in Priorities)
            {
                WorkTypeDef worktype = kvp.Key;
                int priority = kvp.Value;
                //Log.Message("Stored " + owningPawn.Name + "'s " + worktype.defName + " priority of " + priority);
            }
        }
        void Paste()
        {
            foreach(var kvp in Priorities)
            {
                WorkTypeDef worktype = kvp.Key;
                int priority = kvp.Value;
                if (!owningPawn.WorkTypeIsDisabled(worktype))
                {
                    Better_Work_Tab.PawnCompat.WorkSettings(owningPawn).SetPriority(worktype, priority);
                    BetterWorkTabMod.DebugLog("Set " + owningPawn.Name + " " + worktype.defName + " to " + priority, DebugFeature.Workloads);
                }
            }
        }

        public void ExposeData()
        {
            Better_Work_Tab.ScribeCompat.LookReference(ref owningPawn, "owningPawn");
            Better_Work_Tab.ScribeCompat.LookCollection(ref Priorities, "Priorities", Better_Work_Tab.ScribeCompat.DefLookMode, LookMode.Value);
        }
    }
}
