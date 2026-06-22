using System;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Chooses compact WorkGiver names for sub-work headers while preserving full labels for tooltips.
    /// </summary>
    internal static class WorkGiverDisplayNameService
    {
        private const int MaxHeaderChars = 18;

        public static string HeaderLabel(WorkGiverDef def)
        {
            if (def == null)
            {
                return "Work";
            }

            string specific = SpecificHeaderLabel(def.defName);
            if (!specific.NullOrEmpty())
            {
                return specific;
            }

            string label = CleanLabel(def.label);
            string compact = CompactCommonPhrase(label);
            if (!compact.NullOrEmpty() && compact.Length <= MaxHeaderChars)
            {
                return compact.CapitalizeFirst();
            }

            if (!label.NullOrEmpty() && label.Length <= MaxHeaderChars)
            {
                return label.CapitalizeFirst();
            }

            string verbWithObject = BuildVerbWithObject(def, label);
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

        private static string SpecificHeaderLabel(string defName)
        {
            switch (defName)
            {
                case "PatientGoToBedEmergencyTreatment":
                    return "Urgent treatment";
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
                    return "Deliver blueprints";
                case "DeconstructForBlueprint":
                    return "Decon. blueprints";
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

        private static string CompactCommonPhrase(string label)
        {
            if (label.NullOrEmpty())
            {
                return label;
            }

            string compact = label;
            compact = ReplacePrefix(compact, "construct placed ", "build ");
            compact = ReplacePrefix(compact, "deliver resources to ", "deliver ");
            compact = ReplacePrefix(compact, "deconstruct structures for ", "decon. ");
            compact = ReplaceExact(compact, "deconstruct structures", "deconstruct");
            compact = ReplaceExact(compact, "repair damaged things", "repair");
            compact = ReplaceSuffix(compact, " damaged things", string.Empty);
            compact = ReplaceSuffix(compact, " things", string.Empty);
            return compact.Trim();
        }

        private static string BuildVerbWithObject(WorkGiverDef def, string label)
        {
            string verb = CleanLabel(def.verb);
            if (verb.NullOrEmpty() || IsLowSignalVerb(verb))
            {
                return ShortenLabel(label);
            }

            string tail = LastMeaningfulWord(label);
            if (tail.NullOrEmpty() || string.Equals(verb, tail, StringComparison.OrdinalIgnoreCase))
            {
                return verb.Length <= MaxHeaderChars ? verb : ShortenLabel(label);
            }

            string combined = verb + " " + tail;
            return combined.Length <= MaxHeaderChars ? combined : ShortenLabel(label);
        }

        private static string ShortenLabel(string label)
        {
            if (label.NullOrEmpty())
            {
                return label;
            }

            string compact = CompactCommonPhrase(label);
            if (compact.Length <= MaxHeaderChars)
            {
                return compact;
            }

            string[] words = compact.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int length = Math.Min(2, words.Length); length >= 1; length--)
            {
                string candidate = string.Join(" ", words, 0, length);
                if (candidate.Length <= MaxHeaderChars)
                {
                    return candidate;
                }
            }

            return compact.Substring(0, Math.Min(compact.Length, MaxHeaderChars - 1)).TrimEnd() + ".";
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
