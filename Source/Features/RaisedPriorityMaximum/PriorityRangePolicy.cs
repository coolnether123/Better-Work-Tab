using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class PriorityRangePolicy
    {
        private static int cachedFrame = -1;
        private static Game cachedGame;
        private static int cachedHighestLivePriority;

        internal static int GetEffectiveMaxPriority()
        {
            return PriorityProviderSelector.GetSnapshot().MaxPriority;
        }

        internal static int GetRequestableMaxPriority()
        {
            return PriorityProviderSelector.GetSnapshot().MaxPriority;
        }

        internal static int GetDefaultEnabledPriority()
        {
            return PriorityProviderSelector.GetSnapshot().DefaultEnabledPriority;
        }

        internal static int GetBetterWorkTabConfiguredMaxPriority()
        {
            return Math.Max(
                PriorityConstants.VanillaMax,
                ClampMaxPriority(BetterWorkTabMod.Settings?.maxPriorityInt ?? DefaultSettings.maxPriority));
        }

        internal static int GetAutoConfiguredMaxPriority()
        {
            return GetBetterWorkTabConfiguredMaxPriority();
        }

        internal static int ClampMaxPriority(int value)
        {
            return Clamp(value, 1, PriorityConstants.ExtendedHardMax);
        }

        internal static int ClampDefaultEnabledPriority(int value, int maxPriority)
        {
            return Clamp(value, 1, ClampMaxPriority(maxPriority));
        }

        internal static int ClampPriorityForRequest(int priority)
        {
            // Snapshot resolution also observes the highest value already present in the world so
            // its metadata can describe preserved external values. That observation cannot lower
            // the result of clamping this request: the request itself is part of the snapshot's
            // required floor. The runtime policy is therefore the equivalent value-only seam and
            // avoids a world scan on every SetPriority call. Settings are normalized at the settings
            // load/write seams; the runtime getters still clamp malformed numeric values safely.
            return Clamp(
                priority,
                PriorityConstants.Disabled,
                PriorityProviderSelector.GetRuntimePriorityMaximum(priority));
        }

        internal static int ClampStoredPriorityForRuntime(int priority)
        {
            return Clamp(
                priority,
                PriorityConstants.Disabled,
                PriorityProviderSelector.GetRuntimePriorityMaximum(priority));
        }

        internal static int GetDefaultManualPriorityForDisabledWork()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            int effectiveMaxPriority = PriorityProviderSelector.GetSnapshot().MaxPriority;
            if (settings != null &&
                settings.autoDisabledPriorityMode == BetterWorkTabSettings.AutoDisabledPriorityMode.FixedPriority)
            {
                return Clamp(settings.autoDisabledPriorityFixedValue, 1, effectiveMaxPriority);
            }

            int priorityFloor = Math.Max(PriorityConstants.VanillaMax, GetHighestLivePriority());
            int priority = RoundUpToPriorityBand(priorityFloor);
            return Clamp(priority, PriorityConstants.VanillaMax, effectiveMaxPriority);
        }

        internal static int GetPriorityAfterClick(int currentPriority, int delta)
        {
            PriorityProviderSnapshot currentSnapshot =
                PriorityProviderSelector.GetSnapshotForPriority(currentPriority);
            int currentMax = currentSnapshot.MaxPriority;
            int normalized = Clamp(currentPriority, PriorityConstants.Disabled, currentMax);

            if (normalized == PriorityConstants.Disabled)
            {
                if (delta < 0)
                {
                    return AutoProviderSelectionEnabled()
                        ? GetDefaultManualPriorityForDisabledWork()
                        : currentMax;
                }

                return 1;
            }

            if (delta == 0)
            {
                return normalized;
            }

            // Keep one input gesture inside the priority range that was active when the
            // gesture began. In Auto mode, resolving a fresh snapshot for the next value can
            // make the click itself manufacture a higher required priority and switch the
            // provider from vanilla 1..4 to BWT's extended range. That breaks wraparound:
            // clicking 4 upward becomes 5 instead of 0. Explicit writes/imports still use
            // request-aware snapshot resolution; click cycling must not.
            return PriorityCycleMath.StepEnabled(normalized, currentMax, delta < 0);
        }

        internal static int GetNextManualPriority(int currentPriority, int direction)
        {
            PriorityProviderSnapshot snapshot =
                PriorityProviderSelector.GetSnapshotForPriority(currentPriority);
            int maxPriority = snapshot.MaxPriority;
            int normalized = Clamp(currentPriority, PriorityConstants.Disabled, maxPriority);

            if (direction > 0)
            {
                if (normalized == PriorityConstants.Disabled)
                {
                    return AutoProviderSelectionEnabled()
                        ? GetDefaultManualPriorityForDisabledWork()
                        : maxPriority;
                }

                return PriorityCycleMath.StepEnabled(normalized, maxPriority, decrease: true);
            }

            if (direction < 0)
            {
                if (normalized == PriorityConstants.Disabled)
                {
                    return 1;
                }

                // A bounded step follows the same stable-cycle rule as mouse clicks.
                // Do not let probing max + 1 expand Auto mode as a side effect of scrolling
                // or a bulk/header gesture. The active maximum wraps directly to disabled.
                return PriorityCycleMath.StepEnabled(normalized, maxPriority, decrease: false);
            }

            return normalized;
        }

        internal static bool AutoProviderSelectionEnabled()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null &&
                   settings.priorityMode == PriorityMode.Auto &&
                   settings.delegateToExternalPriorityMods;
        }

        internal static int GetHighestLivePriority()
        {
            EnsureCachedPriorityScan();
            return cachedHighestLivePriority;
        }

        internal static void InvalidateCache()
        {
            cachedFrame = -1;
            cachedGame = null;
        }

        private static void EnsureCachedPriorityScan()
        {
            Game game = Current.Game;
            int frame = Time.frameCount;
            if (cachedFrame == frame && ReferenceEquals(cachedGame, game))
            {
                return;
            }

            cachedGame = game;
            cachedFrame = frame;
            cachedHighestLivePriority = ScanHighestLivePriority(game);
        }

        private static int ScanHighestLivePriority(Game game)
        {
            int maxPriority = 0;
            if (game == null)
            {
                return maxPriority;
            }

            try
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (pawn?.workSettings?.priorities == null)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<WorkTypeDef, int> entry in pawn.workSettings.priorities)
                    {
                        if (entry.Key != null && entry.Value > maxPriority)
                        {
                            maxPriority = entry.Value;
                        }
                    }
                }
            }
            catch
            {
                return maxPriority;
            }

            return ClampMaxPriority(maxPriority);
        }

        private static int RoundUpToPriorityBand(int priority)
        {
            const int bandSize = PriorityConstants.VanillaMax;
            if (priority <= bandSize)
            {
                return bandSize;
            }

            int remainder = priority % bandSize;
            return remainder == 0 ? priority : priority + (bandSize - remainder);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
