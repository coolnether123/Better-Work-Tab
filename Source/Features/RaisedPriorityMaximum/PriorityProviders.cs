using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        private static bool initialized;

        internal static void EnsureInitialized()
        {
            lock (SyncRoot)
            {
                if (initialized)
                {
                    return;
                }

                Providers[PriorityConstants.VanillaProviderId] = new VanillaPriorityProvider();
                Providers[PriorityConstants.BwtProviderId] = new BetterWorkTabPriorityProvider();
                initialized = true;

                ReflectionPriorityProviderDiscovery.RegisterKnownProviders();
            }
        }

        internal static IEnumerable<IMaxPriorityProvider> GetAvailableProviders()
        {
            EnsureInitialized();
            lock (SyncRoot)
            {
                return Providers.Values
                    .Where(IsProviderAvailable)
                    .OrderBy(provider => provider.SortOrder)
                    .ThenBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
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
            }

            PriorityAuthorityBroker.InvalidateCaches();
            return true;
        }

        public static bool UnregisterProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || IsBuiltInProviderId(providerId))
            {
                return false;
            }

            EnsureInitialized();
            lock (SyncRoot)
            {
                bool removed = Providers.Remove(providerId.Trim());
                if (removed)
                {
                    PriorityAuthorityBroker.InvalidateCaches();
                }

                return removed;
            }
        }

        private static bool IsProviderAvailable(IMaxPriorityProvider provider)
        {
            try
            {
                return provider != null && provider.IsAvailable;
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

        private static bool IsProviderId(string providerId, string expectedProviderId)
        {
            return string.Equals(providerId, expectedProviderId, StringComparison.OrdinalIgnoreCase);
        }
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

            // Fluffy Work Tab owns its own type detection; the gateway registers it.
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
