using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Properties;
using RimWorld;
using Verse;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.PawnOrganizer.Data;

namespace Better_Work_Tab.Features.Workloads
{
    public class Worklist : IExposable, IRenameable
    {
        public Worklist(string name)
        {
            RenamableLabel = name;
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                PawnWorklists.Add(new PawnWorkload(pawn));
            }
            UseAdvancedMode = ParentPriorityRead.GetLiveManualMode(true);
        }

        public Worklist()
        {
        }

        public bool UseAdvancedMode = true;

        public List<PawnWorkload> PawnWorklists = new List<PawnWorkload>();
        public string worklistName = "New Worklist";

        public List<PawnDivider> Dividers = new List<PawnDivider>();

        public string RenamableLabel { get => worklistName; set => worklistName = value; }

        public string BaseLabel { get; }

        public string InspectLabel { get; }

        public void Apply()
        {
            if (!TryApplyAtomicMutation(out WorkTabApplicationResult result, out string error))
            {
                Log.Warning("[BWT] Skipping legacy workload because its atomic mutation was rejected: " +
                    (error ?? result.Reason ?? "no reason was provided."));
            }
        }

        internal bool TryApplyAtomicMutation(
            out WorkTabApplicationResult result,
            out string error)
        {
            if (!TryBuildAtomicMutationPlan(out WorkTabAtomicMutationPlan mutation, out error))
            {
                result = WorkTabApplicationResult.Rejected(error, default);
                return false;
            }

            result = WorkTabApplication.Current?.ApplyAtomicMutationPlan(mutation) ??
                WorkTabApplicationResult.Rejected(
                    "The work-tab application service is unavailable.",
                    default);
            error = result.Reason;
            return result.Accepted;
        }

        /// <summary>
        /// Compiles the saved legacy values into the same atomic application
        /// plan used by rules, imports, and modern workloads. No live state is
        /// written while compiling.
        /// </summary>
        internal bool TryBuildAtomicMutationPlan(
            out WorkTabAtomicMutationPlan mutation,
            out string error)
        {
            mutation = null;
            if (!TryValidate(out error))
            {
                return false;
            }

            List<PawnWorkload> orderedWorkloads = PawnWorklists
                .OrderBy(workload => workload.OwningPawn.thingIDNumber)
                .ToList();
            List<WorkTypeDef> workTypes = orderedWorkloads
                .SelectMany(workload => workload.GetPriorityWorkTypes())
                .Distinct()
                .OrderBy(workType => workType.defName, StringComparer.Ordinal)
                .ToList();

            mutation = WorkTabAtomicMutationPlan.Capture(
                orderedWorkloads.Select(workload => workload.OwningPawn),
                workTypes);
            mutation.ManualPrioritiesTarget = UseAdvancedMode;

            foreach (PawnWorkload workload in orderedWorkloads)
            {
                if (!workload.TryCompileParentPriorities(mutation, out error))
                {
                    mutation = null;
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Validates the complete legacy object graph immediately before it is
        /// applied. This is intentionally runtime validation: old saves can
        /// contain pawn references that were valid when saved but no longer
        /// resolve in the current game.
        /// </summary>
        public bool TryValidate(out string error)
        {
            error = string.Empty;
            if (PawnWorklists == null)
            {
                error = "The legacy workload has no pawn entries.";
                return false;
            }

            var seenPawns = new HashSet<Pawn>();
            for (int i = 0; i < PawnWorklists.Count; i++)
            {
                PawnWorkload workload = PawnWorklists[i];
                if (workload == null)
                {
                    error = "The legacy workload contains a missing pawn entry.";
                    return false;
                }

                if (workload.OwningPawn == null || !seenPawns.Add(workload.OwningPawn))
                {
                    error = "The legacy workload contains a missing or duplicate pawn entry.";
                    return false;
                }

                if (!workload.TryValidate(out string workloadError))
                {
                    error = workloadError;
                    return false;
                }
            }

            return true;
        }

        public void ExposeData()
        {
            RenamableLabel = worklistName;
            Scribe_Values.Look(ref worklistName, "worklistName", "New Worklist");
            Scribe_Values.Look(ref UseAdvancedMode, "UseAdvancedMode", true);
            worklistName = RenamableLabel;
            Scribe_Collections.Look(ref PawnWorklists, "pawnWorklists", LookMode.Deep);
            Scribe_Collections.Look(ref Dividers, "Dividers", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
            }
        }

        public void Rename(string newLabel)
        {
            if (!string.IsNullOrEmpty(newLabel))
            {
                RenamableLabel = newLabel;
            }
        }

        /// <summary>
        /// Guarantees that collection fields are non-null after load/migration.
        /// </summary>
        public void EnsureCollections()
        {
            if (PawnWorklists == null)
            {
                PawnWorklists = new List<PawnWorkload>();
            }

            if (Dividers == null)
            {
                Dividers = new List<PawnDivider>();
            }
        }
    }
}
