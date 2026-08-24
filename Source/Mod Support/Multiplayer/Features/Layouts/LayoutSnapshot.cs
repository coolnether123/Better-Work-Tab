using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;
using RimWorld;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer;
using Multiplayer.API;

namespace Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts
{
    public class LayoutSnapshot
    {
        public List<PawnDivider> Dividers = new List<PawnDivider>();
        public Dictionary<int, int> PawnRowOrder = new Dictionary<int, int>();
        public Dictionary<string, string> PawnHexColors = new Dictionary<string, string>(); 
        public List<string> ColumnOrder = new List<string>();

        public static LayoutSnapshot Capture()
        {
             var profile = BWTLocalProfileStore.Current;
             var snap = new LayoutSnapshot();
             if (profile == null) return snap;
             
             snap.Dividers = profile.ActiveDividers?.Select(d => d.Copy()).ToList() ?? new List<PawnDivider>();
             snap.PawnRowOrder = new Dictionary<int, int>(profile.PawnRowOrder);
             
             var bg = profile.PawnBackgroundColors?.ToDictionary(k => k.Key, v => ColorUtility.ToHtmlStringRGBA(v.Value));
             snap.PawnHexColors = bg ?? new Dictionary<string, string>();

             // Follow Mode carries the leader's visible layout. Application-owned
             // ColumnCurrentOrder remains the shared simulation execution tiebreaker.
             var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
             if (comp != null && comp.ColumnCurrentOrder != null)
                 snap.ColumnOrder = new List<string>(comp.ColumnCurrentOrder);
             
             return snap;
        }
        
        public void Apply()
        {
             var profile = BWTLocalProfileStore.Current;
             if (profile == null) return;
             
             profile.ActiveDividers = Dividers; 
             profile.PawnRowOrder = PawnRowOrder;
             
             profile.PawnBackgroundColors = new Dictionary<string, Color>();
             foreach(var kvp in PawnHexColors)
             {
                  if (ColorUtility.TryParseHtmlString("#" + kvp.Value, out Color c))
                      profile.PawnBackgroundColors[kvp.Key] = c;
             }
             
             // This is a local presentation projection. It must not mutate the
             // synchronized execution-order source or its generation.
             if (ColumnOrder != null && ColumnOrder.Count > 0)
             {
                  WorkColumnOrderManager.TryApplyLocalFollowedColumnOrder(ColumnOrder);
             }
             
             BWTLocalProfileStore.MarkDirty();
             
             MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
             Better_Work_Tab.PawnOrganizer.PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
        }

        public void Sync(SyncWorker sync)
        {
            sync.Bind(ref Dividers);
            sync.Bind(ref PawnRowOrder);
            sync.Bind(ref PawnHexColors);
            sync.Bind(ref ColumnOrder);
        }
    }
}
