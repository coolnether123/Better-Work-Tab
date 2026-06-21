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

#if v0_13
namespace UnityEngine
{
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color)
        {
            return Byte(color.r).ToString("X2") +
                   Byte(color.g).ToString("X2") +
                   Byte(color.b).ToString("X2");
        }

        public static string ToHtmlStringRGBA(Color color)
        {
            return ToHtmlStringRGB(color) + Byte(color.a).ToString("X2");
        }

        public static bool TryParseHtmlString(string htmlString, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(htmlString))
                return false;

            string value = htmlString[0] == '#' ? htmlString.Substring(1) : htmlString;
            if (value.Length != 6 && value.Length != 8)
                return false;

            try
            {
                byte r = Convert.ToByte(value.Substring(0, 2), 16);
                byte g = Convert.ToByte(value.Substring(2, 2), 16);
                byte b = Convert.ToByte(value.Substring(4, 2), 16);
                byte a = value.Length == 8 ? Convert.ToByte(value.Substring(6, 2), 16) : (byte)255;
                color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte Byte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }
    }
}
#endif

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
    public static class ColorCompat
    {
        public static Color HSVToRGB(float h, float s, float v, bool hdr = false)
        {
#if v0_13
            if (s <= 0f)
                return new Color(v, v, v, 1f);

            h = Mathf.Repeat(h, 1f) * 6f;
            int sector = Mathf.FloorToInt(h);
            float fraction = h - sector;
            float p = v * (1f - s);
            float q = v * (1f - s * fraction);
            float t = v * (1f - s * (1f - fraction));

            switch (sector)
            {
                case 0: return new Color(v, t, p, 1f);
                case 1: return new Color(q, v, p, 1f);
                case 2: return new Color(p, v, t, 1f);
                case 3: return new Color(p, q, v, 1f);
                case 4: return new Color(t, p, v, 1f);
                default: return new Color(v, p, q, 1f);
            }
#else
            return Color.HSVToRGB(h, s, v, hdr);
#endif
        }

        public static void RGBToHSV(Color rgb, out float h, out float s, out float v)
        {
#if v0_13
            float min = Mathf.Min(rgb.r, Mathf.Min(rgb.g, rgb.b));
            float max = Mathf.Max(rgb.r, Mathf.Max(rgb.g, rgb.b));
            v = max;

            float delta = max - min;
            if (max <= 0f || delta <= 0f)
            {
                s = 0f;
                h = 0f;
                return;
            }

            s = delta / max;
            if (Mathf.Approximately(rgb.r, max))
                h = (rgb.g - rgb.b) / delta;
            else if (Mathf.Approximately(rgb.g, max))
                h = 2f + (rgb.b - rgb.r) / delta;
            else
                h = 4f + (rgb.r - rgb.g) / delta;

            h /= 6f;
            if (h < 0f)
                h += 1f;
#else
            Color.RGBToHSV(rgb, out h, out s, out v);
#endif
        }
    }

    public static class FactionCompat
    {
        public static Faction OfPlayer
        {
            get
            {
#if v0_13
                return Faction.OfColony;
#else
                return Faction.OfPlayer;
#endif
            }
        }
    }

    public static class PawnCompat
    {
        public static string NameShortColored(Pawn pawn)
        {
#if v0_13
            return pawn?.LabelBaseShort ?? pawn?.Label ?? "Pawn";
#elif (v0_18 || v0_17 || v0_16)
            return pawn?.LabelShort ?? "Pawn";
#elif (v1_0 || v0_19)
            return pawn?.LabelShortCap ?? pawn?.LabelShort ?? "Pawn";
#else
            return pawn?.NameShortColored ?? "Pawn";
#endif
        }

        public static string LabelShortCap(Pawn pawn)
        {
#if v0_13
            return pawn?.LabelBaseCap ?? pawn?.LabelCap ?? "Pawn";
#elif (v0_18 || v0_17 || v0_16)
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
#if v0_15
                return Find.Map;
#elif (v0_18 || v0_17 || v0_16)
                return Find.VisibleMap ?? Find.Maps?.FirstOrDefault();
#else
                return Find.CurrentMap;
#endif
            }
        }

        public static Map ThingMap(Thing thing)
        {
#if v0_15
            return thing?.Spawned == true ? Find.Map : null;
#else
            return thing?.Map;
#endif
        }

        public static int MapId(Map map)
        {
#if v0_15
            return map == null ? -1 : 0;
#else
            return map?.uniqueID ?? -1;
#endif
        }
    }

    public static class RectCompat
    {
        public static Rect Zero
        {
            get { return new Rect(0f, 0f, 0f, 0f); }
        }

        public static Rect ExpandedBy(Rect rect, float margin)
        {
#if (v0_17 || v0_16)
            return new Rect(
                rect.x - margin,
                rect.y - margin,
                rect.width + margin * 2f,
                rect.height + margin * 2f);
#else
            return rect.ExpandedBy(margin);
#endif
        }

        public static Rect LeftPart(Rect rect, float pct)
        {
            return new Rect(rect.x, rect.y, rect.width * pct, rect.height);
        }

        public static Rect RightPart(Rect rect, float pct)
        {
            float width = rect.width * pct;
            return new Rect(rect.xMax - width, rect.y, width, rect.height);
        }

        public static Rect LeftHalf(Rect rect)
        {
            return LeftPart(rect, 0.5f);
        }

        public static Rect RightHalf(Rect rect)
        {
            return RightPart(rect, 0.5f);
        }
    }

    public static class EventCompat
    {
        public static bool IsMouseLeaveWindow(EventType eventType)
        {
#if (v0_17 || v0_16)
            return false;
#else
            return eventType == EventType.MouseLeaveWindow;
#endif
        }
    }

    public static class PawnsFinderCompat
    {
        public static IEnumerable<Pawn> AllMapsWorldAndTemporaryAlive
        {
            get
            {
#if v0_15
                return Find.Map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>();
#elif (v0_17 || v0_16)
                return PawnsFinder.AllMapsAndWorld_Alive;
#else
                return PawnsFinder.AllMapsWorldAndTemporary_Alive;
#endif
            }
        }
    }

    public static class ColonistBarCompat
    {
        public static void MarkColonistsDirty()
        {
#if v0_13
            return;
#elif v0_15
            Find.ColonistBar?.MarkColonistsListDirty();
#else
            Find.ColonistBar?.MarkColonistsDirty();
#endif
        }
    }

    public static class SkillCompat
    {
        public static int Level(SkillRecord skill)
        {
#if v0_15
            return skill?.level ?? 0;
#else
            return skill?.Level ?? 0;
#endif
        }
    }

    public static class MathCompat
    {
        public static int PositiveMod(int value, int modulus)
        {
#if v0_15
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
#else
            return GenMath.PositiveMod(value, modulus);
#endif
        }
    }

    public static class MessageCompat
    {
        public static void Message(string text, MessageTypeDef type, bool historical = false)
        {
#if (v0_17 || v0_16)
            Messages.Message(text, type?.LegacySound ?? MessageSound.Standard);
#elif v0_18
            Messages.Message(text, type);
#else
            Messages.Message(text, type, historical);
#endif
        }

#if v0_15
        public static void Message(string text, Thing target, MessageTypeDef type, bool historical = false)
        {
            Messages.Message(text, new TargetInfo(target), type?.LegacySound ?? MessageSound.Standard);
        }
#else
        public static void Message(string text, GlobalTargetInfo target, MessageTypeDef type, bool historical = false)
        {
#if (v0_17 || v0_16)
            Messages.Message(text, target, type?.LegacySound ?? MessageSound.Standard);
#elif v0_18
            Messages.Message(text, target, type);
#else
            Messages.Message(text, target, type, historical);
#endif
        }
#endif
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

        public static SoundDef DragSlider
        {
            get
            {
#if v0_14
                return SoundDefOf.TickTiny;
#elif (v0_18 || v0_17 || v0_16)
                return SoundDefOf.DragSlider;
#else
                return UISoundCompat.DragSlider;
#endif
            }
        }

        public static SoundDef Crunch
        {
            get
            {
#if v0_16
                return TickLow;
#else
                return SoundDefOf.Crunch;
#endif
            }
        }
    }

    public static class PlayerKnowledgeCompat
    {
        public static void DemonstrateWorkTab()
        {
#if !(v0_14 || v0_13)
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
#endif
        }

        public static void DemonstrateManualWorkPriorities()
        {
#if !(v0_14 || v0_13)
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.ManualWorkPriorities, KnowledgeAmount.SmallInteraction);
#endif
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

        public static bool HasJobOnThing(WorkGiver_Scanner scanner, Pawn pawn, Thing thing, bool forced)
        {
#if (v0_18 || v0_17 || v0_16)
            return scanner.HasJobOnThing(pawn, thing);
#else
            return scanner.HasJobOnThing(pawn, thing, forced);
#endif
        }

        public static Job JobOnThing(WorkGiver_Scanner scanner, Pawn pawn, Thing thing, bool forced)
        {
#if (v0_18 || v0_17 || v0_16)
            return scanner.JobOnThing(pawn, thing);
#else
            return scanner.JobOnThing(pawn, thing, forced);
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
        public static void Label(Rect rect, string label)
        {
            Widgets.Label(rect, label);
        }

        public static void DrawBox(Rect rect, int thickness = 1)
        {
            Widgets.DrawBox(rect, thickness);
        }

        public static void DrawBox(Rect rect, int thickness, Texture2D lineTexture)
        {
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13
            DrawBox(rect, thickness);
#else
            Widgets.DrawBox(rect, thickness, lineTexture);
#endif
        }

        public static void DrawHighlight(Rect rect)
        {
            Widgets.DrawHighlight(rect);
        }

        public static void DrawHighlightSelected(Rect rect)
        {
            Widgets.DrawHighlightSelected(rect);
        }

        public static void DrawHighlightIfMouseover(Rect rect)
        {
            Widgets.DrawHighlightIfMouseover(rect);
        }

        public static void DrawMenuSection(Rect rect)
        {
#if v0_13
            Widgets.DrawBox(rect, 1);
#else
            Widgets.DrawMenuSection(rect);
#endif
        }

        public static void BeginScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
        {
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
        }

        public static void EndScrollView()
        {
            Widgets.EndScrollView();
        }

        public static string TextField(Rect rect, string text)
        {
            return Widgets.TextField(rect, text);
        }

        public static string TextField(Rect rect, string text, int maxLength)
        {
            string input = Widgets.TextField(rect, text);
            return input != null && input.Length > maxLength ? text : input;
        }

        public static void TextFieldNumeric(Rect rect, ref int value, ref string buffer, int min = 0, int max = int.MaxValue)
        {
            buffer = Widgets.TextField(rect, buffer ?? value.ToString());
            if (int.TryParse(buffer, out int parsed))
                value = Mathf.Clamp(parsed, min, max);
        }

        public static void TextFieldNumeric(Rect rect, ref float value, ref string buffer, float min = 0f, float max = float.MaxValue)
        {
            buffer = Widgets.TextField(rect, buffer ?? value.ToString());
            if (float.TryParse(buffer, out float parsed))
                value = Mathf.Clamp(parsed, min, max);
        }

        public static bool ButtonText(Rect rect, string label, bool active = true)
        {
#if v0_13
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && active;
            bool clicked = Widgets.TextButton(rect, label, true, true);
            GUI.enabled = previousEnabled;
            return clicked && active;
#else
            return Widgets.ButtonText(rect, label, active: active);
#endif
        }

        public static bool ButtonText(
            Rect rect,
            string label,
            bool drawBackground,
            bool doMouseoverSound,
            bool active = true)
        {
#if v0_13
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && active;
            bool clicked = Widgets.TextButton(rect, label, drawBackground, doMouseoverSound);
            GUI.enabled = previousEnabled;
            return clicked && active;
#else
            return Widgets.ButtonText(
                rect,
                label,
                drawBackground: drawBackground,
                doMouseoverSound: doMouseoverSound,
                active: active);
#endif
        }

        public static bool ButtonImage(Rect rect, Texture2D texture)
        {
#if v0_13
            return Widgets.ImageButton(rect, texture);
#else
            return Widgets.ButtonImage(rect, texture);
#endif
        }

        public static bool ButtonImage(Rect rect, Texture2D texture, Color baseColor, Color mouseoverColor)
        {
#if v0_13
            Color previousColor = GUI.color;
            if (Mouse.IsOver(rect))
                GUI.color = mouseoverColor;
            else
                GUI.color = baseColor;

            bool clicked = Widgets.ImageButton(rect, texture, GUI.color);
            GUI.color = previousColor;
            return clicked;
#else
            return Widgets.ButtonImage(rect, texture, baseColor, mouseoverColor);
#endif
        }

        public static bool ButtonImageWithBG(Rect rect, Texture2D texture, Vector2? size = null)
        {
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13
            Widgets.DrawBox(rect, 1);
            Rect imageRect = rect;
            if (size.HasValue)
            {
                Vector2 value = size.Value;
                imageRect = new Rect(rect.center.x - value.x / 2f, rect.center.y - value.y / 2f, value.x, value.y);
            }

#if v0_13
            return Widgets.ImageButton(imageRect, texture);
#else
            return Widgets.ButtonImage(imageRect, texture);
#endif
#else
            return Widgets.ButtonImageWithBG(rect, texture, size);
#endif
        }

        public static bool ButtonInvisible(Rect rect)
        {
#if v0_13
            return Widgets.InvisibleButton(rect);
#else
            return Widgets.ButtonInvisible(rect);
#endif
        }

        public static void CheckboxLabeled(Rect rect, string label, ref bool checkOn, bool disabled = false)
        {
#if v0_13
            Widgets.LabelCheckbox(rect, label, ref checkOn, disabled);
#else
            Widgets.CheckboxLabeled(rect, label, ref checkOn, disabled);
#endif
        }

        public static void DrawBoxSolid(Rect rect, Color color)
        {
#if v0_13
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
#else
            Widgets.DrawBoxSolid(rect, color);
#endif
        }

        public static void DrawBoxSolidWithOutline(Rect rect, Color fillColor, Color outlineColor, int outlineThickness = 1)
        {
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13
            DrawBoxSolid(rect, fillColor);
            Color previousColor = GUI.color;
            GUI.color = outlineColor;
            Widgets.DrawBox(rect, outlineThickness);
            GUI.color = previousColor;
#else
            Widgets.DrawBoxSolidWithOutline(rect, fillColor, outlineColor, outlineThickness);
#endif
        }

        public static void DrawLineHorizontal(float x, float y, float length, Color color)
        {
            DrawBoxSolid(new Rect(x, y, length, 1f), color);
        }

        public static void DrawLineHorizontal(float x, float y, float length)
        {
            DrawLineHorizontal(x, y, length, Color.white);
        }

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
#if v0_14
            Widgets.Checkbox(new Vector2(x, y), ref checkOn, 24f, disabled);
#elif (v0_18 || v0_17 || v0_16)
            Widgets.Checkbox(x, y, ref checkOn, 24f, disabled);
#else
            Widgets.Checkbox(x, y, ref checkOn, disabled: disabled, paintable: paintable);
#endif
        }

        public static float HorizontalSlider(
            Rect rect,
            float value,
            float min,
            float max,
            bool middleAlignment = true,
            string leftAlignedLabel = null,
            string rightAlignedLabel = null)
        {
#if v0_14
            return Mathf.Clamp(GUI.HorizontalSlider(rect, value, min, max), min, max);
#elif v0_15
            return Widgets.HorizontalSlider(rect, value, min, max, middleAlignment);
#else
            return Widgets.HorizontalSlider(
                rect,
                value,
                min,
                max,
                middleAlignment: middleAlignment,
                leftAlignedLabel: leftAlignedLabel,
                rightAlignedLabel: rightAlignedLabel);
#endif
        }
    }

    public static class WidgetsWorkCompat
    {
        public static void DrawWorkBoxFor(float x, float y, Pawn pawn, WorkTypeDef workType, bool incapable)
        {
#if v0_13 || v0_14
            WidgetsWork.DrawWorkBoxFor(new Vector2(x, y), pawn, workType, incapable);
#else
            WidgetsWork.DrawWorkBoxFor(x, y, pawn, workType, incapable);
#endif
        }
    }

    public static class GenFilePathsCompat
    {
        public static string ConfigFolderPath
        {
            get
            {
#if v0_16
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "Config");
                Directory.CreateDirectory(path);
                return path;
#else
                return GenFilePaths.ConfigFolderPath;
#endif
            }
        }
    }

    public static class ScribeFileCompat
    {
        public static void InitLoading(string path)
        {
#if v0_16
            Scribe.InitLoading(path);
#else
            Scribe.loader.InitLoading(path);
#endif
        }

        public static void FinalizeLoading()
        {
#if v0_16
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe.FinalizeLoading();
            }
#else
            Scribe.loader.FinalizeLoading();
#endif
        }

        public static void InitSaving(string path, string documentElementName)
        {
#if v0_16
            Scribe.InitWriting(path, documentElementName);
#else
            Scribe.saver.InitSaving(path, documentElementName);
#endif
        }

        public static void FinalizeSaving()
        {
#if v0_16
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe.FinalizeWriting();
            }
#else
            Scribe.saver.FinalizeSaving();
#endif
        }
    }

    public static class ScribeCompat
    {
        private static bool TryEnterNode(string label)
        {
#if v0_13
            Scribe.EnterNode(label);
            return true;
#else
            return Scribe.EnterNode(label);
#endif
        }

        public static LookMode DefLookMode
        {
            get
            {
#if v0_15
                return LookMode.DefReference;
#else
                return LookMode.Def;
#endif
            }
        }

        public static void LookValue<T>(ref T value, string label, T defaultValue = default, bool forceSave = false)
        {
#if v0_16
            Scribe_Values.LookValue(ref value, label, defaultValue, forceSave);
#else
            Scribe_Values.Look(ref value, label, defaultValue, forceSave);
#endif
        }

        public static void LookCollection<T>(
            ref List<T> list,
            string label,
            LookMode lookMode = LookMode.Undefined,
            params object[] ctorArgs)
        {
#if v0_16
            Scribe_Collections.LookList(ref list, label, lookMode, ctorArgs);
#else
            Scribe_Collections.Look(ref list, label, lookMode, ctorArgs);
#endif
        }

        public static void LookCollection<K, V>(
            ref Dictionary<K, V> dictionary,
            string label,
            LookMode keyLookMode = LookMode.Undefined,
            LookMode valueLookMode = LookMode.Undefined)
        {
#if v0_16
            List<K> keys = null;
            List<V> values = null;

            if (Scribe.mode == LoadSaveMode.Saving && dictionary != null)
            {
                keys = dictionary.Keys.ToList();
                values = new List<V>();
                foreach (K key in keys)
                {
                    values.Add(dictionary[key]);
                }
            }

            if (TryEnterNode(label))
            {
                try
                {
                    LookCollection(ref keys, "keys", keyLookMode);
                    LookCollection(ref values, "values", valueLookMode);

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        dictionary = new Dictionary<K, V>();
                        if (keys != null && values != null)
                        {
                            int count = Math.Min(keys.Count, values.Count);
                            for (int i = 0; i < count; i++)
                            {
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
#else
            Scribe_Collections.Look(ref dictionary, label, keyLookMode, valueLookMode);
#endif
        }

        public static void LookDef<T>(ref T value, string label) where T : Def, new()
        {
#if v0_16
            Scribe_Defs.LookDef(ref value, label);
#else
            Scribe_Defs.Look(ref value, label);
#endif
        }

        public static void LookDeep<T>(ref T target, string label, params object[] ctorArgs)
        {
#if v0_16
            Scribe_Deep.LookDeep(ref target, label, ctorArgs);
#else
            Scribe_Deep.Look(ref target, label, ctorArgs);
#endif
        }

        public static void LookReference<T>(ref T reference, string label, bool saveDestroyedThings = false)
            where T : ILoadReferenceable
        {
#if v0_16
            Scribe_References.LookReference(ref reference, label, saveDestroyedThings);
#else
            Scribe_References.Look(ref reference, label, saveDestroyedThings);
#endif
        }

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

            if (TryEnterNode(label))
            {
                try
                {
                    LookCollection(ref keys, "keys", LookMode.Value);
                    LookCollection(ref values, "values", valueLookMode);

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
            LookCollection(ref dictionary, label, LookMode.Value, valueLookMode);
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
        public static ModMetaData GetActiveModWithIdentifier(string packageId)
        {
#if v0_13
            return null;
#elif (v1_0 || v0_19)
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

#if !v0_13 && (v1_0 || v0_19)
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
#if v0_17 || v0_16
    public class MessageTypeDef
    {
        public readonly MessageSound LegacySound;

        public MessageTypeDef(MessageSound legacySound)
        {
            LegacySound = legacySound;
        }
    }

    public static class MessageTypeDefOf
    {
        public static readonly MessageTypeDef RejectInput = new MessageTypeDef(MessageSound.RejectInput);
        public static readonly MessageTypeDef PositiveEvent = new MessageTypeDef(MessageSound.Benefit);
    }
#endif

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
