using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Shared drag state for custom drag-and-drop in the Work tab.
    /// Keep this small and reset it aggressively when context changes.
    /// </summary>
    internal static class WorkTabDragState
    {
        internal enum DragKind { None, Column, Row }

        internal static DragKind Kind = DragKind.None;

        internal static PawnTable ActiveTable = null; // publicized by Publicizer
        internal static Vector2 MouseStart;
        internal static bool MouseDown;

        // Column drag specifics
        internal static int FromWorkSlot = -1;  // index within work-only subset
        internal static int ToWorkSlot = -1;    // insertion index within work-only subset
        internal static int FromVisibleIndex = -1;
        internal static float ColumnDragDX;
        internal static Rect ColumnOriginRect;
        internal static PawnColumnDef DragColumnDef;
        internal static List<int> WorkSlotToVisible = new List<int>();

        // Row drag specifics
        internal static int FromRow = -1;
        internal static int ToRow = -1;
        internal static float RowDragDY;
        internal static Rect RowOriginRect;
        internal static Pawn DragPawn;

        internal static void Reset()
        {
            Kind = DragKind.None;
            ActiveTable = null;
            MouseDown = false;
            FromWorkSlot = -1;
            ToWorkSlot = -1;
            FromVisibleIndex = -1;
            ColumnDragDX = 0f;
            ColumnOriginRect = default;
            DragColumnDef = null;
            WorkSlotToVisible.Clear();
            FromRow = -1;
            ToRow = -1;
            RowDragDY = 0f;
            RowOriginRect = default;
            DragPawn = null;
        }

        internal static bool IsActiveFor(PawnTable table) => ActiveTable == table && Kind != DragKind.None;
    }
}

