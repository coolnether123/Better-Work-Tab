using System;

namespace Better_Work_Tab.Features.Workloads.V2
{
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
        {
            Pawn = pawn ?? new PawnKey(null);
            WorkType = workType ?? new WorkTypeKey(null);
            WorkGiver = workGiver ?? new WorkGiverKey(null);
        }

        public PawnKey Pawn { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public WorkGiverKey WorkGiver { get; private set; }
        public string CanonicalKey => WorkloadCanonical.Triple(Pawn.Value, WorkType.Value, WorkGiver.Value);
        public bool IsValid => Pawn.IsValid && WorkType.IsValid && WorkGiver.IsValid;

        public int CompareTo(WorkloadSpecificJobKey other)
        {
            if (other == null) return 1;
            int pawnComparison = Pawn.CompareTo(other.Pawn);
            if (pawnComparison != 0) return pawnComparison;
            int workTypeComparison = WorkType.CompareTo(other.WorkType);
            return workTypeComparison != 0 ? workTypeComparison : WorkGiver.CompareTo(other.WorkGiver);
        }

        public bool Equals(WorkloadSpecificJobKey other)
        {
            return !ReferenceEquals(other, null)
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
                int hash = Pawn.GetHashCode();
                hash = (hash * 397) ^ WorkType.GetHashCode();
                return (hash * 397) ^ WorkGiver.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Pawn.Value + "/" + WorkType.Value + "/" + WorkGiver.Value;
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
}
