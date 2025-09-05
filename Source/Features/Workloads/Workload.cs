using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    public class Workload : IRenameable, IExposable
    {
        public Workload(string name)
        {
            Name = name;
            Copy();
        }

        private string Name;

        public Dictionary<Pawn, DefMap<WorkTypeDef, int>> PawnWorklists;
        public string RenamableLabel { get => Name; set => Name=value; }

        public string BaseLabel => "";

        public string InspectLabel => "";


        public void Apply()
        {
            Paste();
        }


        void Copy()
        {
            Log.Message("Copying workload " + Name);

            if (PawnWorklists == null)
            {
                PawnWorklists = new Dictionary<Pawn, DefMap<WorkTypeDef, int>>();
            }

            List<WorkTypeDef> allDefsListForReading = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                PawnWorklists.Add(pawn, new DefMap<WorkTypeDef, int>());
                for (int i = 0; i < allDefsListForReading.Count; i++)
                {
                    WorkTypeDef w = allDefsListForReading[i];
                    PawnWorklists[pawn][w] = ((!pawn.WorkTypeIsDisabled(w)) ? pawn.workSettings.GetPriority(w) : 0);
                }
            }
        }
        void Paste()
        {
            Log.Message("Pasting workload " + Name);
            List<WorkTypeDef> allDefsListForReading = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            foreach (var p in PawnWorklists.Keys)
            {

                for (int i = 0; i < allDefsListForReading.Count; i++)
                {
                    WorkTypeDef w = allDefsListForReading[i];
                    if (!p.WorkTypeIsDisabled(w))
                    {
                        p.workSettings.SetPriority(w, PawnWorklists[p][w]);
                        Log.Message("Set " + p.Name + " " + w.defName + " to " + PawnWorklists[p][w]);
                    }
                }
            }
        }

        public void ExposeData() { }
        //{
        //    Scribe_Values.Look(ref Name, "workloadName");
        //    Scribe_Collections.Look(ref PawnWorklists, "worklistForPawn");
        //}
    }
}
