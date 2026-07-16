using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

#if (v1_0 || v0_19)
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
#if (v0_18 || v0_17 || v0_16)
            return pawn?.LabelShort ?? "Pawn";
#elif (v1_0 || v0_19)
            return pawn?.LabelShortCap ?? pawn?.LabelShort ?? "Pawn";
#else
            return pawn?.NameShortColored ?? "Pawn";
#endif
        }

        public static string LabelShortCap(Pawn pawn)
        {
#if (v0_18 || v0_17 || v0_16)
            return pawn?.LabelShort?.CapitalizeFirst() ?? "Pawn";
#else
            return pawn?.LabelShortCap ?? pawn?.LabelShort ?? "Pawn";
#endif
        }
    }

    public static class MapCompat
    {
        public static Map CurrentMap
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                return Find.VisibleMap ?? Find.Maps?.FirstOrDefault();
#else
                return Find.CurrentMap;
#endif
            }
        }
    }

    public static class PawnsFinderCompat
    {
        public static IEnumerable<Pawn> AllAliveOrDead
        {
            get
            {
#if (v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4)
                return PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead;
#else
                return PawnsFinder.All_AliveOrDead;
#endif
            }
        }
    }

    public static class MessageCompat
    {
        public static void Message(string text, MessageTypeDef type, bool historical = false)
        {
#if (v0_18 || v0_17 || v0_16)
            Messages.Message(text, type);
#else
            Messages.Message(text, type, historical);
#endif
        }

        public static void Message(string text, GlobalTargetInfo target, MessageTypeDef type, bool historical = false)
        {
#if (v0_18 || v0_17 || v0_16)
            Messages.Message(text, target, type);
#else
            Messages.Message(text, target, type, historical);
#endif
        }
    }

    public static class UISoundCompat
    {
        public static SoundDef TickHigh
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                return SoundDefOf.TickHigh;
#else
                return UISoundCompat.TickHigh;
#endif
            }
        }

        public static SoundDef TickLow
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                return SoundDefOf.TickLow;
#else
                return UISoundCompat.TickLow;
#endif
            }
        }

        public static SoundDef TickTiny
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                return SoundDefOf.TickTiny;
#else
                return UISoundCompat.TickTiny;
#endif
            }
        }

        public static SoundDef CheckboxTurnedOn
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                return SoundDefOf.CheckboxTurnedOn;
#else
                return UISoundCompat.CheckboxTurnedOn;
#endif
            }
        }

        public static SoundDef CheckboxTurnedOff
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                return SoundDefOf.CheckboxTurnedOff;
#else
                return UISoundCompat.CheckboxTurnedOff;
#endif
            }
        }
    }

    public static class UICompat
    {
        public static Vector2 MousePosUIInvertedUseEventIfCan
        {
            get
            {
#if (v0_18 || v0_17 || v0_16)
                Event evt = Event.current;
                return evt != null ? evt.mousePosition : Vector2.zero;
#else
                return Verse.UI.MousePosUIInvertedUseEventIfCan;
#endif
            }
        }
    }

    public static class WorkGiverCompat
    {
        public static bool CanBeDoneWhileDrafted(WorkGiverDef workGiver)
        {
#if (v0_18 || v0_17 || v0_16)
            return false;
#else
            return workGiver?.canBeDoneWhileDrafted ?? false;
#endif
        }

        public static bool ShouldSkip(WorkGiver_Scanner scanner, Pawn pawn, bool forced)
        {
#if (v0_18 || v0_17 || v0_16)
            return scanner.ShouldSkip(pawn);
#else
            return scanner.ShouldSkip(pawn, forced);
#endif
        }

        public static bool HasJobOnCell(WorkGiver_Scanner scanner, Pawn pawn, IntVec3 cell, bool forced)
        {
#if (v0_18 || v0_17 || v0_16)
            return scanner.HasJobOnCell(pawn, cell);
#else
            return scanner.HasJobOnCell(pawn, cell, forced);
#endif
        }

        public static Job JobOnCell(WorkGiver_Scanner scanner, Pawn pawn, IntVec3 cell, bool forced)
        {
#if (v0_18 || v0_17 || v0_16)
            return scanner.JobOnCell(pawn, cell);
#else
            return scanner.JobOnCell(pawn, cell, forced);
#endif
        }

        public static void TryPlaceForceFeedback(WorkGiverDef workGiver, IntVec3 clickedCell, Map map)
        {
#if !(v0_18 || v0_17 || v0_16)
            if (workGiver?.forceMote != null)
            {
                MoteMaker.MakeStaticMote(clickedCell, map, workGiver.forceMote);
            }
#endif
        }
    }

    public static class WorkSettingsCompat
    {
        public static void EnsureInitialized(Pawn_WorkSettings workSettings)
        {
            if (workSettings == null)
                return;

#if (v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4)
            if (!workSettings.EverWork)
                workSettings.EnableAndInitialize();
#else
            workSettings.EnableAndInitializeIfNotAlreadyInitialized();
#endif
        }
    }

    public static class ListCompat
    {
        public static void SortStableCompat<T>(this List<T> list, Comparison<T> comparison)
        {
#if (v0_18 || v0_17 || v0_16)
            if (list == null || comparison == null)
                return;

            var indexed = list.Select((item, index) => new { item, index }).ToList();
            indexed.Sort((left, right) =>
            {
                int result = comparison(left.item, right.item);
                return result != 0 ? result : left.index.CompareTo(right.index);
            });

            for (int i = 0; i < indexed.Count; i++)
            {
                list[i] = indexed[i].item;
            }
#else
            list.SortStableCompat(comparison);
#endif
        }
    }

    public static class WidgetsCompat
    {
        public static void DrawLightHighlight(Rect rect)
        {
#if (v0_18 || v0_17 || v0_16)
            Widgets.DrawHighlight(rect);
#else
            Widgets.DrawLightHighlight(rect);
#endif
        }

        public static void Checkbox(float x, float y, ref bool checkOn, bool disabled = false, bool paintable = true)
        {
#if (v0_18 || v0_17 || v0_16)
            Widgets.Checkbox(x, y, ref checkOn, 24f, disabled);
#else
            Widgets.Checkbox(x, y, ref checkOn, disabled: disabled, paintable: paintable);
#endif
        }
    }

    public static class ScribeCompat
    {
        public static void LookStringDictionary<T>(
            ref Dictionary<string, T> dictionary,
            string label,
            LookMode valueLookMode = LookMode.Value)
        {
#if (v0_18 || v0_17 || v0_16)
            List<string> keys = null;
            List<T> values = null;

            if (Scribe.mode == LoadSaveMode.Saving && dictionary != null)
            {
                keys = dictionary.Keys.ToList();
                values = new List<T>();
                foreach (string key in keys)
                {
                    values.Add(dictionary[key]);
                }
            }

            if (Scribe.EnterNode(label))
            {
                try
                {
                    Scribe_Collections.Look(ref keys, "keys", LookMode.Value);
                    Scribe_Collections.Look(ref values, "values", valueLookMode);

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        dictionary = new Dictionary<string, T>();
                        if (keys != null && values != null)
                        {
                            int count = Math.Min(keys.Count, values.Count);
                            for (int i = 0; i < count; i++)
                            {
                                if (keys[i] != null)
                                    dictionary[keys[i]] = values[i];
                            }
                        }
                    }
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                dictionary = new Dictionary<string, T>();
            }
#else
            Scribe_Collections.Look(ref dictionary, label, LookMode.Value, valueLookMode);
#endif
        }
    }

    public static class TraitCompat
    {
        public static string LabelCap(TraitDegreeData data)
        {
            if (data == null)
                return null;

#if (v1_0 || v0_19)
            return data.label?.CapitalizeFirst();
#else
            return data.LabelCap;
#endif
        }
    }

    public static class ModListerCompat
    {
        public static string GetPackageId(ModContentPack mod)
        {
#if (v1_0 || v0_19)
            if (mod == null)
                return null;

            var property = mod.GetType().GetProperty("PackageId");
            return property == null ? mod.Name : property.GetValue(mod, null) as string ?? mod.Name;
#else
            return mod?.PackageId;
#endif
        }

        public static ModMetaData GetActiveModWithIdentifier(string packageId)
        {
#if (v1_0 || v0_19)
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

#if (v1_0 || v0_19)
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
#if !(v1_0 || v0_19)
            if (job != null)
                job.workGiverDef = workGiver;
#endif
        }
    }

    public static class ArrayCompat
    {
        public static T[] Empty<T>()
        {
            return new T[0];
        }

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

#if (v0_18 || v0_17 || v0_16)
namespace RimWorld
{
    public static class MainTabWindowUtility
    {
        public static void NotifyAllPawnTables_PawnsChanged()
        {
            if (Find.WindowStack == null)
                return;

            WindowStack windowStack = Find.WindowStack;
            for (int i = 0; i < windowStack.Count; i++)
            {
                if (windowStack[i] is MainTabWindow_PawnTable table)
                {
                    table.Notify_PawnsChanged();
                }
            }
        }
    }
}
#endif
