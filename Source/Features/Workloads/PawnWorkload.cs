using HarmonyLib;
using System;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
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
            var changes = new List<PriorityChange>();
            AppendPriorityChanges(changes);
            PriorityCommandRouter.ApplyPriorityChanges(changes);
        }


        void Copy()
        {
            if(Priorities == null)
                Priorities = new Dictionary<WorkTypeDef, int>();
            //Log.Message("----------Saving Values-------");
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                WorkTypeDef worktype = w;
                int priority = owningPawn.workSettings.GetPriority(w);
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
        internal void AppendPriorityChanges(List<PriorityChange> changes)
        {
            if (changes == null)
                return;

            foreach(var kvp in Priorities)
            {
                WorkTypeDef worktype = kvp.Key;
                int priority = kvp.Value;
                if (!owningPawn.WorkTypeIsDisabled(worktype))
                {
                    changes.Add(PriorityCommandService.CreateChange(owningPawn, worktype, priority));
                    BetterWorkTabMod.DebugLog("Set " + owningPawn.Name + " " + worktype.defName + " to " + priority, DebugFeature.Workloads);
                }
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref owningPawn, "owningPawn");
            Scribe_Collections.Look(ref Priorities, "Priorities");
        }
    }
}
