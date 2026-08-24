using System;
using System.Collections.Generic;
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
    /// Resolution of one exact effective-state layer. NoOpinion means that the
    /// layer has no value for the target; Clear is an explicit tombstone for
    /// that layer and must never be collapsed into NoOpinion by a cache.
    /// </summary>
    public enum WorkTabEffectiveStateResolutionState
    {
        NoOpinion = 0,
        Set = 1,
        Clear = 2
    }

    public readonly struct WorkTabEffectiveStateResolution<T> : IEquatable<WorkTabEffectiveStateResolution<T>>
    {
        private WorkTabEffectiveStateResolution(
            WorkTabEffectiveStateResolutionState state,
            T value)
        {
            State = state;
            Value = value;
        }

        public WorkTabEffectiveStateResolutionState State { get; }
        public T Value { get; }
        public bool IsNoOpinion => State == WorkTabEffectiveStateResolutionState.NoOpinion;
        public bool IsSet => State == WorkTabEffectiveStateResolutionState.Set;
        public bool IsClear => State == WorkTabEffectiveStateResolutionState.Clear;

        public static WorkTabEffectiveStateResolution<T> NoOpinion =>
            new WorkTabEffectiveStateResolution<T>(
                WorkTabEffectiveStateResolutionState.NoOpinion,
                default(T));

        public static WorkTabEffectiveStateResolution<T> Clear =>
            new WorkTabEffectiveStateResolution<T>(
                WorkTabEffectiveStateResolutionState.Clear,
                default(T));

        public static WorkTabEffectiveStateResolution<T> Set(T value) =>
            new WorkTabEffectiveStateResolution<T>(
                WorkTabEffectiveStateResolutionState.Set,
                value);

        public bool Equals(WorkTabEffectiveStateResolution<T> other)
        {
            return State == other.State &&
                   EqualityComparer<T>.Default.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkTabEffectiveStateResolution<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)State * 397) ^
                       EqualityComparer<T>.Default.GetHashCode(Value);
            }
        }

        public override string ToString()
        {
            object boxed = Value;
            return State + (IsSet ? ":" + (boxed == null ? string.Empty : boxed.ToString()) : string.Empty);
        }
    }

    /// <summary>
    /// A resolver used by the live adapter. Returning false means that the
    /// adapter has no value for the key and lets the provider's fallback win.
    /// </summary>
    public delegate bool WorkTabEffectiveStateResolver<TKey, TValue>(TKey key, out TValue value);

    /// <summary>
    /// Revision vector consumed by effective-state and optimized-renderer
    /// cache keys. ProviderGeneration identifies replacement of an otherwise
    /// similarly named provider; the remaining fields identify the live or
    /// session-local inputs that can change a projected read.
    /// </summary>
    public readonly struct WorkTabEffectiveStateRevisionVector : IEquatable<WorkTabEffectiveStateRevisionVector>
    {
        public WorkTabEffectiveStateRevisionVector(
            long providerGeneration,
            long sourceRevision,
            long sessionRevision,
            long persistenceRevision,
            long authorityRevision,
            long scheduleRevision,
            long specificRevision,
            long settingsRevision,
            long membershipRevision)
        {
            ProviderGeneration = providerGeneration;
            SourceRevision = sourceRevision;
            SessionRevision = sessionRevision;
            PersistenceRevision = persistenceRevision;
            AuthorityRevision = authorityRevision;
            ScheduleRevision = scheduleRevision;
            SpecificRevision = specificRevision;
            SettingsRevision = settingsRevision;
            MembershipRevision = membershipRevision;
        }

        public long ProviderGeneration { get; }
        public long SourceRevision { get; }
        public long SessionRevision { get; }
        public long PersistenceRevision { get; }
        public long AuthorityRevision { get; }
        public long ScheduleRevision { get; }
        public long SpecificRevision { get; }
        public long SettingsRevision { get; }
        public long MembershipRevision { get; }

        public static WorkTabEffectiveStateRevisionVector FromRevision(long revision)
        {
            return new WorkTabEffectiveStateRevisionVector(
                0L,
                revision,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L);
        }

        public bool Equals(WorkTabEffectiveStateRevisionVector other)
        {
            return ProviderGeneration == other.ProviderGeneration &&
                   SourceRevision == other.SourceRevision &&
                   SessionRevision == other.SessionRevision &&
                   PersistenceRevision == other.PersistenceRevision &&
                   AuthorityRevision == other.AuthorityRevision &&
                   ScheduleRevision == other.ScheduleRevision &&
                   SpecificRevision == other.SpecificRevision &&
                   SettingsRevision == other.SettingsRevision &&
                   MembershipRevision == other.MembershipRevision;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkTabEffectiveStateRevisionVector other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ProviderGeneration.GetHashCode();
                hash = (hash * 397) ^ SourceRevision.GetHashCode();
                hash = (hash * 397) ^ SessionRevision.GetHashCode();
                hash = (hash * 397) ^ PersistenceRevision.GetHashCode();
                hash = (hash * 397) ^ AuthorityRevision.GetHashCode();
                hash = (hash * 397) ^ ScheduleRevision.GetHashCode();
                hash = (hash * 397) ^ SpecificRevision.GetHashCode();
                hash = (hash * 397) ^ SettingsRevision.GetHashCode();
                return (hash * 397) ^ MembershipRevision.GetHashCode();
            }
        }

        public override string ToString()
        {
            return ProviderGeneration + "/" + SourceRevision + "/" +
                   SessionRevision + "/" + PersistenceRevision + "/" +
                   AuthorityRevision + "/" + ScheduleRevision + "/" +
                   SpecificRevision + "/" + SettingsRevision + "/" +
                   MembershipRevision;
        }
    }

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
            : this(providerId, revision, source, WorkTabEffectiveStateRevisionVector.FromRevision(revision))
        {
        }

        public WorkTabEffectiveStateRevision(
            string providerId,
            long revision,
            WorkTabEffectiveStateSource source,
            WorkTabEffectiveStateRevisionVector revisionVector)
        {
            ProviderId = providerId ?? string.Empty;
            Revision = revision;
            Source = source;
            RevisionVector = revisionVector;
        }

        public string ProviderId { get; }
        public long Revision { get; }
        public WorkTabEffectiveStateSource Source { get; }
        public WorkTabEffectiveStateRevisionVector RevisionVector { get; }
        public bool IsLive => Source == WorkTabEffectiveStateSource.Live;
        public bool IsPreview => Source == WorkTabEffectiveStateSource.Preview;

        public bool IsCurrent(IWorkTabEffectiveStateProvider provider)
        {
            return provider != null &&
                   StringComparer.Ordinal.Equals(ProviderId, provider.ProviderId) &&
                   Revision == provider.Revision &&
                   Source == provider.Source &&
                   RevisionVector.Equals(
                       provider is IWorkTabEffectiveStateV2Provider v2
                           ? v2.RevisionVector
                           : WorkTabEffectiveStateRevisionVector.FromRevision(provider.Revision));
        }

        public bool IsStale(IWorkTabEffectiveStateProvider provider)
        {
            return !IsCurrent(provider);
        }

        public bool Equals(WorkTabEffectiveStateRevision other)
        {
            return Revision == other.Revision &&
                   Source == other.Source &&
                   StringComparer.Ordinal.Equals(ProviderId, other.ProviderId) &&
                   RevisionVector.Equals(other.RevisionVector);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkTabEffectiveStateRevision other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ((StringComparer.Ordinal.GetHashCode(ProviderId ?? string.Empty) * 397) ^
                            Revision.GetHashCode()) * 397 ^ (int)Source;
                hash = (hash * 397) ^ RevisionVector.GetHashCode();
                return hash;
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
            return ProviderId + "@" + Revision + ":" + Source + "[" + RevisionVector + "]";
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
    /// V2 projection surface. Resolution methods report the exact layer owned
    /// by the provider. Consumers that need an effective value should use the
    /// provider's legacy Get/TryGet methods or the runtime adapters, which
    /// compose Clear by continuing to the canonical lower-precedence layer.
    /// </summary>
    public interface IWorkTabEffectiveStateV2Provider
    {
        WorkTabEffectiveStateRevisionVector RevisionVector { get; }

        WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveSchedule(WorkloadScheduleTargetKey key);
        WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> ResolveSpecificJobPriority(WorkloadSpecificJobTargetKey key);
        WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveWorkTypeOrder(WorkloadWorkTypeOrderKey key);
        WorkTabEffectiveStateResolution<WorkloadSettingValue> ResolvePresentationSetting(string key);
    }

    /// <summary>
    /// Typed preview writes used by schedule, specific-job, and settings
    /// adapters. The older editor remains available for existing callers.
    /// </summary>
    public interface IWorkTabEffectiveStateV2Editor
    {
        WorkTabEffectiveStateMutationResult SetSchedule(
            WorkloadScheduleTargetKey key,
            WorkloadSchedulePayload payload);

        WorkTabEffectiveStateMutationResult ClearSchedule(WorkloadScheduleTargetKey key);

        WorkTabEffectiveStateMutationResult SetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            WorkloadSpecificPriorityPayload payload);

        WorkTabEffectiveStateMutationResult ClearSpecificJobPriority(WorkloadSpecificJobTargetKey key);

        WorkTabEffectiveStateMutationResult SetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            WorkloadWorkTypeOrderPayload payload);

        WorkTabEffectiveStateMutationResult ClearWorkTypeOrder(WorkloadWorkTypeOrderKey key);

        WorkTabEffectiveStateMutationResult SetPresentationSetting(
            string key,
            WorkloadSettingValue value);

        WorkTabEffectiveStateMutationResult ClearPresentationSettingV2(string key);
    }

    /// <summary>
    /// Describes virtual writes against an effective-state source. An editor may
    /// be backed by live callbacks or by a draft; callers must inspect the
    /// status instead of assuming that a requested write happened.
    /// </summary>
    public interface IWorkTabEffectiveStateEditor
    {
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
