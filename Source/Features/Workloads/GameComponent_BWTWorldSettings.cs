using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    internal class GameComponent_BWTWorldSettings : GameComponent
    {
        public GameComponent_BWTWorldSettings(Game game)
        {
        }
        // This stores saved workloads for easy switching between different work setups
        public List<Worklist> SavedWorklists = new List<Worklist>();
        public Worklist CurrentWorklist = null;

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
                // On load, find the worklist by its saved name.
                if (!string.IsNullOrEmpty(currentWorklistName))
                {
                    CurrentWorklist = SavedWorklists.FirstOrDefault(w => w.RenamableLabel == currentWorklistName);
                }

                // The EnsureCurrentWorklist method will act as a fallback if the named
                // worklist wasn't found or if none was active.
                EnsureCurrentWorklist();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureCurrentWorklist();
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
