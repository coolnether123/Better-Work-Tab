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
        private readonly IWorkloadWorldState _component;

        internal LegacyWorkloadBackend(IWorkloadWorldState component)
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
                    WorkloadDiagnosticCode.NoCurrentGame);
            }

            if (_component.CurrentWorklist == null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.MissingCurrentWorkloadId);
            }

            return WorkloadOperationResult<WorkloadDescriptor>.Ok(
                ToDescriptor(_component.CurrentWorklist, true));
        }

        internal WorkloadOperationResult Select(string workloadId)
        {
            WorkloadOperationResult<Worklist> found = Find(workloadId);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult.Fail(found.Code, found.Context);
            }

            _component.SelectWorklist(found.Value);
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult<WorkloadDescriptor> Create(string label)
        {
            WorkloadOperationResult context = RequireMap();
            if (!context.Succeeded) return WorkloadOperationResult<WorkloadDescriptor>.Fail(context.Code, context.Context);

            _component.CreateWorklist(label);
            return Current();
        }

        internal WorkloadOperationResult Rename(string workloadId, string newLabel)
        {
            if (string.IsNullOrEmpty(newLabel))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidLabel);
            }

            WorkloadOperationResult<Worklist> found = Find(workloadId);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult.Fail(found.Code, found.Context);
            }

            _component.RenameWorklist(found.Value, newLabel);
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult Delete(string workloadId)
        {
            WorkloadOperationResult<Worklist> found = Find(workloadId);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult.Fail(found.Code, found.Context);
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
                    return WorkloadOperationResult.Fail(current.Code, current.Context);
                }

                worklist = _component.CurrentWorklist;
            }
            else
            {
                WorkloadOperationResult<Worklist> found = Find(workloadId);
                if (!found.Succeeded)
                {
                    return WorkloadOperationResult.Fail(found.Code, found.Context);
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
                            : WorkloadDiagnosticCode.InvalidState);
                }

                return WorkloadOperationResult.Ok();
            }
            catch (Exception exception)
            {
                Log.Error("[BWT] Legacy workload apply failed safely: " + exception);
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }
        }

        private WorkloadOperationResult<Worklist> Find(string workloadId)
        {
            if (string.IsNullOrEmpty(workloadId))
            {
                return WorkloadOperationResult<Worklist>.Fail(
                    WorkloadDiagnosticCode.NotFound);
            }

            Worklist match = null;
            IReadOnlyList<Worklist> worklists = _component?.SavedWorklists;
            if (worklists != null)
            {
                for (int i = 0; i < worklists.Count; i++)
                {
                    Worklist candidate = worklists[i];
                    if (candidate == null || GetId(candidate) != workloadId) continue;
                    if (match != null)
                    {
                        return WorkloadOperationResult<Worklist>.Fail(
                            WorkloadDiagnosticCode.AmbiguousStableId);
                    }

                    match = candidate;
                }
            }

            return match == null
                ? WorkloadOperationResult<Worklist>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId)
                : WorkloadOperationResult<Worklist>.Ok(match);
        }

        private WorkloadOperationResult RequireMap()
        {
            if (_component == null)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame);
            }

            return Verse.Find.CurrentMap == null
                ? WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentMap)
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
