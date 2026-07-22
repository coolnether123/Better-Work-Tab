using System;
using System.Reflection;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.Clockwork
{
    /// <summary>
    /// Keeps BWT and Clockwork in Clockwork's supported side-by-side mode.
    /// Clockwork remains authoritative for its schedule window and hourly
    /// priority patches while BWT retains the normal Work tab.
    /// </summary>
    internal static class ClockworkCompatibility
    {
        internal const string PackageId = "jaskkro.workshift";
        internal const string WorkScheduleDefName = "WorkSchedule";

        private const string SettingsTypeName = "WorkShift.Core.WorkShiftSettings";
        private const string ModTypeName = "WorkShift.Core.WorkShiftModBase";
        private static readonly FieldInfo CachedLabelCapField =
            typeof(Def).GetField("cachedLabelCap", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool initialized;

        internal static bool IsPresent =>
            ModSupportManager.IsModActive(PackageId);

        internal static void Initialize(Harmony harmony)
        {
            if (initialized || !IsPresent)
            {
                return;
            }

            initialized = true;
            Type settingsType = AccessTools.TypeByName(SettingsTypeName);
            MethodInfo applyTabSettings = settingsType == null
                ? null
                : AccessTools.Method(settingsType, "ApplyTabSettings");
            if (applyTabSettings != null)
            {
                harmony.Patch(
                    applyTabSettings,
                    postfix: new HarmonyMethod(
                        typeof(ClockworkCompatibility),
                        nameof(EnforceSideBySideMode)));
            }

            EnforceSideBySideMode();
            LongEventHandler.ExecuteWhenFinished(EnforceSideBySideMode);
            PriorityAuthorityBroker.InvalidateCaches();
        }

        private static void EnforceSideBySideMode()
        {
            if (!IsPresent)
            {
                return;
            }

            Type modType = AccessTools.TypeByName(ModTypeName);
            object settings = AccessTools.Property(modType, "Settings")?.GetValue(null, null);
            FieldInfo replaceField = AccessTools.Field(SettingsTypeName + ":replaceVanillaWorkTab");
            if (settings != null && replaceField != null)
            {
                replaceField.SetValue(settings, false);
            }

            MainButtonDef work = DefDatabase<MainButtonDef>.GetNamedSilentFail("Work");
            MainButtonDef clockwork =
                DefDatabase<MainButtonDef>.GetNamedSilentFail(WorkScheduleDefName);
            if (work != null)
            {
                work.buttonVisible = true;
            }

            if (clockwork == null)
            {
                return;
            }

            clockwork.buttonVisible = true;
            if (!string.Equals(clockwork.label, "Clockwork", StringComparison.Ordinal))
            {
                clockwork.label = "Clockwork";
                CachedLabelCapField?.SetValue(clockwork, default(TaggedString));
            }
        }
    }
}
