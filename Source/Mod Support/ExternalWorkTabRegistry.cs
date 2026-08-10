using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Better_Work_Tab.API;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Internal handoff contract for integrations whose data cannot be represented as BWT's
    /// generic hourly schedule records. The Sleek adapter uses this to translate child ranks into
    /// BWT's pawn-specific order/override model without touching parent priorities or schedules.
    /// </summary>
    internal interface IExternalWorkTabHandoffImporter
    {
        string StoreId { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        int ImportToBetterWorkTab();
    }

    internal static class ExternalWorkTabRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, IExternalWorkTabStore> Stores =
            new Dictionary<string, IExternalWorkTabStore>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IExternalWorkTabPriorityImporter> Importers =
            new Dictionary<string, IExternalWorkTabPriorityImporter>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IExternalWorkTabHandoffImporter> HandoffImporters =
            new Dictionary<string, IExternalWorkTabHandoffImporter>(StringComparer.OrdinalIgnoreCase);
        private static long registryGeneration;
#if DEBUG
        private static long registryListBuilds;
        private static long storeProbes;
        private static long authorityRefreshPasses;
#endif
        private static int registeredStoreCount;
        private static bool authorityRefreshInProgress;
        private static bool authorityRefreshPending;
        private static bool authorityRefreshDeferred;
        private const int MaxSynchronousAuthorityRefreshPasses = 8;

        internal static long RegistryGeneration => Interlocked.Read(ref registryGeneration);
        internal static long RegistryListBuilds
        {
            get
            {
#if DEBUG
                return Interlocked.Read(ref registryListBuilds);
#else
                return 0;
#endif
            }
        }

        internal static long StoreProbes
        {
            get
            {
#if DEBUG
                return Interlocked.Read(ref storeProbes);
#else
                return 0;
#endif
            }
        }
        internal static int RegisteredStoreCount => Volatile.Read(ref registeredStoreCount);

        internal static bool AuthorityRefreshDeferred
        {
            get
            {
                lock (SyncRoot)
                {
                    return authorityRefreshDeferred;
                }
            }
        }

#if DEBUG
        internal static long AuthorityRefreshPasses => Interlocked.Read(ref authorityRefreshPasses);
#endif

        internal static bool RegisterStore(IExternalWorkTabStore store)
        {
            if (store == null || string.IsNullOrWhiteSpace(store.StoreId))
            {
                return false;
            }

            string storeId = store.StoreId.Trim();

            lock (SyncRoot)
            {
                if (!Stores.ContainsKey(storeId))
                {
                    registeredStoreCount++;
                }

                Stores[storeId] = store;
                registryGeneration++;
            }

            NotifyAuthorityChangedAfterRegistryMutation();
            return true;
        }

        internal static bool UnregisterStore(string storeId)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            bool removed;
            lock (SyncRoot)
            {
                removed = Stores.Remove(storeId.Trim());
                if (removed)
                {
                    registeredStoreCount--;
                    registryGeneration++;
                }
            }

            if (removed)
            {
                NotifyAuthorityChangedAfterRegistryMutation();
            }

            return removed;
        }

        internal static bool RegisterImporter(IExternalWorkTabPriorityImporter importer)
        {
            if (importer == null || string.IsNullOrWhiteSpace(importer.StoreId))
            {
                return false;
            }

            string storeId = importer.StoreId.Trim();

            lock (SyncRoot)
            {
                Importers[storeId] = importer;
            }

            return true;
        }

        internal static bool UnregisterImporter(string storeId)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                return Importers.Remove(storeId.Trim());
            }
        }

        internal static IExternalWorkTabStore GetAuthoritativeStore()
        {
            return PriorityAuthorityBroker.GetAuthoritativeStore();
        }

        internal static bool AnyStoreSuspended
        {
            get
            {
                AvailableStoresResult result = GetAvailableStores();
                return result.IsCoherent && result.Stores.Any(SafeIsSuspended);
            }
        }

        internal static bool ShouldMirrorTimePrioritySchedules
        {
            get
            {
                AvailableStoresResult result = GetAvailableStores();
                return result.IsCoherent && result.Stores.Any(SafeMirrorsTimePrioritySchedules);
            }
        }

        internal static IDisposable SuspendAllMirroring()
        {
            AvailableStoresResult result = GetAvailableStores();
            return new CompositeSuspendScope(
                (result.IsCoherent ? result.Stores : new List<IExternalWorkTabStore>())
                    .Select(SafeSuspend)
                    .Where(scope => scope != null)
                    .ToList());
        }

        internal static void PushWorkType(Pawn pawn, WorkTypeDef workType)
        {
            AvailableStoresResult result = GetAvailableStores();
            if (!result.IsCoherent)
            {
                return;
            }

            foreach (IExternalWorkTabStore store in result.Stores)
            {
                SafeRun(store, () => store.PushWorkType(pawn, workType));
            }
        }

        internal static void PushWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
            AvailableStoresResult result = GetAvailableStores();
            if (!result.IsCoherent)
            {
                return;
            }

            foreach (IExternalWorkTabStore store in result.Stores)
            {
                SafeRun(store, () => store.PushWorkGiver(pawn, workGiver));
            }
        }

        internal static void PushWorkTypeForAllPawns(WorkTypeDef workType)
        {
            AvailableStoresResult result = GetAvailableStores();
            if (!result.IsCoherent)
            {
                return;
            }

            foreach (IExternalWorkTabStore store in result.Stores)
            {
                SafeRun(store, () => store.PushWorkTypeForAllPawns(workType));
            }
        }

        internal static void PushWorkGiverForAllPawns(WorkGiverDef workGiver)
        {
            AvailableStoresResult result = GetAvailableStores();
            if (!result.IsCoherent)
            {
                return;
            }

            foreach (IExternalWorkTabStore store in result.Stores)
            {
                SafeRun(store, () => store.PushWorkGiverForAllPawns(workGiver));
            }
        }

        internal static int PushAllPawns()
        {
            AvailableStoresResult result = GetAvailableStores();
            if (!result.IsCoherent)
            {
                return 0;
            }

            int pushed = 0;
            foreach (IExternalWorkTabStore store in result.Stores)
            {
                try
                {
                    pushed += store.PushAllPawns();
                }
                catch (Exception ex)
                {
                    WarnStore(store, "push all pawns", ex);
                }
            }

            return pushed;
        }

        internal static int PushAllPawnsToStore(IExternalWorkTabStore store)
        {
            if (!SafeIsAvailable(store))
            {
                return 0;
            }

            try
            {
                return store.PushAllPawns();
            }
            catch (Exception ex)
            {
                WarnStore(store, "publish all pawns", ex);
                return 0;
            }
        }

        internal static bool TryGetWorkTypePriority(
            Pawn pawn,
            WorkTypeDef workType,
            int hour,
            out int priority)
        {
            return TryGetWorkTypePriority(
                PriorityAuthorityBroker.GetAuthoritativeStore(),
                pawn,
                workType,
                hour,
                out priority);
        }

        internal static bool TryGetWorkTypePriority(
            IExternalWorkTabStore store,
            Pawn pawn,
            WorkTypeDef workType,
            int hour,
            out int priority)
        {
            priority = 0;
            if (store == null)
            {
                return false;
            }

            try
            {
                return store.TryGetWorkTypePriority(pawn, workType, hour, out priority);
            }
            catch (Exception ex)
            {
                WarnStore(store, "read work type priority", ex);
                priority = 0;
                return false;
            }
        }

        internal static bool TryGetWorkGiverPriority(
            Pawn pawn,
            WorkGiverDef workGiver,
            int hour,
            out int priority)
        {
            return TryGetWorkGiverPriority(
                PriorityAuthorityBroker.GetAuthoritativeStore(),
                pawn,
                workGiver,
                hour,
                out priority);
        }

        internal static bool TryGetWorkGiverPriority(
            IExternalWorkTabStore store,
            Pawn pawn,
            WorkGiverDef workGiver,
            int hour,
            out int priority)
        {
            priority = 0;
            if (store == null)
            {
                return false;
            }

            try
            {
                return store.TryGetWorkGiverPriority(pawn, workGiver, hour, out priority);
            }
            catch (Exception ex)
            {
                WarnStore(store, "read work giver priority", ex);
                priority = 0;
                return false;
            }
        }

        internal static int ImportFromAvailableImporter()
        {
            foreach (IExternalWorkTabPriorityImporter importer in GetAvailableImporters())
            {
                try
                {
                    IReadOnlyList<ExternalPawnWorkGiverPriorityRecord> records;
                    int changed = importer.TryReadPriorityRecords(out records)
                        ? ImportWorkGiverPrioritySchedules(records)
                        : 0;
                    if (changed > 0)
                    {
                        return changed;
                    }
                }
                catch (Exception ex)
                {
                    BetterWorkTabMod.DebugLog(
                        "[ExternalWorkTab] Import failed for " + importer.DisplayName + ": " + ex.Message,
                        DebugFeature.ModSupport);
                }
            }

            return 0;
        }

        internal static bool RegisterHandoffImporter(IExternalWorkTabHandoffImporter importer)
        {
            if (importer == null || string.IsNullOrWhiteSpace(importer.StoreId))
            {
                return false;
            }

            string storeId = importer.StoreId.Trim();

            lock (SyncRoot)
            {
                HandoffImporters[storeId] = importer;
            }

            return true;
        }

        internal static bool UnregisterHandoffImporter(string storeId)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                return HandoffImporters.Remove(storeId.Trim());
            }
        }

        internal static int ImportFromStore(string storeId)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return ImportFromAvailableImporter();
            }

            IExternalWorkTabHandoffImporter handoffImporter;
            IExternalWorkTabPriorityImporter importer;
            lock (SyncRoot)
            {
                HandoffImporters.TryGetValue(storeId.Trim(), out handoffImporter);
                Importers.TryGetValue(storeId.Trim(), out importer);
            }

            if (handoffImporter != null)
            {
                try
                {
                    return handoffImporter.IsAvailable
                        ? handoffImporter.ImportToBetterWorkTab()
                        : 0;
                }
                catch (Exception ex)
                {
                    BetterWorkTabMod.DebugLog(
                        "[ExternalWorkTab] Handoff import failed for " + handoffImporter.DisplayName + ": " + ex.Message,
                        DebugFeature.ModSupport);
                    return 0;
                }
            }

            if (importer == null)
            {
                BetterWorkTabMod.DebugLog(
                    "[ExternalWorkTab] No importer is registered for the requested store: " +
                    storeId,
                    DebugFeature.ModSupport);
                return 0;
            }

            try
            {
                IReadOnlyList<ExternalPawnWorkGiverPriorityRecord> records;
                return importer.IsAvailable && importer.TryReadPriorityRecords(out records)
                    ? ImportWorkGiverPrioritySchedules(records)
                    : 0;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[ExternalWorkTab] Import failed for " + importer.DisplayName + ": " + ex.Message,
                    DebugFeature.ModSupport);
                return 0;
            }
        }

        internal static int ImportWorkGiverPrioritySchedules(
            IEnumerable<ExternalPawnWorkGiverPriorityRecord> records)
        {
            return ExternalWorkTabPriorityImportService.Import(records);
        }

        internal static AuthoritativeStoreResult FindAuthoritativeStore()
        {
            DrainDeferredAuthorityRefresh();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<IExternalWorkTabStore> stores = CopyStores(out long observedGeneration);
                IExternalWorkTabStore selected = null;
                foreach (IExternalWorkTabStore store in stores)
                {
                    // Store callbacks deliberately run outside SyncRoot. A third-party property
                    // may register/unregister another store or wait for another thread.
                    if (!SafeIsAvailable(store) ||
                        SafeAuthority(store) != ExternalWorkTabPriorityAuthority.ExternalStore)
                    {
                        continue;
                    }

                    if (selected == null ||
                        store.SortOrder < selected.SortOrder ||
                        (store.SortOrder == selected.SortOrder &&
                         string.Compare(store.DisplayName, selected.DisplayName, StringComparison.OrdinalIgnoreCase) < 0) ||
                        (store.SortOrder == selected.SortOrder &&
                         string.Equals(store.DisplayName, selected.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                         string.Compare(store.StoreId, selected.StoreId, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        selected = store;
                    }
                }

                lock (SyncRoot)
                {
                    if (registryGeneration == observedGeneration)
                    {
                        return AuthoritativeStoreResult.Coherent(selected, observedGeneration);
                    }
                }
            }

            lock (SyncRoot)
            {
                // Do not stamp an older probe result with the current generation. The caller must
                // retain its last coherent authority snapshot and retry on a later safe read.
                return AuthoritativeStoreResult.Unstable(registryGeneration);
            }
        }

        private static AvailableStoresResult GetAvailableStores()
        {
            DrainDeferredAuthorityRefresh();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<IExternalWorkTabStore> stores = CopyStores(out long observedGeneration);
                List<IExternalWorkTabStore> available = stores
                    .Where(SafeIsAvailable)
                    .OrderBy(store => store.SortOrder)
                    .ThenBy(store => store.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(store => store.StoreId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                lock (SyncRoot)
                {
                    if (registryGeneration == observedGeneration)
                    {
                        return AvailableStoresResult.Coherent(available, observedGeneration);
                    }
                }
            }

            lock (SyncRoot)
            {
                return AvailableStoresResult.Unstable(registryGeneration);
            }
        }

        private static List<IExternalWorkTabPriorityImporter> GetAvailableImporters()
        {
            List<IExternalWorkTabPriorityImporter> importers;
            lock (SyncRoot)
            {
                importers = Importers.Values.ToList();
            }

            return importers
                .Where(SafeIsAvailable)
                .OrderBy(importer => importer.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<IExternalWorkTabStore> CopyStores(out long generation)
        {
            lock (SyncRoot)
            {
                generation = registryGeneration;
                RecordRegistryListBuild();
                return Stores.Values.ToList();
            }
        }

        private static bool SafeIsAvailable(IExternalWorkTabStore store)
        {
            RecordStoreProbe();
            try
            {
                return store != null && store.IsAvailable;
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeIsAvailable(IExternalWorkTabPriorityImporter importer)
        {
            try
            {
                return importer != null && importer.IsAvailable;
            }
            catch
            {
                return false;
            }
        }

        private static ExternalWorkTabPriorityAuthority SafeAuthority(IExternalWorkTabStore store)
        {
            RecordStoreProbe();
            try
            {
                return store?.PriorityAuthority ?? ExternalWorkTabPriorityAuthority.NoOpinion;
            }
            catch
            {
                return ExternalWorkTabPriorityAuthority.NoOpinion;
            }
        }

        [Conditional("DEBUG")]
        private static void RecordRegistryListBuild()
        {
#if DEBUG
            Interlocked.Increment(ref registryListBuilds);
#endif
        }

        [Conditional("DEBUG")]
        private static void RecordStoreProbe()
        {
#if DEBUG
            Interlocked.Increment(ref storeProbes);
#endif
        }

        private static bool SafeIsSuspended(IExternalWorkTabStore store)
        {
            try
            {
                return store != null && store.IsMirroringSuspended;
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeMirrorsTimePrioritySchedules(IExternalWorkTabStore store)
        {
            try
            {
                return store != null && store.MirrorsTimePrioritySchedules;
            }
            catch
            {
                return false;
            }
        }

        private static IDisposable SafeSuspend(IExternalWorkTabStore store)
        {
            try
            {
                return store?.SuspendMirroring();
            }
            catch (Exception ex)
            {
                WarnStore(store, "suspend mirroring", ex);
                return null;
            }
        }

        private static void SafeRun(IExternalWorkTabStore store, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                WarnStore(store, "mirror priority change", ex);
            }
        }

        private static void WarnStore(IExternalWorkTabStore store, string action, Exception ex)
        {
            BetterWorkTabMod.DebugLog(
                "[ExternalWorkTab] Failed to " + action + " for " +
                (store?.DisplayName ?? "<unknown>") + ": " + ex.Message,
                DebugFeature.ModSupport);
        }

        private static void NotifyAuthorityChangedAfterRegistryMutation()
        {
            lock (SyncRoot)
            {
                if (authorityRefreshInProgress)
                {
                    authorityRefreshPending = true;
                    return;
                }

                authorityRefreshInProgress = true;
                authorityRefreshDeferred = false;
            }

            RunBoundedAuthorityRefresh();
        }

        private static void DrainDeferredAuthorityRefresh()
        {
            lock (SyncRoot)
            {
                if (!authorityRefreshDeferred || authorityRefreshInProgress)
                {
                    return;
                }

                authorityRefreshDeferred = false;
                authorityRefreshInProgress = true;
            }

            RunBoundedAuthorityRefresh();
        }

        private static void RunBoundedAuthorityRefresh()
        {
            try
            {
                for (int pass = 0; pass < MaxSynchronousAuthorityRefreshPasses; pass++)
                {
#if DEBUG
                    Interlocked.Increment(ref authorityRefreshPasses);
#endif
                    PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
                    lock (SyncRoot)
                    {
                        if (!authorityRefreshPending)
                        {
                            return;
                        }

                        authorityRefreshPending = false;
                    }
                }
            }
            finally
            {
                lock (SyncRoot)
                {
                    authorityRefreshInProgress = false;
                    if (authorityRefreshPending)
                    {
                        authorityRefreshPending = false;
                        authorityRefreshDeferred = true;
                    }
                }
            }
        }

        private sealed class CompositeSuspendScope : IDisposable
        {
            private readonly List<IDisposable> scopes;
            private readonly bool[] completed;
            private readonly object disposeSyncRoot = new object();
            private bool disposing;
            private bool disposed;

            internal CompositeSuspendScope(List<IDisposable> scopes)
            {
                this.scopes = scopes ?? new List<IDisposable>();
                completed = new bool[this.scopes.Count];
            }

            public void Dispose()
            {
                List<Exception> failures = null;
                lock (disposeSyncRoot)
                {
                    if (disposed || disposing)
                    {
                        return;
                    }

                    disposing = true;
                    try
                    {
                        for (int i = scopes.Count - 1; i >= 0; i--)
                        {
                            if (completed[i])
                            {
                                continue;
                            }

                            try
                            {
                                scopes[i]?.Dispose();
                                completed[i] = true;
                            }
                            catch (Exception ex)
                            {
                                (failures ?? (failures = new List<Exception>())).Add(ex);
                            }
                        }

                        disposed = completed.All(value => value);
                    }
                    finally
                    {
                        disposing = false;
                    }
                }

                if (failures == null || failures.Count == 0)
                {
                    return;
                }

                if (failures.Count == 1)
                {
                    throw failures[0];
                }

                throw new AggregateException(
                    "One or more external work-tab mirroring scopes could not be disposed.",
                    failures);
            }
        }

        internal readonly struct AuthoritativeStoreResult
        {
            private AuthoritativeStoreResult(
                IExternalWorkTabStore store,
                long generation,
                bool isCoherent)
            {
                Store = store;
                Generation = generation;
                IsCoherent = isCoherent;
            }

            internal IExternalWorkTabStore Store { get; }
            internal long Generation { get; }
            internal bool IsCoherent { get; }

            internal static AuthoritativeStoreResult Coherent(
                IExternalWorkTabStore store,
                long generation)
            {
                return new AuthoritativeStoreResult(store, generation, true);
            }

            internal static AuthoritativeStoreResult Unstable(long generation)
            {
                return new AuthoritativeStoreResult(null, generation, false);
            }
        }

        private readonly struct AvailableStoresResult
        {
            private AvailableStoresResult(
                List<IExternalWorkTabStore> stores,
                long generation,
                bool isCoherent)
            {
                Stores = stores;
                Generation = generation;
                IsCoherent = isCoherent;
            }

            internal List<IExternalWorkTabStore> Stores { get; }
            internal long Generation { get; }
            internal bool IsCoherent { get; }

            internal static AvailableStoresResult Coherent(
                List<IExternalWorkTabStore> stores,
                long generation)
            {
                return new AvailableStoresResult(stores, generation, true);
            }

            internal static AvailableStoresResult Unstable(long generation)
            {
                return new AvailableStoresResult(
                    new List<IExternalWorkTabStore>(),
                    generation,
                    false);
            }
        }
    }
}
