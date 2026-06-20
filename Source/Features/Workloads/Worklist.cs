using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Better_Work_Tab.PawnOrganizer.Data;

namespace Better_Work_Tab.Features.Workloads
{
    public class Worklist : IExposable
    {
        public Worklist(string name)
        {
            RenamableLabel = name;
            foreach (var pawn in MapCompat.CurrentMap.mapPawns.FreeColonists)
            {
                PawnWorklists.Add(new PawnWorkload(pawn));
            }
            UseAdvancedMode = Current.Game.playSettings.useWorkPriorities;
        }

        public Worklist()
        {
        }

        public bool UseAdvancedMode = true;

        public List<PawnWorkload> PawnWorklists = new List<PawnWorkload>();
        public string worklistName = "New Worklist";

        public List<PawnDivider> Dividers = new List<PawnDivider>();

        public string RenamableLabel { get => worklistName; set => worklistName = value; }

        public string BaseLabel { get; }

        public string InspectLabel { get; }

        public void Apply()
        {
            Current.Game.playSettings.useWorkPriorities = UseAdvancedMode;
            foreach (var pw in PawnWorklists)
            {
                pw.Apply();
            }
        }

        public void ExposeData()
        {
            RenamableLabel = worklistName;
            Scribe_Values.Look(ref worklistName, "worklistName", "New Worklist");
            Scribe_Values.Look(ref UseAdvancedMode, "UseAdvancedMode", true);
            worklistName = RenamableLabel;
            Scribe_Collections.Look(ref PawnWorklists, "pawnWorklists", LookMode.Deep);
            Scribe_Collections.Look(ref Dividers, "Dividers", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
            }
        }

        public void Rename(string newLabel)
        {
            if (!string.IsNullOrEmpty(newLabel))
            {
                RenamableLabel = newLabel;
            }
        }

        /// <summary>
        /// Guarantees that collection fields are non-null after load/migration.
        /// </summary>
        public void EnsureCollections()
        {
            if (PawnWorklists == null)
            {
                PawnWorklists = new List<PawnWorkload>();
            }

            if (Dividers == null)
            {
                Dividers = new List<PawnDivider>();
            }
        }
    }
}
