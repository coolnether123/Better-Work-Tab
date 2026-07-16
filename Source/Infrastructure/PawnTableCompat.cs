using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab
{
    /// <summary>
    /// Compatibility accessors for PawnTable members that changed visibility or shape across RimWorld versions.
    /// </summary>
    public static class PawnTableCompat
    {
        private static readonly IList<Pawn> EmptyPawns = new List<Pawn>();

#if v1_1 || (v1_0 || v0_19)
        private static readonly FieldInfo CachedColumnWidthsField =
            AccessTools.Field(typeof(PawnTable), "cachedColumnWidths");

        private static readonly FieldInfo CachedRowHeightsField =
            AccessTools.Field(typeof(PawnTable), "cachedRowHeights");

        private static readonly FieldInfo CachedHeaderHeightField =
            AccessTools.Field(typeof(PawnTable), "cachedHeaderHeight");

        private static readonly FieldInfo CachedSizeField =
            AccessTools.Field(typeof(PawnTable), "cachedSize");

        private static readonly FieldInfo ScrollPositionField =
            AccessTools.Field(typeof(PawnTable), "scrollPosition");

        private static readonly Dictionary<PawnTable, Vector2> FallbackScrollPositions =
            new Dictionary<PawnTable, Vector2>();
#endif

        public static bool IsWorkTable(PawnTable table)
        {
            if (table == null)
                return false;

#if v1_1 || (v1_0 || v0_19)
            var columns = GetColumnsListForReading(table);
            if (columns == null)
                return false;

            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i]?.Worker is PawnColumnWorker_WorkPriority)
                    return true;
            }

            return false;
#else
            return table.def == PawnTableDefOf.Work;
#endif
        }

        public static IList<PawnColumnDef> GetColumnsListForReading(PawnTable table)
        {
            if (table == null)
                return null;

#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
            return table.ColumnsListForReading;
#else
            return table.Columns;
#endif
        }

        public static IList<Pawn> GetPawnsListForReading(PawnTable table)
        {
            if (table == null)
                return EmptyPawns;

            return table.PawnsListForReading ?? EmptyPawns;
        }

        public static IList<Pawn> GetCachedPawns(PawnTable table)
        {
            if (table == null)
                return EmptyPawns;

#if v1_1 || (v1_0 || v0_19)
            return GetPawnsListForReading(table);
#else
            return table.cachedPawns ?? GetPawnsListForReading(table);
#endif
        }

        public static int GetColumnCount(PawnTable table)
        {
            return GetColumnsListForReading(table)?.Count ?? 0;
        }

        public static bool IsDirty(PawnTable table)
        {
#if v1_1 || v0_16
            // 1.1 does not expose the cache-dirty flag. Conservatively force a refresh.
            return true;
#else
            return table == null || table.dirty;
#endif
        }

        public static int GetPawnCount(PawnTable table)
        {
            return GetCachedPawns(table).Count;
        }

        public static float GetHeaderHeight(PawnTable table)
        {
            if (table == null)
                return 0f;

#if v1_1 || (v1_0 || v0_19)
            return table.HeaderHeight;
#else
            return table.cachedHeaderHeight;
#endif
        }

        public static float GetCachedHeaderHeight(PawnTable table)
        {
            if (table == null)
                return 0f;

#if v1_1 || (v1_0 || v0_19)
            if (CachedHeaderHeightField?.GetValue(table) is float cachedHeaderHeight)
                return cachedHeaderHeight;

            return 0f;
#else
            return table.cachedHeaderHeight;
#endif
        }

        public static Vector2 GetSize(PawnTable table)
        {
            return table?.Size ?? Vector2.zero;
        }

        public static Vector2 GetCachedSize(PawnTable table)
        {
            if (table == null)
                return Vector2.zero;

#if v1_1 || (v1_0 || v0_19)
            if (CachedSizeField?.GetValue(table) is Vector2 cachedSize)
                return cachedSize;

            return Vector2.zero;
#else
            return table.cachedSize;
#endif
        }

        public static List<float> GetCachedRowHeights(PawnTable table)
        {
            if (table == null)
                return null;

#if v1_1 || (v1_0 || v0_19)
            return CachedRowHeightsField?.GetValue(table) as List<float>;
#else
            return table.cachedRowHeights;
#endif
        }

        public static float GetCachedColumnWidth(PawnTable table, int columnIndex, float fallback)
        {
            var widths = GetCachedColumnWidths(table);
            if (widths != null && columnIndex >= 0 && columnIndex < widths.Count)
                return widths[columnIndex];

            return fallback;
        }

        public static bool TrySetCachedRowHeights(PawnTable table, List<float> rowHeights)
        {
            if (table == null)
                return false;

#if v1_1 || (v1_0 || v0_19)
            if (CachedRowHeightsField == null)
                return false;

            CachedRowHeightsField.SetValue(table, rowHeights);
            return true;
#else
            table.cachedRowHeights = rowHeights;
            return true;
#endif
        }

        public static bool TrySetCachedSize(PawnTable table, Vector2 size)
        {
            if (table == null)
                return false;

#if v1_1 || (v1_0 || v0_19)
            if (CachedSizeField == null)
                return false;

            CachedSizeField.SetValue(table, size);
            return true;
#else
            table.cachedSize = size;
            return true;
#endif
        }

        public static Vector2 GetScrollPosition(PawnTable table)
        {
            if (table == null)
                return Vector2.zero;

#if v1_1 || (v1_0 || v0_19)
            if (ScrollPositionField?.GetValue(table) is Vector2 reflected)
                return reflected;

            return FallbackScrollPositions.TryGetValue(table, out var stored) ? stored : Vector2.zero;
#else
            return table.scrollPosition;
#endif
        }

        public static void SetScrollPosition(PawnTable table, Vector2 scrollPosition)
        {
            if (table == null)
                return;

#if v1_1 || (v1_0 || v0_19)
            if (ScrollPositionField != null)
            {
                ScrollPositionField.SetValue(table, scrollPosition);
                return;
            }

            FallbackScrollPositions[table] = scrollPosition;
#else
            table.scrollPosition = scrollPosition;
#endif
        }

        public static void BeginScrollView(PawnTable table, Rect outRect, Rect viewRect)
        {
            Vector2 scrollPosition = GetScrollPosition(table);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            SetScrollPosition(table, scrollPosition);
        }

        private static List<float> GetCachedColumnWidths(PawnTable table)
        {
            if (table == null)
                return null;

#if v1_1 || (v1_0 || v0_19)
            return CachedColumnWidthsField?.GetValue(table) as List<float>;
#else
            return table.cachedColumnWidths;
#endif
        }
    }
}
