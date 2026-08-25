using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.UI.Settings;

namespace Better_Work_Tab.UI.Workloads
{
    /// <summary>
    /// Workload-owned translation between the neutral presentation-settings
    /// boundary and the workload persistence scalar. Normal settings code
    /// never needs the workload model in order to remain independently useful.
    /// </summary>
    internal static class WorkloadPresentationSettingsAdapter
    {
        private static bool _registered;

        internal static void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            WorkloadPresentationServices.Register(
                () => new WorkloadPresentationSettingsTransaction(
                    new PresentationSettingsStore()),
                TryGetScalarKind,
                () => BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged());
            BWTWorkloadSettingsOwnershipPolicy.RegisterPresentationModeTransition(
                TryTransitionMode);
        }

        private static string TryTransitionMode(bool useLegacy, bool persist)
        {
            WorkloadOperationResult result = WorkloadModeService.TryTransition(
                useLegacy ? WorkloadBackendMode.Legacy : WorkloadBackendMode.Modern,
                persist);
            return result.Succeeded
                ? string.Empty
                : WorkloadPresentationResolver.Resolve(result);
        }

        private static bool TryGetScalarKind(
            string settingId,
            out WorkloadScalarKind kind)
        {
            kind = WorkloadScalarKind.Empty;
            if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                    settingId,
                    out BWTPresentationSettingDefinitionState state))
            {
                return false;
            }

            return TryToWorkloadKind(state.ScalarKind, out kind);
        }

        private sealed class PresentationSettingsStore : IWorkloadPresentationSettingsStore
        {
            public object Identity => BetterWorkTabMod.Settings;
            public long Revision => BWTWorkloadSettingsOwnershipPolicy.GlobalSettingsRevision;

            public bool TryRead(string settingId, out WorkloadScalarValue value)
            {
                value = WorkloadScalarValue.Empty;
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
                    BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        settingId,
                        out BWTPresentationSettingDefinitionState state) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryReadGlobalExact(
                        state,
                        settings,
                        out object exact) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryToScalar(
                        state,
                        exact,
                        false,
                        out PresentationValue neutral) &&
                    TryToWorkloadValue(neutral, out value);
            }

            public bool TryCanonicalize(
                string settingId,
                WorkloadScalarValue requested,
                out WorkloadScalarValue canonical,
                out string reason)
            {
                canonical = WorkloadScalarValue.Empty;
                reason = string.Empty;
                return TryToPresentationValue(requested, out PresentationValue neutral) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        settingId,
                        out BWTPresentationSettingDefinitionState state) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryGetCanonicalGlobalExact(
                        state,
                        neutral,
                        out object exact,
                        out reason) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryToScalar(
                        state,
                        exact,
                        false,
                        out PresentationValue normalized) &&
                    TryToWorkloadValue(normalized, out canonical);
            }

            public bool TryWrite(
                string settingId,
                WorkloadScalarValue value,
                out string reason)
            {
                reason = string.Empty;
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
                    TryToPresentationValue(value, out PresentationValue neutral) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                        settingId,
                        out BWTPresentationSettingDefinitionState state) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryGetCanonicalGlobalExact(
                        state,
                        neutral,
                        out object exact,
                        out reason) &&
                    BWTWorkloadSettingsOwnershipPolicy.TryWriteGlobalExact(
                        state,
                        settings,
                        exact,
                        out reason);
            }

            public long BeginOwnedMutation(IEnumerable<string> settingIds) =>
                BWTWorkloadSettingsOwnershipPolicy.BeginOwnedGlobalSettingsMutation(settingIds);

            public IDisposable BeginWriteLease() =>
                BWTWorkloadSettingsOwnershipPolicy.BeginAuthorizedGlobalSettingsWrite();

            public bool TryPersist(out string reason)
            {
                reason = string.Empty;
                try
                {
                    BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                    settings?.Write();
                    return settings != null;
                }
                catch (Exception exception)
                {
                    reason = exception.Message;
                    return false;
                }
            }
        }

        private static bool TryToWorkloadKind(
            PresentationValueKind source,
            out WorkloadScalarKind target)
        {
            target = (WorkloadScalarKind)(int)source;
            return source == PresentationValueKind.Boolean ||
                   source == PresentationValueKind.Integer ||
                   source == PresentationValueKind.String;
        }

        private static bool TryToPresentationValue(
            WorkloadScalarValue source,
            out PresentationValue target)
        {
            switch (source.Kind)
            {
                case WorkloadScalarKind.Boolean:
                    target = PresentationValue.FromBoolean(source.BooleanValue);
                    return true;
                case WorkloadScalarKind.Integer:
                    target = PresentationValue.FromInteger(source.IntegerValue);
                    return true;
                case WorkloadScalarKind.String:
                    target = PresentationValue.FromString(source.StringValue);
                    return true;
                default:
                    target = PresentationValue.Empty;
                    return false;
            }
        }

        private static bool TryToWorkloadValue(
            PresentationValue source,
            out WorkloadScalarValue target)
        {
            switch (source.Kind)
            {
                case PresentationValueKind.Boolean:
                    target = WorkloadScalarValue.FromBoolean(source.BooleanValue);
                    return true;
                case PresentationValueKind.Integer:
                    target = WorkloadScalarValue.FromInteger(source.IntegerValue);
                    return true;
                case PresentationValueKind.String:
                    target = WorkloadScalarValue.FromString(source.StringValue);
                    return true;
                default:
                    target = WorkloadScalarValue.Empty;
                    return false;
            }
        }
    }
}
