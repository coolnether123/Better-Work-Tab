using System;
using Better_Work_Tab.Foundation;

namespace Better_Work_Tab.UI.WorkGrid.Invalidation
{
    [Flags]
    public enum WorkGridInvalidationCategory
    {
        None = 0,
        GameState = 1 << 0,
        PawnListOrder = 1 << 1,
        ColumnLayout = 1 << 2,
        Priority = 1 << 3,
        CapabilitySkill = 1 << 4,
        ScheduleHour = 1 << 5,
        SubWorkOverride = 1 << 6,
        SettingsThemeLanguageScale = 1 << 7,
        HoverInteraction = 1 << 8,
        Animation = 1 << 9,
        All = GameState | PawnListOrder | ColumnLayout | Priority | CapabilitySkill |
              ScheduleHour | SubWorkOverride | SettingsThemeLanguageScale |
              HoverInteraction | Animation
    }

    public readonly struct WorkGridPriorityKey : IEquatable<WorkGridPriorityKey>
    {
        public WorkGridPriorityKey(int pawnId, ushort workTypeId)
        {
            PawnId = pawnId;
            WorkTypeId = workTypeId;
        }

        public int PawnId { get; }
        public ushort WorkTypeId { get; }

        public bool Equals(WorkGridPriorityKey other) =>
            PawnId == other.PawnId && WorkTypeId == other.WorkTypeId;

        public override bool Equals(object obj) => obj is WorkGridPriorityKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (PawnId * 397) ^ WorkTypeId;
            }
        }
    }

    public readonly struct WorkGridRevisionSet
    {
        internal WorkGridRevisionSet(long[] revisions)
        {
            GameState = revisions[0];
            PawnListOrder = revisions[1];
            ColumnLayout = revisions[2];
            Priority = revisions[3];
            CapabilitySkill = revisions[4];
            ScheduleHour = revisions[5];
            SubWorkOverride = revisions[6];
            SettingsThemeLanguageScale = revisions[7];
            HoverInteraction = revisions[8];
            Animation = revisions[9];
        }

        public long GameState { get; }
        public long PawnListOrder { get; }
        public long ColumnLayout { get; }
        public long Priority { get; }
        public long CapabilitySkill { get; }
        public long ScheduleHour { get; }
        public long SubWorkOverride { get; }
        public long SettingsThemeLanguageScale { get; }
        public long HoverInteraction { get; }
        public long Animation { get; }
    }

    /// <summary>Framework-safe category revisions plus sparse priority dirtiness.</summary>
    public sealed class WorkGridInvalidationLedger
    {
        private static readonly WorkGridInvalidationCategory[] Categories =
        {
            WorkGridInvalidationCategory.GameState,
            WorkGridInvalidationCategory.PawnListOrder,
            WorkGridInvalidationCategory.ColumnLayout,
            WorkGridInvalidationCategory.Priority,
            WorkGridInvalidationCategory.CapabilitySkill,
            WorkGridInvalidationCategory.ScheduleHour,
            WorkGridInvalidationCategory.SubWorkOverride,
            WorkGridInvalidationCategory.SettingsThemeLanguageScale,
            WorkGridInvalidationCategory.HoverInteraction,
            WorkGridInvalidationCategory.Animation
        };

        private readonly long[] _revisions = new long[Categories.Length];
        private readonly SparseDirtySet<WorkGridPriorityKey> _priorityKeys =
            new SparseDirtySet<WorkGridPriorityKey>();

        public WorkGridRevisionSet Current => new WorkGridRevisionSet(_revisions);
        public int PriorityDirtyCount => _priorityKeys.Count;
        public bool IsPriorityDirty(WorkGridPriorityKey key) => _priorityKeys.Contains(key);

        public WorkGridPriorityKey[] CopyPriorityDirtyKeys()
        {
            if (_priorityKeys.Count == 0)
            {
                return Array.Empty<WorkGridPriorityKey>();
            }

            var keys = new WorkGridPriorityKey[_priorityKeys.Count];
            _priorityKeys.CopyTo(keys, 0);
            return keys;
        }

        public void Invalidate(WorkGridInvalidationCategory categories)
        {
            for (int i = 0; i < Categories.Length; i++)
            {
                if ((categories & Categories[i]) != 0)
                {
                    _revisions[i]++;
                }
            }
        }

        public void InvalidatePriority(WorkGridPriorityKey key)
        {
            _priorityKeys.Add(key);
            _revisions[3]++;
        }

        public void ClearPriorityDirtyKeys()
        {
            _priorityKeys.Clear();
        }

        public void Reset()
        {
            Array.Clear(_revisions, 0, _revisions.Length);
            _priorityKeys.Clear();
        }
    }
}
