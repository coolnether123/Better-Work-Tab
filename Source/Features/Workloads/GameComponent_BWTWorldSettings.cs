using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Spine.Profiling;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    internal class GameComponent_BWTWorldSettings : GameComponent
    {
        public List<Worklist> SavedWorklists = new List<Worklist>();
        public Worklist CurrentWorklist = null;

        public GameComponent_BWTWorldSettings(Game game) : base()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            DisplayElementPool.Clear();
            EnsureCurrentWorklist();

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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
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
                Log.Message("[BetterWorkTab] Created default worklist during initialization.");
            }

            if (!SavedWorklists.Contains(CurrentWorklist))
            {
                SavedWorklists.Add(CurrentWorklist);
            }
        }
    }
}
