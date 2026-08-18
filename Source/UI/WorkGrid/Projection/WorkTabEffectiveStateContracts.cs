using System;
using Better_Work_Tab.Features.Workloads.V2;

namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    /// <summary>
    /// Identifies whether a value came from the authoritative game state or from a
    /// session-local projection. The distinction is intentionally explicit so a
    /// preview can never be mistaken for a live mutation by a cache consumer.
    /// </summary>
    public enum WorkTabEffectiveStateSource
    {
        Live = 0,
        Preview = 1
    }

    public enum WorkTabEffectiveStateDimension
    {
        ParentPriority = 0,
        ManualMode = 1,
        Schedule = 2,
        SpecificJobOverride = 3,
        SpecificJobOrder = 4,
        PresentationSetting = 5
    }

    public enum WorkTabEffectiveStateMutationStatus
    {
        Accepted = 0,
        NoOp = 1,
        Blocked = 2
    }

    /// <summary>
    /// A resolver used by the live adapter. Returning false means that the
    /// adapter has no value for the key and lets the provider's fallback win.
    /// </summary>
    public delegate bool WorkTabEffectiveStateResolver<TKey, TValue>(TKey key, out TValue value);

    /// <summary>
    /// Small immutable stamp for snapshot and cache consumers. A preview edit
    /// changes this stamp on its projected provider only; it does not call the
    /// live WorkTab invalidation hub.
    /// </summary>
    public readonly struct WorkTabEffectiveStateRevision : IEquatable<WorkTabEffectiveStateRevision>
    {
        public WorkTabEffectiveStateRevision(
            string providerId,
            long revision,
            WorkTabEffectiveStateSource source)
        {
            ProviderId = providerId ?? string.Empty;
            Revision = revision;
            Source = source;
        }

        public string ProviderId { get; }
        public long Revision { get; }
        public WorkTabEffectiveStateSource Source { get; }
        public bool IsLive => Source == WorkTabEffectiveStateSource.Live;
        public bool IsPreview => Source == WorkTabEffectiveStateSource.Preview;

        public bool IsCurrent(IWorkTabEffectiveStateProvider provider)
        {
            return provider != null &&
                   StringComparer.Ordinal.Equals(ProviderId, provider.ProviderId) &&
                   Revision == provider.Revision &&
                   Source == provider.Source;
        }

        public bool IsStale(IWorkTabEffectiveStateProvider provider)
        {
            return !IsCurrent(provider);
        }

        public bool Equals(WorkTabEffectiveStateRevision other)
        {
            return Revision == other.Revision &&
                   Source == other.Source &&
                   StringComparer.Ordinal.Equals(ProviderId, other.ProviderId);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkTabEffectiveStateRevision other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((StringComparer.Ordinal.GetHashCode(ProviderId ?? string.Empty) * 397) ^
                        Revision.GetHashCode()) * 397 ^ (int)Source;
            }
        }

        public static bool operator ==(
            WorkTabEffectiveStateRevision left,
            WorkTabEffectiveStateRevision right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            WorkTabEffectiveStateRevision left,
            WorkTabEffectiveStateRevision right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return ProviderId + "@" + Revision + ":" + Source;
        }
    }

    /// <summary>
    /// Reads the values that a Work-tab cell is allowed to observe. The core
    /// contract uses stable workload keys rather than retaining RimWorld
    /// objects, while the runtime bridge supplies object-based convenience
    /// methods for Harmony callers.
    /// </summary>
    public interface IWorkTabEffectiveStateRevisionSource
    {
        WorkTabEffectiveStateRevision RevisionToken { get; }
    }

    public interface IWorkTabEffectiveStateProvider : IWorkTabEffectiveStateRevisionSource
    {
        string ProviderId { get; }
        long Revision { get; }
        WorkTabEffectiveStateSource Source { get; }
        bool IsLive { get; }
        bool IsPreview { get; }

        int GetParentPriority(WorkloadParentPriorityKey key, int fallbackPriority);
        bool TryGetParentPriority(WorkloadParentPriorityKey key, out int priority);

        bool IsManualMode(WorkloadParentPriorityKey key, bool fallbackManualMode);
        bool TryGetManualMode(WorkloadParentPriorityKey key, out bool manualMode);

        ScheduleKey GetSchedule(PawnKey key, ScheduleKey fallbackSchedule);
        bool TryGetSchedule(PawnKey key, out ScheduleKey schedule);

        WorkloadScalarValue GetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue fallbackValue);

        bool TryGetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            out WorkloadScalarValue value);

        int GetSpecificJobOrder(WorkloadSpecificJobKey key, int fallbackOrder);
        bool TryGetSpecificJobOrder(WorkloadSpecificJobKey key, out int order);

        WorkloadScalarValue GetPresentationSetting(
            string key,
            WorkloadScalarValue fallbackValue);

        bool TryGetPresentationSetting(string key, out WorkloadScalarValue value);
    }

    /// <summary>
    /// Describes virtual writes against an effective-state source. An editor may
    /// be backed by live callbacks or by a draft; callers must inspect the
    /// status instead of assuming that a requested write happened.
    /// </summary>
    public interface IWorkTabEffectiveStateEditor
    {
        WorkTabEffectiveStateMutationResult SetParentPriority(
            WorkloadParentPriorityKey key,
            int priority);

        WorkTabEffectiveStateMutationResult ClearParentPriority(
            WorkloadParentPriorityKey key);

        WorkTabEffectiveStateMutationResult SetManualMode(
            WorkloadParentPriorityKey key,
            bool manualMode);

        WorkTabEffectiveStateMutationResult ClearManualMode(
            WorkloadParentPriorityKey key);

        WorkTabEffectiveStateMutationResult SetSchedule(
            PawnKey key,
            ScheduleKey schedule);

        WorkTabEffectiveStateMutationResult ClearSchedule(PawnKey key);

        WorkTabEffectiveStateMutationResult SetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue value);

        WorkTabEffectiveStateMutationResult ClearSpecificJobOverride(
            WorkloadSpecificJobKey key);

        WorkTabEffectiveStateMutationResult SetSpecificJobOrder(
            WorkloadSpecificJobKey key,
            int order);

        WorkTabEffectiveStateMutationResult ClearSpecificJobOrder(
            WorkloadSpecificJobKey key);

        WorkTabEffectiveStateMutationResult SetPresentationSetting(
            string key,
            WorkloadScalarValue value);

        WorkTabEffectiveStateMutationResult ClearPresentationSetting(string key);
    }

    public readonly struct WorkTabEffectiveStateMutationResult
    {
        private WorkTabEffectiveStateMutationResult(
            WorkTabEffectiveStateMutationStatus status,
            WorkTabEffectiveStateDimension dimension,
            long previousRevision,
            long revision,
            string reason)
        {
            Status = status;
            Dimension = dimension;
            PreviousRevision = previousRevision;
            Revision = revision;
            Reason = reason ?? string.Empty;
        }

        public WorkTabEffectiveStateMutationStatus Status { get; }
        public WorkTabEffectiveStateDimension Dimension { get; }
        public long PreviousRevision { get; }
        public long Revision { get; }
        public string Reason { get; }
        public bool Accepted => Status == WorkTabEffectiveStateMutationStatus.Accepted;
        public bool IsNoOp => Status == WorkTabEffectiveStateMutationStatus.NoOp;
        public bool IsBlocked => Status == WorkTabEffectiveStateMutationStatus.Blocked;
        public bool Changed => Accepted;

        public static WorkTabEffectiveStateMutationResult AcceptedChange(
            WorkTabEffectiveStateDimension dimension,
            long previousRevision,
            long revision,
            string reason = null)
        {
            return new WorkTabEffectiveStateMutationResult(
                WorkTabEffectiveStateMutationStatus.Accepted,
                dimension,
                previousRevision,
                revision,
                reason ?? "The projected value was accepted.");
        }

        public static WorkTabEffectiveStateMutationResult NoOp(
            WorkTabEffectiveStateDimension dimension,
            long revision,
            string reason = null)
        {
            return new WorkTabEffectiveStateMutationResult(
                WorkTabEffectiveStateMutationStatus.NoOp,
                dimension,
                revision,
                revision,
                reason ?? "The requested value is already effective.");
        }

        public static WorkTabEffectiveStateMutationResult Blocked(
            WorkTabEffectiveStateDimension dimension,
            long revision,
            string reason)
        {
            return new WorkTabEffectiveStateMutationResult(
                WorkTabEffectiveStateMutationStatus.Blocked,
                dimension,
                revision,
                revision,
                reason ?? "The effective-state mutation is blocked.");
        }

        public override string ToString()
        {
            return Status + " " + Dimension + " " + PreviousRevision + "->" + Revision +
                   (string.IsNullOrEmpty(Reason) ? string.Empty : ": " + Reason);
        }
    }

    public static class WorkTabEffectiveStateProviderExtensions
    {
        public static int GetManualPriority(
            this IWorkTabEffectiveStateProvider provider,
            WorkloadParentPriorityKey key,
            int fallbackPriority)
        {
            return provider == null ? fallbackPriority : provider.GetParentPriority(key, fallbackPriority);
        }

        public static int GetSpecificJobIntegerOverride(
            this IWorkTabEffectiveStateProvider provider,
            WorkloadSpecificJobKey key,
            int fallbackPriority)
        {
            WorkloadScalarValue fallback = WorkloadScalarValue.FromInteger(fallbackPriority);
            WorkloadScalarValue value = provider == null
                ? fallback
                : provider.GetSpecificJobOverride(key, fallback);
            return value.Kind == WorkloadScalarKind.Integer
                ? value.IntegerValue
                : fallbackPriority;
        }

        public static bool TryGetSpecificJobIntegerOverride(
            this IWorkTabEffectiveStateProvider provider,
            WorkloadSpecificJobKey key,
            out int priority)
        {
            priority = 0;
            if (provider == null || !provider.TryGetSpecificJobOverride(key, out WorkloadScalarValue value) ||
                value.Kind != WorkloadScalarKind.Integer)
            {
                return false;
            }

            priority = value.IntegerValue;
            return true;
        }

        public static WorkTabEffectiveStateMutationResult SetSchedule(
            this IWorkTabEffectiveStateEditor editor,
            PawnKey key,
            int scheduleId)
        {
            if (editor == null)
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    0,
                    "No effective-state editor is available.");
            }

            return editor.SetSchedule(key, new ScheduleKey(scheduleId));
        }

        public static WorkTabEffectiveStateMutationResult RemoveParentPriority(
            this IWorkTabEffectiveStateEditor editor,
            WorkloadParentPriorityKey key)
        {
            return editor == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.ParentPriority,
                    0,
                    "No effective-state editor is available.")
                : editor.ClearParentPriority(key);
        }

        public static WorkTabEffectiveStateMutationResult RemoveManualMode(
            this IWorkTabEffectiveStateEditor editor,
            WorkloadParentPriorityKey key)
        {
            return editor == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.ManualMode,
                    0,
                    "No effective-state editor is available.")
                : editor.ClearManualMode(key);
        }

        public static WorkTabEffectiveStateMutationResult RemoveSchedule(
            this IWorkTabEffectiveStateEditor editor,
            PawnKey key)
        {
            return editor == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    0,
                    "No effective-state editor is available.")
                : editor.ClearSchedule(key);
        }

        public static WorkTabEffectiveStateMutationResult RemoveSpecificJobOverride(
            this IWorkTabEffectiveStateEditor editor,
            WorkloadSpecificJobKey key)
        {
            return editor == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    0,
                    "No effective-state editor is available.")
                : editor.ClearSpecificJobOverride(key);
        }

        public static WorkTabEffectiveStateMutationResult RemoveSpecificJobOrder(
            this IWorkTabEffectiveStateEditor editor,
            WorkloadSpecificJobKey key)
        {
            return editor == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    0,
                    "No effective-state editor is available.")
                : editor.ClearSpecificJobOrder(key);
        }

        public static WorkTabEffectiveStateMutationResult RemovePresentationSetting(
            this IWorkTabEffectiveStateEditor editor,
            string key)
        {
            return editor == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.PresentationSetting,
                    0,
                    "No effective-state editor is available.")
                : editor.ClearPresentationSetting(key);
        }
    }
}
