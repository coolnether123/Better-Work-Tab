using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
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

        public bool Apply()
        {
            if (!TryValidate(out string error))
            {
                Log.Warning("[BWT] Skipping invalid legacy workload entry: " + error);
                return false;
            }

            return Paste();
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
        bool Paste()
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                Log.Warning(
                    "[BWT] Skipping legacy workload entry because Better Work Tab does not currently own coherent priority authority.");
                return false;
            }

            foreach (var kvp in Priorities)
            {
                WorkTypeDef worktype = kvp.Key;
                int priority = kvp.Value;
                if (worktype != null && owningPawn != null && owningPawn.workSettings != null &&
                    !owningPawn.WorkTypeIsDisabled(worktype))
                {
                    if (!WorkPrioritySystem.SetPriority(owningPawn.workSettings, worktype, priority) ||
                        !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
                    {
                        Log.Warning(
                            "[BWT] Stopped legacy workload entry because priority authority changed during apply.");
                        return false;
                    }

                    BetterWorkTabMod.DebugLog("Set " + owningPawn.Name + " " + worktype.defName + " to " + priority, DebugFeature.Workloads);
                }
            }

            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref owningPawn, "owningPawn");
            Scribe_Collections.Look(ref Priorities, "Priorities");
        }
    }
}
