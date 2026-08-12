using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Spine.Api;
using Verse;

namespace Spine.Harmony
{
    public sealed class HarmonyPatchOptions
    {
        public bool AllowDebugPatches { get; set; }
        public bool AllowDangerousPatches { get; set; }
        public bool AllowStructReturns { get; set; }
        public string[] Before { get; set; }
        public string[] After { get; set; }
        public int? Priority { get; set; }
        public Action<object, string> OnResult { get; set; }
    }

    /// <summary>
    /// Stable, owner-preserving entry point for guarded Harmony installation.
    /// Consumers retain their own Harmony ID; Spine only provides consistent
    /// validation, failure isolation, and reporting.
    /// </summary>
    public interface IHarmonyPatchingFacade
    {
        SpineApiDescriptor Descriptor { get; }

        void PatchAll(
            HarmonyLib.Harmony harmony,
            Assembly assembly,
            HarmonyPatchOptions options = null);

        IList<MethodBase> PatchType(
            HarmonyLib.Harmony harmony,
            Type patchType,
            HarmonyPatchOptions options = null);

        bool TryPatch(
            HarmonyLib.Harmony harmony,
            MethodBase target,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null,
            Action<string> report = null);

        IHarmonyPatchInstaller CreateInstaller(
            string consumerId,
            string logPrefix);
    }

    /// <summary>
    /// Consumer-scoped patch installer with exact Harmony ownership, idempotent
    /// installation, and mandatory target-specific failure diagnostics.
    /// </summary>
    public interface IHarmonyPatchInstaller
    {
        bool PatchAllOnce(
            Assembly assembly,
            HarmonyPatchOptions options = null);

        bool PatchTypeOnce(
            string patchName,
            Type patchType,
            int expectedPatchCount = 1,
            HarmonyPatchOptions options = null);

        bool TryPatch(
            string patchName,
            MethodBase target,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null);
    }

    internal sealed class HarmonyPatchingFacade : IHarmonyPatchingFacade
    {
        internal static readonly HarmonyPatchingFacade Instance =
            new HarmonyPatchingFacade();

        private static readonly SpineApiDescriptor CurrentDescriptor =
            new SpineApiDescriptor(
                "CoolNether123.Spine.HarmonyPatching",
                new SemanticVersion(1, 0, 0),
                SpineCapability.HarmonyPatching);

        private HarmonyPatchingFacade()
        {
        }

        public SpineApiDescriptor Descriptor => CurrentDescriptor;

        public void PatchAll(
            HarmonyLib.Harmony harmony,
            Assembly assembly,
            HarmonyPatchOptions options = null)
        {
            HarmonyUtil.PatchAll(harmony, assembly, Convert(options));
        }

        public IList<MethodBase> PatchType(
            HarmonyLib.Harmony harmony,
            Type patchType,
            HarmonyPatchOptions options = null)
        {
            return HarmonyUtil.PatchType(harmony, patchType, Convert(options));
        }

        public bool TryPatch(
            HarmonyLib.Harmony harmony,
            MethodBase target,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null,
            Action<string> report = null)
        {
            if (harmony == null)
            {
                report?.Invoke("Consumer Harmony instance is null.");
                return false;
            }

            if (target == null)
            {
                report?.Invoke("Target method was not found.");
                return false;
            }

            if (prefix == null && postfix == null && transpiler == null)
            {
                report?.Invoke("No Harmony patch method was supplied.");
                return false;
            }

            try
            {
                harmony.Patch(target, prefix, postfix, transpiler);
                report?.Invoke("patched");
                return true;
            }
            catch (Exception exception)
            {
                report?.Invoke("error: " + exception.Message);
                return false;
            }
        }

        public IHarmonyPatchInstaller CreateInstaller(
            string consumerId,
            string logPrefix)
        {
            return new HarmonyPatchInstaller(consumerId, logPrefix, this);
        }

        private static HarmonyUtil.PatchOptions Convert(
            HarmonyPatchOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new HarmonyUtil.PatchOptions
            {
                AllowDebugPatches = options.AllowDebugPatches,
                AllowDangerousPatches = options.AllowDangerousPatches,
                AllowStructReturns = options.AllowStructReturns,
                Before = options.Before,
                After = options.After,
                Priority = options.Priority,
                OnResult = options.OnResult
            };
        }
    }

    internal sealed class HarmonyPatchInstaller : IHarmonyPatchInstaller
    {
        private readonly HarmonyLib.Harmony harmony;
        private readonly string logPrefix;
        private readonly HarmonyPatchingFacade facade;
        private readonly Dictionary<string, bool> results =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly object sync = new object();

        internal HarmonyPatchInstaller(
            string consumerId,
            string logPrefix,
            HarmonyPatchingFacade facade)
        {
            if (string.IsNullOrWhiteSpace(consumerId))
            {
                throw new ArgumentException(
                    "A Harmony consumer identifier is required.",
                    nameof(consumerId));
            }

            harmony = new HarmonyLib.Harmony(consumerId);
            this.logPrefix = string.IsNullOrWhiteSpace(logPrefix)
                ? "[" + consumerId + "]"
                : logPrefix.Trim();
            this.facade = facade ?? throw new ArgumentNullException(nameof(facade));
        }

        public bool PatchAllOnce(
            Assembly assembly,
            HarmonyPatchOptions options = null)
        {
            const string key = HarmonyPatchOperationKeys.Assembly;
            lock (sync)
            {
                if (results.TryGetValue(key, out bool previous))
                {
                    return previous;
                }

                bool success = true;
                HarmonyPatchOptions effective = CopyOptions(options);
                Action<object, string> consumerResult = effective.OnResult;
                effective.OnResult = (target, result) =>
                {
                    consumerResult?.Invoke(target, result);
                    if (IsFailure(result))
                    {
                        success = false;
                        Log.Error(logPrefix + " patch " +
                            DescribeTarget(target) + ": " + result);
                    }
                };

                if (assembly == null)
                {
                    success = false;
                    Log.Error(logPrefix + " patch assembly was null.");
                }
                else
                {
                    try
                    {
                        facade.PatchAll(harmony, assembly, effective);
                    }
                    catch (Exception exception)
                    {
                        success = false;
                        Log.Error(logPrefix + " patch installation failed: " +
                            exception.Message);
                    }
                }

                results[key] = success;
                return success;
            }
        }

        public bool PatchTypeOnce(
            string patchName,
            Type patchType,
            int expectedPatchCount = 1,
            HarmonyPatchOptions options = null)
        {
            string key = HarmonyPatchOperationKeys.ForType(patchName);
            string name = patchName.Trim();
            lock (sync)
            {
                if (results.TryGetValue(key, out bool previous))
                {
                    return previous;
                }

                bool success = true;
                HarmonyPatchOptions effective = CopyOptions(options);
                Action<object, string> consumerResult = effective.OnResult;
                effective.OnResult = (target, result) =>
                {
                    consumerResult?.Invoke(target, result);
                    if (IsFailure(result))
                    {
                        success = false;
                        Log.Error(logPrefix + " patch " + name + " (" +
                            DescribeTarget(target) + "): " + result);
                    }
                };

                IList<MethodBase> patched = patchType == null
                    ? new List<MethodBase>()
                    : facade.PatchType(harmony, patchType, effective);
                if (patched.Count != expectedPatchCount)
                {
                    success = false;
                    Log.Error(logPrefix + " patch " + name + " expected " +
                        expectedPatchCount + " target(s), applied " +
                        patched.Count + ".");
                }

                results[key] = success;
                return success;
            }
        }

        public bool TryPatch(
            string patchName,
            MethodBase target,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null)
        {
            string key = HarmonyPatchOperationKeys.ForMethod(patchName);
            string name = patchName.Trim();
            lock (sync)
            {
                if (results.TryGetValue(key, out bool previous))
                {
                    return previous;
                }

                string detail = null;
                bool success = facade.TryPatch(
                    harmony,
                    target,
                    prefix,
                    postfix,
                    transpiler,
                    report => detail = report);
                if (!success)
                {
                    Log.Error(logPrefix + " patch " + name + ": " +
                        (detail ?? "unknown failure"));
                }

                results[key] = success;
                return success;
            }
        }

        private static HarmonyPatchOptions CopyOptions(
            HarmonyPatchOptions options)
        {
            if (options == null)
            {
                return new HarmonyPatchOptions();
            }

            return new HarmonyPatchOptions
            {
                AllowDebugPatches = options.AllowDebugPatches,
                AllowDangerousPatches = options.AllowDangerousPatches,
                AllowStructReturns = options.AllowStructReturns,
                Before = options.Before,
                After = options.After,
                Priority = options.Priority,
                OnResult = options.OnResult
            };
        }

        private static bool IsFailure(string result)
        {
            return !string.IsNullOrEmpty(result) &&
                (result.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ||
                 result.StartsWith("skipped:", StringComparison.OrdinalIgnoreCase));
        }

        private static string DescribeTarget(object target)
        {
            return target == null ? "<unknown target>" : target.ToString();
        }
    }
}
