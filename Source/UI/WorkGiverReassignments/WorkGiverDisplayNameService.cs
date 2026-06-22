using System;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    public enum WorkGiverHeaderLabelStyle
    {
        Standard,
        VanillaStaggered
    }

    /// <summary>
    /// Chooses compact WorkGiver names for sub-work headers while preserving full labels for tooltips.
    /// </summary>
    internal static class WorkGiverDisplayNameService
    {
        private const int MaxStandardHeaderChars = 18;
        private const int MaxVanillaHeaderChars = 14;

        public static string HeaderLabel(WorkGiverDef def, WorkGiverHeaderLabelStyle style = WorkGiverHeaderLabelStyle.Standard)
        {
            if (def == null)
            {
                return "Work";
            }

            int maxChars = MaxCharsFor(style);

            string specific = SpecificHeaderLabel(def.defName, style);
            if (!specific.NullOrEmpty())
            {
                return specific;
            }

            string label = CleanLabel(def.label);
            string compact = CompactCommonPhrase(label, style);
            if (!compact.NullOrEmpty() && compact.Length <= maxChars)
            {
                return compact.CapitalizeFirst();
            }

            if (!label.NullOrEmpty() && label.Length <= maxChars)
            {
                return label.CapitalizeFirst();
            }

            string verbWithObject = BuildVerbWithObject(def, label, style);
            if (!verbWithObject.NullOrEmpty())
            {
                return verbWithObject.CapitalizeFirst();
            }

            string verb = CleanLabel(def.verb);
            if (!verb.NullOrEmpty())
            {
                return verb.CapitalizeFirst();
            }

            return def.defName;
        }

        public static string FullLabel(WorkGiverDef def)
        {
            if (def == null)
            {
                return "Work";
            }

            string label = CleanLabel(def.label);
            return label.NullOrEmpty() ? def.defName : label.CapitalizeFirst();
        }

        private static int MaxCharsFor(WorkGiverHeaderLabelStyle style)
        {
            return style == WorkGiverHeaderLabelStyle.VanillaStaggered
                ? MaxVanillaHeaderChars
                : MaxStandardHeaderChars;
        }

        private static string SpecificHeaderLabel(string defName, WorkGiverHeaderLabelStyle style)
        {
            switch (defName)
            {
                case "FixBrokenDownBuilding":
                    return "Fix breakdowns";
                case "PatientGoToBedEmergencyTreatment":
                    return style == WorkGiverHeaderLabelStyle.VanillaStaggered ? "Urgent rest" : "Urgent treatment";
                case "PatientGoToBedTreatment":
                    return "Treatment rest";
                case "PatientGoToBedRecuperate":
                    return "Bed rest";
                case "DoctorTendEmergency":
                    return "Urgent tend";
                case "DoctorTendToSelfEmergency":
                    return "Urgent self-tend";
                case "DoctorTendToSelf":
                    return "Self-tend";
                case "ConstructFinishFrames":
                    return "Build frames";
                case "ConstructDeliverResourcesToFrames":
                    return "Deliver frames";
                case "ConstructDeliverResourcesToBlueprints":
                case "DeliverResourcesToBlueprints":
                    return style == WorkGiverHeaderLabelStyle.VanillaStaggered ? "Deliver plans" : "Deliver blueprints";
                case "DeconstructForBlueprint":
                    return style == WorkGiverHeaderLabelStyle.VanillaStaggered ? "Clear for plan" : "Decon. blueprints";
                case "Deconstruct":
                    return "Deconstruct";
                case "Repair":
                    return "Repair";
                case "FillIn":
                    return "Fill in";
                default:
                    return null;
            }
        }

        private static string CompactCommonPhrase(string label, WorkGiverHeaderLabelStyle style)
        {
            if (label.NullOrEmpty())
            {
                return label;
            }

            string compact = label;
            compact = ReplaceExact(compact, "fix broken-down buildings", "fix breakdowns");
            compact = ReplaceExact(compact, "repair broken-down buildings", "fix breakdowns");
            compact = ReplacePrefix(compact, "construct placed ", "build ");
            compact = ReplacePrefix(compact, "deliver resources to ", "deliver ");
            compact = ReplacePrefix(compact, "deconstruct structures for ", "decon. ");
            compact = ReplaceExact(compact, "deconstruct structures", "deconstruct");
            compact = ReplaceExact(compact, "repair damaged things", "repair");
            compact = ReplaceSuffix(compact, " damaged things", string.Empty);
            compact = ReplaceSuffix(compact, " things", string.Empty);
            if (style == WorkGiverHeaderLabelStyle.VanillaStaggered)
            {
                compact = ReplaceSuffix(compact, " blueprints", " plans");
                compact = ReplaceSuffix(compact, " broken-down", " breakdowns");
                compact = ReplacePrefix(compact, "decon. ", "clear ");
            }
            return compact.Trim();
        }

        private static string BuildVerbWithObject(WorkGiverDef def, string label, WorkGiverHeaderLabelStyle style)
        {
            int maxChars = MaxCharsFor(style);
            string verb = CleanLabel(def.verb);
            if (verb.NullOrEmpty() || IsLowSignalVerb(verb))
            {
                return ShortenLabel(label, style);
            }

            string tail = LastMeaningfulWord(label);
            if (tail.NullOrEmpty() || string.Equals(verb, tail, StringComparison.OrdinalIgnoreCase))
            {
                return verb.Length <= maxChars ? verb : ShortenLabel(label, style);
            }

            string combined = verb + " " + tail;
            return combined.Length <= maxChars ? combined : ShortenLabel(label, style);
        }

        private static string ShortenLabel(string label, WorkGiverHeaderLabelStyle style)
        {
            if (label.NullOrEmpty())
            {
                return label;
            }

            int maxChars = MaxCharsFor(style);
            string compact = CompactCommonPhrase(label, style);
            if (compact.Length <= maxChars)
            {
                return compact;
            }

            string[] words = compact.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int length = Math.Min(2, words.Length); length >= 1; length--)
            {
                string candidate = string.Join(" ", words, 0, length);
                if (candidate.Length <= maxChars)
                {
                    return candidate;
                }
            }

            return compact.Substring(0, Math.Min(compact.Length, maxChars - 1)).TrimEnd() + ".";
        }

        private static string LastMeaningfulWord(string label)
        {
            if (label.NullOrEmpty())
            {
                return null;
            }

            string[] words = label.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = words.Length - 1; i >= 0; i--)
            {
                string word = words[i].Trim();
                if (word.Length <= 2 || IsFillerWord(word))
                {
                    continue;
                }

                return word;
            }

            return null;
        }

        private static bool IsLowSignalVerb(string verb)
        {
            return string.Equals(verb, "work on", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(verb, "do", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(verb, "use", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFillerWord(string word)
        {
            return string.Equals(word, "things", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(word, "structures", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(word, "resources", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(word, "placed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(word, "damaged", StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanLabel(string value)
        {
            return value.NullOrEmpty() ? string.Empty : value.Trim();
        }

        private static string ReplacePrefix(string value, string prefix, string replacement)
        {
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? replacement + value.Substring(prefix.Length)
                : value;
        }

        private static string ReplaceSuffix(string value, string suffix, string replacement)
        {
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - suffix.Length) + replacement
                : value;
        }

        private static string ReplaceExact(string value, string match, string replacement)
        {
            return string.Equals(value, match, StringComparison.OrdinalIgnoreCase) ? replacement : value;
        }
    }
}
