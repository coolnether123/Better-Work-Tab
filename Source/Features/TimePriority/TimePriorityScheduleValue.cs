using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.TimePriority
{
    internal enum TimePriorityScheduleMutationOutcome
    {
        Rejected,
        Applied,
        AppliedAfterAuthorityChange
    }

    /// <summary>Immutable 24-hour values plus an independent pinned-hour mask.</summary>
    internal sealed class TimePriorityScheduleValue : IEquatable<TimePriorityScheduleValue>
    {
        internal const int HourCount = 24;
        private const int ValidHourMask = (1 << HourCount) - 1;
        internal const int AllHoursMask = ValidHourMask;
        private readonly int[] _priorities = new int[HourCount];
        private readonly bool _isComplete;

        internal static readonly TimePriorityScheduleValue AllLinked =
            new TimePriorityScheduleValue(new int[HourCount], 0);

        internal TimePriorityScheduleValue(IEnumerable<int> priorities, int pinnedHourMask)
        {
            int count = 0;
            if (priorities != null)
            {
                foreach (int priority in priorities)
                {
                    if (count < HourCount)
                    {
                        _priorities[count] = priority;
                    }

                    count++;
                }
            }

            _isComplete = count == HourCount;
            PinnedHourMask = pinnedHourMask;
        }

        internal int PinnedHourMask { get; }
        internal bool HasPinnedHours => PinnedHourMask != 0;
        internal bool IsValid
        {
            get
            {
                if (!_isComplete || PinnedHourMask < 0 ||
                    (PinnedHourMask & ~ValidHourMask) != 0)
                {
                    return false;
                }

                for (int hour = 0; hour < HourCount; hour++)
                {
                    if (_priorities[hour] < 0) return false;
                }

                return true;
            }
        }

        internal bool IsPinned(int hour) =>
            hour >= 0 && hour < HourCount && (PinnedHourMask & (1 << hour)) != 0;

        internal int PriorityAt(int hour) =>
            hour < 0 || hour >= HourCount ? -1 : _priorities[hour];

        internal int[] CopyPriorities()
        {
            var copy = new int[HourCount];
            Array.Copy(_priorities, copy, HourCount);
            return copy;
        }

        internal TimePriorityScheduleValue WithPinnedHour(int hour, int priority)
        {
            if (!IsValid || hour < 0 || hour >= HourCount) return this;
            int[] values = CopyPriorities();
            values[hour] = priority;
            return new TimePriorityScheduleValue(values, PinnedHourMask | (1 << hour));
        }

        internal TimePriorityScheduleValue WithoutPinnedHour(int hour)
        {
            return !IsValid || hour < 0 || hour >= HourCount || !IsPinned(hour)
                ? this
                : new TimePriorityScheduleValue(_priorities, PinnedHourMask & ~(1 << hour));
        }

        public bool Equals(TimePriorityScheduleValue other)
        {
            if (ReferenceEquals(other, null) || PinnedHourMask != other.PinnedHourMask)
            {
                return false;
            }

            for (int hour = 0; hour < HourCount; hour++)
            {
                if (IsPinned(hour) && _priorities[hour] != other._priorities[hour]) return false;
            }

            return true;
        }

        internal bool EqualsExact(TimePriorityScheduleValue other)
        {
            if (ReferenceEquals(other, null) || PinnedHourMask != other.PinnedHourMask) return false;
            for (int hour = 0; hour < HourCount; hour++)
                if (_priorities[hour] != other._priorities[hour]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as TimePriorityScheduleValue);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PinnedHourMask;
                for (int hour = 0; hour < HourCount; hour++)
                {
                    if (IsPinned(hour)) hash = (hash * 397) ^ _priorities[hour];
                }

                return hash;
            }
        }
    }
}
