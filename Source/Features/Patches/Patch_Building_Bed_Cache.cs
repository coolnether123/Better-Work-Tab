using Better_Work_Tab.Features.Caching;
using HarmonyLib;
using RimWorld;
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
    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    public static class Patch_Building_Bed_SpawnSetup
    {
        /// <summary>
        /// Called after a Thing is spawned. If it's a bed, invalidate the cache.
        /// </summary>
        public static void Postfix(Thing __instance, Map map)
        {
            if (__instance is Building_Bed)
            {
                // New bed spawned on this map; drop cache entry so next lookup recalculates
                BedCountCache.InvalidateForMap(map);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class Patch_Building_Bed_DeSpawn
    {
        /// <summary>
        /// Called when a Thing is despawned (destroyed, moved, etc).
        /// If it's a bed, invalidate the cache.
        /// </summary>
        public static void Postfix(Thing __instance)
        {
            if (__instance is Building_Bed)
            {
                // __instance.Map may be null after DeSpawn, so we clear all maps
                // (This is a very rare event, so the cost is acceptable)
                BedCountCache.InvalidateForMap(null);
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
            BedCountCache.InvalidateForMap(__instance.Map);
        }
    }

    /// <summary>
    /// Handle faction changes (if bed ownership changes to/from player).
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.SetFaction))]
    public static class Patch_Thing_SetFaction
    {
        public static void Postfix(Thing __instance)
        {
            if (__instance is Building_Bed bed)
            {
                // Beds changing ownership to/from the player alters usable colonist slots
                BedCountCache.InvalidateForMap(bed.Map);
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
            BedCountCache.Clear();
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
            BedCountCache.Clear();
        }
    }
}
