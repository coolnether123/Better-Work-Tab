using System.Collections.Generic;
using RimWorld;
using Verse;
using Better_Work_Tab.Features.Workloads;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Per-save custom display names for Work columns and specific jobs.
    /// Stores defName -> player label; never touches defNames themselves.
    /// Backing data lives in GameComponent_BWTWorldSettings (Scribe keys
    /// "customWorkTypeLabels" / "customWorkGiverLabels").
    /// </summary>
    internal static class CustomLabelStore
    {
        /// <summary>Bumped on every change so label caches can invalidate.</summary>
        internal static int Version { get; private set; }

        private static GameComponent_BWTWorldSettings Data =>
            Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();

        internal static bool TryGetWorkTypeLabel(WorkTypeDef def, out string label)
        {
            label = null;
            var map = Data?.CustomWorkTypeLabels;
            return def != null && map != null && map.TryGetValue(def.defName, out label) && !label.NullOrEmpty();
        }

        internal static bool TryGetWorkGiverLabel(WorkGiverDef def, out string label)
        {
            label = null;
            var map = Data?.CustomWorkGiverLabels;
            return def != null && map != null && map.TryGetValue(def.defName, out label) && !label.NullOrEmpty();
        }

        /// <summary>Set or clear (null/empty) a custom label for a Work column.</summary>
        internal static void SetWorkTypeLabel(WorkTypeDef def, string label)
        {
            SetLabel(Data?.CustomWorkTypeLabels, def?.defName, label);
        }

        /// <summary>Set or clear (null/empty) a custom label for a specific job.</summary>
        internal static void SetWorkGiverLabel(WorkGiverDef def, string label)
        {
            SetLabel(Data?.CustomWorkGiverLabels, def?.defName, label);
        }

        internal static bool HasCustomLabel(WorkTypeDef def) => TryGetWorkTypeLabel(def, out _);

        internal static bool HasCustomLabel(WorkGiverDef def) => TryGetWorkGiverLabel(def, out _);

        private static void SetLabel(Dictionary<string, string> map, string defName, string label)
        {
            if (map == null || defName.NullOrEmpty())
            {
                return;
            }

            label = label?.Trim();
            bool changed;
            if (label.NullOrEmpty())
            {
                changed = map.Remove(defName);
            }
            else
            {
                changed = !map.TryGetValue(defName, out string existing) || existing != label;
                map[defName] = label;
            }

            if (changed)
            {
                Version++;
            }
        }
    }
}
