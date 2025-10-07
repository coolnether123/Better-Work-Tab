using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;

namespace Better_Work_Tab.DragDrop
{
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    internal static class WorkTabDragPatch
    {
        // A dictionary to hold a separate drag session for each pawn table.
        // This prevents state conflicts and is much more robust than a global static.
        private static readonly Dictionary<PawnTable, DragSession> _dragSessions = new Dictionary<PawnTable, DragSession>();

        // Helper to get or create a session for a given table.
        private static DragSession GetSessionFor(PawnTable table)
        {
            if (!_dragSessions.TryGetValue(table, out DragSession session))
            {
                session = new DragSession();
                _dragSessions[table] = session;
            }
            return session;
        }

        // Prefix handles mouse input BEFORE vanilla uses the event.
        public static void Prefix(PawnTable __instance, Vector2 position)
        {
            var session = GetSessionFor(__instance);
            DragDropHandler.OnGUI(__instance, position, session);
        }

        // Postfix handles drawing AFTER vanilla draws the table.
        public static void Postfix(PawnTable __instance, Vector2 position)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var session = GetSessionFor(__instance);
            if (session.IsDragging())
            {
                DragDropHandler.DrawDragVisuals(__instance, position, session);
            }
        }
    }
}