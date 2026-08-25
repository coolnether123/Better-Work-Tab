using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
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

        internal Pawn OwningPawn => owningPawn;

        /// <summary>
        /// Checks every reference that Paste would dereference. Legacy workload
        /// files contain live object references, so a pawn that left the save,
        /// a missing priority dictionary, or a stale WorkTypeDef must make the
        /// whole operation fail closed instead of throwing halfway through an
        /// apply.
        /// </summary>
        internal bool TryValidate(out string error)
        {
            error = string.Empty;
            if (owningPawn == null)
            {
                error = "A legacy workload entry has no owning pawn.";
                return false;
            }

            if (owningPawn.Dead || owningPawn.Destroyed)
            {
                error = "A legacy workload entry refers to a dead or destroyed pawn.";
                return false;
            }

            IEnumerable<Pawn> allPawns;
            try
            {
                allPawns = PawnsFinder.All_AliveOrDead;
            }
            catch (Exception exception)
            {
                error = "The current save could not resolve legacy workload pawns: " + exception.Message;
                return false;
            }

            if (allPawns == null || !allPawns.Contains(owningPawn))
            {
                error = "A legacy workload entry refers to a pawn that is no longer in the current save.";
                return false;
            }

            if (owningPawn.workSettings == null)
            {
                error = "A legacy workload entry refers to a pawn without work settings.";
                return false;
            }

            if (Priorities == null)
            {
                error = "A legacy workload entry has no priority data.";
                return false;
            }

            foreach (KeyValuePair<WorkTypeDef, int> entry in Priorities)
            {
                if (entry.Key == null)
                {
                    error = "A legacy workload entry contains a missing WorkTypeDef reference.";
                    return false;
                }
            }

            return true;
        }

        internal IEnumerable<WorkTypeDef> GetPriorityWorkTypes()
        {
            return Priorities?.Keys ?? Enumerable.Empty<WorkTypeDef>();
        }

        /// <summary>
        /// Adds this entry's desired parent priorities to a pre-captured
        /// application plan. The plan captures every baseline through
        /// the priority domain before this method is called.
        /// </summary>
        internal bool TryCompileParentPriorities(
            WorkTabAtomicMutationPlan mutation,
            out string error)
        {
            error = string.Empty;
            if (mutation == null)
            {
                error = "The legacy workload has no atomic mutation plan.";
                return false;
            }

            foreach (KeyValuePair<WorkTypeDef, int> entry in Priorities
                         .OrderBy(pair => pair.Key.defName, StringComparer.Ordinal))
            {
                WorkTypeDef workType = entry.Key;
                if (owningPawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                if (!mutation.SetPriority(owningPawn, workType, entry.Value))
                {
                    error = "The legacy workload could not compile a parent-priority target.";
                    return false;
                }
            }

            return true;
        }


        void Copy()
        {
            if(Priorities == null)
                Priorities = new Dictionary<WorkTypeDef, int>();
            //Log.Message("----------Saving Values-------");
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                WorkTypeDef worktype = w;
                int priority = WorkTabDomainPorts.Priority.ReadStored(owningPawn, w);
                //Log.Message("Saved " + owningPawn.Name + "'s " + worktype.defName + " priority of " + priority);
                Priorities[worktype] = priority;
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
        public void ExposeData()
        {
            Scribe_References.Look(ref owningPawn, "owningPawn");
            Scribe_Collections.Look(ref Priorities, "Priorities");
        }
    }
}
