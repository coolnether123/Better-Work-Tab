using System.Linq;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    /// <summary>
    /// Centralizes multiplayer synchronization helpers for Better Work Tab actions.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BetterWorkTabMultiplayer
    {
        static BetterWorkTabMultiplayer()
        {
            if (!MP.enabled)
            {
                return;
            }

            MP.RegisterSyncMethod(typeof(BetterWorkTabMultiplayer), nameof(SyncSelectWorklist));
            MP.RegisterSyncMethod(typeof(BetterWorkTabMultiplayer), nameof(SyncApplyWorklist));
            MP.RegisterSyncMethod(typeof(BetterWorkTabMultiplayer), nameof(SyncCreateWorklist));
            MP.RegisterSyncMethod(typeof(BetterWorkTabMultiplayer), nameof(SyncDeleteWorklist));
        }

        // Ruleset syncing removed as per user request.


        internal static void RequestWorklistSelection(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            int index = GetWorklistIndex(component, worklist);
            if (index < 0)
            {
                return;
            }

            SyncSelectWorklist(component, index);
        }

        internal static void RequestApplyWorklist(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            int index = GetWorklistIndex(component, worklist);
            if (index < 0)
            {
                return;
            }

            SyncApplyWorklist(component, index);
        }

        internal static void RequestWorklistSelectionByIndex(GameComponent_BWTWorldSettings component, int index)
        {
            SyncSelectWorklist(component, index);
        }

        internal static void RequestApplyWorklistByIndex(GameComponent_BWTWorldSettings component, int index)
        {
            SyncApplyWorklist(component, index);
        }

        private static int GetWorklistIndex(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            if (component?.SavedWorklists == null || worklist == null)
            {
                return -1;
            }

            return component.SavedWorklists.IndexOf(worklist);
        }

        private static bool TryGetWorklist(GameComponent_BWTWorldSettings component, int index, out Worklist worklist)
        {
            worklist = null;

            if (component?.SavedWorklists == null)
            {
                return false;
            }

            if (index < 0 || index >= component.SavedWorklists.Count)
            {
                return false;
            }

            worklist = component.SavedWorklists[index];
            return worklist != null;
        }

        private static void SyncSelectWorklist(GameComponent_BWTWorldSettings component, int index)
        {
            if (component == null)
            {
                return;
            }

            if (!TryGetWorklist(component, index, out var worklist))
            {
                return;
            }

            component.SelectWorklist(worklist);
        }

        private static void SyncApplyWorklist(GameComponent_BWTWorldSettings component, int index)
        {
            if (component == null)
            {
                return;
            }

            if (!TryGetWorklist(component, index, out var worklist))
            {
                return;
            }

            component.ApplyWorklist(worklist);
        }

        internal static void RequestCreateWorklist(GameComponent_BWTWorldSettings component)
        {
            SyncCreateWorklist(component, "New Worklist");
        }

        internal static void RequestDeleteWorklist(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            int index = GetWorklistIndex(component, worklist);
            if (index < 0)
            {
                return;
            }

            SyncDeleteWorklist(component, index);
        }

        internal static void RequestDeleteWorklistByIndex(GameComponent_BWTWorldSettings component, int index)
        {
            SyncDeleteWorklist(component, index);
        }

        private static void SyncCreateWorklist(GameComponent_BWTWorldSettings component, string label)
        {
            component?.CreateWorklist(label);
        }

        private static void SyncDeleteWorklist(GameComponent_BWTWorldSettings component, int index)
        {
            if (component == null)
            {
                return;
            }

            if (!TryGetWorklist(component, index, out var worklist))
            {
                return;
            }

            component.DeleteWorklist(worklist);
        }

        internal static void RequestRenameWorklist(GameComponent_BWTWorldSettings component, Worklist worklist, string newLabel)
        {
            if (component == null || worklist == null || string.IsNullOrEmpty(newLabel))
                return;
            component.RenameWorklist(worklist, newLabel);
        }
    }
}
