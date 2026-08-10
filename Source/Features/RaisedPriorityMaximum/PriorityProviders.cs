using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Better_Work_Tab.API;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using HarmonyLib;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class PriorityProviderRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, IMaxPriorityProvider> Providers =
            new Dictionary<string, IMaxPriorityProvider>(StringComparer.OrdinalIgnoreCase);
        private static readonly IMaxPriorityProvider VanillaProvider = new VanillaPriorityProvider();
        private static readonly IMaxPriorityProvider BetterWorkTabProvider =
            new BetterWorkTabPriorityProvider();
        private static readonly IMaxPriorityProvider[] SafeFallbackProviders =
        {
            VanillaProvider,
            BetterWorkTabProvider
        };
        private static readonly PriorityProviderRecord[] SafeFallbackProviderRecords =
        {
            new PriorityProviderRecord(
                VanillaProvider,
                PriorityConstants.VanillaProviderId,
                "Vanilla RimWorld",
                0,
                PriorityConstants.VanillaMax,
                PriorityConstants.VanillaDefaultEnabled),
            new PriorityProviderRecord(
                BetterWorkTabProvider,
                PriorityConstants.BwtProviderId,
                "Better Work Tab",
                int.MaxValue,
                PriorityConstants.ExtendedHardMax,
                PriorityConstants.VanillaDefaultEnabled)
        };

        private static bool initialized;
        private static long providerGeneration;
        private static long availabilityGeneration;
        private static int externalProviderCount;
        private static int registeredProviderCount;
        private static ProviderAvailabilitySnapshot availableSnapshot =
            ProviderAvailabilitySnapshot.Empty;
        private static RuntimeProviderPolicy runtimePolicy;
        private static bool availabilityRefreshInProgress;
        private static bool availabilityRefreshPending;
        private static bool availabilityRefreshDeferred;
        private static int discoveryInProgress;
        private const int MaxSynchronousAvailabilityRefreshPasses = 8;
        private static readonly int[] SafeRuntimeAutoMax = CreateDefaultRuntimeAutoMax();

        internal static long Generation => Interlocked.Read(ref providerGeneration);

        internal static long AvailabilityGeneration => Interlocked.Read(ref availabilityGeneration);

        internal static int ExternalProviderCount
        {
            get
            {
                ProviderAvailabilitySnapshot snapshot = Volatile.Read(ref availableSnapshot);
                return snapshot.IsCoherent &&
                       snapshot.RegistrationGeneration == Generation &&
                       snapshot.AvailabilityGeneration == AvailabilityGeneration
                    ? Volatile.Read(ref externalProviderCount)
                    : 0;
            }
        }

        internal static int RegisteredProviderCount => Volatile.Read(ref registeredProviderCount);

        internal static int GetRuntimeAutoMax(int requestedPriority)
        {
            int index = requestedPriority < 0
                ? 0
                : requestedPriority >= PriorityConstants.ExtendedHardMax
                    ? PriorityConstants.ExtendedHardMax
                    : requestedPriority;
            RuntimeProviderPolicy policy = Volatile.Read(ref runtimePolicy);
            return policy != null &&
                   policy.RegistrationGeneration == Generation &&
                   policy.AvailabilityGeneration == AvailabilityGeneration
                ? policy.AutoMaxByRequestedPriority[index]
                : PriorityConstants.VanillaMax;
        }

        internal static int RuntimeSelectedProviderMax
        {
            get
            {
                RuntimeProviderPolicy policy = Volatile.Read(ref runtimePolicy);
                return policy != null &&
                       policy.RegistrationGeneration == Generation &&
                       policy.AvailabilityGeneration == AvailabilityGeneration
                    ? policy.SelectedProviderMax
                    : PriorityConstants.VanillaMax;
            }
        }

        internal static void EnsureInitialized()
        {
            bool shouldDiscover;
            if (Volatile.Read(ref initialized))
            {
                return;
            }

            lock (SyncRoot)
            {
                if (initialized)
                {
                    return;
                }

                Providers[PriorityConstants.VanillaProviderId] = VanillaProvider;
                Providers[PriorityConstants.BwtProviderId] = BetterWorkTabProvider;
                Interlocked.Exchange(ref discoveryInProgress, 1);
                initialized = true;
                shouldDiscover = true;
            }

            // Discovery can call back into RegisterProvider. Keep every third-party probe outside
            // SyncRoot, including the initial discovery pass.
            if (shouldDiscover)
            {
                try
                {
                    ReflectionPriorityProviderDiscovery.RegisterKnownProviders();
                }
                finally
                {
                    Interlocked.Exchange(ref discoveryInProgress, 0);
                }

                RequestAvailabilityRefresh();
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

                    KeyValuePair<string, IMaxPriorityProvider>[] providers;
                    long observedRegistrationGeneration;
                    long observedAvailabilityGeneration;
                    lock (SyncRoot)
                    {
                        observedRegistrationGeneration = providerGeneration;
                        observedAvailabilityGeneration = availabilityGeneration;
                        providers = Providers.ToArray();
                    }

                    var records = new List<PriorityProviderRecord>(providers.Length);
                    for (int i = 0; i < providers.Length; i++)
                    {
                        PriorityProviderRecord record;
                        if (TryBuildProviderRecord(providers[i].Key, providers[i].Value, out record))
                        {
                            records.Add(record);
                        }
                    }

                    records.Sort(CompareProviderRecords);
                    var recordArray = records.ToArray();
                    var providerArray = new IMaxPriorityProvider[recordArray.Length];
                    int usableExternalCount = 0;
                    for (int i = 0; i < recordArray.Length; i++)
                    {
                        providerArray[i] = recordArray[i].Provider;
                        if (!IsBuiltInProviderId(recordArray[i].ProviderId))
                        {
                            usableExternalCount++;
                        }
                    }

                    var next = new ProviderAvailabilitySnapshot(
                        recordArray,
                        providerArray,
                        observedRegistrationGeneration,
                        observedAvailabilityGeneration,
                        true);
                    lock (SyncRoot)
                    {
                        if (providerGeneration != observedRegistrationGeneration ||
                            availabilityGeneration != observedAvailabilityGeneration)
                        {
                            continue;
                        }

                        Volatile.Write(ref availableSnapshot, next);
                        Volatile.Write(ref externalProviderCount, usableExternalCount);
                        Volatile.Write(ref runtimePolicy, null);
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
                    if (!published)
                    {
                        availabilityRefreshPending = false;
                        availabilityRefreshDeferred = true;
                        Volatile.Write(
                            ref availableSnapshot,
                            ProviderAvailabilitySnapshot.Unstable(
                                providerGeneration,
                                availabilityGeneration));
                        Volatile.Write(ref externalProviderCount, 0);
                        Volatile.Write(ref runtimePolicy, null);
                    }
                    else if (!availabilityRefreshPending)
                    {
                        availabilityRefreshDeferred = false;
                    }
                }
            }
        }

        internal static void EnsureRuntimePolicy(int autoMaxPriority, string selectedProviderId)
        {
            if (RegisteredProviderCount == 0)
            {
                Volatile.Write(ref runtimePolicy, null);
                return;
            }

            if (Volatile.Read(ref availabilityRefreshDeferred))
            {
                DrainDeferredAvailabilityRefresh();
            }

            autoMaxPriority = PriorityAuthorityBroker.ClampMaxPriority(autoMaxPriority);
            long registrationGeneration = Generation;
            long currentAvailabilityGeneration = AvailabilityGeneration;
            ProviderAvailabilitySnapshot available = Volatile.Read(ref availableSnapshot);
            if (!available.IsCoherent ||
                available.RegistrationGeneration != registrationGeneration ||
                available.AvailabilityGeneration != currentAvailabilityGeneration ||
                ExternalProviderCount == 0)
            {
                if (Generation == registrationGeneration &&
                    AvailabilityGeneration == currentAvailabilityGeneration)
                {
                    Volatile.Write(
                        ref runtimePolicy,
                        new RuntimeProviderPolicy(
                            registrationGeneration,
                            currentAvailabilityGeneration,
                            autoMaxPriority,
                            selectedProviderId,
                            PriorityConstants.VanillaMax,
                            SafeRuntimeAutoMax));
                }

                return;
            }

            RuntimeProviderPolicy existing = Volatile.Read(ref runtimePolicy);
            if (existing != null &&
                existing.RegistrationGeneration == registrationGeneration &&
                existing.AvailabilityGeneration == currentAvailabilityGeneration &&
                existing.AutoMaxPriority == autoMaxPriority &&
                string.Equals(existing.SelectedProviderId, selectedProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PriorityProviderRecord[] records = available.Records;
            int[] autoMaxByRequestedPriority = CreateDefaultRuntimeAutoMax();
            int selectedMax = PriorityConstants.VanillaMax;
            for (int i = 0; i < records.Length; i++)
            {
                PriorityProviderRecord record = records[i];
                if (string.Equals(record.ProviderId, selectedProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedMax = record.MaxPriority;
                }
            }

            for (int requested = 0; requested <= PriorityConstants.ExtendedHardMax; requested++)
            {
                int required = Math.Max(PriorityConstants.VanillaMax, requested);
                int bestMax = int.MaxValue;
                string bestDisplayName = null;
                string bestProviderId = null;
                for (int i = 0; i < records.Length; i++)
                {
                    PriorityProviderRecord record = records[i];
                    if (IsBuiltInProviderId(record.ProviderId))
                    {
                        continue;
                    }

                    int limitedMax = Math.Min(record.MaxPriority, autoMaxPriority);
                    if (limitedMax < required || limitedMax > bestMax ||
                        (limitedMax == bestMax && CompareProviderNames(record.DisplayName, record.ProviderId, bestDisplayName, bestProviderId) >= 0))
                    {
                        continue;
                    }

                    bestMax = limitedMax;
                    bestDisplayName = record.DisplayName;
                    bestProviderId = record.ProviderId;
                }

                autoMaxByRequestedPriority[requested] = bestMax == int.MaxValue
                    ? required <= PriorityConstants.VanillaMax ? PriorityConstants.VanillaMax : autoMaxPriority
                    : bestMax;
            }

            Volatile.Write(
                ref runtimePolicy,
                new RuntimeProviderPolicy(
                    registrationGeneration,
                    currentAvailabilityGeneration,
                    autoMaxPriority,
                    selectedProviderId,
                    selectedMax,
                    autoMaxByRequestedPriority));
        }

        internal static IEnumerable<IMaxPriorityProvider> GetAvailableProviders()
        {
            EnsureInitialized();
            if (Volatile.Read(ref discoveryInProgress) != 0)
            {
                return SafeFallbackProviders;
            }

            if (Volatile.Read(ref availabilityRefreshDeferred))
            {
                DrainDeferredAvailabilityRefresh();
            }

            ProviderAvailabilitySnapshot snapshot = Volatile.Read(ref availableSnapshot);
            return snapshot.IsCoherent &&
                   snapshot.RegistrationGeneration == Generation &&
                   snapshot.AvailabilityGeneration == AvailabilityGeneration
                ? snapshot.Providers
                : SafeFallbackProviders;
        }

        internal static PriorityProviderRecord[] GetAvailableProviderRecords()
        {
            EnsureInitialized();
            if (Volatile.Read(ref discoveryInProgress) != 0)
            {
                return SafeFallbackProviderRecords;
            }

            if (Volatile.Read(ref availabilityRefreshDeferred))
            {
                DrainDeferredAvailabilityRefresh();
            }

            ProviderAvailabilitySnapshot snapshot = Volatile.Read(ref availableSnapshot);
            return snapshot.IsCoherent &&
                   snapshot.RegistrationGeneration == Generation &&
                   snapshot.AvailabilityGeneration == AvailabilityGeneration
                ? snapshot.Records
                : SafeFallbackProviderRecords;
        }

        internal static bool TryFindByProviderId(string providerId, out IMaxPriorityProvider provider)
        {
            provider = null;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return false;
            }

            EnsureInitialized();
            lock (SyncRoot)
            {
                return Providers.TryGetValue(providerId.Trim(), out provider);
            }
        }

        internal static bool IsCurrentProviderRegistration(IMaxPriorityProvider expectedProvider)
        {
            string providerId;
            try
            {
                providerId = expectedProvider?.ProviderId?.Trim();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(providerId))
            {
                return false;
            }

            EnsureInitialized();
            lock (SyncRoot)
            {
                IMaxPriorityProvider currentProvider;
                return Providers.TryGetValue(providerId, out currentProvider) &&
                       ReferenceEquals(currentProvider, expectedProvider);
            }
        }

        internal static bool TryFindAvailableProviderRecord(
            string providerId,
            out PriorityProviderRecord record)
        {
            record = default(PriorityProviderRecord);
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return false;
            }

            PriorityProviderRecord[] records = GetAvailableProviderRecords();
            for (int i = 0; i < records.Length; i++)
            {
                if (string.Equals(records[i].ProviderId, providerId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    record = records[i];
                    return true;
                }
            }

            return false;
        }

        public static bool RegisterProvider(IMaxPriorityProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.ProviderId))
            {
                return false;
            }

            string providerId = provider.ProviderId.Trim();
            if (IsBuiltInProviderId(providerId))
            {
                return false;
            }

            EnsureInitialized();
            lock (SyncRoot)
            {
                Providers[providerId] = provider;
                providerGeneration++;
                Volatile.Write(ref registeredProviderCount, Providers.Count - 2);
            }

            NotifyAvailabilityChanged();
            PriorityAuthorityBroker.InvalidateCaches(refreshProviderRegistry: false);
            return true;
        }

        public static bool UnregisterProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || IsBuiltInProviderId(providerId))
            {
                return false;
            }

            EnsureInitialized();
            bool removed;
            lock (SyncRoot)
            {
                removed = Providers.Remove(providerId.Trim());
                if (removed)
                {
                    providerGeneration++;
                    Volatile.Write(ref registeredProviderCount, Providers.Count - 2);
                }
            }

            if (removed)
            {
                NotifyAvailabilityChanged();
                PriorityAuthorityBroker.InvalidateCaches(refreshProviderRegistry: false);
            }

            return removed;
        }

        internal static void NotifyAvailabilityChanged()
        {
            if (!Volatile.Read(ref initialized) && RegisteredProviderCount == 0)
            {
                return;
            }

            EnsureInitialized();
            RequestAvailabilityRefresh();
        }

        private static void RequestAvailabilityRefresh()
        {
            if (Volatile.Read(ref discoveryInProgress) != 0)
            {
                return;
            }

            lock (SyncRoot)
            {
                Interlocked.Increment(ref availabilityGeneration);
            }

            bool shouldRefresh = false;
            lock (SyncRoot)
            {
                if (availabilityRefreshInProgress)
                {
                    availabilityRefreshPending = true;
                }
                else
                {
                    availabilityRefreshInProgress = true;
                    availabilityRefreshDeferred = false;
                    shouldRefresh = true;
                }
            }

            if (shouldRefresh)
            {
                RunBoundedAvailabilityRefresh();
            }
        }

        internal static void InvalidateRuntimePolicy()
        {
            Volatile.Write(ref runtimePolicy, null);
        }

        private static bool TryBuildProviderRecord(
            string registeredId,
            IMaxPriorityProvider provider,
            out PriorityProviderRecord record)
        {
            record = default(PriorityProviderRecord);
            try
            {
                if (provider == null || !provider.IsAvailable)
                {
                    return false;
                }

                int maxPriority;
                if (!provider.TryGetMaxPriority(out maxPriority))
                {
                    return false;
                }

                string providerId = string.IsNullOrWhiteSpace(provider.ProviderId)
                    ? registeredId
                    : provider.ProviderId.Trim();
                string displayName = provider.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = providerId;
                }

                int defaultPriority;
                if (!provider.TryGetDefaultEnabledPriority(out defaultPriority))
                {
                    defaultPriority = PriorityConstants.VanillaDefaultEnabled;
                }

                record = new PriorityProviderRecord(
                    provider,
                    providerId,
                    displayName,
                    provider.SortOrder,
                    PriorityAuthorityBroker.ClampMaxPriority(maxPriority),
                    PriorityAuthorityBroker.ClampDefaultEnabledPriority(defaultPriority, maxPriority));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsBuiltInProviderId(string providerId)
        {
            return IsProviderId(providerId, PriorityConstants.AutoProviderId) ||
                   IsProviderId(providerId, PriorityConstants.VanillaProviderId) ||
                   IsProviderId(providerId, PriorityConstants.BwtProviderId);
        }

        private static int[] CreateDefaultRuntimeAutoMax()
        {
            int[] defaults = new int[PriorityConstants.ExtendedHardMax + 1];
            for (int i = 0; i < defaults.Length; i++)
            {
                defaults[i] = PriorityConstants.VanillaMax;
            }

            return defaults;
        }

        private static int CompareProviderRecords(PriorityProviderRecord left, PriorityProviderRecord right)
        {
            int result = left.SortOrder.CompareTo(right.SortOrder);
            return result != 0 ? result : CompareProviderNames(left.DisplayName, left.ProviderId, right.DisplayName, right.ProviderId);
        }

        private static int CompareProviderNames(
            string leftDisplayName,
            string leftProviderId,
            string rightDisplayName,
            string rightProviderId)
        {
            int result = string.Compare(leftDisplayName, rightDisplayName, StringComparison.OrdinalIgnoreCase);
            return result != 0
                ? result
                : string.Compare(leftProviderId, rightProviderId, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ProviderAvailabilitySnapshot
        {
            internal static readonly ProviderAvailabilitySnapshot Empty =
                new ProviderAvailabilitySnapshot(
                    new PriorityProviderRecord[0],
                    new IMaxPriorityProvider[0],
                    0,
                    0,
                    true);

            internal ProviderAvailabilitySnapshot(
                PriorityProviderRecord[] records,
                IMaxPriorityProvider[] providers,
                long registrationGeneration,
                long availabilityGeneration,
                bool isCoherent)
            {
                Records = records;
                Providers = providers;
                RegistrationGeneration = registrationGeneration;
                AvailabilityGeneration = availabilityGeneration;
                IsCoherent = isCoherent;
            }

            internal readonly PriorityProviderRecord[] Records;
            internal readonly IMaxPriorityProvider[] Providers;
            internal readonly long RegistrationGeneration;
            internal readonly long AvailabilityGeneration;
            internal readonly bool IsCoherent;

            internal static ProviderAvailabilitySnapshot Unstable(
                long registrationGeneration,
                long availabilityGeneration)
            {
                return new ProviderAvailabilitySnapshot(
                    new PriorityProviderRecord[0],
                    new IMaxPriorityProvider[0],
                    registrationGeneration,
                    availabilityGeneration,
                    false);
            }
        }

        private sealed class RuntimeProviderPolicy
        {
            internal RuntimeProviderPolicy(
                long registrationGeneration,
                long availabilityGeneration,
                int autoMaxPriority,
                string selectedProviderId,
                int selectedProviderMax,
                int[] autoMaxByRequestedPriority)
            {
                RegistrationGeneration = registrationGeneration;
                AvailabilityGeneration = availabilityGeneration;
                AutoMaxPriority = autoMaxPriority;
                SelectedProviderId = selectedProviderId;
                SelectedProviderMax = selectedProviderMax;
                AutoMaxByRequestedPriority = autoMaxByRequestedPriority;
            }

            internal readonly long RegistrationGeneration;
            internal readonly long AvailabilityGeneration;
            internal readonly int AutoMaxPriority;
            internal readonly string SelectedProviderId;
            internal readonly int SelectedProviderMax;
            internal readonly int[] AutoMaxByRequestedPriority;
        }

        private static bool IsProviderId(string providerId, string expectedProviderId)
        {
            return string.Equals(providerId, expectedProviderId, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal readonly struct PriorityProviderRecord
    {
        internal PriorityProviderRecord(
            IMaxPriorityProvider provider,
            string providerId,
            string displayName,
            int sortOrder,
            int maxPriority,
            int defaultEnabledPriority)
        {
            Provider = provider;
            ProviderId = providerId;
            DisplayName = displayName;
            SortOrder = sortOrder;
            MaxPriority = maxPriority;
            DefaultEnabledPriority = defaultEnabledPriority;
        }

        internal readonly IMaxPriorityProvider Provider;
        internal readonly string ProviderId;
        internal readonly string DisplayName;
        internal readonly int SortOrder;
        internal readonly int MaxPriority;
        internal readonly int DefaultEnabledPriority;
    }

    internal sealed class VanillaPriorityProvider : IMaxPriorityProvider
    {
        public string ProviderId => PriorityConstants.VanillaProviderId;
        public string DisplayName => "Vanilla RimWorld";
        public bool IsAvailable => true;
        public int SortOrder => 0;

        public bool TryGetMaxPriority(out int maxPriority)
        {
            maxPriority = PriorityConstants.VanillaMax;
            return true;
        }

        public bool TryGetDefaultEnabledPriority(out int defaultEnabledPriority)
        {
            defaultEnabledPriority = PriorityConstants.VanillaDefaultEnabled;
            return true;
        }
    }

    internal sealed class BetterWorkTabPriorityProvider : IMaxPriorityProvider
    {
        public string ProviderId => PriorityConstants.BwtProviderId;
        public string DisplayName => "Better Work Tab";
        public bool IsAvailable => true;
        public int SortOrder => int.MaxValue;

        public bool TryGetMaxPriority(out int maxPriority)
        {
            maxPriority = PriorityAuthorityBroker.GetBetterWorkTabConfiguredMaxPriority();
            return true;
        }

        public bool TryGetDefaultEnabledPriority(out int defaultEnabledPriority)
        {
            defaultEnabledPriority = PriorityAuthorityBroker.ClampDefaultEnabledPriority(
                PriorityConstants.VanillaDefaultEnabled,
                PriorityAuthorityBroker.GetBetterWorkTabConfiguredMaxPriority());
            return true;
        }
    }

    internal sealed class RegisteredPriorityProvider : IMaxPriorityProvider
    {
        private readonly string providerId;
        private readonly string displayName;
        private readonly int maxPriority;
        private readonly int defaultEnabledPriority;
        private readonly int sortOrder;

        internal RegisteredPriorityProvider(
            string providerId,
            string displayName,
            int maxPriority,
            int defaultEnabledPriority,
            int sortOrder)
        {
            this.providerId = providerId?.Trim() ?? string.Empty;
            this.displayName = string.IsNullOrWhiteSpace(displayName) ? this.providerId : displayName.Trim();
            this.maxPriority = maxPriority;
            this.defaultEnabledPriority = defaultEnabledPriority;
            this.sortOrder = sortOrder;
        }

        public string ProviderId => providerId;
        public string DisplayName => displayName;
        public bool IsAvailable => maxPriority > PriorityConstants.Disabled;
        public int SortOrder => sortOrder;

        public bool TryGetMaxPriority(out int priority)
        {
            priority = PriorityAuthorityBroker.ClampMaxPriority(maxPriority);
            return priority > PriorityConstants.Disabled;
        }

        public bool TryGetDefaultEnabledPriority(out int priority)
        {
            priority = PriorityAuthorityBroker.ClampDefaultEnabledPriority(
                defaultEnabledPriority,
                PriorityAuthorityBroker.ClampMaxPriority(maxPriority));
            return true;
        }
    }

    internal static class PriorityProviderIntegrationCatalog
    {
        internal const string PriorityMasterProviderId = "prioritymaster";
        internal const string PriorityMasterDisplayName = "PriorityMaster";

        internal const string MechWorkTabProviderId = "mech-work-tab";
        internal const string MechWorkTabDisplayName = "Mech Work Tab";

        internal const string ClockworkProviderId = "clockwork";
        internal const string ClockworkDisplayName = "Clockwork";

        internal const string PawnCentricWorkPrioritiesProviderId = "pawn-centric-work-priorities";
        internal const string PawnCentricWorkPrioritiesDisplayName = "Pawn Centric Work Priorities";

        internal const string FluffyWorkTabProviderId = "fluffy-work-tab";
        internal const string FluffyWorkTabDisplayName = "Fluffy Work Tab";
        internal const string SleekWorkTabProviderId = "sleek-work-priorities";
        internal const string SleekWorkTabDisplayName = "Sleek Work Priorities";
    }

    internal sealed class ReflectionMaxPriorityProvider : IMaxPriorityProvider
    {
        private readonly ReflectionPriorityValueReader maxPriorityReader;
        private readonly ReflectionPriorityValueReader defaultPriorityReader;
        private readonly string providerId;
        private readonly string displayName;
        private readonly int sortOrder;

        internal ReflectionMaxPriorityProvider(
            string providerId,
            string displayName,
            ReflectionPriorityValueReader maxPriorityReader,
            ReflectionPriorityValueReader defaultPriorityReader,
            int sortOrder)
        {
            this.maxPriorityReader = maxPriorityReader;
            this.defaultPriorityReader = defaultPriorityReader;
            this.providerId = providerId;
            this.displayName = displayName;
            this.sortOrder = sortOrder;
        }

        public string ProviderId => providerId;
        public string DisplayName => displayName;
        public int SortOrder => sortOrder;

        public bool IsAvailable
        {
            get
            {
                int priority;
                return TryGetMaxPriority(out priority);
            }
        }

        public bool TryGetMaxPriority(out int maxPriority)
        {
            maxPriority = 0;
            int reflectedPriority;
            if (maxPriorityReader == null || !maxPriorityReader.TryReadInt(out reflectedPriority))
            {
                return false;
            }

            maxPriority = PriorityAuthorityBroker.ClampMaxPriority(reflectedPriority);
            return maxPriority > PriorityConstants.Disabled;
        }

        public bool TryGetDefaultEnabledPriority(out int defaultEnabledPriority)
        {
            int maxPriority;
            if (!TryGetMaxPriority(out maxPriority))
            {
                defaultEnabledPriority = 0;
                return false;
            }

            int reflectedDefaultPriority;
            defaultEnabledPriority = defaultPriorityReader != null &&
                                     defaultPriorityReader.TryReadInt(out reflectedDefaultPriority)
                ? reflectedDefaultPriority
                : PriorityConstants.VanillaDefaultEnabled;
            defaultEnabledPriority = PriorityAuthorityBroker.ClampDefaultEnabledPriority(
                defaultEnabledPriority,
                maxPriority);
            return true;
        }
    }

    internal sealed class ReflectionPriorityValueReader
    {
        private static readonly object[] NoArguments = new object[0];
        private readonly MemberInfo member;
        private readonly MemberInfo instanceSourceMember;
        private readonly ReflectionPriorityValueReader[] fallbackReaders;

        private ReflectionPriorityValueReader(MemberInfo member, MemberInfo instanceSourceMember = null)
        {
            this.member = member;
            this.instanceSourceMember = instanceSourceMember;
        }

        private ReflectionPriorityValueReader(ReflectionPriorityValueReader[] fallbackReaders)
        {
            this.fallbackReaders = fallbackReaders;
        }

        internal static ReflectionPriorityValueReader FirstAvailable(params ReflectionPriorityValueReader[] readers)
        {
            var available = readers?.Where(reader => reader != null).ToArray();
            if (available == null || available.Length == 0)
            {
                return null;
            }

            return available.Length == 1
                ? available[0]
                : new ReflectionPriorityValueReader(available);
        }

        internal static ReflectionPriorityValueReader ForStaticMember(MemberInfo member)
        {
            return CanReadStaticMember(member)
                ? new ReflectionPriorityValueReader(member)
                : null;
        }

        internal static ReflectionPriorityValueReader ForInstanceMember(
            MemberInfo sourceMember,
            MemberInfo instanceMember)
        {
            if (!CanReadStaticReferenceMember(sourceMember, instanceMember?.DeclaringType) ||
                !CanReadInstanceMember(instanceMember))
            {
                return null;
            }

            return new ReflectionPriorityValueReader(instanceMember, sourceMember);
        }

        internal bool TryReadInt(out int value)
        {
            value = 0;

            if (fallbackReaders != null)
            {
                for (int i = 0; i < fallbackReaders.Length; i++)
                {
                    if (fallbackReaders[i].TryReadInt(out value))
                    {
                        return true;
                    }
                }

                return false;
            }

            try
            {
                object target = null;
                if (!IsStaticMember(member))
                {
                    if (instanceSourceMember == null ||
                        !TryReadMemberValue(instanceSourceMember, null, out target) ||
                        target == null)
                    {
                        return false;
                    }
                }

                object rawValue;
                if (!TryReadMemberValue(member, target, out rawValue) || rawValue == null)
                {
                    return false;
                }

                value = Convert.ToInt32(rawValue);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        internal static Type GetMemberValueType(MemberInfo member)
        {
            var method = member as MethodInfo;
            if (method != null)
            {
                return method.GetParameters().Length == 0 ? method.ReturnType : null;
            }

            var field = member as FieldInfo;
            if (field != null)
            {
                return field.FieldType;
            }

            var property = member as PropertyInfo;
            return property != null && property.GetIndexParameters().Length == 0
                ? property.PropertyType
                : null;
        }

        private static bool CanReadStaticMember(MemberInfo member)
        {
            return member != null &&
                   IsStaticMember(member) &&
                   IsNumericMember(member);
        }

        private static bool CanReadInstanceMember(MemberInfo member)
        {
            return member != null &&
                   !IsStaticMember(member) &&
                   IsNumericMember(member);
        }

        private static bool CanReadStaticReferenceMember(MemberInfo member, Type expectedType)
        {
            if (member == null || expectedType == null || !IsStaticMember(member))
            {
                return false;
            }

            Type valueType = GetMemberValueType(member);
            return valueType != null &&
                   (expectedType.IsAssignableFrom(valueType) ||
                    valueType.IsAssignableFrom(expectedType));
        }

        private static bool TryReadMemberValue(MemberInfo member, object target, out object value)
        {
            value = null;
            var method = member as MethodInfo;
            if (method != null)
            {
                value = method.Invoke(target, NoArguments);
                return true;
            }

            var field = member as FieldInfo;
            if (field != null)
            {
                value = field.GetValue(target);
                return true;
            }

            var property = member as PropertyInfo;
            MethodInfo getter = property?.GetGetMethod(true);
            if (getter == null)
            {
                return false;
            }

            value = getter.Invoke(target, NoArguments);
            return true;
        }

        private static bool IsStaticMember(MemberInfo member)
        {
            var method = member as MethodInfo;
            if (method != null)
            {
                return method.IsStatic;
            }

            var field = member as FieldInfo;
            if (field != null)
            {
                return field.IsStatic;
            }

            var property = member as PropertyInfo;
            return property?.GetGetMethod(true)?.IsStatic ?? false;
        }

        private static bool IsNumericMember(MemberInfo member)
        {
            Type valueType = GetMemberValueType(member);
            if (valueType == null || valueType == typeof(void))
            {
                return false;
            }

            switch (Type.GetTypeCode(valueType))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static class ReflectionPriorityProviderDiscovery
    {
        internal static void RegisterKnownProviders()
        {
            RegisterPriorityMasterProvider();
            RegisterMechWorkTabProvider();
            RegisterClockworkProvider();
            RegisterPawnCentricWorkPrioritiesProvider();

            // Fluffy Work Tab owns its own type probes; the gateway registers it.
            FluffyWorkTabGateway.RegisterPriorityProvider();
            SleekWorkTabGateway.RegisterPriorityProvider();
        }

        private static void RegisterPriorityMasterProvider()
        {
            ReflectionPriorityValueReader maxReader = ReflectionPriorityValueReader.FirstAvailable(
                FindStaticNumericReader(
                    "PriorityMod.Tools.PatchHook",
                    "GetMaximumPriority",
                    "GetMaxPriority"),
                FindInstanceNumericReader(
                    "PriorityMod.Core.PriorityMaster",
                    new[] { "settings", "Settings" },
                    "PriorityMod.Settings.PrioritySettings",
                    new[] { "GetMaxPriority", "GetUserMaxPriority", "maxPriority", "MaxPriority" }));

            ReflectionPriorityValueReader defaultReader = ReflectionPriorityValueReader.FirstAvailable(
                FindStaticNumericReader(
                    "PriorityMod.Tools.PatchHook",
                    "GetDefaultPriority",
                    "GetDefPriority"),
                FindInstanceNumericReader(
                    "PriorityMod.Core.PriorityMaster",
                    new[] { "settings", "Settings" },
                    "PriorityMod.Settings.PrioritySettings",
                    new[] { "GetDefPriority", "GetUserDefPriority", "defaultPriority", "DefaultPriority" }));

            RegisterProvider(
                PriorityProviderIntegrationCatalog.PriorityMasterProviderId,
                PriorityProviderIntegrationCatalog.PriorityMasterDisplayName,
                maxReader,
                defaultReader,
                50);
        }

        private static void RegisterMechWorkTabProvider()
        {
            RegisterProvider(
                PriorityProviderIntegrationCatalog.MechWorkTabProviderId,
                PriorityProviderIntegrationCatalog.MechWorkTabDisplayName,
                FindStaticNumericReader("SM_MechTab.MechTabModSettings", "maxPriority", "MaxPriority"),
                null,
                70);
        }

        private static void RegisterClockworkProvider()
        {
            RegisterProvider(
                PriorityProviderIntegrationCatalog.ClockworkProviderId,
                PriorityProviderIntegrationCatalog.ClockworkDisplayName,
                FindInstanceNumericReader(
                    "WorkShift.Core.WorkShiftModBase",
                    new[] { "Settings" },
                    "WorkShift.Core.WorkShiftSettings",
                    new[] { "maxPriorityLevels" }),
                null,
                60);
        }

        private static void RegisterPawnCentricWorkPrioritiesProvider()
        {
            ReflectionPriorityValueReader maxReader = ReflectionPriorityValueReader.FirstAvailable(
                FindStaticNumericReader("PawnCentricWorkPriorities.WorkUI", "MaxWorkPriority", "get_MaxWorkPriority"),
                FindStaticNumericReader("WorkUI", "MaxWorkPriority", "get_MaxWorkPriority"));

            RegisterProvider(
                PriorityProviderIntegrationCatalog.PawnCentricWorkPrioritiesProviderId,
                PriorityProviderIntegrationCatalog.PawnCentricWorkPrioritiesDisplayName,
                maxReader,
                null,
                80);
        }

        private static void RegisterProvider(
            string providerId,
            string displayName,
            ReflectionPriorityValueReader maxReader,
            ReflectionPriorityValueReader defaultReader,
            int sortOrder)
        {
            if (maxReader == null)
            {
                return;
            }

            PriorityProviderRegistry.RegisterProvider(
                new ReflectionMaxPriorityProvider(
                    providerId,
                    displayName,
                    maxReader,
                    defaultReader,
                    sortOrder));
        }

        private static ReflectionPriorityValueReader FindStaticNumericReader(
            string typeName,
            params string[] memberNames)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null || memberNames == null)
            {
                return null;
            }

            for (int i = 0; i < memberNames.Length; i++)
            {
                ReflectionPriorityValueReader reader =
                    ReflectionPriorityValueReader.ForStaticMember(FindReadableMember(type, memberNames[i]));
                if (reader != null)
                {
                    return reader;
                }
            }

            return null;
        }

        private static ReflectionPriorityValueReader FindInstanceNumericReader(
            string sourceTypeName,
            string[] sourceMemberNames,
            string instanceTypeName,
            string[] memberNames)
        {
            Type sourceType = AccessTools.TypeByName(sourceTypeName);
            Type configuredInstanceType = AccessTools.TypeByName(instanceTypeName);
            if (sourceType == null || sourceMemberNames == null || memberNames == null)
            {
                return null;
            }

            for (int sourceIndex = 0; sourceIndex < sourceMemberNames.Length; sourceIndex++)
            {
                MemberInfo sourceMember = FindReadableMember(sourceType, sourceMemberNames[sourceIndex]);
                if (sourceMember == null)
                {
                    continue;
                }

                Type instanceType = configuredInstanceType ??
                                    ReflectionPriorityValueReader.GetMemberValueType(sourceMember);
                if (instanceType == null)
                {
                    continue;
                }

                for (int memberIndex = 0; memberIndex < memberNames.Length; memberIndex++)
                {
                    ReflectionPriorityValueReader reader =
                        ReflectionPriorityValueReader.ForInstanceMember(
                            sourceMember,
                            FindReadableMember(instanceType, memberNames[memberIndex]));
                    if (reader != null)
                    {
                        return reader;
                    }
                }
            }

            return null;
        }

        private static MemberInfo FindReadableMember(Type type, string memberName)
        {
            if (type == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            MethodInfo method = AccessTools.Method(type, memberName, Type.EmptyTypes);
            if (method != null)
            {
                return method;
            }

            PropertyInfo property = AccessTools.Property(type, memberName);
            if (property != null)
            {
                return property;
            }

            FieldInfo field = AccessTools.Field(type, memberName);
            if (field != null)
            {
                return field;
            }

            return !memberName.StartsWith("get_", StringComparison.Ordinal)
                ? AccessTools.Method(type, "get_" + memberName, Type.EmptyTypes)
                : null;
        }
    }
}
