using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Better_Work_Tab.Features.TimePriority;

namespace Better_Work_Tab.Features.Workloads.V2
{
    /// <summary>
    /// The semantic state of a workload-owned target. An absent target is
    /// equivalent to NoOpinion when persisted; Clear must remain an explicit
    /// target record because it suppresses the lower-precedence live value.
    /// </summary>
    public enum WorkloadIntentState
    {
        NoOpinion = 0,
        Set = 1,
        Clear = 2
    }

    public enum WorkloadTargetScope
    {
        PawnLocal = 0,
        GlobalShared = 1
    }

    public enum WorkloadScheduleTargetKind
    {
        ParentWorkType = 0,
        WorkGiver = 1
    }

    public enum WorkloadSettingOwnership
    {
        Global = 0,
        WorkloadOwned = 1
    }

    /// <summary>
    /// A side-effect-free typed intent carrier used by every new V2
    /// dimension. The generic value is meaningful only for Set.
    /// </summary>
    public struct WorkloadIntent<T> : IEquatable<WorkloadIntent<T>>
    {
        private readonly T _value;

        private WorkloadIntent(WorkloadIntentState state, T value)
        {
            State = state;
            _value = value;
        }

        public WorkloadIntentState State { get; private set; }
        public T Value => _value;
        public bool HasValue => State == WorkloadIntentState.Set;
        public bool IsNoOpinion => State == WorkloadIntentState.NoOpinion;
        public bool IsClear => State == WorkloadIntentState.Clear;

        public static WorkloadIntent<T> NoOpinion
        {
            get { return new WorkloadIntent<T>(WorkloadIntentState.NoOpinion, default(T)); }
        }

        public static WorkloadIntent<T> Clear
        {
            get { return new WorkloadIntent<T>(WorkloadIntentState.Clear, default(T)); }
        }

        public static WorkloadIntent<T> CreateSet(T value)
        {
            return new WorkloadIntent<T>(WorkloadIntentState.Set, value);
        }

        public string CanonicalForm
        {
            get
            {
                return ((int)State).ToString() + ":" +
                    (State == WorkloadIntentState.Set
                        ? WorkloadCanonical.Encode(ReferenceEquals(_value, null) ? string.Empty : _value.ToString())
                        : string.Empty);
            }
        }

        public bool Equals(WorkloadIntent<T> other)
        {
            return State == other.State && EqualityComparer<T>.Default.Equals(_value, other._value);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkloadIntent<T> && Equals((WorkloadIntent<T>)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)State * 397) ^ EqualityComparer<T>.Default.GetHashCode(_value);
            }
        }

        public override string ToString()
        {
            return CanonicalForm;
        }
    }

    public sealed class PawnKey : IEquatable<PawnKey>, IComparable<PawnKey>
    {
        public PawnKey(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; private set; }

        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public static bool TryCreate(string value, out PawnKey key)
        {
            key = new PawnKey(value);
            return key.IsValid;
        }

        public int CompareTo(PawnKey other)
        {
            return other == null ? 1 : StringComparer.Ordinal.Compare(Value, other.Value);
        }

        public bool Equals(PawnKey other)
        {
            return !ReferenceEquals(other, null) && StringComparer.Ordinal.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PawnKey);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }
    }

    public sealed class WorkTypeKey : IEquatable<WorkTypeKey>, IComparable<WorkTypeKey>
    {
        public WorkTypeKey(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; private set; }

        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public static bool TryCreate(string value, out WorkTypeKey key)
        {
            key = new WorkTypeKey(value);
            return key.IsValid;
        }

        public int CompareTo(WorkTypeKey other)
        {
            return other == null ? 1 : StringComparer.Ordinal.Compare(Value, other.Value);
        }

        public bool Equals(WorkTypeKey other)
        {
            return !ReferenceEquals(other, null) && StringComparer.Ordinal.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkTypeKey);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }
    }

    public sealed class WorkGiverKey : IEquatable<WorkGiverKey>, IComparable<WorkGiverKey>
    {
        public WorkGiverKey(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; private set; }

        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public static bool TryCreate(string value, out WorkGiverKey key)
        {
            key = new WorkGiverKey(value);
            return key.IsValid;
        }

        public int CompareTo(WorkGiverKey other)
        {
            return other == null ? 1 : StringComparer.Ordinal.Compare(Value, other.Value);
        }

        public bool Equals(WorkGiverKey other)
        {
            return !ReferenceEquals(other, null) && StringComparer.Ordinal.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkGiverKey);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }
    }

    public sealed class ScheduleKey : IEquatable<ScheduleKey>, IComparable<ScheduleKey>
    {
        public ScheduleKey(int value)
        {
            Value = value;
        }

        public int Value { get; private set; }

        public bool IsValid => Value >= 0;

        public static bool TryCreate(int value, out ScheduleKey key)
        {
            key = new ScheduleKey(value);
            return key.IsValid;
        }

        public int CompareTo(ScheduleKey other)
        {
            return other == null ? 1 : Value.CompareTo(other.Value);
        }

        public bool Equals(ScheduleKey other)
        {
            return !ReferenceEquals(other, null) && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ScheduleKey);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return WorkloadCanonical.Integer(Value);
        }
    }

    public sealed class WorkloadParentPriorityKey : IEquatable<WorkloadParentPriorityKey>, IComparable<WorkloadParentPriorityKey>
    {
        public WorkloadParentPriorityKey(PawnKey pawn, WorkTypeKey workType)
        {
            Pawn = pawn ?? new PawnKey(null);
            WorkType = workType ?? new WorkTypeKey(null);
        }

        public PawnKey Pawn { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public string CanonicalKey => WorkloadCanonical.Pair(Pawn.Value, WorkType.Value);
        public bool IsValid => Pawn.IsValid && WorkType.IsValid;

        public int CompareTo(WorkloadParentPriorityKey other)
        {
            if (other == null) return 1;
            int pawnComparison = Pawn.CompareTo(other.Pawn);
            return pawnComparison != 0 ? pawnComparison : WorkType.CompareTo(other.WorkType);
        }

        public bool Equals(WorkloadParentPriorityKey other)
        {
            return !ReferenceEquals(other, null) && Pawn.Equals(other.Pawn) && WorkType.Equals(other.WorkType);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadParentPriorityKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Pawn.GetHashCode() * 397) ^ WorkType.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Pawn.Value + "/" + WorkType.Value;
        }
    }

    public sealed class WorkloadSpecificJobKey : IEquatable<WorkloadSpecificJobKey>, IComparable<WorkloadSpecificJobKey>
    {
        public WorkloadSpecificJobKey(PawnKey pawn, WorkTypeKey workType, WorkGiverKey workGiver)
            : this(WorkloadTargetScope.PawnLocal, pawn, workType, workGiver)
        {
        }

        public WorkloadSpecificJobKey(
            WorkloadTargetScope scope,
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver)
        {
            Scope = scope;
            Pawn = pawn ?? new PawnKey(null);
            WorkType = workType ?? new WorkTypeKey(null);
            WorkGiver = workGiver ?? new WorkGiverKey(null);
        }

        public WorkloadSpecificJobKey(WorkloadSpecificJobTargetKey target)
            : this(
                target == null ? WorkloadTargetScope.PawnLocal : target.Scope,
                target == null ? null : target.Pawn,
                target == null ? null : target.WorkType,
                target == null ? null : target.WorkGiver)
        {
        }

        public WorkloadTargetScope Scope { get; private set; }
        public PawnKey Pawn { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public WorkGiverKey WorkGiver { get; private set; }
        public bool IsGlobal => Scope == WorkloadTargetScope.GlobalShared;
        public string CanonicalKey => IsGlobal
            ? WorkloadCanonical.Pair(WorkType.Value, WorkGiver.Value)
            : WorkloadCanonical.Triple(Pawn.Value, WorkType.Value, WorkGiver.Value);
        public string ScopedCanonicalKey =>
            WorkloadCanonical.Integer((int)Scope) + WorkloadCanonical.Encode(CanonicalKey);
        public bool IsValid =>
            WorkType.IsValid &&
            WorkGiver.IsValid &&
            (IsGlobal || Pawn.IsValid);

        public WorkloadSpecificJobTargetKey ToTargetKey()
        {
            return new WorkloadSpecificJobTargetKey(Scope, Pawn, WorkType, WorkGiver);
        }

        public int CompareTo(WorkloadSpecificJobKey other)
        {
            if (other == null) return 1;
            int scopeComparison = Scope.CompareTo(other.Scope);
            if (scopeComparison != 0) return scopeComparison;
            int pawnComparison = Pawn.CompareTo(other.Pawn);
            if (pawnComparison != 0) return pawnComparison;
            int workTypeComparison = WorkType.CompareTo(other.WorkType);
            return workTypeComparison != 0 ? workTypeComparison : WorkGiver.CompareTo(other.WorkGiver);
        }

        public bool Equals(WorkloadSpecificJobKey other)
        {
            return !ReferenceEquals(other, null)
                && Scope == other.Scope
                && Pawn.Equals(other.Pawn)
                && WorkType.Equals(other.WorkType)
                && WorkGiver.Equals(other.WorkGiver);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadSpecificJobKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Scope;
                hash = (hash * 397) ^ Pawn.GetHashCode();
                hash = (hash * 397) ^ WorkType.GetHashCode();
                return (hash * 397) ^ WorkGiver.GetHashCode();
            }
        }

        public override string ToString()
        {
            return (IsGlobal ? "global/" : "local/") + CanonicalKey;
        }
    }

    /// <summary>
    /// Typed specific-job target identity. Global/shared targets deliberately
    /// have no pawn identity; the legacy WorkloadSpecificJobKey adapter keeps
    /// an empty PawnKey only for old callers that require one.
    /// </summary>
    public sealed class WorkloadSpecificJobTargetKey : IEquatable<WorkloadSpecificJobTargetKey>, IComparable<WorkloadSpecificJobTargetKey>
    {
        public WorkloadSpecificJobTargetKey(
            WorkloadTargetScope scope,
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver)
        {
            Scope = scope;
            Pawn = pawn ?? new PawnKey(null);
            WorkType = workType ?? new WorkTypeKey(null);
            WorkGiver = workGiver ?? new WorkGiverKey(null);
        }

        public static WorkloadSpecificJobTargetKey ForPawn(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver)
        {
            return new WorkloadSpecificJobTargetKey(
                WorkloadTargetScope.PawnLocal,
                pawn,
                workType,
                workGiver);
        }

        public static WorkloadSpecificJobTargetKey Global(
            WorkTypeKey workType,
            WorkGiverKey workGiver)
        {
            return new WorkloadSpecificJobTargetKey(
                WorkloadTargetScope.GlobalShared,
                null,
                workType,
                workGiver);
        }

        public WorkloadTargetScope Scope { get; private set; }
        public PawnKey Pawn { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public WorkGiverKey WorkGiver { get; private set; }
        public bool IsGlobal => Scope == WorkloadTargetScope.GlobalShared;
        public bool IsValid =>
            WorkType.IsValid &&
            WorkGiver.IsValid &&
            (IsGlobal || Pawn.IsValid);
        public string CanonicalKey =>
            ((int)Scope).ToString() + ":" +
            WorkloadCanonical.Encode(Pawn.Value) +
            WorkloadCanonical.Encode(WorkType.Value) +
            WorkloadCanonical.Encode(WorkGiver.Value);

        public WorkloadSpecificJobKey ToLegacyKey()
        {
            return new WorkloadSpecificJobKey(Scope, Pawn, WorkType, WorkGiver);
        }

        public int CompareTo(WorkloadSpecificJobTargetKey other)
        {
            if (other == null) return 1;
            int scope = Scope.CompareTo(other.Scope);
            if (scope != 0) return scope;
            int pawn = Pawn.CompareTo(other.Pawn);
            if (pawn != 0) return pawn;
            int workType = WorkType.CompareTo(other.WorkType);
            return workType != 0 ? workType : WorkGiver.CompareTo(other.WorkGiver);
        }

        public bool Equals(WorkloadSpecificJobTargetKey other)
        {
            return !ReferenceEquals(other, null) &&
                Scope == other.Scope &&
                Pawn.Equals(other.Pawn) &&
                WorkType.Equals(other.WorkType) &&
                WorkGiver.Equals(other.WorkGiver);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadSpecificJobTargetKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Scope;
                hash = (hash * 397) ^ Pawn.GetHashCode();
                hash = (hash * 397) ^ WorkType.GetHashCode();
                return (hash * 397) ^ WorkGiver.GetHashCode();
            }
        }

        public override string ToString()
        {
            return CanonicalKey;
        }
    }

    public sealed class WorkloadWorkTypeOrderKey : IEquatable<WorkloadWorkTypeOrderKey>, IComparable<WorkloadWorkTypeOrderKey>
    {
        public WorkloadWorkTypeOrderKey(WorkloadTargetScope scope, PawnKey pawn, WorkTypeKey workType)
        {
            Scope = scope;
            Pawn = pawn ?? new PawnKey(null);
            WorkType = workType ?? new WorkTypeKey(null);
        }

        public static WorkloadWorkTypeOrderKey ForPawn(PawnKey pawn, WorkTypeKey workType)
        {
            return new WorkloadWorkTypeOrderKey(WorkloadTargetScope.PawnLocal, pawn, workType);
        }

        public static WorkloadWorkTypeOrderKey Global(WorkTypeKey workType)
        {
            return new WorkloadWorkTypeOrderKey(WorkloadTargetScope.GlobalShared, null, workType);
        }

        public WorkloadTargetScope Scope { get; private set; }
        public PawnKey Pawn { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public bool IsGlobal => Scope == WorkloadTargetScope.GlobalShared;
        public bool IsValid => WorkType.IsValid && (IsGlobal || Pawn.IsValid);
        public string CanonicalKey =>
            ((int)Scope).ToString() + ":" +
            WorkloadCanonical.Encode(Pawn.Value) +
            WorkloadCanonical.Encode(WorkType.Value);

        public int CompareTo(WorkloadWorkTypeOrderKey other)
        {
            if (other == null) return 1;
            int scope = Scope.CompareTo(other.Scope);
            if (scope != 0) return scope;
            int pawn = Pawn.CompareTo(other.Pawn);
            return pawn != 0 ? pawn : WorkType.CompareTo(other.WorkType);
        }

        public bool Equals(WorkloadWorkTypeOrderKey other)
        {
            return !ReferenceEquals(other, null) &&
                Scope == other.Scope &&
                Pawn.Equals(other.Pawn) &&
                WorkType.Equals(other.WorkType);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadWorkTypeOrderKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (((int)Scope * 397) ^ Pawn.GetHashCode()) * 397 ^ WorkType.GetHashCode();
            }
        }

        public override string ToString()
        {
            return CanonicalKey;
        }
    }

    public sealed class WorkloadScheduleTargetKey : IEquatable<WorkloadScheduleTargetKey>, IComparable<WorkloadScheduleTargetKey>
    {
        public WorkloadScheduleTargetKey(
            WorkloadTargetScope scope,
            PawnKey pawn,
            WorkloadScheduleTargetKind targetKind,
            WorkTypeKey workType,
            WorkGiverKey workGiver = null)
        {
            Scope = scope;
            Pawn = pawn ?? new PawnKey(null);
            TargetKind = targetKind;
            WorkType = workType ?? new WorkTypeKey(null);
            WorkGiver = workGiver ?? new WorkGiverKey(null);
        }

        public static WorkloadScheduleTargetKey ForParent(
            PawnKey pawn,
            WorkTypeKey workType)
        {
            return new WorkloadScheduleTargetKey(
                WorkloadTargetScope.PawnLocal,
                pawn,
                WorkloadScheduleTargetKind.ParentWorkType,
                workType);
        }

        public static WorkloadScheduleTargetKey ForWorkGiver(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver)
        {
            return new WorkloadScheduleTargetKey(
                WorkloadTargetScope.PawnLocal,
                pawn,
                WorkloadScheduleTargetKind.WorkGiver,
                workType,
                workGiver);
        }

        public static WorkloadScheduleTargetKey GlobalWorkGiver(
            WorkTypeKey workType,
            WorkGiverKey workGiver)
        {
            return new WorkloadScheduleTargetKey(
                WorkloadTargetScope.GlobalShared,
                null,
                WorkloadScheduleTargetKind.WorkGiver,
                workType,
                workGiver);
        }

        public WorkloadTargetScope Scope { get; private set; }
        public PawnKey Pawn { get; private set; }
        public WorkloadScheduleTargetKind TargetKind { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public WorkGiverKey WorkGiver { get; private set; }
        public bool IsGlobal => Scope == WorkloadTargetScope.GlobalShared;
        public bool IsValid =>
            WorkType.IsValid &&
            ((TargetKind == WorkloadScheduleTargetKind.ParentWorkType && !WorkGiver.IsValid) ||
             (TargetKind == WorkloadScheduleTargetKind.WorkGiver && WorkGiver.IsValid)) &&
            (IsGlobal
                ? TargetKind == WorkloadScheduleTargetKind.WorkGiver
                : Pawn.IsValid);
        public string CanonicalKey =>
            ((int)Scope).ToString() + ":" +
            ((int)TargetKind).ToString() + ":" +
            WorkloadCanonical.Encode(Pawn.Value) +
            WorkloadCanonical.Encode(WorkType.Value) +
            WorkloadCanonical.Encode(WorkGiver.Value);

        public int CompareTo(WorkloadScheduleTargetKey other)
        {
            if (other == null) return 1;
            int scope = Scope.CompareTo(other.Scope);
            if (scope != 0) return scope;
            int kind = TargetKind.CompareTo(other.TargetKind);
            if (kind != 0) return kind;
            int pawn = Pawn.CompareTo(other.Pawn);
            if (pawn != 0) return pawn;
            int workType = WorkType.CompareTo(other.WorkType);
            return workType != 0 ? workType : WorkGiver.CompareTo(other.WorkGiver);
        }

        public bool Equals(WorkloadScheduleTargetKey other)
        {
            return !ReferenceEquals(other, null) &&
                Scope == other.Scope &&
                Pawn.Equals(other.Pawn) &&
                TargetKind == other.TargetKind &&
                WorkType.Equals(other.WorkType) &&
                WorkGiver.Equals(other.WorkGiver);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadScheduleTargetKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Scope;
                hash = (hash * 397) ^ Pawn.GetHashCode();
                hash = (hash * 397) ^ (int)TargetKind;
                hash = (hash * 397) ^ WorkType.GetHashCode();
                return (hash * 397) ^ WorkGiver.GetHashCode();
            }
        }

        public override string ToString()
        {
            return CanonicalKey;
        }
    }

    public struct WorkloadSpecificPriorityPayload : IEquatable<WorkloadSpecificPriorityPayload>
    {
        public WorkloadSpecificPriorityPayload(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; private set; }
        public bool IsValid => Priority >= 0;
        public string CanonicalForm => WorkloadCanonical.Integer(Priority);

        public bool Equals(WorkloadSpecificPriorityPayload other)
        {
            return Priority == other.Priority;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkloadSpecificPriorityPayload && Equals((WorkloadSpecificPriorityPayload)obj);
        }

        public override int GetHashCode()
        {
            return Priority;
        }

        public override string ToString()
        {
            return CanonicalForm;
        }
    }

    public sealed class WorkloadWorkTypeOrderPayload : IEquatable<WorkloadWorkTypeOrderPayload>
    {
        private readonly ReadOnlyCollection<WorkGiverKey> _orderedWorkGivers;

        public WorkloadWorkTypeOrderPayload(
            IEnumerable<WorkGiverKey> orderedWorkGivers,
            bool isComplete = true)
        {
            var values = new List<WorkGiverKey>();
            if (orderedWorkGivers != null)
            {
                foreach (WorkGiverKey value in orderedWorkGivers)
                {
                    values.Add(value ?? new WorkGiverKey(null));
                }
            }

            _orderedWorkGivers = values.AsReadOnly();
            IsComplete = isComplete;
        }

        public IReadOnlyList<WorkGiverKey> OrderedWorkGivers => _orderedWorkGivers;
        public bool IsComplete { get; private set; }
        public bool IsValid
        {
            get
            {
                if (!IsComplete) return false;
                var seen = new HashSet<WorkGiverKey>();
                for (int i = 0; i < _orderedWorkGivers.Count; i++)
                {
                    if (!_orderedWorkGivers[i].IsValid || !seen.Add(_orderedWorkGivers[i])) return false;
                }

                return true;
            }
        }

        public string CanonicalForm
        {
            get
            {
                var builder = new System.Text.StringBuilder();
                builder.Append(IsComplete ? "1:" : "0:");
                for (int i = 0; i < _orderedWorkGivers.Count; i++)
                {
                    builder.Append(WorkloadCanonical.Encode(_orderedWorkGivers[i].Value)).Append(';');
                }

                return builder.ToString();
            }
        }

        public bool Equals(WorkloadWorkTypeOrderPayload other)
        {
            if (ReferenceEquals(other, null) || IsComplete != other.IsComplete ||
                _orderedWorkGivers.Count != other._orderedWorkGivers.Count)
            {
                return false;
            }

            for (int i = 0; i < _orderedWorkGivers.Count; i++)
            {
                if (!_orderedWorkGivers[i].Equals(other._orderedWorkGivers[i])) return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadWorkTypeOrderPayload);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(CanonicalForm);
        }

        public override string ToString()
        {
            return CanonicalForm;
        }
    }

    public sealed class WorkloadSchedulePayload : IEquatable<WorkloadSchedulePayload>
    {
        public const int HourCount = TimePriorityScheduleValue.HourCount;
        private readonly TimePriorityScheduleValue _value;

        public WorkloadSchedulePayload(IEnumerable<int> priorities, int pinnedHourMask)
        {
            _value = new TimePriorityScheduleValue(priorities, pinnedHourMask);
        }

        public IReadOnlyList<int> Priorities => _value.CopyPriorities();
        public int PinnedHourMask => _value.PinnedHourMask;
        public bool IsValid => _value.IsValid;

        public bool IsPinned(int hour)
        {
            return _value.IsPinned(hour);
        }

        public int PriorityAt(int hour)
        {
            return _value.PriorityAt(hour);
        }

        public string CanonicalForm
        {
            get
            {
                var builder = new System.Text.StringBuilder();
                builder.Append(PinnedHourMask).Append(':');
                for (int i = 0; i < HourCount; i++)
                {
                    // A linked hour has no stored numeric value.  The number
                    // retained in the payload is only a convenient value to
                    // use if that hour is pinned later; it must not make two
                    // semantically identical linked schedules differ.
                    if (IsPinned(i))
                    {
                        builder.Append(WorkloadCanonical.Integer(PriorityAt(i)));
                    }

                    builder.Append(';');
                }

                return builder.ToString();
            }
        }

        public bool Equals(WorkloadSchedulePayload other)
        {
            return !ReferenceEquals(other, null) && _value.Equals(other._value);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WorkloadSchedulePayload);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(CanonicalForm);
        }

        public override string ToString()
        {
            return CanonicalForm;
        }
    }


    public enum WorkloadScalarKind
    {
        Empty = 0,
        Boolean = 1,
        Integer = 2,
        String = 3
    }

    public struct WorkloadScalarValue : IEquatable<WorkloadScalarValue>
    {
        private readonly bool _booleanValue;
        private readonly int _integerValue;
        private readonly string _stringValue;

        private WorkloadScalarValue(WorkloadScalarKind kind, bool booleanValue, int integerValue, string stringValue)
        {
            Kind = kind;
            _booleanValue = booleanValue;
            _integerValue = integerValue;
            _stringValue = stringValue;
        }

        public WorkloadScalarKind Kind { get; private set; }
        public bool BooleanValue => _booleanValue;
        public int IntegerValue => _integerValue;
        public string StringValue => _stringValue;

        public static WorkloadScalarValue Empty
        {
            get { return new WorkloadScalarValue(WorkloadScalarKind.Empty, false, 0, null); }
        }

        public static WorkloadScalarValue FromBoolean(bool value)
        {
            return new WorkloadScalarValue(WorkloadScalarKind.Boolean, value, 0, null);
        }

        public static WorkloadScalarValue FromInteger(int value)
        {
            return new WorkloadScalarValue(WorkloadScalarKind.Integer, false, value, null);
        }

        public static WorkloadScalarValue FromString(string value)
        {
            return value == null
                ? Empty
                : new WorkloadScalarValue(WorkloadScalarKind.String, false, 0, value);
        }

        public string CanonicalValue
        {
            get
            {
                switch (Kind)
                {
                    case WorkloadScalarKind.Boolean:
                        return "b" + WorkloadCanonical.Boolean(_booleanValue);
                    case WorkloadScalarKind.Integer:
                        return "i" + WorkloadCanonical.Integer(_integerValue);
                    case WorkloadScalarKind.String:
                        return "s" + WorkloadCanonical.Encode(_stringValue);
                    default:
                        return "n";
                }
            }
        }

        public bool Equals(WorkloadScalarValue other)
        {
            return Kind == other.Kind
                && _booleanValue == other._booleanValue
                && _integerValue == other._integerValue
                && StringComparer.Ordinal.Equals(_stringValue, other._stringValue);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkloadScalarValue && Equals((WorkloadScalarValue)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (_booleanValue ? 1 : 0);
                hash = (hash * 397) ^ _integerValue;
                hash = (hash * 397) ^ (_stringValue == null ? 0 : StringComparer.Ordinal.GetHashCode(_stringValue));
                return hash;
            }
        }

        public override string ToString()
        {
            return CanonicalValue;
        }
    }

    /// <summary>
    /// Typed presentation value. Keeping the scalar payload and ownership
    /// metadata together prevents a later settings writer from accidentally
    /// treating a global setting as a workload-owned override.
    /// </summary>
    public struct WorkloadSettingValue : IEquatable<WorkloadSettingValue>
    {
        private readonly WorkloadScalarValue _scalar;

        public WorkloadSettingValue(
            WorkloadScalarValue scalar,
            WorkloadSettingOwnership ownership)
        {
            _scalar = scalar;
            Ownership = ownership;
        }

        public WorkloadScalarValue Scalar => _scalar;
        public WorkloadSettingOwnership Ownership { get; private set; }
        public WorkloadScalarKind Kind => _scalar.Kind;
        public bool IsValid =>
            Ownership == WorkloadSettingOwnership.Global ||
            Ownership == WorkloadSettingOwnership.WorkloadOwned;
        public string CanonicalForm =>
            ((int)Ownership).ToString() + ":" + _scalar.CanonicalValue;

        public static WorkloadSettingValue Global(WorkloadScalarValue scalar)
        {
            return new WorkloadSettingValue(scalar, WorkloadSettingOwnership.Global);
        }

        public static WorkloadSettingValue WorkloadOwned(WorkloadScalarValue scalar)
        {
            return new WorkloadSettingValue(scalar, WorkloadSettingOwnership.WorkloadOwned);
        }

        public bool Equals(WorkloadSettingValue other)
        {
            return Ownership == other.Ownership && _scalar.Equals(other._scalar);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkloadSettingValue && Equals((WorkloadSettingValue)obj);
        }

        public override int GetHashCode()
        {
            unchecked { return ((int)Ownership * 397) ^ _scalar.GetHashCode(); }
        }

        public override string ToString()
        {
            return CanonicalForm;
        }
    }
}
