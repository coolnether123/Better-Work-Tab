using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// Installs gameplay callbacks only while BWT owns the relevant authority and has
    /// material persisted behavior to apply. Transitions run on the main thread at
    /// GameComponent lifecycle boundaries. RequestRefresh is only an event
    /// latch: registry callbacks and the public authority-notification seam may
    /// request work from another thread, but they never perform Harmony
    /// transitions themselves.
    /// </summary>
    internal static class DynamicGameplayPatchController
    {
        internal const string HarmonyId = "Coolnether123.betterworktab";
        private const string FluffyHarmonyId = "fluffy.worktab";
        private static Harmony harmony;
        private static PatchDescriptor[] orderingPatches;
        private static PatchDescriptor[] priorityPatches;
        private static bool initialized;
        private static int refreshRequested;
        private static bool orderingInstalled;
        private static bool priorityInstalled;
        private static bool orderingEnabled;
        private static bool priorityEnabled;
        private static string lastError;

        internal static bool IsOrderingBehaviorActive => orderingEnabled;
        internal static bool IsPriorityReadPatchActive => priorityEnabled;

        internal static void Initialize(Harmony harmonyInstance)
        {
            if (harmonyInstance == null)
            {
                RecordError("Dynamic gameplay patch initialization skipped because the Harmony instance was null.");
                return;
            }
            if (initialized)
            {
                if (!ReferenceEquals(harmony, harmonyInstance))
                    RecordError("Dynamic gameplay patch initialization received a second Harmony instance; retaining BWT's original owner instance.");
                RequestRefresh();
                return;
            }
            try
            {
                PatchDescriptor[] resolvedOrderingPatches = CreateOrderingPatches();
                PatchDescriptor[] resolvedPriorityPatches = CreatePriorityPatches();
                harmony = harmonyInstance;
                orderingPatches = resolvedOrderingPatches;
                priorityPatches = resolvedPriorityPatches;
                initialized = true;
                RequestRefresh();
            }
            catch (Exception exception)
            {
                harmony = null;
                orderingPatches = null;
                priorityPatches = null;
                initialized = false;
                orderingInstalled = priorityInstalled = orderingEnabled = priorityEnabled = false;
                RecordError("Dynamic gameplay patch initialization failed: " + exception.Message);
            }
        }

        internal static void RequestRefresh() => Interlocked.Exchange(ref refreshRequested, 1);

        internal static void ProcessPendingRefresh()
        {
            if (!initialized || harmony == null)
                return;

            // Store registration/unregistration and every mutable policy seam
            // call RequestRefresh. Re-auditing Harmony once per frame while a
            // store is present turns this transition controller into a steady-
            // state game-loop cost; the event-driven request is the authority.
            // The exchange is the claim point: a request before it is consumed by
            // this pass, while a request after it remains pending for the next
            // main-thread pass instead of being cleared by this one.
            if (Interlocked.Exchange(ref refreshRequested, 0) == 0)
                return;

            bool externalStoreRegistered = ExternalWorkTabRegistry.RegisteredStoreCount > 0;
            Refresh(externalStoreRegistered);
        }

        private static void Refresh(bool externalStoreRegistered)
        {
            bool desiredOrdering = false;
            bool authorityReadSucceeded = true;
            try
            {
                desiredOrdering = PriorityAuthorityBroker.ShouldRunBetterWorkTabOrdering;
            }
            catch (Exception exception)
            {
                authorityReadSucceeded = false;
                RecordError("Dynamic gameplay patch state evaluation failed: " + exception.Message);
            }
            bool orderingSucceeded = RefreshGroup(orderingPatches, authorityReadSucceeded && desiredOrdering, "ordering", ref orderingInstalled, ref orderingEnabled);
            bool prioritySucceeded = RefreshGroup(priorityPatches, authorityReadSucceeded && externalStoreRegistered, "priority read", ref priorityInstalled, ref priorityEnabled);
            if (orderingSucceeded && prioritySucceeded)
                lastError = null;
            else
                RequestRefresh();
        }

        private static bool RefreshGroup(PatchDescriptor[] group, bool desired, string groupName, ref bool installed, ref bool enabled)
        {
            enabled = false;
            try
            {
                if (desired && installed && AllExpectedPatches(group))
                {
                    installed = true;
                    enabled = true;
                    return true;
                }
                bool removed = TryUninstallGroup(group, groupName);
                if (!removed || AnyOwnedPatch(group))
                {
                    installed = true;
                    RequestRefresh();
                    return false;
                }
                if (!desired)
                {
                    installed = false;
                    return true;
                }
                if (!TryInstallGroup(group, groupName) || !AllExpectedPatches(group))
                {
                    installed = AnyOwnedPatch(group);
                    RequestRefresh();
                    return false;
                }
                installed = true;
                enabled = true;
                return true;
            }
            catch (Exception exception)
            {
                RecordError("Dynamic " + groupName + " patch transition failed: " + exception.Message);
                installed = true;
                RequestRefresh();
                return false;
            }
        }

        private static bool TryInstallGroup(PatchDescriptor[] group, string groupName)
        {
            try
            {
                for (int i = 0; i < group.Length; i++)
                {
                    PatchDescriptor patch = group[i];
                    if (HasForeignPatchUsingCallback(patch))
                        throw new InvalidOperationException("A different Harmony owner already uses callback " + patch.Description + ".");
                    harmony.Patch(patch.Original,
                        prefix: patch.PatchType == HarmonyPatchType.Prefix ? patch.Options : null,
                        postfix: patch.PatchType == HarmonyPatchType.Postfix ? patch.Options : null);
                    if (!HasExpectedPatch(patch))
                        throw new InvalidOperationException("Harmony did not retain the expected owned callback for " + patch.Description + ".");
                }
                return true;
            }
            catch (Exception exception)
            {
                bool rollbackSucceeded = TryUninstallGroup(group, groupName + " rollback");
                RecordError("Dynamic " + groupName + " patch group failed: " + exception.Message);
                if (!rollbackSucceeded || AnyOwnedPatch(group))
                    RecordError("Dynamic " + groupName + " patch rollback left an owned callback installed; it remains disabled.");
                return false;
            }
        }

        private static bool TryUninstallGroup(PatchDescriptor[] group, string groupName)
        {
            if (group == null)
                return true;
            bool succeeded = true;
            for (int i = group.Length - 1; i >= 0; i--)
                succeeded &= TryUninstall(group[i], groupName);
            return succeeded;
        }

        private static bool TryUninstall(PatchDescriptor patch, string groupName)
        {
            try
            {
                if (!HasOwnedPatch(patch))
                    return true;
                if (HasForeignPatchUsingCallback(patch))
                {
                    RecordError("Dynamic " + groupName + " unpatch refused because a foreign owner uses " + patch.Description + ".");
                    return false;
                }
                // Use the exact callback MethodInfo only after the foreign-owner check.
                harmony.Unpatch(patch.Original, patch.Callback);
                if (HasOwnedPatch(patch))
                {
                    RecordError("Dynamic " + groupName + " unpatch verification failed for " + patch.Description + ".");
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                RecordError("Dynamic " + groupName + " unpatch failed for " + patch.Description + ": " + exception.Message);
                return false;
            }
        }

        private static bool AllExpectedPatches(PatchDescriptor[] group)
        {
            if (group == null || group.Length == 0)
                return false;
            for (int i = 0; i < group.Length; i++)
                if (!HasExpectedPatch(group[i]))
                    return false;
            return true;
        }

        private static bool AnyOwnedPatch(PatchDescriptor[] group)
        {
            if (group == null)
                return false;
            for (int i = 0; i < group.Length; i++)
                if (HasOwnedPatch(group[i]))
                    return true;
            return false;
        }

        private static bool HasExpectedPatch(PatchDescriptor expected)
        {
            HarmonyLib.Patch actual = FindOwnedPatch(expected);
            return actual != null && actual.priority == expected.Options.priority &&
                   SameOrdering(actual.before, expected.Options.before) && SameOrdering(actual.after, expected.Options.after);
        }

        private static bool HasOwnedPatch(PatchDescriptor expected) => FindOwnedPatch(expected) != null;

        private static HarmonyLib.Patch FindOwnedPatch(PatchDescriptor expected)
        {
            if (expected?.Original == null || expected.Callback == null)
                return null;
            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(expected.Original);
            if (patchInfo == null)
                return null;
            IList<HarmonyLib.Patch> patches = expected.PatchType == HarmonyPatchType.Prefix
                ? patchInfo.Prefixes
                : patchInfo.Postfixes;
            for (int i = 0; i < patches.Count; i++)
            {
                HarmonyLib.Patch patch = patches[i];
                if (string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal) && patch.PatchMethod == expected.Callback)
                    return patch;
            }
            return null;
        }

        private static bool HasForeignPatchUsingCallback(PatchDescriptor expected)
        {
            if (expected?.Original == null || expected.Callback == null)
                return false;
            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(expected.Original);
            if (patchInfo == null)
                return false;
            foreach (HarmonyLib.Patch patch in GetAllPatches(patchInfo))
                if (patch.PatchMethod == expected.Callback && !string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static IEnumerable<HarmonyLib.Patch> GetAllPatches(HarmonyLib.Patches patchInfo)
        {
            foreach (HarmonyLib.Patch patch in patchInfo.Prefixes) yield return patch;
            foreach (HarmonyLib.Patch patch in patchInfo.Postfixes) yield return patch;
            foreach (HarmonyLib.Patch patch in patchInfo.Transpilers) yield return patch;
            foreach (HarmonyLib.Patch patch in patchInfo.Finalizers) yield return patch;
            foreach (HarmonyLib.Patch patch in patchInfo.InnerPrefixes) yield return patch;
            foreach (HarmonyLib.Patch patch in patchInfo.InnerPostfixes) yield return patch;
        }

        private static bool SameOrdering(string[] expected, string[] actual)
        {
            int expectedLength = expected?.Length ?? 0;
            if (expectedLength != (actual?.Length ?? 0))
                return false;
            for (int i = 0; i < expectedLength; i++)
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static PatchDescriptor[] CreateOrderingPatches()
        {
            MethodInfo canUse = RequireTarget(typeof(JobGiver_Work), "PawnCanUseWorkGiver", new[] { typeof(Pawn), typeof(WorkGiver) });
            MethodInfo thing = RequireTarget(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing), new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            MethodInfo cell = RequireTarget(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnCell), new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
            MethodInfo cache = RequireTarget(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder", Type.EmptyTypes);
            return new[]
            {
                CreateDescriptor(canUse, HarmonyPatchType.Prefix, typeof(Patch_JobGiver_Work_PawnCanUseWorkGiver), "Prefix", Priority.First, true, false, "JobGiver_Work.PawnCanUseWorkGiver prefix"),
                CreateDescriptor(canUse, HarmonyPatchType.Postfix, typeof(Patch_JobGiver_Work_PawnCanUseWorkGiver), "Postfix", Priority.Last, false, true, "JobGiver_Work.PawnCanUseWorkGiver postfix"),
                CreateDescriptor(thing, HarmonyPatchType.Postfix, typeof(Patch_WorkGiver_Scanner_HasJobOnThing), "Postfix", Priority.Last, false, true, "WorkGiver_Scanner.HasJobOnThing postfix"),
                CreateDescriptor(cell, HarmonyPatchType.Postfix, typeof(Patch_WorkGiver_Scanner_HasJobOnCell), "Postfix", Priority.Last, false, true, "WorkGiver_Scanner.HasJobOnCell postfix"),
                CreateDescriptor(cache, HarmonyPatchType.Prefix, typeof(Patch_WorkExecutionOrder_ReplaceCache), "Prefix", Priority.First, true, false, "Pawn_WorkSettings.CacheWorkGiversInOrder prefix")
            };
        }

        private static PatchDescriptor[] CreatePriorityPatches()
        {
            MethodInfo getPriority = RequireTarget(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority), new[] { typeof(WorkTypeDef) });
            return new[]
            {
                CreateDescriptor(getPriority, HarmonyPatchType.Postfix, typeof(Patch_Pawn_WorkSettings_GetPriority_PriorityAuthority), "Postfix", Priority.Last, false, true, "Pawn_WorkSettings.GetPriority postfix")
            };
        }

        private static PatchDescriptor CreateDescriptor(MethodBase original, HarmonyPatchType patchType, Type callbackType, string callbackName, int priority, bool beforeFluffy, bool afterFluffy, string description)
        {
            MethodInfo callback = AccessTools.Method(callbackType, callbackName);
            if (callback == null || !callback.IsStatic)
                throw new InvalidOperationException("Dynamic Harmony callback was not resolved as static: " + callbackType?.FullName + "." + callbackName);
            HarmonyMethod options = new HarmonyMethod(callback)
            {
                priority = priority,
                before = beforeFluffy ? new[] { FluffyHarmonyId } : null,
                after = afterFluffy ? new[] { FluffyHarmonyId } : null
            };
            return new PatchDescriptor(original, patchType, options, description);
        }

        private static MethodInfo RequireTarget(Type declaringType, string methodName, Type[] argumentTypes)
        {
            MethodInfo method = AccessTools.Method(declaringType, methodName, argumentTypes);
            if (method == null)
                throw new InvalidOperationException("Dynamic Harmony target was not resolved: " + declaringType?.FullName + "." + methodName);
            return method;
        }

        private static void RecordError(string message)
        {
            string error = message ?? "Unknown dynamic gameplay patch error.";
            if (!string.Equals(lastError, error, StringComparison.Ordinal))
                Log.Error("[Better Work Tab] " + error);
            lastError = error;
        }

        private sealed class PatchDescriptor
        {
            internal PatchDescriptor(MethodBase original, HarmonyPatchType patchType, HarmonyMethod options, string description)
            {
                Original = original;
                PatchType = patchType;
                Options = options;
                Callback = options?.method;
                Description = description;
            }
            internal MethodBase Original { get; }
            internal HarmonyPatchType PatchType { get; }
            internal HarmonyMethod Options { get; }
            internal MethodInfo Callback { get; }
            internal string Description { get; }
        }
    }
}
