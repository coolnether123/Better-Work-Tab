using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Spine.Api;
using Verse;

namespace Spine.UI.Tooltips
{
    /// <summary>
    /// Opt-in service that keeps tooltip measurement consistent with rendering.
    /// </summary>
    internal sealed class StableTooltipSizing : ITooltipSizingFacade
    {
        private const string HarmonyId =
            "CoolNether123.Spine.StableTooltipSizing";
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, int> ConsumerLeases =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static HarmonyLib.Harmony harmony;
        private static MethodInfo target;
        private static int activeLeaseCount;
        private static bool installed;

        internal static readonly StableTooltipSizing Instance =
            new StableTooltipSizing();

        private StableTooltipSizing()
        {
        }

        public IDisposable Acquire(string consumerId)
        {
            if (string.IsNullOrWhiteSpace(consumerId))
            {
                throw new ArgumentException(
                    "A tooltip-service consumer identifier is required.",
                    nameof(consumerId));
            }

            lock (Sync)
            {
                EnsureInstalled();
                ConsumerLeases.TryGetValue(consumerId, out var count);
                ConsumerLeases[consumerId] = count + 1;
                activeLeaseCount++;
            }

            return new Lease(consumerId);
        }

        private static void EnsureInstalled()
        {
            if (installed)
            {
                return;
            }

            target = AccessTools.PropertyGetter(
                typeof(ActiveTip),
                nameof(ActiveTip.TipRect));
            if (target == null)
            {
                throw new MissingMethodException(
                    typeof(ActiveTip).FullName,
                    nameof(ActiveTip.TipRect));
            }

            harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(StableTooltipSizing),
                    nameof(BeforeMeasure)),
                postfix: new HarmonyMethod(
                    typeof(StableTooltipSizing),
                    nameof(AfterMeasure)),
                finalizer: new HarmonyMethod(
                    typeof(StableTooltipSizing),
                    nameof(AfterMeasureFailure)));
            installed = true;
        }

        private static void Release(string consumerId)
        {
            lock (Sync)
            {
                if (!ConsumerLeases.TryGetValue(
                    consumerId,
                    out var count))
                {
                    return;
                }

                if (count <= 1)
                {
                    ConsumerLeases.Remove(consumerId);
                }
                else
                {
                    ConsumerLeases[consumerId] = count - 1;
                }

                activeLeaseCount--;
                if (activeLeaseCount != 0 || !installed)
                {
                    return;
                }

                harmony.Unpatch(
                    target,
                    HarmonyPatchType.All,
                    HarmonyId);
                installed = false;
                harmony = null;
                target = null;
            }
        }

        private static void BeforeMeasure(out GameFont __state)
        {
            __state = Text.Font;
            Text.Font = GameFont.Small;
        }

        private static void AfterMeasure(GameFont __state)
        {
            Text.Font = __state;
        }

        private static Exception AfterMeasureFailure(
            Exception __exception,
            GameFont __state)
        {
            Text.Font = __state;
            return __exception;
        }

        private sealed class Lease : IDisposable
        {
            private readonly string consumerId;
            private bool disposed;

            public Lease(string consumerId)
            {
                this.consumerId = consumerId;
            }

            public void Dispose()
            {
                lock (Sync)
                {
                    if (disposed)
                    {
                        return;
                    }
                    disposed = true;
                }

                Release(consumerId);
            }
        }
    }
}
