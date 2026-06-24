using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Patches
{
    /// <summary>
    /// Synchronizes BWT row layout back into PawnTable cached sizing.
    /// </summary>
    [HarmonyPatch]
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

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnTable), "RecacheIfDirty")
                ?? AccessTools.Method(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI));
        }

        public static void Postfix(PawnTable __instance)
        {
            if (!PawnTableCompat.IsWorkTable(__instance))
            {
                return;
            }

            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || layout.Rows == null || layout.Rows.Count == 0)
            {
                return;
            }

            var descriptors = layout.GetRowDescriptors();
            if (descriptors == null || descriptors.Count == 0)
            {
                return;
            }

            float headerHeight = layout.HeaderHeight;
            float pinnedRowsHeight = TimePriorityPlannerPrototype.HeaderPinnedRowsHeight +
                SubWorkDrilldownState.GlobalRowVisibleHeight;
            float contentHeight = layout.ContentHeight;
            float totalHeight = headerHeight + contentHeight;
            float width = PawnTableCompat.GetCachedSize(__instance).x;
            if (width <= 0f)
            {
                width = PawnTableCompat.GetSize(__instance).x;
            }
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
            {
                return;
            }

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

            // Pinned BWT rows draw outside the vanilla scroll body. Keep them out of
            // PawnTable cached height so old tables do not show a blank bottom row.
            PawnTableCompat.TrySetCachedRowHeights(__instance, state.RowHeights);
            PawnTableCompat.TrySetCachedSize(__instance, new Vector2(width, totalHeight));
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.01f;
        }

        private static bool IsAlreadySynced(PawnTable table, SyncState state, float totalHeight)
        {
            return ReferenceEquals(PawnTableCompat.GetCachedRowHeights(table), state.RowHeights) &&
                   Approximately(PawnTableCompat.GetCachedSize(table).y, totalHeight);
        }
    }

    /// <summary>
    /// Keeps the work tab bottom-anchored after vanilla pawn-table size updates.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.Notify_PawnsChanged))]
    public static class Patch_MainTabWindow_PawnTable_AnchorToBottom
    {
        public static void Postfix(MainTabWindow_PawnTable __instance)
        {
            if (__instance == null || __instance.GetType().Name != "MainTabWindow_BetterWork")
            {
                return;
            }

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (__instance == null)
                {
                    return;
                }

                var windowRect = __instance.windowRect;
                float screenBottom = Verse.UI.screenHeight - 35f;
                float targetY = Mathf.Max(0f, screenBottom - windowRect.height);

                windowRect.y = targetY;
                __instance.windowRect = windowRect;
            });
        }
    }
}
