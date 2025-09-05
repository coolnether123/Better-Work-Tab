using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    internal class GameComponent_WorkloadSaver : GameComponent
    {
        public GameComponent_WorkloadSaver(Game game)
        {
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
