using Better_Work_Tab.Features.Caching;
using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.PawnOrganizer;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// Invalidates bed count cache when:
    /// 1. Beds are placed (SpawnSetup)
    /// 2. Beds are removed (DeSpawn)
    /// 3. Beds change ownership/prisoner status
    ///
    /// This ensures the cache stays fresh  
    /// </summary>
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.SpawnSetup))]
    public static class Patch_Building_Bed_SpawnSetup
    {
        /// <summary>
        /// Called after a Thing is spawned. If it's a bed, invalidate the cache.
        /// </summary>
        public static void Postfix(Thing __instance
#if !v0_15
            , Map map
#endif
        )
        {
            if (__instance is Building_Bed)
            {
#if v0_15
                Map map = MapCompat.ThingMap(__instance);
#endif
                // New bed spawned on this map; drop cache entry so next lookup recalculates
                BedCachePatchUtility.SafeInvalidateForMap(map, "bed spawned");
            }
        }
    }

    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.DeSpawn))]
    public static class Patch_Building_Bed_DeSpawn
    {
        /// <summary>
        /// Called when a Thing is despawned (destroyed, moved, etc).
        /// If it's a bed, invalidate the cache.
        /// </summary>
        public static void Postfix(Building_Bed __instance)
        {
            if (__instance != null)
            {
                // The map property may be null after DeSpawn; therefore, all maps are invalidated.
                // (This event is infrequent, maintaining acceptable computational efficiency)
                BedCachePatchUtility.SafeInvalidateForMap(null, "bed despawned");
            }
        }
    }

    /// <summary>
    /// Handle prisoner status changes (bed reassignment between colonist/prisoner use).
    /// If a bed's ForPrisoners flag changes, invalidate cache.
    /// </summary>
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.ForPrisoners), MethodType.Setter)]
    public static class Patch_Building_Bed_ForPrisoners
    {
        public static void Postfix(Building_Bed __instance)
        {
            // Switching prisoner flag moves the bed between colonist/prisoner pools
            BedCachePatchUtility.SafeInvalidateForMap(MapCompat.ThingMap(__instance), "bed prisoner flag changed");
        }
    }

    /// <summary>
    /// Handle faction changes (if bed ownership changes to/from player).
    /// </summary>
    [HarmonyPatch(typeof(Building), nameof(Building.SetFaction))]
    public static class Patch_Building_SetFaction
    {
        public static void Postfix(Building __instance)
        {
            if (__instance is Building_Bed bed)
            {
                // Beds changing ownership to/from the player alters usable colonist slots
                BedCachePatchUtility.SafeInvalidateForMap(MapCompat.ThingMap(bed), "bed faction changed");
            }
        }
    }

    /// <summary>
    /// Clear cache on game load to avoid stale data.
    /// </summary>
#if !v0_13
#if v0_15
    [HarmonyPatch(typeof(Game), nameof(Game.LoadData))]
#else
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
#endif
    public static class Patch_Game_LoadGame
    {
        public static void Postfix()
        {
            GameCacheResetUtility.Reset("game loaded");
        }
    }
#endif

    /// <summary>
    /// Clear cache when game unloads to free memory.
    /// </summary>
#if !v0_13
#if v0_15
    [HarmonyPatch(typeof(MapIniter_NewGame), nameof(MapIniter_NewGame.InitNewGeneratedMap))]
#else
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
#endif
    public static class Patch_Game_InitNewGame
    {
        public static void Postfix()
        {
            GameCacheResetUtility.Reset("new game initialized");
        }
    }

    internal static class GameCacheResetUtility
    {
        public static void Reset(string reason)
        {
            BedCachePatchUtility.SafeClear(reason);
            SafeReset(reason, "work-grid snapshot", WorkGridSnapshotProvider.ClearActive);
            SafeReset(reason, "layout geometry", () => PawnOrganizerSystem.Instance?.Layout?.ClearGeometrySnapshot());
            SafeReset(reason, "invalidation audit", WorkGridInvalidationAudit.Reset);
            SafeReset(reason, "invalidation hub", WorkTabInvalidationHub.ResetForGameTeardown);
        }

        private static void SafeReset(string reason, string component, Action reset)
        {
            try
            {
                reset();
            }
            catch (Exception ex)
            {
                Log.Warning($"[BWT] Skipped {component} reset after {reason}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    internal static class BedCachePatchUtility
    {
        public static void SafeInvalidateForMap(Map map, string reason)
        {
            try
            {
                BedCountCache.InvalidateForMap(map);
            }
            catch (Exception ex)
            {
                Log.Warning($"[BWT] Skipped bed cache invalidation after {reason}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void SafeClear(string reason)
        {
            try
            {
                BedCountCache.Clear();
            }
            catch (Exception ex)
            {
                Log.Warning($"[BWT] Skipped bed cache clear after {reason}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
#endif
}
