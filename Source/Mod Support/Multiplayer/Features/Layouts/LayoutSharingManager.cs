using Multiplayer.API;
using Verse;
using RimWorld;
using System.Linq;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.PawnOrganizer.Data;

namespace Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts
{
    [StaticConstructorOnStartup]
    public static class LayoutSharingManager
    {
         public static string ObservedLeaderName = null;
         private static LayoutSnapshot _localBeforeFollow = null; // Backup before following
         
         public static bool IsFollowing => !string.IsNullOrEmpty(ObservedLeaderName);
         
         static LayoutSharingManager()
         {
             if (MP.enabled)
             {
                 MP.RegisterSyncWorker<PawnDivider>(SyncPawnDivider);
                 MP.RegisterSyncWorker<LayoutSnapshot>(SyncLayoutSnapshot);
             }
         }

         static void SyncPawnDivider(SyncWorker sync, ref PawnDivider d)
         {
             if (sync.isWriting)
             {
                 if (d == null)
                 {
                     sync.Write(false);
                     return;
                 }
                 sync.Write(true);
             }
             else
             {
                 if (!sync.Read<bool>())
                 {
                     d = null;
                     return;
                 }
             }
             
             if (d == null) d = new PawnDivider();
             
             sync.Bind(ref d.DividerName);
             sync.Bind(ref d.DividerColor);
             sync.Bind(ref d.DisplayOrder);
             sync.Bind(ref d.ShowLabel);
             sync.Bind(ref d.IsCollapsed);
             sync.Bind(ref d.Height);
             
             // Manually handle enum if needed, but Bind usually supports it
             // If not, we can cast. Let's be safe.
             byte font = (byte)d.LabelFont;
             sync.Bind(ref font);
             d.LabelFont = (GameFont)font;
         }
         
         static void SyncLayoutSnapshot(SyncWorker sync, ref LayoutSnapshot snap)
         {
             if (sync.isWriting)
             {
                 if (snap == null)
                 {
                     sync.Write(false);
                     return;
                 }
                 sync.Write(true);
             }
             else
             {
                 if (!sync.Read<bool>())
                 {
                     snap = null;
                     return;
                 }
             }

             if (snap == null) snap = new LayoutSnapshot();
             snap.Sync(sync);
         }
         
         public static void SetFollow(string leaderName)
         {
              if (string.IsNullOrEmpty(leaderName))
              {
                  StopFollowing();
                  return;
              }
              
              // Backup current layout before following
              _localBeforeFollow = LayoutSnapshot.Capture();
              BWTLocalProfileStore.SuspendSaving = true;
              
              ObservedLeaderName = leaderName;
              Log.Message($"[BWT-MP] Started following: {leaderName}");
              
              if (!string.IsNullOrEmpty(leaderName)) 
                  RequestLayout(leaderName); 
         }
         
         public static void StopFollowing()
         {
             if (_localBeforeFollow != null)
             {
                 // Restore local layout
                 _localBeforeFollow.Apply();
                 _localBeforeFollow = null;
             }
             
             ObservedLeaderName = null;
             BWTLocalProfileStore.SuspendSaving = false;
             BWTLocalProfileStore.MarkDirty(); // Save restored layout
             
             Log.Message("[BWT-MP] Stopped following (restored local layout)");
         }
         
         public static void CopyPawnRowToLocalAndStop(Pawn pawn)
         {
             if (!IsFollowing || pawn == null || _localBeforeFollow == null) return;
             
             // Get current row order from the followed view
             var profile = BWTLocalProfileStore.Current;
             if (profile != null && profile.PawnRowOrder.TryGetValue(pawn.thingIDNumber, out int currentOrder))
             {
                 // Insert this pawn into the backup at its current position
                 _localBeforeFollow.PawnRowOrder[pawn.thingIDNumber] = currentOrder;
             }
             
             // Stop following and apply modified backup
             Log.Message($"[BWT-MP] Copied pawn row (ID: {pawn.thingIDNumber}) to local layout and stopped following");
             StopFollowing();
         }
         
         public static void CopyDividerToLocalAndStop(PawnDivider divider)
         {
             if (!IsFollowing || divider == null || _localBeforeFollow == null) return;
             
             // Copy the divider into the backup
             var copiedDivider = divider.Copy();
             if (!_localBeforeFollow.Dividers.Any(d => d.DividerName == copiedDivider.DividerName))
             {
                 _localBeforeFollow.Dividers.Add(copiedDivider);
             }
             
             // Stop following and apply modified backup
             Log.Message($"[BWT-MP] Copied divider '{divider.DividerName}' to local layout and stopped following");
             StopFollowing();
         }
         
         public static void RequestLayout(string target)
         {
              ReceiveLayoutRequest(MultiplayerBridge.LocalPlayerName, target);
         }
         
         [SyncMethod]
         public static void ReceiveLayoutRequest(string requestor, string target)
         {
              if (MultiplayerBridge.LocalPlayerName == target)
              {
                   if (BWTLocalProfileStore.Current != null && BWTLocalProfileStore.Current.AllowLayoutRequests)
                   {
                        Log.Message($"[BWT-MP] Received layout request from {requestor}, sending snapshot");
                        var snap = LayoutSnapshot.Capture();
                        ReceiveSnapshot(MultiplayerBridge.LocalPlayerName, requestor, snap);
                   }
                   else
                   {
                        Log.Message($"[BWT-MP] Denied layout request from {requestor} (AllowLayoutRequests=false)");
                   }
              }
         }
         
         public static void NotifyLayoutChanged()
         {
             if (!MultiplayerBridge.Active) return;
             
             if (BWTLocalProfileStore.Current != null && BWTLocalProfileStore.Current.AllowLiveLayoutBroadcast)
             {
                  // Throttle? Or just send. 
                  // If frequent updates (drag), we should probably only send on Drop.
                  var snap = LayoutSnapshot.Capture();
                  ReceiveSnapshot(MultiplayerBridge.LocalPlayerName, null, snap);
             }
         }

         [SyncMethod]
         public static void ReceiveSnapshot(string sender, string target, LayoutSnapshot snap)
         {
              if (target == MultiplayerBridge.LocalPlayerName)
              {
                   snap.Apply();
                   Log.Message($"[BWT] Layout loaded from {sender}");
                   return;
              }
              
              if (string.IsNullOrEmpty(target) && ObservedLeaderName == sender)
              {
                   snap.Apply();
                   Log.Message($"[BWT-MP] Layout updated from followed leader: {sender}");
              }
         }
    }
}
