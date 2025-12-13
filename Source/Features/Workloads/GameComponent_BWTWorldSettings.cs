using Better_Work_Tab.Features;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
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
        public List<string> ColumnBaselineOrder = new List<string>();

        public GameComponent_BWTWorldSettings(Game game) : base()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            DisplayElementPool.Clear();
            EnsureCurrentWorklist();
            ColumnBaselineManager.EnsureBaseline(this);

            // Enable profiling while you are testing.
            // Turn this off or gate it behind a dev flag for release.
            SpineTiming.Enabled = true;
        }

        public override void ExposeData()
        {
            string currentWorklistName = "";

            if (Scribe.mode == LoadSaveMode.Saving && CurrentWorklist != null)
            {
                currentWorklistName = CurrentWorklist.RenamableLabel;
            }

            Scribe_Values.Look(ref currentWorklistName, "currentWorklistName");
            Scribe_Collections.Look(ref SavedWorklists, "SavedWorklists", LookMode.Deep, new object[0]);
            Scribe_Collections.Look(ref ColumnBaselineOrder, "columnBaselineOrder", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (ColumnBaselineOrder == null)
                {
                    ColumnBaselineOrder = new List<string>();
                }

                if (ColumnBaselineOrder.Count == 0)
                {
                    ColumnBaselineOrder = ColumnBaselineManager.CaptureCurrentOrder();
                    BetterWorkTabMod.DebugLog($"[BWT] Migration captured baseline order on load: {string.Join(", ", ColumnBaselineOrder)}", DebugFeature.DragDrop);
                }

                if (!string.IsNullOrEmpty(currentWorklistName))
                {
                    CurrentWorklist = SavedWorklists.FirstOrDefault(
                        w => w.RenamableLabel == currentWorklistName);
                }

                EnsureCurrentWorklist();
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
                CurrentWorklist = new Worklist { RenamableLabel = "Default Worklist" };
                BetterWorkTabMod.DebugLog("[BetterWorkTab] Created default worklist during initialization.", DebugFeature.Workloads);
            }

            if (!SavedWorklists.Contains(CurrentWorklist))
            {
                SavedWorklists.Add(CurrentWorklist);
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
            var newList = new Worklist(string.IsNullOrEmpty(label) ? "New Worklist" : label);
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
