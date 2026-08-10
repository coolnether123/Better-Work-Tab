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
        private static readonly Dictionary<string, ImporterRegistration> Importers =
            new Dictionary<string, ImporterRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ImporterRegistration> HandoffImporters =
            new Dictionary<string, ImporterRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> StoreRegistrationGenerations =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static long registryGeneration;
        private static long nextStoreRegistrationGeneration;
        private static RegistrySnapshot availableSnapshot = RegistrySnapshot.Empty;
#if DEBUG
        private static long registryListBuilds;
        private static long storeProbes;
        private static long authorityRefreshPasses;
#endif
        private static int registeredStoreCount;
        private static int registeredStoreEntries;
        private static bool availabilityRefreshInProgress;
        private static bool availabilityRefreshPending;
        private static bool availabilityRefreshDeferred;
        private static bool authorityRefreshInProgress;
        private static bool authorityRefreshPending;
        private static bool authorityRefreshDeferred;
        private const int MaxSynchronousAuthorityRefreshPasses = 8;
        private const int MaxSynchronousAvailabilityRefreshPasses = 8;

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
        internal static int RegisteredStoreEntryCount => Volatile.Read(ref registeredStoreEntries);

        internal static bool AuthorityRefreshDeferred
        {
            get
            {
                lock (SyncRoot)
                {
                    return authorityRefreshDeferred || availabilityRefreshDeferred;
                }
            }
        }

#if DEBUG
        internal static long AuthorityRefreshPasses => Interlocked.Read(ref authorityRefreshPasses);
#endif

        internal static bool RegisterStore(IExternalWorkTabStore store)
        {
            string storeId;
            try
            {
                storeId = store?.StoreId?.Trim();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (!Stores.ContainsKey(storeId))
                {
                    registeredStoreEntries++;
                    Volatile.Write(ref registeredStoreEntries, registeredStoreEntries);
                }

                Stores[storeId] = store;
                StoreRegistrationGenerations[storeId] = ++nextStoreRegistrationGeneration;
                registryGeneration++;
            }

            RequestAvailabilityRefresh(generationChanged: false);
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
                    registeredStoreEntries--;
                    StoreRegistrationGenerations.Remove(storeId.Trim());
                    registryGeneration++;
                    Volatile.Write(ref registeredStoreEntries, registeredStoreEntries);
                }
            }

            if (removed)
            {
                RequestAvailabilityRefresh(generationChanged: false);
                NotifyAuthorityChangedAfterRegistryMutation();
            }

            return removed;
        }

        internal static bool RegisterImporter(IExternalWorkTabPriorityImporter importer)
        {
            string storeId;
            try
            {
                storeId = importer?.StoreId?.Trim();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                Importers[storeId] = new ImporterRegistration(importer);
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
                if (!result.IsCoherent)
                {
                    return false;
                }

                for (int i = 0; i < result.Stores.Length; i++)
                {
                    if (SafeIsSuspended(result.Stores[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static bool ShouldMirrorTimePrioritySchedules
        {
            get
            {
                AvailableStoresResult result = GetAvailableStores();
                if (!result.IsCoherent)
                {
                    return false;
                }

                for (int i = 0; i < result.Stores.Length; i++)
                {
                    if (SafeMirrorsTimePrioritySchedules(result.Stores[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static IDisposable SuspendAllMirroring()
        {
            AvailableStoresResult result = GetAvailableStores();
            return new CompositeSuspendScope(
                BuildSuspendScopes(result));
        }

        internal static void PushWorkType(Pawn pawn, WorkTypeDef workType)
        {
            AvailableStoresResult result = GetAvailableStores();
            if (!result.IsCoherent)
            {
                return;
            }

                for (int i = 0; i < result.Stores.Length; i++)
                {
                    IExternalWorkTabStore store = result.Stores[i];
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

                for (int i = 0; i < result.Stores.Length; i++)
                {
                    IExternalWorkTabStore store = result.Stores[i];
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

                for (int i = 0; i < result.Stores.Length; i++)
                {
                    IExternalWorkTabStore store = result.Stores[i];
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

                for (int i = 0; i < result.Stores.Length; i++)
                {
                    IExternalWorkTabStore store = result.Stores[i];
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
            for (int i = 0; i < result.Stores.Length; i++)
            {
                IExternalWorkTabStore store = result.Stores[i];
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
            if (store == null || !SafeIsAvailable(store))
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

        internal static int PushAllPawnsToStore(
            string storeId,
            IExternalWorkTabStore store,
            long expectedRegistrationGeneration)
        {
            if (!IsCurrentStoreRegistration(storeId, store, expectedRegistrationGeneration) ||
                !SafeIsAvailable(store))
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
                    string importerStoreId = importer?.StoreId?.Trim();
                    IExternalWorkTabStore currentStore;
                    long currentRegistrationGeneration;
                    if (!TryGetCurrentStoreRegistration(
                            importerStoreId,
                            out currentStore,
                            out currentRegistrationGeneration))
                    {
                        // An importer may be registered before its store. Keep the importer
                        // pending, but never let it read data until the matching store exists.
                        continue;
                    }

                    int changed = ImportFromStore(
                        importerStoreId,
                        currentStore,
                        currentRegistrationGeneration);
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

        // Preserve the original internal seam for integrations compiled against this assembly while
        // resolving the current store identity and registration token before the invocation path.
        internal static int ImportFromStore(string storeId)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return ImportFromAvailableImporter();
            }

            string normalizedStoreId = storeId.Trim();
            IExternalWorkTabStore currentStore;
            long currentRegistrationGeneration;
            if (!TryGetCurrentStoreRegistration(
                    normalizedStoreId,
                    out currentStore,
                    out currentRegistrationGeneration))
            {
                return 0;
            }

            return ImportFromStore(
                normalizedStoreId,
                currentStore,
                currentRegistrationGeneration);
        }

        internal static bool RegisterHandoffImporter(IExternalWorkTabHandoffImporter importer)
        {
            string storeId;
            try
            {
                storeId = importer?.StoreId?.Trim();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                HandoffImporters[storeId] = new ImporterRegistration(importer);
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

        internal static int ImportFromStore(
            string storeId,
            IExternalWorkTabStore expectedStore,
            long expectedRegistrationGeneration)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return ImportFromAvailableImporter();
            }

            IExternalWorkTabHandoffImporter handoffImporter;
            IExternalWorkTabPriorityImporter importer;
            string normalizedStoreId = storeId.Trim();
            lock (SyncRoot)
            {
                IExternalWorkTabStore currentStore;
                long currentRegistrationGeneration;
                if (!Stores.TryGetValue(normalizedStoreId, out currentStore) ||
                    !StoreRegistrationGenerations.TryGetValue(
                        normalizedStoreId,
                        out currentRegistrationGeneration) ||
                    !ReferenceEquals(currentStore, expectedStore) ||
                    currentRegistrationGeneration != expectedRegistrationGeneration)
                {
                    return 0;
                }

                ImporterRegistration registration;
                if (HandoffImporters.TryGetValue(normalizedStoreId, out registration))
                {
                    handoffImporter = registration.HandoffImporter;
                }
                else
                {
                    handoffImporter = null;
                }

                if (Importers.TryGetValue(normalizedStoreId, out registration))
                {
                    importer = registration.PriorityImporter;
                }
                else
                {
                    importer = null;
                }
            }

            if (handoffImporter != null)
            {
                try
                {
                    if (!handoffImporter.IsAvailable ||
                        !IsCurrentStoreRegistration(
                            normalizedStoreId,
                            expectedStore,
                            expectedRegistrationGeneration) ||
                        !IsCurrentHandoffImporterRegistration(
                            normalizedStoreId,
                            handoffImporter))
                    {
                        return 0;
                    }

                    return handoffImporter.ImportToBetterWorkTab();
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
                if (!importer.IsAvailable ||
                    !IsCurrentStoreRegistration(
                        normalizedStoreId,
                        expectedStore,
                        expectedRegistrationGeneration) ||
                    !IsCurrentPriorityImporterRegistration(
                        normalizedStoreId,
                        importer))
                {
                    return 0;
                }

                if (!importer.TryReadPriorityRecords(out records) ||
                    !IsCurrentStoreRegistration(
                        normalizedStoreId,
                        expectedStore,
                        expectedRegistrationGeneration) ||
                    !IsCurrentPriorityImporterRegistration(
                        normalizedStoreId,
                        importer))
                {
                    return 0;
                }

                return ImportWorkGiverPrioritySchedules(records);
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
            if (RegisteredStoreEntryCount == 0)
            {
                return AuthoritativeStoreResult.Coherent(null, null, 0, 0);
            }

            DrainDeferredAuthorityRefresh();
            DrainDeferredAvailabilityRefresh();
            RegistrySnapshot snapshot = Volatile.Read(ref availableSnapshot);
            long currentGeneration = RegistryGeneration;
            return snapshot.IsCoherent && snapshot.Generation == currentGeneration
                ? AuthoritativeStoreResult.Coherent(
                     snapshot.AuthoritativeStore,
                     snapshot.AuthoritativeStoreId,
                     snapshot.Generation,
                     snapshot.AuthoritativeStoreRegistrationGeneration)
                : AuthoritativeStoreResult.Unstable(currentGeneration);
        }

        private static AvailableStoresResult GetAvailableStores()
        {
            if (RegisteredStoreEntryCount == 0)
            {
                return AvailableStoresResult.Coherent(
                    RegistrySnapshot.Empty.AvailableStores,
                    RegistrySnapshot.Empty.Generation);
            }

            DrainDeferredAvailabilityRefresh();
            RegistrySnapshot snapshot = Volatile.Read(ref availableSnapshot);
            long currentGeneration = RegistryGeneration;
            return snapshot.IsCoherent && snapshot.Generation == currentGeneration
                ? AvailableStoresResult.Coherent(snapshot.AvailableStores, snapshot.Generation)
                : AvailableStoresResult.Unstable(currentGeneration);
        }

        private static List<IExternalWorkTabPriorityImporter> GetAvailableImporters()
        {
            List<IExternalWorkTabPriorityImporter> importers;
            lock (SyncRoot)
            {
                importers = Importers.Values
                    .Select(registration => registration.PriorityImporter)
                    .Where(importer => importer != null)
                    .ToList();
            }

            // Store identity is the importer binding. An importer can arrive before its store,
            // but it must remain dormant until that store is registered; this also prevents an
            // old importer from being selected after a same-ID store replacement.
            return importers
                .Where(importer =>
                {
                    string storeId;
                    try
                    {
                        storeId = importer?.StoreId?.Trim();
                    }
                    catch
                    {
                        return false;
                    }

                    IExternalWorkTabStore store;
                    long registrationGeneration;
                    return TryGetCurrentStoreRegistration(
                               storeId,
                               out store,
                               out registrationGeneration) &&
                           SafeIsAvailable(importer);
                })
                .OrderBy(importer => importer.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static void NotifyAvailabilityChanged()
        {
            RequestAvailabilityRefresh(generationChanged: true);
        }

        private static void RequestAvailabilityRefresh(bool generationChanged)
        {
            bool shouldRefresh = false;
            lock (SyncRoot)
            {
                if (generationChanged)
                {
                    registryGeneration++;
                }

                if (availabilityRefreshInProgress)
                {
                    availabilityRefreshPending = true;
                }
                else
                {
                    availabilityRefreshInProgress = true;
                    availabilityRefreshDeferred = false;
                    authorityRefreshDeferred = false;
                    shouldRefresh = true;
                }
            }

            if (shouldRefresh)
            {
                RunBoundedAvailabilityRefresh();
            }
        }

        private static void DrainDeferredAvailabilityRefresh()
        {
            bool shouldRefresh = false;
            lock (SyncRoot)
            {
                if (availabilityRefreshDeferred && !availabilityRefreshInProgress)
                {
                    availabilityRefreshDeferred = false;
                    availabilityRefreshInProgress = true;
                    shouldRefresh = true;
                }
            }

            if (shouldRefresh)
            {
                RunBoundedAvailabilityRefresh();
            }
        }

        private static void RunBoundedAvailabilityRefresh()
        {
            bool published = false;
            try
            {
                for (int attempt = 0; attempt < MaxSynchronousAvailabilityRefreshPasses; attempt++)
                {
                    lock (SyncRoot)
                    {
                        availabilityRefreshPending = false;
                    }

                    RecordRegistryListBuild();
                    KeyValuePair<string, IExternalWorkTabStore>[] stores;
                    long observedGeneration;
                    lock (SyncRoot)
                    {
                        observedGeneration = registryGeneration;
                        stores = Stores.ToArray();
                    }

                    var records = new List<StoreRecord>(stores.Length);
                    for (int i = 0; i < stores.Length; i++)
                    {
                        StoreRecord record;
                        if (TryBuildStoreRecord(stores[i].Key, stores[i].Value, out record))
                        {
                            records.Add(record);
                        }
                    }

                    records.Sort(CompareStoreRecords);
                    var availableStores = new IExternalWorkTabStore[records.Count];
                    IExternalWorkTabStore authoritativeStore = null;
                    string authoritativeStoreId = null;
                    long authoritativeStoreRegistrationGeneration = 0;
                    for (int i = 0; i < records.Count; i++)
                    {
                        availableStores[i] = records[i].Store;
                        if (authoritativeStore == null && records[i].PriorityAuthority == ExternalWorkTabPriorityAuthority.ExternalStore)
                        {
                            authoritativeStore = records[i].Store;
                            authoritativeStoreId = records[i].StoreId;
                            authoritativeStoreRegistrationGeneration = records[i].RegistrationGeneration;
                        }
                    }

                    var next = new RegistrySnapshot(
                        availableStores,
                        authoritativeStore,
                        authoritativeStoreId,
                        authoritativeStoreRegistrationGeneration,
                        observedGeneration,
                        true);
                    lock (SyncRoot)
                    {
                        if (registryGeneration != observedGeneration)
                        {
                            continue;
                        }

                        Volatile.Write(ref availableSnapshot, next);
                        Volatile.Write(ref registeredStoreCount, availableStores.Length);
                        published = true;
                        return;
                    }
                }
            }
            finally
            {
                lock (SyncRoot)
                {
                    availabilityRefreshInProgress = false;
                    if (!published && availabilityRefreshPending)
                    {
                        availabilityRefreshPending = false;
                        availabilityRefreshDeferred = true;
                        authorityRefreshDeferred = true;
                    }
                    else if (!published)
                    {
                        Volatile.Write(
                            ref availableSnapshot,
                            RegistrySnapshot.Unstable(registryGeneration));
                        availabilityRefreshDeferred = true;
                        authorityRefreshDeferred = true;
                    }
                    else if (published && !availabilityRefreshPending)
                    {
                        availabilityRefreshDeferred = false;
                        authorityRefreshDeferred = false;
                    }
                }
            }
        }

        internal static bool IsCurrentStoreRegistration(IExternalWorkTabStore expectedStore)
        {
            string storeId;
            try
            {
                storeId = expectedStore?.StoreId?.Trim();
            }
            catch
            {
                return false;
            }

            IExternalWorkTabStore currentStore;
            long registrationGeneration;
            return TryGetCurrentStoreRegistration(
                       storeId,
                       out currentStore,
                       out registrationGeneration) &&
                   ReferenceEquals(currentStore, expectedStore);
        }

        private static bool TryGetCurrentStoreRegistration(
            string storeId,
            out IExternalWorkTabStore currentStore,
            out long currentRegistrationGeneration)
        {
            currentStore = null;
            currentRegistrationGeneration = 0;
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            string normalizedStoreId = storeId.Trim();
            lock (SyncRoot)
            {
                return Stores.TryGetValue(normalizedStoreId, out currentStore) &&
                       StoreRegistrationGenerations.TryGetValue(
                           normalizedStoreId,
                           out currentRegistrationGeneration);
            }
        }

        private static bool IsCurrentStoreRegistration(
            string storeId,
            IExternalWorkTabStore expectedStore,
            long expectedRegistrationGeneration)
        {
            if (string.IsNullOrWhiteSpace(storeId) ||
                expectedStore == null ||
                expectedRegistrationGeneration == 0)
            {
                return false;
            }

            lock (SyncRoot)
            {
                IExternalWorkTabStore currentStore;
                long currentRegistrationGeneration;
                return Stores.TryGetValue(storeId.Trim(), out currentStore) &&
                       StoreRegistrationGenerations.TryGetValue(
                           storeId.Trim(),
                           out currentRegistrationGeneration) &&
                       ReferenceEquals(currentStore, expectedStore) &&
                       currentRegistrationGeneration == expectedRegistrationGeneration;
            }
        }

        private static bool IsCurrentPriorityImporterRegistration(
            string storeId,
            IExternalWorkTabPriorityImporter expectedImporter)
        {
            if (string.IsNullOrWhiteSpace(storeId) || expectedImporter == null)
            {
                return false;
            }

            lock (SyncRoot)
            {
                ImporterRegistration registration;
                return Importers.TryGetValue(storeId.Trim(), out registration) &&
                       ReferenceEquals(registration.PriorityImporter, expectedImporter);
            }
        }

        private static bool IsCurrentHandoffImporterRegistration(
            string storeId,
            IExternalWorkTabHandoffImporter expectedImporter)
        {
            if (string.IsNullOrWhiteSpace(storeId) || expectedImporter == null)
            {
                return false;
            }

            lock (SyncRoot)
            {
                ImporterRegistration registration;
                return HandoffImporters.TryGetValue(storeId.Trim(), out registration) &&
                       ReferenceEquals(registration.HandoffImporter, expectedImporter);
            }
        }

        private static bool TryBuildStoreRecord(
            string registeredId,
            IExternalWorkTabStore store,
            out StoreRecord record)
        {
            record = default(StoreRecord);
            long registrationGeneration;
            lock (SyncRoot)
            {
                StoreRegistrationGenerations.TryGetValue(registeredId, out registrationGeneration);
            }

            try
            {
                RecordStoreProbe();
                if (store == null || !store.IsAvailable)
                {
                    return false;
                }

                string storeId = string.IsNullOrWhiteSpace(store.StoreId) ? registeredId : store.StoreId.Trim();
                string displayName = store.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = storeId;
                }

                record = new StoreRecord(
                    store,
                    storeId,
                    displayName,
                    store.SortOrder,
                    store.PriorityAuthority,
                    registrationGeneration);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int CompareStoreRecords(StoreRecord left, StoreRecord right)
        {
            int result = left.SortOrder.CompareTo(right.SortOrder);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            return result != 0
                ? result
                : string.Compare(left.StoreId, right.StoreId, StringComparison.OrdinalIgnoreCase);
        }

        private static List<IDisposable> BuildSuspendScopes(AvailableStoresResult result)
        {
            var scopes = new List<IDisposable>();
            if (!result.IsCoherent)
            {
                return scopes;
            }

            for (int i = 0; i < result.Stores.Length; i++)
            {
                IDisposable scope = SafeSuspend(result.Stores[i]);
                if (scope != null)
                {
                    scopes.Add(scope);
                }
            }

            return scopes;
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
                    PriorityAuthorityBroker.NotifyPotentialAuthorityChanged(refreshRegistry: false);
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
                List<int> pending = new List<int>();
                lock (disposeSyncRoot)
                {
                    if (disposed || disposing)
                    {
                        return;
                    }

                    disposing = true;
                    for (int i = scopes.Count - 1; i >= 0; i--)
                    {
                        if (!completed[i])
                        {
                            pending.Add(i);
                        }
                    }
                }

                // Child callbacks must never run while disposeSyncRoot is held. Each successful child
                // is committed independently so a later call retries only the failures.
                for (int i = 0; i < pending.Count; i++)
                {
                    int scopeIndex = pending[i];
                    try
                    {
                        scopes[scopeIndex]?.Dispose();
                        lock (disposeSyncRoot)
                        {
                            completed[scopeIndex] = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        (failures ?? (failures = new List<Exception>())).Add(ex);
                    }
                }

                lock (disposeSyncRoot)
                {
                    disposing = false;
                    disposed = true;
                    for (int i = 0; i < completed.Length; i++)
                    {
                        if (!completed[i])
                        {
                            disposed = false;
                            break;
                        }
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
                string storeId,
                long generation,
                long registrationGeneration,
                bool isCoherent)
            {
                Store = store;
                StoreId = storeId;
                Generation = generation;
                RegistrationGeneration = registrationGeneration;
                IsCoherent = isCoherent;
            }

            internal IExternalWorkTabStore Store { get; }
            internal string StoreId { get; }
            internal long Generation { get; }
            internal long RegistrationGeneration { get; }
            internal bool IsCoherent { get; }

            internal static AuthoritativeStoreResult Coherent(
                IExternalWorkTabStore store,
                string storeId,
                long generation,
                long registrationGeneration)
            {
                return new AuthoritativeStoreResult(
                    store,
                    storeId,
                    generation,
                    registrationGeneration,
                    true);
            }

            internal static AuthoritativeStoreResult Unstable(long generation)
            {
                return new AuthoritativeStoreResult(null, null, generation, 0, false);
            }
        }

        private readonly struct StoreRecord
        {
            internal StoreRecord(
                IExternalWorkTabStore store,
                string storeId,
                string displayName,
                int sortOrder,
                ExternalWorkTabPriorityAuthority priorityAuthority,
                long registrationGeneration)
            {
                Store = store;
                StoreId = storeId;
                DisplayName = displayName;
                SortOrder = sortOrder;
                PriorityAuthority = priorityAuthority;
                RegistrationGeneration = registrationGeneration;
            }

            internal readonly IExternalWorkTabStore Store;
            internal readonly string StoreId;
            internal readonly string DisplayName;
            internal readonly int SortOrder;
            internal readonly ExternalWorkTabPriorityAuthority PriorityAuthority;
            internal readonly long RegistrationGeneration;
        }

        private sealed class RegistrySnapshot
        {
            internal static readonly RegistrySnapshot Empty =
                new RegistrySnapshot(
                    new IExternalWorkTabStore[0],
                    null,
                    null,
                    0,
                    0,
                    true);

            internal RegistrySnapshot(
                IExternalWorkTabStore[] availableStores,
                IExternalWorkTabStore authoritativeStore,
                string authoritativeStoreId,
                long authoritativeStoreRegistrationGeneration,
                long generation,
                bool isCoherent)
            {
                AvailableStores = availableStores;
                AuthoritativeStore = authoritativeStore;
                AuthoritativeStoreId = authoritativeStoreId;
                AuthoritativeStoreRegistrationGeneration = authoritativeStoreRegistrationGeneration;
                Generation = generation;
                IsCoherent = isCoherent;
            }

            internal readonly IExternalWorkTabStore[] AvailableStores;
            internal readonly IExternalWorkTabStore AuthoritativeStore;
            internal readonly string AuthoritativeStoreId;
            internal readonly long AuthoritativeStoreRegistrationGeneration;
            internal readonly long Generation;
            internal readonly bool IsCoherent;

            internal static RegistrySnapshot Unstable(long generation)
            {
                return new RegistrySnapshot(
                    new IExternalWorkTabStore[0],
                    null,
                    null,
                    0,
                    generation,
                    false);
            }
        }

        private readonly struct ImporterRegistration
        {
            internal ImporterRegistration(object importer)
            {
                PriorityImporter = importer as IExternalWorkTabPriorityImporter;
                HandoffImporter = importer as IExternalWorkTabHandoffImporter;
            }

            internal readonly IExternalWorkTabPriorityImporter PriorityImporter;
            internal readonly IExternalWorkTabHandoffImporter HandoffImporter;
        }

        private readonly struct AvailableStoresResult
        {
            private AvailableStoresResult(
                IExternalWorkTabStore[] stores,
                long generation,
                bool isCoherent)
            {
                Stores = stores;
                Generation = generation;
                IsCoherent = isCoherent;
            }

            internal IExternalWorkTabStore[] Stores { get; }
            internal long Generation { get; }
            internal bool IsCoherent { get; }

            internal static AvailableStoresResult Coherent(
                IExternalWorkTabStore[] stores,
                long generation)
            {
                return new AvailableStoresResult(stores, generation, true);
            }

            internal static AvailableStoresResult Unstable(long generation)
            {
                return new AvailableStoresResult(
                    new IExternalWorkTabStore[0],
                    generation,
                    false);
            }
        }
    }
}
