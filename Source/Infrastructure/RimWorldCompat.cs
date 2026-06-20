using RimWorld;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

#if v1_0
namespace Verse
{
    public static class PawnWorkCompatExtensions
    {
        public static bool WorkTypeIsDisabled(this Pawn pawn, WorkTypeDef workType)
        {
            return pawn?.story?.WorkTypeIsDisabled(workType) ?? true;
        }

        public static bool WorkTagIsDisabled(this Pawn pawn, WorkTags workTags)
        {
            return pawn?.story?.WorkTagIsDisabled(workTags) ?? false;
        }
    }

    public static class StringCompatExtensions
    {
        public static string Colorize(this string text, Color color)
        {
            string value = text ?? string.Empty;
            return "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">" + value + "</color>";
        }

        public static string StripTags(this string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return Regex.Replace(text, "<.*?>", string.Empty);
        }

        public static string Resolve(this string text)
        {
            return text ?? string.Empty;
        }
    }
}
#endif

namespace Better_Work_Tab
{
    public static class PawnCompat
    {
        public static string NameShortColored(Pawn pawn)
        {
#if v1_0
            return pawn?.LabelShortCap ?? pawn?.LabelShort ?? "Pawn";
#else
            return pawn?.NameShortColored ?? "Pawn";
#endif
        }
    }

    public static class TraitCompat
    {
        public static string LabelCap(TraitDegreeData data)
        {
            if (data == null)
                return null;

#if v1_0
            return data.label?.CapitalizeFirst();
#else
            return data.LabelCap;
#endif
        }
    }

    public static class ModListerCompat
    {
        public static ModMetaData GetActiveModWithIdentifier(string packageId)
        {
#if v1_0
            if (string.IsNullOrEmpty(packageId))
                return null;

            foreach (var mod in ModLister.AllInstalledMods)
            {
                if (mod != null && mod.Active && MatchesIdentifier(mod, packageId))
                    return mod;
            }

            return null;
#else
            return ModLister.GetActiveModWithIdentifier(packageId);
#endif
        }

#if v1_0
        private static bool MatchesIdentifier(ModMetaData mod, string packageId)
        {
            if (string.Equals(mod.Identifier, packageId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(mod.Name, packageId, StringComparison.OrdinalIgnoreCase))
                return true;

            string aboutPath = Path.Combine(Path.Combine(mod.RootDir.FullName, "About"), "About.xml");
            if (!File.Exists(aboutPath))
                return false;

            try
            {
                var root = XDocument.Load(aboutPath).Root;
                string declaredPackageId = root?.Element("packageId")?.Value;
                return string.Equals(declaredPackageId, packageId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
#endif
    }

    public static class JobCompat
    {
        public static void SetWorkGiverDef(Job job, WorkGiverDef workGiver)
        {
#if !v1_0
            if (job != null)
                job.workGiverDef = workGiver;
#endif
        }
    }

    public static class ArrayCompat
    {
        public static void Fill<T>(T[] array, T value)
        {
            if (array == null)
                return;

            for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
        }
    }
}
