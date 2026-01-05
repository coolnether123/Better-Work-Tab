using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Caching
{
    /// <summary>
    /// Caches bed counts per map to avoid expensive queries every frame.
    /// Uses throttled polling (4-second intervals) with event-driven invalidation.
    /// When beds are built/destroyed, the cache is immediately invalidated.
    /// </summary>
    public static class BedCountCache
    {
        // Cache storage: Map ID → (bed count, last calculation time)
        // Initial capacity is a hint only; dictionary grows automatically if more maps exist.
        private static readonly Dictionary<int, (int Count, float LastCalcTime)> _bedCountCache =
            new Dictionary<int, (int, float)>(4);

        // Configuration
        private const float CacheValiditySeconds = 4f; // Recalculate every 4 seconds max
        private static bool _initialized;

        /// <summary>
        /// Gets the current colonist bed count for a map.
        /// Returns cached value if fresh, otherwise recalculates.
        /// </summary>
        public static int GetBedCount(Map map)
        {
            if (map == null)
                return 0;

            var settings = BetterWorkTabMod.Settings;
            bool allowCache = (settings?.enablePerformanceOptimizations ?? true) &&
                              (settings?.cacheBedCounts ?? true);
            if (!allowCache)
            {
                return CalculateBedCount(map);
            }

            int mapId = map.uniqueID;
            float currentTime = Time.realtimeSinceStartup;

            // Check if we have a cached entry that's still valid
            if (_bedCountCache.TryGetValue(mapId, out var cached))
            {
                float timeSinceLastCalc = currentTime - cached.LastCalcTime;

                // Return cached value if fresh (less than 4 seconds old)
                if (timeSinceLastCalc < CacheValiditySeconds)
                {
                    // Cache hit path: no expensive bed iteration this frame
                    return cached.Count;
                }
            }

            // Cache expired or doesn't exist - recalculate
            int bedCount = CalculateBedCount(map);
            _bedCountCache[mapId] = (bedCount, currentTime);

            return bedCount;
        }

        /// <summary>
        /// Invalidates the cache for a specific map (or all maps if null).
        /// Called when beds are built, destroyed, or ownership changes.
        /// </summary>
        public static void InvalidateForMap(Map map)
        {
            if (map == null)
            {
                // Invalidate ALL maps (nuclear option, use sparingly)
                _bedCountCache.Clear();
                BetterWorkTabMod.DebugLog(
                    "[BedCountCache] Invalidated all maps (map is null)",
                    DebugFeature.Performance);
                return;
            }

            int mapId = map.uniqueID;

            // Force recalculation on next GetBedCount call by removing entry
            if (_bedCountCache.TryGetValue(mapId, out _))
            {
                // Removal avoids counting work this frame; next read will repopulate
                _bedCountCache.Remove(mapId);
                BetterWorkTabMod.DebugLog(
                    $"[BedCountCache] Invalidated map {mapId} (bed added/removed)",
                    DebugFeature.Performance);
            }
        }

        /// <summary>
        /// Clears all cached data. Call on game load/unload.
        /// </summary>
        public static void Clear()
        {
            _bedCountCache.Clear();
            _initialized = false;
        }

        /// <summary>
        /// Counts colonist beds (not prisoner beds) on a map.
        /// Called when cache needs recalculation.
        /// </summary>
        private static int CalculateBedCount(Map map)
        {
            if (map?.listerBuildings == null)
                return 0;

            int totalSlots = 0;

            // Retrieve all player-faction beds (this is a computationally expensive operation; results are cached)
            var beds = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>();

            if (beds != null)
            {
                foreach (var bed in beds)
                {
                    // Skip prisoner beds and non-player faction beds
                    if (bed == null || bed.ForPrisoners || bed.Faction != Faction.OfPlayer)
                        continue;

                    // Skip animal beds (those without the humanlike property)
                    if (!bed.def.building.bed_humanlike)
                        continue;

                    // Skip cribs (babies do not appear in the work tab)
                    if (bed.ForHumanBabies)
                        continue;

                    // Skip deathrest caskets (Biotech)
                    if (ModsConfig.BiotechActive && bed.def == ThingDefOf.DeathrestCasket)
                        continue;

                    // Count the available sleeping slots on valid colonist beds
                    totalSlots += bed.SleepingSlotsCount;
                }
            }

            return totalSlots;
        }

        /// <summary>
        /// Gets cache statistics for debugging (how many maps cached, cache hit rate).
        /// </summary>
        public static string GetCacheStats()
        {
            return $"BedCountCache: {_bedCountCache.Count} maps cached";
        }
    }
}
