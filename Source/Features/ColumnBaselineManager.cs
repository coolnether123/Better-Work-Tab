using Better_Work_Tab.Features.Workloads;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// Tracks baseline column order per save and a "true vanilla" order derived from official RimWorld content.
    /// Baseline is captured once per save (including modded columns). True vanilla is static for the session.
    /// </summary>
    public static class ColumnBaselineManager
    {
        private static List<string> _trueVanillaOrder;

        /// <summary>
        /// Returns the per-save baseline order, capturing it from the current work table if missing.
        /// </summary>
        public static List<string> GetBaselineOrder(GameComponent_BWTWorldSettings worldSettings)
        {
            EnsureBaseline(worldSettings);
            return worldSettings?.ColumnBaselineOrder ?? new List<string>();
        }

        /// <summary>
        /// Returns the static vanilla order (official RimWorld work types only).
        /// </summary>
        public static List<string> GetTrueVanillaOrder()
        {
            EnsureTrueVanillaOrder();
            return _trueVanillaOrder ?? new List<string>();
        }

        /// <summary>
        /// Ensures the per-save baseline list exists, capturing the current column order if none was stored yet.
        /// </summary>
        public static void EnsureBaseline(GameComponent_BWTWorldSettings worldSettings)
        {
            if (worldSettings == null)
            {
                return;
            }

            if (worldSettings.ColumnBaselineOrder == null)
            {
                worldSettings.ColumnBaselineOrder = new List<string>();
            }

            if (worldSettings.ColumnBaselineOrder.Count == 0)
            {
                worldSettings.ColumnBaselineOrder = CaptureCurrentOrder();
                BetterWorkTabMod.DebugLog($"[BWT] Captured column baseline order: {string.Join(", ", worldSettings.ColumnBaselineOrder)}", DebugFeature.DragDrop);
            }
        }

        /// <summary>
        /// Captures the current order of work priority columns from the work table def.
        /// </summary>
        public static List<string> CaptureCurrentOrder()
        {
            var def = PawnTableDefOf.Work;
            var result = new List<string>();

            if (def?.columns == null)
            {
                return result;
            }

            foreach (var col in def.columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    result.Add(col.workType.defName);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns true if the column is currently sitting at its baseline position.
        /// Baseline comparison is used to decide whether to mark a column as "moved" by the player.
        /// </summary>
        public static bool IsInBaselinePosition(WorkTypeDef workType, GameComponent_BWTWorldSettings worldSettings)
        {
            if (workType?.defName == null)
            {
                return true;
            }

            var baselineOrder = GetBaselineOrder(worldSettings);
            if (baselineOrder.Count == 0)
            {
                return true;
            }

            var currentOrder = CaptureCurrentOrder();
            int baselinePos = baselineOrder.IndexOf(workType.defName);
            int currentPos = currentOrder.IndexOf(workType.defName);

            if (baselinePos < 0 || currentPos < 0)
            {
                return true;
            }

            return baselinePos == currentPos;
        }

        private static void EnsureTrueVanillaOrder()
        {
            if (_trueVanillaOrder != null)
            {
                return;
            }

            _trueVanillaOrder = new List<string>();
            var def = PawnTableDefOf.Work;

            if (def?.columns == null)
            {
                return;
            }

            // Only track columns from official RimWorld content (Core/DLC); modded work types are excluded.
            foreach (var col in def.columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    var pack = col.workType.modContentPack;
                    if (pack == null || pack.IsOfficialMod)
                    {
                        _trueVanillaOrder.Add(col.workType.defName);
                    }
                }
            }

            BetterWorkTabMod.DebugLog($"[BWT] Captured true vanilla column order: {string.Join(", ", _trueVanillaOrder)}", DebugFeature.DragDrop);
        }
    }
}
