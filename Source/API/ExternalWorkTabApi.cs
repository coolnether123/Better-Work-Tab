using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.API
{
    /// <summary>
    /// Declares whether an external work-tab store currently owns priority data.
    /// </summary>
    /// <remarks>
    /// This is a data-authority contract, not a drawing contract. A store can own priorities while
    /// Better Work Tab still renders the Work tab. External integrations should return
    /// <see cref="ExternalWorkTabPriorityAuthority.ExternalStore"/> only while their own priority
    /// tracker is the source of truth for reads and writes.
    /// </remarks>
    public enum ExternalWorkTabPriorityAuthority
    {
        NoOpinion = 0,
        BetterWorkTab = 1,
        ExternalStore = 2
    }

    /// <summary>
    /// Public contract for a work-tab mod that can mirror Better Work Tab priority data.
    /// </summary>
    /// <remarks>
    /// The critical invariant is two-phase mirroring. If a store implements work-type writes by
    /// cascading into child work givers, it must write all affected parent work types first, then
    /// re-apply child work-giver schedules from Better Work Tab's own stores. During a push, do not
    /// read through an effective-priority API that may route back to the external mirror; that turns
    /// the push into a stale readback loop.
    /// <para>
    /// Use <see cref="SuspendMirroring"/> around imports where the external store is the source of
    /// truth. While suspended, Better Work Tab must not echo partially imported local state back into
    /// the external store.
    /// </para>
    /// </remarks>
    public interface IExternalWorkTabStore
    {
        string StoreId { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        int SortOrder { get; }
        ExternalWorkTabPriorityAuthority PriorityAuthority { get; }
        bool IsMirroringSuspended { get; }
        bool MirrorsTimePrioritySchedules { get; }

        IDisposable SuspendMirroring();
        void PushWorkType(Pawn pawn, WorkTypeDef workType);
        void PushWorkGiver(Pawn pawn, WorkGiverDef workGiver);
        void PushWorkTypeForAllPawns(WorkTypeDef workType);
        void PushWorkGiverForAllPawns(WorkGiverDef workGiver);
        int PushAllPawns();
        IEnumerable<WorkGiverDef> GetWorkGiversAffectedByWorkTypeCascade(WorkTypeDef workType);
        bool TryGetWorkTypePriority(Pawn pawn, WorkTypeDef workType, int hour, out int priority);
        bool TryGetWorkGiverPriority(Pawn pawn, WorkGiverDef workGiver, int hour, out int priority);
    }

    /// <summary>
    /// One work giver's 24-hour priority schedule from an external work-tab store.
    /// </summary>
    public sealed class ExternalWorkGiverPriorityRecord
    {
        public ExternalWorkGiverPriorityRecord(WorkGiverDef workGiver, int[] priorities)
        {
            WorkGiver = workGiver;
            Priorities = priorities != null ? priorities.ToArray() : new int[0];
        }

        public WorkGiverDef WorkGiver { get; }
        public int[] Priorities { get; }
    }

    /// <summary>
    /// All imported work-giver schedules for one pawn.
    /// </summary>
    public sealed class ExternalPawnWorkGiverPriorityRecord
    {
        public ExternalPawnWorkGiverPriorityRecord(
            Pawn pawn,
            IEnumerable<ExternalWorkGiverPriorityRecord> workGivers)
        {
            Pawn = pawn;
            WorkGivers = (workGivers ?? Enumerable.Empty<ExternalWorkGiverPriorityRecord>())
                .Where(record => record?.WorkGiver != null)
                .ToList()
                .AsReadOnly();
        }

        public Pawn Pawn { get; }
        public IList<ExternalWorkGiverPriorityRecord> WorkGivers { get; }
    }

    /// <summary>
    /// Public importer contract for abandoned-mod rescue and work-tab migrations.
    /// </summary>
    /// <remarks>
    /// The importer only supplies per-pawn/per-work-giver 24-hour arrays. Better Work Tab synthesizes
    /// parent work-type schedules, pawn fallback priorities, sub-work overrides, and its hierarchy.
    /// That keeps parsing and reflection in the adapter while the migration algorithm stays stable.
    /// </remarks>
    public interface IExternalWorkTabPriorityImporter
    {
        string StoreId { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        bool TryReadPriorityRecords(out IList<ExternalPawnWorkGiverPriorityRecord> records);
    }

    /// <summary>
    /// Stable entry point for third-party work-tab integrations.
    /// </summary>
    public static class ExternalWorkTabApi
    {
        public const int ApiVersion = 1;
        public const string AssemblyName = "Better Work Tab";
        public const string TypeName = "Better_Work_Tab.API.ExternalWorkTabApi";

        public static bool RegisterStore(IExternalWorkTabStore store)
        {
            return ExternalWorkTabRegistry.RegisterStore(store);
        }

        public static bool UnregisterStore(string storeId)
        {
            return ExternalWorkTabRegistry.UnregisterStore(storeId);
        }

        public static bool RegisterPriorityImporter(IExternalWorkTabPriorityImporter importer)
        {
            return ExternalWorkTabRegistry.RegisterImporter(importer);
        }

        public static bool UnregisterPriorityImporter(string storeId)
        {
            return ExternalWorkTabRegistry.UnregisterImporter(storeId);
        }

        public static int ImportWorkGiverPrioritySchedules(
            IEnumerable<ExternalPawnWorkGiverPriorityRecord> records)
        {
            return ExternalWorkTabRegistry.ImportWorkGiverPrioritySchedules(records);
        }

        public static void NotifyAuthorityChanged()
        {
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
        }
    }
}
