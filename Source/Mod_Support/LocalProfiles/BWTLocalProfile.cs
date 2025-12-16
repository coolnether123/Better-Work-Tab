using System.Collections.Generic;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer.Data;

namespace Better_Work_Tab.Mod_Support.LocalProfiles
{
    internal sealed class BWTLocalProfile : IExposable
    {
        public string SaveKey;
        public string PlayerKey;

        public List<Worklist> Worklists = new();
        public string SelectedWorklistName;

        public List<PawnDivider> ActiveDividers = new();

        // thingIDNumber -> display order (local only in MP)
        public Dictionary<int, int> PawnRowOrder = new();

        // pawn label (or a stable key you already use) -> colors
        public Dictionary<string, Color> PawnBackgroundColors = new();
        public Dictionary<string, Color> PawnTextColors = new();

        // privacy toggles (local-only)
        public bool AllowLayoutRequests = true;
        public bool AllowPresenceBroadcast = false;

        public void ExposeData()
        {
            Scribe_Values.Look(ref SaveKey, nameof(SaveKey));
            Scribe_Values.Look(ref PlayerKey, nameof(PlayerKey));

            Scribe_Collections.Look(ref Worklists, nameof(Worklists), LookMode.Deep);
            Scribe_Values.Look(ref SelectedWorklistName, nameof(SelectedWorklistName));

            Scribe_Collections.Look(ref ActiveDividers, nameof(ActiveDividers), LookMode.Deep);

            Scribe_Collections.Look(ref PawnRowOrder, nameof(PawnRowOrder), LookMode.Value, LookMode.Value);

            Scribe_Collections.Look(ref PawnBackgroundColors, nameof(PawnBackgroundColors), LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref PawnTextColors, nameof(PawnTextColors), LookMode.Value, LookMode.Value);

            Scribe_Values.Look(ref AllowLayoutRequests, nameof(AllowLayoutRequests), true);
            Scribe_Values.Look(ref AllowPresenceBroadcast, nameof(AllowPresenceBroadcast), false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Worklists ??= new List<Worklist>();
                ActiveDividers ??= new List<PawnDivider>();
                PawnRowOrder ??= new Dictionary<int, int>();
                PawnBackgroundColors ??= new Dictionary<string, Color>();
                PawnTextColors ??= new Dictionary<string, Color>();
            }
        }
    }
}
