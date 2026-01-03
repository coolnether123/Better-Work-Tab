using Better_Work_Tab.PawnOrganizer;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Patches
{
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
        private static readonly FieldInfo CachedRowHeightsField =
            AccessTools.Field(typeof(PawnTable), "cachedRowHeights");

        private static readonly FieldInfo CachedSizeField =
            AccessTools.Field(typeof(PawnTable), "cachedSize");

        private static readonly FieldInfo MaxTableHeightField =
            AccessTools.Field(typeof(PawnTable), "maxTableHeight");

        public static void Postfix(PawnTable __instance)
        {
            // Only process the Work tab
            if (__instance.def != PawnTableDefOf.Work)
                return;

            if (CachedRowHeightsField == null || CachedSizeField == null)
                return;

            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || layout.Rows == null || layout.Rows.Count == 0)
                return;

            // Invalidate cached descriptors to force rebuild with current state
            if (layout is WorkTabLayoutController workLayout)
            {
                workLayout.InvalidateRowDescriptors();
            }

            var descriptors = layout.GetRowDescriptors();
            if (descriptors == null || descriptors.Count == 0)
                return;

            // ═══════════════════════════════════════════════════════════════════════════
            // Build row heights from layout descriptors
            // Create a new list each frame (not static) to avoid reference corruption
            // ═══════════════════════════════════════════════════════════════════════════
            var rowHeights = new List<float>(descriptors.Count);
            float contentHeight = 0f;

            foreach (var desc in descriptors)
            {
                rowHeights.Add(desc.Height);
                contentHeight += desc.Height;
            }

            float headerHeight = __instance.cachedHeaderHeight;
            float totalHeight = headerHeight + contentHeight;

            // ═══════════════════════════════════════════════════════════════════════════
            // Sync back to vanilla's fields
            // We do NOT clamp to maxTableHeight here; we let the window's RequestedTabSize
            // handle clamping to screen bounds. This prevents the PawnTable size from 
            // being smaller than its content, which would trigger unnecessary scrollbars
            // even when the window has room to grow.
            // ═══════════════════════════════════════════════════════════════════════════
            float width = __instance.cachedSize.x;
            CachedRowHeightsField.SetValue(__instance, rowHeights);
            CachedSizeField.SetValue(__instance, new Vector2(width, totalHeight));
        }
    }

    /// <summary>
    /// Enforces bottom-anchoring for the work tab window.
    /// 
    /// When content changes, the window resizes. This patch ensures it grows UPWARD
    /// (by moving its top edge up) rather than growing downward, keeping it pinned
    /// to the bottom of the screen.
    /// 
    /// Runs after vanilla's sizing logic to enforce this invariant:
    /// windowRect.y + windowRect.height = screenHeight - 35
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.Notify_PawnsChanged))]
    public static class Patch_MainTabWindow_PawnTable_AnchorToBottom
    {
        public static void Postfix(MainTabWindow_PawnTable __instance)
        {
            if (__instance == null || __instance.GetType().Name != "MainTabWindow_BetterWork")
                return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (__instance == null)
                    return;

                var windowRect = __instance.windowRect;

                // Calculate Y such that bottom of window stays at (screenHeight - 35)
                float screenBottom = Verse.UI.screenHeight - 35f;
                float targetY = Mathf.Max(0f, screenBottom - windowRect.height);

                windowRect.y = targetY;
                __instance.windowRect = windowRect;
            });
        }
    }
}