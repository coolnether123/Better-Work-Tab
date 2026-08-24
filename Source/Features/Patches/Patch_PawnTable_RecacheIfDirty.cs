using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Layout;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Patches
{
    /// <summary>
    /// Narrow guard used while vanilla MainTabWindow_PawnTable.PostOpen runs for a
    /// cache-valid BWT table. Vanilla always dirties the table on every reopen; BWT
    /// can retain it when the authoritative table/layout identity is unchanged.
    /// </summary>
    internal static class WarmOpenPawnTableCache
    {
        [System.ThreadStatic]
        private static PawnTable _guardedTable;

        internal static void Begin(PawnTable table)
        {
            _guardedTable = table;
        }

        internal static void End()
        {
            _guardedTable = null;
        }

        internal static bool ShouldSuppressDirty(PawnTable table)
        {
            return table != null && ReferenceEquals(table, _guardedTable);
        }
    }

    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.SetDirty))]
    public static class Patch_PawnTable_SetDirty_WarmOpenCache
    {
        public static bool Prefix(PawnTable __instance)
        {
            return !WarmOpenPawnTableCache.ShouldSuppressDirty(__instance);
        }
    }

    /// <summary>
    /// Synchronizes our custom layout system (with dividers and collapsed sections)
    /// to vanilla's PawnTable cached data.
    /// 
    /// Responsibility:
    /// - Extract row heights from custom layout descriptors
    /// - Build the cachedRowHeights list that vanilla uses for rendering
    /// - Clamp total height to maxTableHeight to prevent content overflow
    /// - Update cachedSize with clamped dimensions
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.RecacheIfDirty))]
    public static class Patch_PawnTable_RecacheIfDirty
    {
        private sealed class SyncState
        {
            public int LayoutRevision = -1;
            public int DescriptorCount = -1;
            public float HeaderHeight = -1f;
            public float PinnedRowsHeight = -1f;
            public float ContentHeight = -1f;
            public float Width = -1f;
            public readonly List<float> RowHeights = new List<float>();
        }

        private static readonly Dictionary<PawnTable, SyncState> SyncStates = new Dictionary<PawnTable, SyncState>();

        private static readonly FieldInfo CachedRowHeightsField =
            AccessTools.Field(typeof(PawnTable), "cachedRowHeights");

        private static readonly FieldInfo CachedSizeField =
            AccessTools.Field(typeof(PawnTable), "cachedSize");

        public static void Postfix(PawnTable __instance)
        {
            // Only process the Work tab
            if (__instance.def != PawnTableDefOf.Work ||
                !FluffyWorkTabGateway.ShouldRunBetterWorkTabFeatures)
                return;

            if (CachedRowHeightsField == null || CachedSizeField == null)
                return;

            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || layout.Rows == null || layout.Rows.Count == 0)
                return;

            var descriptors = layout.GetRowDescriptors();
            if (descriptors == null || descriptors.Count == 0)
                return;

            float headerHeight = layout.HeaderHeight;
            float pinnedRowsHeight = WorkGridLayoutMetrics.GetPinnedRowsHeight();
            float contentHeight = layout.ContentHeight;
            float totalHeight = headerHeight + contentHeight;
            float width = __instance.cachedSize.x;
            int layoutRevision = layout is WorkTabLayoutController workLayout ? workLayout.LayoutRevision : -1;

            if (!SyncStates.TryGetValue(__instance, out var state))
            {
                state = new SyncState();
                SyncStates[__instance] = state;
            }

            bool sameLayout =
                state.LayoutRevision == layoutRevision &&
                state.DescriptorCount == descriptors.Count &&
                Approximately(state.HeaderHeight, headerHeight) &&
                Approximately(state.PinnedRowsHeight, pinnedRowsHeight) &&
                Approximately(state.ContentHeight, contentHeight) &&
                Approximately(state.Width, width);

            if (sameLayout && IsAlreadySynced(__instance, state, totalHeight))
                return;

            state.RowHeights.Clear();
            for (int i = 0; i < descriptors.Count; i++)
            {
                state.RowHeights.Add(descriptors[i].Height);
            }

            state.LayoutRevision = layoutRevision;
            state.DescriptorCount = descriptors.Count;
            state.HeaderHeight = headerHeight;
            state.PinnedRowsHeight = pinnedRowsHeight;
            state.ContentHeight = contentHeight;
            state.Width = width;

            // ---------------------------------------------------------------------------
            // Sync back to vanilla's fields
            // We do NOT clamp to maxTableHeight here; we let the window's RequestedTabSize
            // handle clamping to screen bounds. Pinned BWT rows are deliberately excluded
            // from PawnTable.cachedSize because they are drawn outside the vanilla scroll body.
            // Including them here makes the table body one pinned row taller than its content,
            // which presents as a blank row at the bottom when sub-work is opened.
            // ---------------------------------------------------------------------------
            CachedRowHeightsField.SetValue(__instance, state.RowHeights);
            CachedSizeField.SetValue(__instance, new Vector2(width, totalHeight));
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.01f;
        }

        private static bool IsAlreadySynced(PawnTable table, SyncState state, float totalHeight)
        {
            return ReferenceEquals(CachedRowHeightsField.GetValue(table), state.RowHeights) &&
                   Approximately(table.cachedSize.y, totalHeight);
        }
    }

    /// <summary>
    /// Invalidates BWT layout when vanilla reports a pawn-table change.
    ///
    /// MainTabWindow_BetterWork's sizing controller owns staged bottom anchoring.
    /// This patch deliberately does not reposition the window: an asynchronous
    /// callback here could overwrite a newer layout published by the BWT window.
    /// Other pawn-table windows are not intercepted.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.Notify_PawnsChanged))]
    public static class Patch_MainTabWindow_PawnTable_InvalidateBwtLayout
    {
        public static void Postfix(MainTabWindow_PawnTable __instance)
        {
            if (__instance == null || __instance.GetType().Name != "MainTabWindow_BetterWork")
                return;

            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.PawnListOrder);
        }
    }
}
