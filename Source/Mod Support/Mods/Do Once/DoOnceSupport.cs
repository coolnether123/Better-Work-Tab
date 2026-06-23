using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Reflection;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Do Once borrows BWT's unassigned-work float menu behavior. When both mods are loaded,
    /// BWT owns that surface so users get one set of options with BWT's sub-work awareness.
    /// </summary>
    public sealed class DoOnceSupport : ModSupportModuleBase
    {
        public const string DoOncePackageId = "TreadTheDawnGames.DoOnce";
        public const string DoOnceHarmonyId = "com.TreadTheDan.DoOnce";

        private const string CompatibilityHarmonyId = "Coolnether123.betterworktab.dooncecompat";
        private static readonly Harmony CompatibilityHarmony = new Harmony(CompatibilityHarmonyId);
        private static bool detected;
        private static bool warned;

        public override string PackageId => DoOncePackageId;
        public override string DisplayName => "Do Once - Noncommittal Work";

        public override void OnModsDetected()
        {
            detected = true;
            SuppressDoOnceRuntime("mod support startup");
            LongEventHandler.ExecuteWhenFinished(() => SuppressDoOnceRuntime("long-event startup"));
        }

        internal static bool IsActive
        {
            get
            {
                return detected || ModLister.GetActiveModWithIdentifier(DoOncePackageId, ignorePostfix: true) != null;
            }
        }

        internal static void EnsureBwtOwnsUnassignedWorkMenu()
        {
            if (!IsActive)
            {
                return;
            }

            SuppressDoOnceRuntime("float-menu guard");
        }

        internal static void SuppressDoOnceRuntime(string reason)
        {
            if (!IsActive)
            {
                return;
            }

            try
            {
                int removed = 0;
                removed += Unpatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption), HarmonyPatchType.Postfix);
                removed += Unpatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiversOptionsFor), HarmonyPatchType.Postfix);
                removed += Unpatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI), HarmonyPatchType.Prefix);
                removed += Unpatch(typeof(Window), nameof(Window.PreClose), HarmonyPatchType.Postfix);

                ClearDoOnceAdditionalOptions();

                if (removed > 0)
                {
                    BetterWorkTabMod.DebugLog(
                        $"[Do Once] Suppressed {removed} Do Once Harmony patch(es) during {reason}; BWT will provide the unassigned-work menu.",
                        DebugFeature.ModSupport);
                }
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[BWT][Do Once] Failed to suppress duplicate Do Once menu patches; duplicate unassigned-work options may appear. " + ex.Message);
                }
            }
        }

        private static int Unpatch(Type type, string methodName, HarmonyPatchType patchType)
        {
            MethodInfo method = AccessTools.Method(type, methodName);
            if (method == null)
            {
                return 0;
            }

            int count = CountPatchesFromOwner(method, patchType, DoOnceHarmonyId);
            if (count <= 0)
            {
                return 0;
            }

            CompatibilityHarmony.Unpatch(method, patchType, DoOnceHarmonyId);
            return count;
        }

        private static int CountPatchesFromOwner(MethodBase method, HarmonyPatchType patchType, string owner)
        {
            HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
            if (patches == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Patch patch in GetPatches(patches, patchType))
            {
                if (string.Equals(patch.owner, owner, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static IEnumerable GetPatches(HarmonyLib.Patches patches, HarmonyPatchType patchType)
        {
            switch (patchType)
            {
                case HarmonyPatchType.Prefix:
                    return patches.Prefixes;
                case HarmonyPatchType.Postfix:
                    return patches.Postfixes;
                case HarmonyPatchType.Transpiler:
                    return patches.Transpilers;
                case HarmonyPatchType.Finalizer:
                    return patches.Finalizers;
                default:
                    return Array.Empty<Patch>();
            }
        }

        private static void ClearDoOnceAdditionalOptions()
        {
            Type optionBufferType = AccessTools.TypeByName("DoOnce.Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor");
            if (optionBufferType == null)
            {
                return;
            }

            FieldInfo field = AccessTools.Field(optionBufferType, "AdditionalOptions");
            if (field?.GetValue(null) is IList options)
            {
                options.Clear();
            }
        }
    }
}
