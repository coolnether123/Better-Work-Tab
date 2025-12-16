using System.Collections.Generic;
using System.Linq;
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

            // Serialize colors as hex strings for reliable save/load
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var bgColorStrings = PawnBackgroundColors?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ColorUtility.ToHtmlStringRGBA(kvp.Value)) ?? new Dictionary<string, string>();
                var textColorStrings = PawnTextColors?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ColorUtility.ToHtmlStringRGBA(kvp.Value)) ?? new Dictionary<string, string>();

                Scribe_Collections.Look(ref bgColorStrings, "PawnBackgroundColors", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref textColorStrings, "PawnTextColors", LookMode.Value, LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Dictionary<string, string> bgColorStrings = null;
                Dictionary<string, string> textColorStrings = null;

                Scribe_Collections.Look(ref bgColorStrings, "PawnBackgroundColors", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref textColorStrings, "PawnTextColors", LookMode.Value, LookMode.Value);

                PawnBackgroundColors = bgColorStrings?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ParseColor(kvp.Value)) ?? new Dictionary<string, Color>();
                PawnTextColors = textColorStrings?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ParseColor(kvp.Value)) ?? new Dictionary<string, Color>();
            }

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

        private static Color ParseColor(string hexString)
        {
            if (ColorUtility.TryParseHtmlString("#" + hexString, out Color color))
                return color;
            return Color.white; // fallback
        }
    }
}
