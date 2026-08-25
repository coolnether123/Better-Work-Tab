using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    /// <summary>
    /// Stable, value-only plan for every application-owned atomic change.
    /// Compilers can observe earlier planned assignments without publishing
    /// partial live state; the application boundary owns execution and replay.
    /// </summary>
    internal sealed class WorkTabAtomicMutationPlan
    {
        private readonly Dictionary<TimePriorityCacheKey, WorkTabRuleParentPriority> _parents;
        private readonly Dictionary<TimePriorityCacheKey, WorkTabRuleSpecificPriority> _specific;
        private readonly Dictionary<TimePriorityCacheKey, WorkTabRuleSpecificOrder> _orders;
        private readonly Dictionary<TimePriorityCacheKey, WorkTabRuleSchedule> _schedules;

        private WorkTabAtomicMutationPlan(
            Dictionary<TimePriorityCacheKey, WorkTabRuleParentPriority> parents)
        {
            _parents = parents;
            _specific = new Dictionary<TimePriorityCacheKey, WorkTabRuleSpecificPriority>();
            _orders = new Dictionary<TimePriorityCacheKey, WorkTabRuleSpecificOrder>();
            _schedules = new Dictionary<TimePriorityCacheKey, WorkTabRuleSchedule>();
        }

        internal bool RequiresManualPriorities { get; set; }
        /// <summary>
        /// An explicit manual-priority target for compilers whose saved state
        /// owns both enabled and disabled values. A null value leaves the
        /// global mode unchanged; the older true-only requirement remains for
        /// existing rule compilers.
        /// </summary>
        internal bool? ManualPrioritiesTarget { get; set; }
        internal int? RequiredPriorityMaximum { get; set; }

        internal static WorkTabAtomicMutationPlan Capture(
            IEnumerable<Pawn> pawns,
            IEnumerable<WorkTypeDef> workTypes)
        {
            var values = new Dictionary<TimePriorityCacheKey, WorkTabRuleParentPriority>();
            foreach (Pawn pawn in pawns ?? Enumerable.Empty<Pawn>())
            {
                if (pawn?.workSettings == null)
                {
                    continue;
                }

                foreach (WorkTypeDef workType in workTypes ?? Enumerable.Empty<WorkTypeDef>())
                {
                    if (workType == null)
                    {
                        continue;
                    }

                    int priority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    values[TimePriorityTarget.ForWorkType(pawn, workType).CacheKey] =
                        new WorkTabRuleParentPriority(pawn, workType, priority, priority);
                }
            }

            return new WorkTabAtomicMutationPlan(values);
        }

        internal static WorkTabAtomicMutationPlan CaptureTrustedImport(WorkTabTrustedImport import)
        {
            var mutation = new WorkTabAtomicMutationPlan(
                new Dictionary<TimePriorityCacheKey, WorkTabRuleParentPriority>())
            {
                RequiredPriorityMaximum = import?.RequiredPriorityMaximum
            };
            if (import == null) return mutation;
            for (int i = 0; i < import.ParentPriorities.Count; i++)
                mutation.SetImportedPriority(import.ParentPriorities[i]);
            for (int i = 0; i < import.SpecificPriorities.Count; i++)
                mutation.SetImportedSpecificPriority(import.SpecificPriorities[i]);
            for (int i = 0; i < import.SpecificOrders.Count; i++)
                mutation.SetImportedSpecificOrder(import.SpecificOrders[i]);
            for (int i = 0; i < import.Schedules.Count; i++)
                mutation.SetSchedule(
                    import.Schedules[i].Target,
                    import.Schedules[i].Value,
                    import.Schedules[i].FallbackPriority);
            return mutation;
        }

        internal int GetPriority(Pawn pawn, WorkTypeDef workType)
        {
            return _parents.TryGetValue(
                    TimePriorityTarget.ForWorkType(pawn, workType).CacheKey,
                    out WorkTabRuleParentPriority entry)
                ? entry.Desired
                : WorkPrioritySystem.DisabledPriority;
        }

        internal bool SetPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            TimePriorityCacheKey target = TimePriorityTarget.ForWorkType(pawn, workType).CacheKey;
            if (!_parents.TryGetValue(target, out WorkTabRuleParentPriority entry))
            {
                return false;
            }

            _parents[target] = entry.WithDesired(WorkPrioritySystem.ClampPriority(priority));
            return true;
        }

        internal IReadOnlyList<WorkTabRuleParentPriority> ParentPriorities =>
            _parents.Values
                .Where(entry => entry.Expected != entry.Desired)
                .OrderBy(entry => entry.Pawn.thingIDNumber)
                .ThenBy(entry => entry.WorkType.defName, StringComparer.Ordinal)
                .ToList();

        internal int GetSpecificPriority(Pawn pawn, WorkGiverDef workGiver, int fallback)
        {
            return _specific.TryGetValue(
                    TimePriorityTarget.ForWorkGiver(pawn, workGiver).CacheKey,
                    out WorkTabRuleSpecificPriority entry)
                ? entry.Desired
                : fallback;
        }

        internal bool SetSpecificPriority(Pawn pawn, WorkGiverDef workGiver, int desired)
        {
            if (pawn?.workSettings == null || workGiver == null)
            {
                return false;
            }

            TimePriorityCacheKey target = TimePriorityTarget.ForWorkGiver(pawn, workGiver).CacheKey;
            if (!_specific.TryGetValue(target, out WorkTabRuleSpecificPriority entry))
            {
                SpecificJobPriorityStorageSnapshot captured =
                    SpecificJobPriorityAuthorityAdapter.Capture(pawn, workGiver);

                entry = new WorkTabRuleSpecificPriority(
                    pawn,
                    workGiver,
                    captured.StorageKind,
                    captured.HadOverride,
                    captured.Priority,
                    desired,
                    clear: false);
            }
            else
            {
                entry = entry.WithDesired(desired, clear: false);
            }

            _specific[target] = entry;
            return true;
        }

        internal bool SetSchedule(
            TimePriorityTarget target,
            TimePriorityScheduleValue desired,
            int fallbackPriority)
        {
            if (desired?.IsValid != true ||
                !TimePriorityService.TryCaptureLiveScheduleSnapshot(
                    target, fallbackPriority, out TimePriorityLiveScheduleSnapshot expected, out _))
            {
                return false;
            }

            TimePriorityCacheKey key = target.CacheKey;
            if (_schedules.TryGetValue(key, out WorkTabRuleSchedule prior))
            {
                _schedules[key] = prior.WithDesired(desired, fallbackPriority);
                return true;
            }

            if (expected.Schedule.Equals(desired))
            {
                return true;
            }

            _schedules[key] = new WorkTabRuleSchedule(target, expected, desired, fallbackPriority);
            return true;
        }

        internal IReadOnlyList<WorkTabRuleSpecificPriority> SpecificPriorities =>
            _specific.Values
                .Where(entry => entry.Clear ? entry.HadOverride :
                    entry.Initial != entry.Desired || !entry.HadOverride)
                .OrderBy(entry => entry.Pawn.thingIDNumber)
                .ThenBy(entry => entry.WorkGiver.defName, StringComparer.Ordinal)
                .ToList();

        internal IReadOnlyList<WorkTabRuleSpecificOrder> SpecificOrders =>
            _orders.Values
                .OrderBy(entry => entry.Pawn.thingIDNumber)
                .ThenBy(entry => entry.WorkType.defName, StringComparer.Ordinal)
                .ToList();

        internal IReadOnlyList<WorkTabRuleSchedule> Schedules =>
            _schedules.Values.OrderBy(entry => entry.Target.Key, StringComparer.Ordinal).ToList();

        internal bool TryEncode(out string payload)
        {
            payload = string.Empty;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(2);
                    writer.Write(RequiresManualPriorities);
                    writer.Write(ManualPrioritiesTarget.HasValue
                        ? ManualPrioritiesTarget.Value ? (byte)2 : (byte)1
                        : (byte)0);
                    writer.Write(RequiredPriorityMaximum ?? -1);
                    WriteParents(writer, ParentPriorities);
                    WriteSpecifics(writer, SpecificPriorities);
                    WriteOrders(writer, SpecificOrders);
                    WriteSchedules(writer, Schedules);
                    payload = Convert.ToBase64String(stream.ToArray());
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryDecode(string payload, out WorkTabAtomicMutationPlan mutation)
        {
            mutation = null;
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(payload)))
                using (var reader = new BinaryReader(stream))
                {
                    int version = reader.ReadInt32();
                    if (version != 1 && version != 2) return false;
                    var decoded = new WorkTabAtomicMutationPlan(
                        new Dictionary<TimePriorityCacheKey, WorkTabRuleParentPriority>())
                    {
                        RequiresManualPriorities = reader.ReadBoolean()
                    };
                    if (version >= 2)
                    {
                        byte manualTarget = reader.ReadByte();
                        if (manualTarget > 2) return false;
                        decoded.ManualPrioritiesTarget = manualTarget == 0
                            ? (bool?)null
                            : manualTarget == 2;
                    }
                    int maximum = reader.ReadInt32();
                    decoded.RequiredPriorityMaximum = maximum < 0 ? (int?)null : maximum;
                    if (!ReadParents(reader, decoded) || !ReadSpecifics(reader, decoded) ||
                        !ReadOrders(reader, decoded) || !ReadSchedules(reader, decoded) ||
                        stream.Position != stream.Length) return false;
                    mutation = decoded;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void WriteParents(BinaryWriter writer, IReadOnlyList<WorkTabRuleParentPriority> values)
        {
            writer.Write(values.Count);
            foreach (WorkTabRuleParentPriority value in values)
            {
                writer.Write(value.Pawn.thingIDNumber);
                writer.Write(value.WorkType.defName);
                writer.Write(value.Expected);
                writer.Write(value.Desired);
            }
        }

        private static bool ReadParents(BinaryReader reader, WorkTabAtomicMutationPlan mutation)
        {
            int count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = TimePriorityService.FindPawn(reader.ReadInt32());
                WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(reader.ReadString());
                int expected = reader.ReadInt32();
                int desired = reader.ReadInt32();
                if (pawn?.workSettings == null || workType == null) return false;
                mutation._parents[TimePriorityTarget.ForWorkType(pawn, workType).CacheKey] =
                    new WorkTabRuleParentPriority(pawn, workType, expected, desired);
            }
            return true;
        }

        private static void WriteSpecifics(BinaryWriter writer, IReadOnlyList<WorkTabRuleSpecificPriority> values)
        {
            writer.Write(values.Count);
            foreach (WorkTabRuleSpecificPriority value in values)
            {
                writer.Write(value.Pawn.thingIDNumber);
                writer.Write(value.WorkGiver.defName);
                writer.Write(value.StorageKind == SpecificJobPriorityStorageKind.ExternalAuthority);
                writer.Write(value.HadOverride);
                writer.Write(value.Initial);
                writer.Write(value.Desired);
                writer.Write(value.Clear);
            }
        }

        private static bool ReadSpecifics(BinaryReader reader, WorkTabAtomicMutationPlan mutation)
        {
            int count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = TimePriorityService.FindPawn(reader.ReadInt32());
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(reader.ReadString());
                bool externalStorage = reader.ReadBoolean();
                bool hadOverride = reader.ReadBoolean();
                int initial = reader.ReadInt32();
                int desired = reader.ReadInt32();
                bool clear = reader.ReadBoolean();
                if (pawn?.workSettings == null || workGiver == null) return false;
                mutation._specific[TimePriorityTarget.ForWorkGiver(pawn, workGiver).CacheKey] =
                    new WorkTabRuleSpecificPriority(
                        pawn,
                        workGiver,
                        externalStorage
                            ? SpecificJobPriorityStorageKind.ExternalAuthority
                            : SpecificJobPriorityStorageKind.ReassignmentManager,
                        hadOverride,
                        initial,
                        desired,
                        clear,
                        clampDesired: false);
            }
            return true;
        }

        private static void WriteOrders(BinaryWriter writer, IReadOnlyList<WorkTabRuleSpecificOrder> values)
        {
            writer.Write(values.Count);
            foreach (WorkTabRuleSpecificOrder value in values)
            {
                writer.Write(value.Pawn.thingIDNumber);
                writer.Write(value.WorkType.defName);
                writer.Write(value.Expected.HasStoredOrder);
                WriteStrings(writer, value.Expected.OrderedWorkGiverNames);
                WriteStrings(writer, value.Desired);
            }
        }

        private static bool ReadOrders(BinaryReader reader, WorkTabAtomicMutationPlan mutation)
        {
            int count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = TimePriorityService.FindPawn(reader.ReadInt32());
                WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(reader.ReadString());
                bool stored = reader.ReadBoolean();
                List<string> expectedOrder = ReadStrings(reader);
                List<string> desired = ReadStrings(reader);
                if (pawn?.workSettings == null || workType == null) return false;
                mutation._orders[TimePriorityTarget.ForWorkType(pawn, workType).CacheKey] =
                    new WorkTabRuleSpecificOrder(
                        pawn,
                        workType,
                        new WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot(
                            pawn.thingIDNumber, workType.defName, stored, expectedOrder),
                        desired);
            }
            return true;
        }

        private static void WriteSchedules(BinaryWriter writer, IReadOnlyList<WorkTabRuleSchedule> values)
        {
            writer.Write(values.Count);
            foreach (WorkTabRuleSchedule value in values)
            {
                writer.Write(value.Target.PawnId);
                writer.Write((int)value.Target.Kind);
                writer.Write(value.Target.WorkTypeDefName ?? string.Empty);
                writer.Write(value.Target.TargetDefName ?? string.Empty);
                writer.Write(value.Expected.HadSchedule);
                writer.Write(value.Expected.FallbackPriority);
                writer.Write(value.Expected.ServiceVersion);
                writer.Write(value.Expected.AuthorityRevision);
                writer.Write(value.Expected.Schedule.PinnedHourMask);
                WriteIntegers(writer, value.Expected.Schedule.CopyPriorities());
                writer.Write(value.Desired.PinnedHourMask);
                WriteIntegers(writer, value.Desired.CopyPriorities());
            }
        }

        private static bool ReadSchedules(BinaryReader reader, WorkTabAtomicMutationPlan mutation)
        {
            int count = ReadCount(reader);
            for (int i = 0; i < count; i++)
            {
                int pawnId = reader.ReadInt32();
                int kind = reader.ReadInt32();
                string workType = reader.ReadString();
                string targetName = reader.ReadString();
                bool hadSchedule = reader.ReadBoolean();
                int fallback = reader.ReadInt32();
                int version = reader.ReadInt32();
                long authority = reader.ReadInt64();
                int expectedMask = reader.ReadInt32();
                int[] expectedValues = ReadIntegers(reader);
                int desiredMask = reader.ReadInt32();
                int[] desiredValues = ReadIntegers(reader);
                if (!Enum.IsDefined(typeof(TimePriorityTargetKind), kind)) return false;
                TimePriorityTarget target = TimePriorityTarget.FromRaw(
                    pawnId, (TimePriorityTargetKind)kind, workType, targetName);
                var expected = new TimePriorityLiveScheduleSnapshot(
                    target,
                    new TimePriorityScheduleValue(expectedValues, expectedMask),
                    hadSchedule,
                    fallback,
                    version,
                    authority);
                var desired = new TimePriorityScheduleValue(desiredValues, desiredMask);
                if (!expected.Schedule.IsValid || !desired.IsValid) return false;
                mutation._schedules[target.CacheKey] = new WorkTabRuleSchedule(
                    target, expected, desired, fallback);
            }
            return true;
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000) throw new InvalidDataException();
            return count;
        }

        private static void WriteStrings(BinaryWriter writer, IReadOnlyList<string> values)
        {
            writer.Write(values?.Count ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Count; i++) writer.Write(values[i] ?? string.Empty);
        }

        private static List<string> ReadStrings(BinaryReader reader)
        {
            int count = ReadCount(reader);
            var values = new List<string>(count);
            for (int i = 0; i < count; i++) values.Add(reader.ReadString());
            return values;
        }

        private static void WriteIntegers(BinaryWriter writer, IReadOnlyList<int> values)
        {
            writer.Write(values?.Count ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Count; i++) writer.Write(values[i]);
        }

        private static int[] ReadIntegers(BinaryReader reader)
        {
            int count = ReadCount(reader);
            var values = new int[count];
            for (int i = 0; i < count; i++) values[i] = reader.ReadInt32();
            return values;
        }

        private bool SetImportedPriority(WorkTabTrustedParentPriority entry)
        {
            if (entry.Pawn?.workSettings == null || entry.WorkType == null ||
                !WorkTabActionability.CanApplyParent(entry.Pawn, entry.WorkType)) return false;
            TimePriorityCacheKey target = TimePriorityTarget.ForWorkType(
                entry.Pawn,
                entry.WorkType).CacheKey;
            if (!_parents.TryGetValue(target, out WorkTabRuleParentPriority current))
            {
                int initial = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                    entry.Pawn.workSettings,
                    entry.WorkType);
                current = new WorkTabRuleParentPriority(
                    entry.Pawn,
                    entry.WorkType,
                    initial,
                    initial);
            }
            _parents[target] = current.WithDesired(entry.Priority);
            return true;
        }

        private bool SetImportedSpecificPriority(WorkTabTrustedSpecificPriority entry)
        {
            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(entry.WorkGiver);
            if (entry.Pawn?.workSettings == null || entry.WorkGiver == null || workType == null ||
                !WorkTabActionability.CanApplySpecific(entry.Pawn, workType, entry.WorkGiver)) return false;
            TimePriorityCacheKey target = TimePriorityTarget.ForWorkGiver(
                entry.Pawn,
                entry.WorkGiver).CacheKey;
            if (!_specific.TryGetValue(target, out WorkTabRuleSpecificPriority current))
            {
                bool hadOverride = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                    entry.Pawn, entry.WorkGiver, out int initial);
                current = new WorkTabRuleSpecificPriority(
                    entry.Pawn,
                    entry.WorkGiver,
                    SpecificJobPriorityStorageKind.ReassignmentManager,
                    hadOverride,
                    initial,
                    entry.Priority ?? WorkPrioritySystem.DisabledPriority,
                    !entry.Priority.HasValue,
                    clampDesired: false);
            }
            else current = current.WithDesired(
                entry.Priority ?? WorkPrioritySystem.DisabledPriority,
                !entry.Priority.HasValue,
                clampDesired: false);
            _specific[target] = current;
            return true;
        }

        private bool SetImportedSpecificOrder(WorkTabTrustedSpecificOrder entry)
        {
            if (entry.Pawn?.workSettings == null || entry.WorkType == null ||
                entry.OrderedWorkGivers?.Count <= 0 ||
                !WorkTabActionability.CanApplyParent(entry.Pawn, entry.WorkType)) return false;
            TimePriorityCacheKey target = TimePriorityTarget.ForWorkType(
                entry.Pawn,
                entry.WorkType).CacheKey;
            _orders[target] = new WorkTabRuleSpecificOrder(
                entry.Pawn,
                entry.WorkType,
                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                    entry.Pawn, entry.WorkType),
                entry.OrderedWorkGivers);
            return true;
        }

    }

    internal readonly struct WorkTabRuleParentPriority
    {
        internal WorkTabRuleParentPriority(Pawn pawn, WorkTypeDef workType, int expected, int desired)
        {
            Pawn = pawn;
            WorkType = workType;
            Expected = expected;
            Desired = desired;
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal int Expected { get; }
        internal int Desired { get; }
        internal WorkTabRuleParentPriority WithDesired(int desired) =>
            new WorkTabRuleParentPriority(Pawn, WorkType, Expected, desired);
    }

    internal readonly struct WorkTabRuleSpecificOrder
    {
        internal WorkTabRuleSpecificOrder(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot expected,
            IReadOnlyList<string> desired)
        {
            Pawn = pawn;
            WorkType = workType;
            Expected = expected;
            Desired = desired == null ? null : new List<string>(desired);
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot Expected { get; }
        internal IReadOnlyList<string> Desired { get; }
    }

    internal readonly struct WorkTabRuleSchedule
    {
        internal WorkTabRuleSchedule(
            TimePriorityTarget target,
            TimePriorityLiveScheduleSnapshot expected,
            TimePriorityScheduleValue desired,
            int fallbackPriority)
        {
            Target = target; Expected = expected; Desired = desired;
            FallbackPriority = fallbackPriority;
        }

        internal TimePriorityTarget Target { get; }
        internal TimePriorityLiveScheduleSnapshot Expected { get; }
        internal TimePriorityScheduleValue Desired { get; }
        internal int FallbackPriority { get; }
        internal WorkTabRuleSchedule WithDesired(TimePriorityScheduleValue desired, int fallbackPriority) =>
            new WorkTabRuleSchedule(Target, Expected, desired, fallbackPriority);
    }
}
