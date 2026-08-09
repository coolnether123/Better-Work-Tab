using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.API;
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

        internal static bool RegisterStore(IExternalWorkTabStore store)
        {
            if (store == null || string.IsNullOrWhiteSpace(store.StoreId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                Stores[store.StoreId.Trim()] = store;
            }

            return true;
        }

        internal static bool UnregisterStore(string storeId)
        {
            if (string.IsNullOrWhiteSpace(storeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                return Stores.Remove(storeId.Trim());
            }
        }

        internal static bool RegisterImporter(IExternalWorkTabPriorityImporter importer)
        {
            if (importer == null || string.IsNullOrWhiteSpace(importer.StoreId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                Importers[importer.StoreId.Trim()] = importer;
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
            return GetAvailableStores()
                .Where(store => SafeAuthority(store) == ExternalWorkTabPriorityAuthority.ExternalStore)
                .OrderBy(store => store.SortOrder)
                .ThenBy(store => store.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        internal static bool AnyStoreSuspended => GetAvailableStores().Any(store => SafeIsSuspended(store));

        internal static bool ShouldMirrorTimePrioritySchedules =>
            GetAvailableStores().Any(store => SafeMirrorsTimePrioritySchedules(store));

        internal static IDisposable SuspendAllMirroring()
        {
            return new CompositeSuspendScope(
                GetAvailableStores()
                    .Select(SafeSuspend)
                    .Where(scope => scope != null)
                    .ToList());
        }

        internal static void PushWorkType(Pawn pawn, WorkTypeDef workType)
        {
            foreach (IExternalWorkTabStore store in GetAvailableStores())
            {
                SafeRun(store, () => store.PushWorkType(pawn, workType));
            }
        }

        internal static void PushWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
            foreach (IExternalWorkTabStore store in GetAvailableStores())
            {
                SafeRun(store, () => store.PushWorkGiver(pawn, workGiver));
            }
        }

        internal static void PushWorkTypeForAllPawns(WorkTypeDef workType)
        {
            foreach (IExternalWorkTabStore store in GetAvailableStores())
            {
                SafeRun(store, () => store.PushWorkTypeForAllPawns(workType));
            }
        }

        internal static void PushWorkGiverForAllPawns(WorkGiverDef workGiver)
        {
            foreach (IExternalWorkTabStore store in GetAvailableStores())
            {
                SafeRun(store, () => store.PushWorkGiverForAllPawns(workGiver));
            }
        }

        internal static int PushAllPawns()
        {
            int pushed = 0;
            foreach (IExternalWorkTabStore store in GetAvailableStores())
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

        internal static bool TryGetWorkTypePriority(
            Pawn pawn,
            WorkTypeDef workType,
            int hour,
            out int priority)
        {
            priority = 0;
            IExternalWorkTabStore store = GetAuthoritativeStore();
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
            priority = 0;
            IExternalWorkTabStore store = GetAuthoritativeStore();
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

            lock (SyncRoot)
            {
                HandoffImporters[importer.StoreId.Trim()] = importer;
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

        private static List<IExternalWorkTabStore> GetAvailableStores()
        {
            lock (SyncRoot)
            {
                return Stores.Values
                    .Where(SafeIsAvailable)
                    .OrderBy(store => store.SortOrder)
                    .ThenBy(store => store.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static List<IExternalWorkTabPriorityImporter> GetAvailableImporters()
        {
            lock (SyncRoot)
            {
                return Importers.Values
                    .Where(SafeIsAvailable)
                    .OrderBy(importer => importer.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static bool SafeIsAvailable(IExternalWorkTabStore store)
        {
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
            try
            {
                return store?.PriorityAuthority ?? ExternalWorkTabPriorityAuthority.NoOpinion;
            }
            catch
            {
                return ExternalWorkTabPriorityAuthority.NoOpinion;
            }
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

        private sealed class CompositeSuspendScope : IDisposable
        {
            private readonly List<IDisposable> scopes;
            private bool disposed;

            internal CompositeSuspendScope(List<IDisposable> scopes)
            {
                this.scopes = scopes ?? new List<IDisposable>();
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                for (int i = scopes.Count - 1; i >= 0; i--)
                {
                    scopes[i]?.Dispose();
                }
            }
        }
    }
}
