using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal sealed class PriorityChange
    {
        public Pawn Pawn;
        public WorkTypeDef WorkType;
        public int Priority;

        public PriorityChange()
        {
        }

        public PriorityChange(Pawn pawn, WorkTypeDef workType, int priority)
        {
            Pawn = pawn;
            WorkType = workType;
            Priority = priority;
        }
    }

    internal static class PriorityAuthority
    {
        internal static int GetConfiguredMaxPriority()
        {
            return Math.Max(1, BetterWorkTabMod.Settings?.maxPriorityInt ?? DefaultSettings.maxPriority);
        }

        internal static int GetEffectiveMaxPriority()
        {
            if (!MultiplayerBridge.Active)
            {
                return GetConfiguredMaxPriority();
            }

            int sharedMaxPriority = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.SharedMaxPriority ?? 0;
            return sharedMaxPriority > 0
                ? Math.Max(1, sharedMaxPriority)
                : Math.Max(1, DefaultSettings.maxPriority);
        }

        internal static int ClampPriority(int priority)
        {
            return Mathf.Clamp(priority, 0, GetEffectiveMaxPriority());
        }

        internal static void SyncLocalSettingsToSharedValue(int maxPriority)
        {
            if (BetterWorkTabMod.Settings != null)
            {
                BetterWorkTabMod.Settings.maxPriorityInt = Math.Max(1, maxPriority);
            }
        }

        internal static int GetNextManualPriority(int currentPriority, int direction)
        {
            int maxPriority = GetEffectiveMaxPriority();
            if (direction > 0)
            {
                if (currentPriority == 0)
                {
                    return maxPriority;
                }

                if (currentPriority > 1)
                {
                    return currentPriority - 1;
                }

                return currentPriority;
            }

            if (direction < 0)
            {
                if (currentPriority == maxPriority)
                {
                    return 0;
                }

                if (currentPriority > 0)
                {
                    return currentPriority + 1;
                }
            }

            return currentPriority;
        }

        internal static List<PriorityChange> BuildHeaderPriorityChanges(IEnumerable<Pawn> pawns, WorkTypeDef workType, int button, bool useWorkPriorities)
        {
            var changes = new List<PriorityChange>();
            if (pawns == null || workType == null)
            {
                return changes;
            }

            foreach (Pawn pawn in pawns)
            {
                if (pawn == null || pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                int currentPriority = pawn.workSettings.GetPriority(workType);
                int nextPriority = currentPriority;

                if (useWorkPriorities)
                {
                    int direction = button == 0 ? 1 : -1;
                    nextPriority = GetNextManualPriority(currentPriority, direction);
                }
                else
                {
                    nextPriority = button == 0 ? MaxPriorityLogic.GetDefaultEnabledPriority() : 0;
                }

                if (nextPriority != currentPriority)
                {
                    changes.Add(new PriorityChange(pawn, workType, nextPriority));
                }
            }

            return changes;
        }
    }

    internal static class PriorityCommandService
    {
        internal static PriorityChange CreateChange(Pawn pawn, WorkTypeDef workType, int priority)
        {
            return new PriorityChange(pawn, workType, PriorityAuthority.ClampPriority(priority));
        }

        internal static void ApplyPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return;
            }

            pawn.workSettings.SetPriority(workType, PriorityAuthority.ClampPriority(priority));
        }

        internal static void ApplyPriorityChanges(IEnumerable<PriorityChange> changes)
        {
            if (changes == null)
            {
                return;
            }

            foreach (PriorityChange change in changes)
            {
                if (change == null)
                {
                    continue;
                }

                ApplyPriority(change.Pawn, change.WorkType, change.Priority);
            }
        }

        internal static void SetUseWorkPriorities(bool enabled)
        {
            if (Current.Game?.playSettings == null)
            {
                return;
            }

            bool changed = Current.Game.playSettings.useWorkPriorities != enabled;
            Current.Game.playSettings.useWorkPriorities = enabled;
            if (!changed)
            {
                return;
            }

            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }
        }
    }

    internal static class PriorityCommandRouter
    {
        internal static void ApplyPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (MultiplayerBridge.Active)
            {
                PriorityMultiplayerSync.ApplyPriorityChange(pawn, workType, PriorityAuthority.ClampPriority(priority));
                return;
            }

            PriorityCommandService.ApplyPriority(pawn, workType, priority);
        }

        internal static void ApplyPriorityChanges(List<PriorityChange> changes)
        {
            if (changes == null || changes.Count == 0)
            {
                return;
            }

            if (MultiplayerBridge.Active)
            {
                PriorityMultiplayerSync.ApplyPriorityChanges(changes);
                return;
            }

            PriorityCommandService.ApplyPriorityChanges(changes);
        }

        internal static void SetUseWorkPriorities(bool enabled)
        {
            if (MultiplayerBridge.Active)
            {
                PriorityMultiplayerSync.SetUseWorkPriorities(enabled);
                return;
            }

            PriorityCommandService.SetUseWorkPriorities(enabled);
        }

        internal static void SynchronizeSharedMaxPriorityFromSettings()
        {
            if (!MultiplayerBridge.Active)
            {
                return;
            }

            PriorityMultiplayerSync.SetSharedMaxPriority(PriorityAuthority.GetConfiguredMaxPriority());
        }

        internal static void EnsureSharedMaxPriorityInitialized()
        {
            if (!MultiplayerBridge.Active)
            {
                return;
            }

            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null)
            {
                return;
            }

            if (component.SharedMaxPriority > 0)
            {
                PriorityAuthority.SyncLocalSettingsToSharedValue(component.SharedMaxPriority);
                return;
            }

            if (MultiplayerBridge.Host)
            {
                PriorityMultiplayerSync.SetSharedMaxPriority(PriorityAuthority.GetConfiguredMaxPriority());
            }
            else
            {
                PriorityAuthority.SyncLocalSettingsToSharedValue(DefaultSettings.maxPriority);
            }
        }
    }

    [StaticConstructorOnStartup]
    internal static class PriorityMultiplayerSync
    {
        static PriorityMultiplayerSync()
        {
            if (!MP.enabled)
            {
                return;
            }

            MP.RegisterSyncWorker<PriorityChange>(SyncPriorityChange);
        }

        private static void SyncPriorityChange(SyncWorker sync, ref PriorityChange change)
        {
            if (sync.isWriting)
            {
                if (change == null)
                {
                    sync.Write(false);
                    return;
                }

                sync.Write(true);
            }
            else
            {
                if (!sync.Read<bool>())
                {
                    change = null;
                    return;
                }
            }

            if (change == null)
            {
                change = new PriorityChange();
            }

            sync.Bind(ref change.Pawn);
            sync.Bind(ref change.WorkType);
            sync.Bind(ref change.Priority);
        }

        [SyncMethod]
        public static void ApplyPriorityChange(Pawn pawn, WorkTypeDef workType, int priority)
        {
            PriorityCommandService.ApplyPriority(pawn, workType, priority);
        }

        [SyncMethod]
        public static void ApplyPriorityChanges(List<PriorityChange> changes)
        {
            PriorityCommandService.ApplyPriorityChanges(changes);
        }

        [SyncMethod]
        public static void SetSharedMaxPriority(int maxPriority)
        {
            int clampedMaxPriority = Math.Max(1, maxPriority);
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component != null)
            {
                component.SharedMaxPriority = clampedMaxPriority;
            }

            PriorityAuthority.SyncLocalSettingsToSharedValue(clampedMaxPriority);
        }

        [SyncMethod]
        public static void SetUseWorkPriorities(bool enabled)
        {
            PriorityCommandService.SetUseWorkPriorities(enabled);
        }
    }
}
