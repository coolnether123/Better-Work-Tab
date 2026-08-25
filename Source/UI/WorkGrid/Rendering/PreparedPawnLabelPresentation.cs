using System;
using System.Reflection;
using Better_Work_Tab.PawnOrganizer.API;
using HarmonyLib;
using RimWorld;
using Spine.UI;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal readonly struct PreparedPawnLabelPresentation
    {
        internal PreparedPawnLabelPresentation(
            Pawn pawn,
            string richText,
            Color baseTextColor,
            bool showIcon,
            float maximumContentHeight,
            bool contrastMode)
        {
            Pawn = pawn;
            RichText = richText ?? string.Empty;
            BaseTextColor = baseTextColor;
            ShowIcon = showIcon;
            MaximumContentHeight = maximumContentHeight;
            ContrastMode = contrastMode;
        }

        internal Pawn Pawn { get; }
        internal string RichText { get; }
        internal Color BaseTextColor { get; }
        internal bool ShowIcon { get; }
        internal float MaximumContentHeight { get; }
        internal bool ContrastMode { get; }
        internal bool IsPrepared => Pawn != null && !string.IsNullOrEmpty(RichText);
    }

    internal static class PreparedPawnLabelCapture
    {
        private static readonly MethodInfo GetLabelMethod =
            AccessTools.Method(typeof(PawnColumnWorker_Label), "GetLabel", new[] { typeof(Pawn) });
        private static readonly Func<PawnColumnWorker_Label, Pawn, TaggedString> GetLabel =
            AccessTools.MethodDelegate<Func<PawnColumnWorker_Label, Pawn, TaggedString>>(GetLabelMethod);

        internal static PreparedPawnLabelPresentation Capture(
            PawnColumnWorker_Label worker,
            Pawn pawn)
        {
            if (worker == null || pawn == null)
            {
                return default;
            }

            bool contrast = PawnColorDatabase.TryGetColor(pawn, out Color background) &&
                background.a > 0f;
            TaggedString label = GetLabel(worker, pawn);
            Color baseTextColor = Color.white;
            string richText;
            if (contrast)
            {
                richText = label.Resolve().StripTags();
                baseTextColor = TextColorHelper.GetContrastingTextColor(
                    background,
                    Color.black,
                    Color.white);
            }
            else
            {
                if (pawn.IsSlave || pawn.IsColonyMech)
                {
                    label = label.Colorize(PawnNameColorUtility.PawnNameColorOf(pawn));
                }
                richText = label.Resolve();
            }

            float maximumHeight = worker.def.groupable
                ? float.MaxValue
                : worker.GetMinCellHeight(pawn);
            return new PreparedPawnLabelPresentation(
                pawn,
                richText,
                baseTextColor,
                worker.def.showIcon,
                maximumHeight,
                contrast);
        }

        internal static int ComputeSourceSignature(
            PawnColumnWorker_Label worker,
            PawnTable table)
        {
            unchecked
            {
                int hash = PawnColorDatabase.Version;
                hash = (hash * 397) ^ (worker?.def?.useLabelShort == true ? 1 : 0);
                if (table?.cachedPawns == null)
                {
                    return hash;
                }

                for (int index = 0; index < table.cachedPawns.Count; index++)
                {
                    Pawn pawn = table.cachedPawns[index];
                    hash = (hash * 397) ^ (pawn?.thingIDNumber ?? 0);
                    if (pawn == null)
                    {
                        continue;
                    }
                    hash = (hash * 397) ^ (pawn.Name?.ToStringShort?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (pawn.story?.Title?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (pawn.KindLabel?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (pawn.IsSlave ? 1 : 0);
                    hash = (hash * 397) ^ (pawn.IsColonyMech ? 1 : 0);
                    if (pawn.IsSlave || pawn.IsColonyMech)
                    {
                        hash = (hash * 397) ^
                            PawnNameColorUtility.PawnNameColorOf(pawn).GetHashCode();
                    }
                    hash = (hash * 397) ^ (pawn.IsSubhuman ? 1 : 0);
                    hash = (hash * 397) ^ (pawn.mutant?.HasTurned == true ? 1 : 0);
                }
                return hash;
            }
        }
    }
}
