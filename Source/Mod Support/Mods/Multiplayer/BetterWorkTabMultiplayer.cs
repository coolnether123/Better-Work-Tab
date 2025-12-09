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

            SyncSelectWorklist(index);
        }

        internal static void RequestApplyWorklist(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            int index = GetWorklistIndex(component, worklist ?? component?.CurrentWorklist);
            SyncApplyWorklist(index);
        }

        internal static void RequestCreateWorklist(GameComponent_BWTWorldSettings component)
        {
            string label = $"Custom Workload {component?.SavedWorklists.Count ?? 0}";
            SyncCreateWorklist(label);
        }

        internal static void RequestRenameWorklist(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            // Renaming is now local-only or handled differently, removed sync call.
        }

        internal static void RequestDeleteWorklist(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            int index = GetWorklistIndex(component, worklist);
            if (index < 0)
            {
                return;
            }

            SyncDeleteWorklist(index);
        }

        private static int GetWorklistIndex(GameComponent_BWTWorldSettings component, Worklist worklist)
        {
            if (component == null || worklist == null)
            {
                return -1;
            }

            return component.SavedWorklists.IndexOf(worklist);
        }



        [SyncMethod]
        private static void SyncSelectWorklist(int index)
        {
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null || index < 0 || index >= component.SavedWorklists.Count)
            {
                return;
            }

            component.CurrentWorklist = component.SavedWorklists[index];
        }

        [SyncMethod]
        private static void SyncApplyWorklist(int index)
        {
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null)
            {
                return;
            }

            Worklist worklist = null;
            if (index >= 0 && index < component.SavedWorklists.Count)
            {
                worklist = component.SavedWorklists[index];
            }
            else
            {
                worklist = component.CurrentWorklist;
            }

            worklist?.Apply();
        }

        [SyncMethod]
        private static void SyncCreateWorklist(string defaultLabel)
        {
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null)
            {
                return;
            }

            var newWorklist = new Worklist(defaultLabel);
            if (component.CurrentWorklist != null && component.CurrentWorklist.Dividers.Any())
            {
                // We perform a deep copy of each divider to prevent the new and old
                // worklists from sharing the same divider object references.
                newWorklist.Dividers = component.CurrentWorklist.Dividers.Select(d => d.Copy()).ToList();
            }
            component.SavedWorklists.Add(newWorklist);
            component.CurrentWorklist = newWorklist;
            Find.WindowStack.Add(new Dialog_NameNewWorklist(newWorklist));
        }



        [SyncMethod]
        private static void SyncDeleteWorklist(int index)
        {
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null || index < 0 || index >= component.SavedWorklists.Count)
            {
                return;
            }

            bool removingCurrent = component.CurrentWorklist == component.SavedWorklists[index];
            component.SavedWorklists.RemoveAt(index);

            if (!component.SavedWorklists.Any())
            {
                component.CurrentWorklist = null;
                return;
            }

            if (removingCurrent)
            {
                int newIndex = Mathf.Clamp(index - 1, 0, component.SavedWorklists.Count - 1);
                component.CurrentWorklist = component.SavedWorklists[newIndex];
            }
        }
    }
}
