using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// A simple static class to track which work type column header is currently being hovered by the mouse.
    /// This state is used by the DoCell patch to dynamically change its rendering.
    /// </summary>
    public static class ColumnHoverManager
    {
        public static WorkTypeDef HoveredWorkType { get; private set; }

        public static void Set(WorkTypeDef workType) => HoveredWorkType = workType;
        public static void Clear() => HoveredWorkType = null;
    }

    /// <summary>
    /// This class contains the Harmony patches responsible for updating the ColumnHoverManager's state each frame.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_ColumnHeaderHover
    {
        static Patch_ColumnHeaderHover()
        {
            var harmony = new Harmony("Coolnether123.betterworktab.columnhover");

            // Patch DoWindowContents to clear the hover state at the start of each frame.
            harmony.Patch(
                AccessTools.Method(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents)),
                prefix: new HarmonyMethod(typeof(Patch_ColumnHeaderHover), nameof(ClearHoverState_Prefix))
            );

            // Patch DoHeader to set the hover state when the mouse is over a column header.
            harmony.Patch(
                AccessTools.Method(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader)),
                postfix: new HarmonyMethod(typeof(Patch_ColumnHeaderHover), nameof(SetHoverState_Postfix))
            );
        }

        /// <summary>
        /// Prefix patch that runs before the main window draws, ensuring the hover state is reset every frame.
        /// </summary>
        private static void ClearHoverState_Prefix()
        {
            // Only clear if the event is a Repaint, which is the main drawing pass.
            if (Event.current.type == EventType.Repaint)
            {
                ColumnHoverManager.Clear();
            }
        }

        /// <summary>
        /// Postfix patch that runs after a column header is drawn. It checks if the mouse is
        /// over the header and updates the hover manager accordingly.
        /// </summary>
        private static void SetHoverState_Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect)
        {
            if (Event.current.type == EventType.Repaint && Mouse.IsOver(rect))
            {
                ColumnHoverManager.Set(__instance.def.workType);
            }
        }
    }
}