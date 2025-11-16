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
            Scribe_Deep.Look(ref CurrentWorklist, "CurrentWorklist");
            Scribe_Collections.Look(ref SavedWorklists, "SavedWorklists", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
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
