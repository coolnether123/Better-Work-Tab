using Better_Work_Tab.Features.Caching;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

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
        public static void Postfix(Building_Bed __instance, Map map)
        {
            if (__instance != null)
            {
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
            BedCachePatchUtility.SafeInvalidateForMap(__instance?.Map, "bed prisoner flag changed");
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
                BedCachePatchUtility.SafeInvalidateForMap(bed.Map, "bed faction changed");
            }
        }
    }

    /// <summary>
    /// Clear cache on game load to avoid stale data.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public static class Patch_Game_LoadGame
    {
        public static void Postfix()
        {
            BedCachePatchUtility.SafeClear("game loaded");
        }
    }

    /// <summary>
    /// Clear cache when game unloads to free memory.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class Patch_Game_InitNewGame
    {
        public static void Postfix()
        {
            BedCachePatchUtility.SafeClear("new game initialized");
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
}
