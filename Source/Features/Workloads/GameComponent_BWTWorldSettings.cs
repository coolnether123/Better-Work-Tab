using Better_Work_Tab.Features;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.Data;
using Multiplayer.API;
using Spine.Profiling;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    public class GameComponent_BWTWorldSettings : GameComponent
    {
        public List<Worklist> SavedWorklists = new List<Worklist>();
        public Worklist CurrentWorklist = null;
        public List<string> ColumnCurrentOrder = new List<string>();
        public List<string> ColumnBaselineOrder = new List<string>();
        public List<PawnDivider> ActiveDividers = new List<PawnDivider>();

        public GameComponent_BWTWorldSettings(Game game) : base()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // In MP: clients should NOT load their local column order
            // Only host's order matters for deterministic AI
            if (MP.enabled && !MP.IsHosting)
            {
                // Skip initializing column order from local settings
                // It will be synced from the host when they reorder
                BetterWorkTabMod.DebugLog(
                    "[BWT] Client joined MP session. Awaiting host column order sync.",
                    DebugFeature.DragDrop);
            }
            else
            {
                // Host: initialize normally
                WorkColumnOrderManager.InitializeOnGameLoad();

                // If in MP, broadcast current order to all clients immediately
                if (MP.enabled)
                {
                    var currentOrder = WorkColumnOrderManager.GetCurrentOrder();
                    WorkColumnOrderSync.SyncEntireColumnOrder(currentOrder);
                    BetterWorkTabMod.DebugLog(
                        "[BWT] Host syncing column order to clients.",
                        DebugFeature.DragDrop);
                }
            }

            DisplayElementPool.Clear();
            EnsureCurrentWorklist();
            ColumnBaselineManager.EnsureBaseline(this);

            // Profiling is on during dev; gate or disable for release builds.
            SpineTiming.Enabled = true;
        }

        public override void ExposeData()
        {
            // Keep a valid worklist reference for compatibility
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                EnsureCurrentWorklist();
            }

            string currentWorklistName = "";

            if (Scribe.mode == LoadSaveMode.Saving && CurrentWorklist != null)
            {
                currentWorklistName = CurrentWorklist.RenamableLabel;
            }

            Scribe_Values.Look(ref currentWorklistName, "currentWorklistName");
            Scribe_Collections.Look(ref SavedWorklists, "SavedWorklists", LookMode.Deep, new object[0]);
            Scribe_Collections.Look(ref ColumnBaselineOrder, "columnBaselineOrder", LookMode.Value);
            Scribe_Collections.Look(ref ColumnCurrentOrder, "columnCurrentOrder", LookMode.Value);  // ← ADD THIS LINE
            Scribe_Collections.Look(ref ActiveDividers, "ActiveDividers", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (ColumnBaselineOrder == null)
                {
                    ColumnBaselineOrder = new List<string>();
                }

                if (ColumnCurrentOrder == null)
                {
                    ColumnCurrentOrder = new List<string>();
                }

                if (ColumnBaselineOrder.Count == 0)
                {
                    ColumnBaselineOrder = ColumnBaselineManager.CaptureCurrentOrder();
                }

                if (ColumnCurrentOrder.Count == 0)
                {
                    ColumnCurrentOrder = new List<string>(ColumnBaselineOrder);
                }

                // Defensive: ensure every worklist has a dividers list after load
                if (SavedWorklists != null)
                {
                    for (int i = 0; i < SavedWorklists.Count; i++)
                    {
                        SavedWorklists[i].EnsureCollections();
                    }
                }

                if (!string.IsNullOrEmpty(currentWorklistName))
                {
                    CurrentWorklist = SavedWorklists.FirstOrDefault(
                        w => w.RenamableLabel == currentWorklistName);
                }

                // Fallback: if we couldn't resolve by name but have saved worklists, pick the first one.
                if (CurrentWorklist == null && SavedWorklists.Count > 0)
                {
                    CurrentWorklist = SavedWorklists[0];
                }

                EnsureCurrentWorklist();

                // Migration: first-load fallback to old per-worklist dividers
                if ((ActiveDividers == null || ActiveDividers.Count == 0) && CurrentWorklist != null && CurrentWorklist.Dividers != null)
                {
                    ActiveDividers = new List<PawnDivider>(CurrentWorklist.Dividers.Select(d => d?.Copy()).Where(d => d != null));
                }
                if (ActiveDividers == null)
                {
                    ActiveDividers = new List<PawnDivider>();
                }
            }
        }

        public override void GameComponentUpdate()
        {
            SpineTiming.OnFrameStart();
            Patch_WorkPriority_DoCell_Unified.TrimCacheIfNeeded();
            base.GameComponentUpdate();
        }

        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();

            // Handle 1 / Shift+1 for reporting / clearing
            SpineTiming.HandleInput();
        }

        private void EnsureCurrentWorklist()
        {
            if (CurrentWorklist == null)
            {
                int i = 1;
                while (SavedWorklists.Any(w => w.RenamableLabel == $"Workload {i}"))
                {
                    i++;
                }
                CurrentWorklist = new Worklist { RenamableLabel = $"Workload {i}" };
                BetterWorkTabMod.DebugLog($"[BetterWorkTab] Created default worklist: {CurrentWorklist.RenamableLabel}", DebugFeature.Workloads);
            }

            if (!SavedWorklists.Contains(CurrentWorklist))
            {
                SavedWorklists.Add(CurrentWorklist);
            }

            if (ActiveDividers == null)
            {
                ActiveDividers = new List<PawnDivider>();
            }
        }

        public void SelectWorklist(Worklist worklist)
        {
            if (worklist == null)
            {
                return;
            }

            if (!SavedWorklists.Contains(worklist))
            {
                SavedWorklists.Add(worklist);
            }

            CurrentWorklist = worklist;
        }

        public void ApplyWorklist(Worklist worklist)
        {
            worklist?.Apply();
        }

        public void CreateWorklist(string label)
        {
            string finalLabel = label;
            if (string.IsNullOrEmpty(finalLabel))
            {
                int i = 1;
                while (SavedWorklists.Any(w => w.RenamableLabel == $"Workload {i}"))
                {
                    i++;
                }
                finalLabel = $"Workload {i}";
            }

            var newList = new Worklist(finalLabel);
            SavedWorklists.Add(newList);
            CurrentWorklist = newList;
        }

        public void DeleteWorklist(Worklist worklist)
        {
            if (worklist == null)
                return;

            SavedWorklists.Remove(worklist);
            if (CurrentWorklist == worklist)
            {
                CurrentWorklist = SavedWorklists.FirstOrDefault();
                EnsureCurrentWorklist();
            }
        }

        public void RenameWorklist(Worklist worklist, string newLabel)
        {
            if (worklist == null || string.IsNullOrEmpty(newLabel))
                return;

            worklist.Rename(newLabel);
        }
    }
}
