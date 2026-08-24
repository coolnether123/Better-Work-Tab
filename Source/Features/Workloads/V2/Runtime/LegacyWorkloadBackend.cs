using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.Workloads;
using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>
    /// Compatibility backend for the 1.0.5 Worklist implementation. It reads
    /// the existing component state, but every mutation goes through the
    /// component's existing methods so legacy behavior remains centralized.
    /// </summary>
    internal sealed class LegacyWorkloadBackend
    {
        private const string LegacyIdPrefix = "legacy:";
        private readonly GameComponent_BWTWorldSettings _component;

        internal LegacyWorkloadBackend(GameComponent_BWTWorldSettings component)
        {
            _component = component;
        }

        internal IReadOnlyList<WorkloadDescriptor> List()
        {
            var result = new List<WorkloadDescriptor>();
            if (_component?.SavedWorklists == null) return result;

            for (int i = 0; i < _component.SavedWorklists.Count; i++)
            {
                Worklist worklist = _component.SavedWorklists[i];
                if (worklist == null) continue;
                result.Add(ToDescriptor(worklist, worklist == _component.CurrentWorklist));
            }

            return result;
        }

        internal WorkloadOperationResult<WorkloadDescriptor> Current()
        {
            if (_component == null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no Better Work Tab world component.");
            }

            if (_component.CurrentWorklist == null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.MissingCurrentWorkloadId,
                    "There is no current legacy workload.");
            }

            return WorkloadOperationResult<WorkloadDescriptor>.Ok(
                ToDescriptor(_component.CurrentWorklist, true));
        }

        internal WorkloadOperationResult Select(string workloadId)
        {
            WorkloadOperationResult<Worklist> found = Find(workloadId);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult.Fail(found.Code, found.Message);
            }

            _component.SelectWorklist(found.Value);
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult<WorkloadDescriptor> Create(string label)
        {
            WorkloadOperationResult context = RequireMap();
            if (!context.Succeeded) return WorkloadOperationResult<WorkloadDescriptor>.Fail(context.Code, context.Message);

            _component.CreateWorklist(label);
            return Current();
        }

        internal WorkloadOperationResult Rename(string workloadId, string newLabel)
        {
            if (string.IsNullOrEmpty(newLabel))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidLabel,
                    "A workload label is required.");
            }

            WorkloadOperationResult<Worklist> found = Find(workloadId);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult.Fail(found.Code, found.Message);
            }

            _component.RenameWorklist(found.Value, newLabel);
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult Delete(string workloadId)
        {
            WorkloadOperationResult<Worklist> found = Find(workloadId);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult.Fail(found.Code, found.Message);
            }

            _component.DeleteWorklist(found.Value);
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult Apply(string workloadId = null)
        {
            Worklist worklist;
            if (string.IsNullOrEmpty(workloadId))
            {
                WorkloadOperationResult<WorkloadDescriptor> current = Current();
                if (!current.Succeeded)
                {
                    return WorkloadOperationResult.Fail(current.Code, current.Message);
                }

                worklist = _component.CurrentWorklist;
            }
            else
            {
                WorkloadOperationResult<Worklist> found = Find(workloadId);
                if (!found.Succeeded)
                {
                    return WorkloadOperationResult.Fail(found.Code, found.Message);
                }

                worklist = found.Value;
            }

            string validationError = string.Empty;
            WorkTabApplicationResult result = default;
            try
            {
                if (worklist == null || !worklist.TryApplyAtomicMutation(
                        out result,
                        out validationError))
                {
                    return WorkloadOperationResult.Fail(
                        HasAuthorityFailure(validationError)
                            ? WorkloadDiagnosticCode.ExternalPriorityAuthority
                            : WorkloadDiagnosticCode.InvalidState,
                        "The legacy workload could not be applied atomically: " +
                        (validationError ?? result.Reason ?? "the workload record is missing."));
                }

                return WorkloadOperationResult.Ok();
            }
            catch (Exception exception)
            {
                Log.Error("[BWT] Legacy workload apply failed safely: " + exception);
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The legacy workload could not be applied safely: " + exception.Message);
            }
        }

        private WorkloadOperationResult<Worklist> Find(string workloadId)
        {
            if (string.IsNullOrEmpty(workloadId))
            {
                return WorkloadOperationResult<Worklist>.Fail(
                    WorkloadDiagnosticCode.MissingStableId,
                    "A legacy workload identifier is required.");
            }

            Worklist match = null;
            List<Worklist> worklists = _component?.SavedWorklists;
            if (worklists != null)
            {
                for (int i = 0; i < worklists.Count; i++)
                {
                    Worklist candidate = worklists[i];
                    if (candidate == null || GetId(candidate) != workloadId) continue;
                    if (match != null)
                    {
                        return WorkloadOperationResult<Worklist>.Fail(
                            WorkloadDiagnosticCode.AmbiguousStableId,
                            "The legacy workload identifier matches more than one workload.");
                    }

                    match = candidate;
                }
            }

            return match == null
                ? WorkloadOperationResult<Worklist>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId,
                    "The legacy workload identifier was not found.")
                : WorkloadOperationResult<Worklist>.Ok(match);
        }

        private WorkloadOperationResult RequireMap()
        {
            if (_component == null)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no Better Work Tab world component.");
            }

            return Verse.Find.CurrentMap == null
                ? WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentMap,
                    "A current map is required for legacy workload creation.")
                : WorkloadOperationResult.Ok();
        }

        private static bool HasAuthorityFailure(string reason)
        {
            return reason?.IndexOf("authority", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static WorkloadDescriptor ToDescriptor(Worklist worklist, bool isCurrent)
        {
            string label = worklist?.RenamableLabel ?? string.Empty;
            return new WorkloadDescriptor(
                WorkloadBackendMode.Legacy,
                GetId(worklist),
                label,
                isCurrent,
                false,
                false,
                0);
        }

        private static string GetId(Worklist worklist)
        {
            return LegacyIdPrefix + (worklist?.RenamableLabel ?? string.Empty);
        }
    }
}
