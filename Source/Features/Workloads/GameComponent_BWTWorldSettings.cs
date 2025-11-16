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
            // Ensure CurrentWorklist is initialized if it's null after loading
            if (CurrentWorklist == null)
            {
                CurrentWorklist = new Worklist { RenamableLabel = "Default Worklist" };
                SavedWorklists.Add(CurrentWorklist);
                Log.Message("[BetterWorkTab] Created default worklist as CurrentWorklist was null.");
            }
        }

        // This stores saved workloads for easy switching between different work setups
        public List<Worklist> SavedWorklists = new List<Worklist>();
        public Worklist CurrentWorklist = null;

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref CurrentWorklist, "CurrentWorklist");
            Scribe_Collections.Look(ref SavedWorklists, "SavedWorklists", LookMode.Deep);
        }

    }
}
