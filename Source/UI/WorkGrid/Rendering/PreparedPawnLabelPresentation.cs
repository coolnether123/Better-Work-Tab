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
    /// <summary>
    /// Inputs that can change label measurement without changing the label text.
    /// Exact values are kept so reuse never depends on a hash collision.
    /// </summary>
    internal readonly struct PreparedPawnLabelMetricKey : IEquatable<PreparedPawnLabelMetricKey>
    {
        internal PreparedPawnLabelMetricKey(
            int uiScaleMilli,
            long settingsThemeLanguageScaleRevision)
        {
            UiScaleMilli = uiScaleMilli;
            SettingsThemeLanguageScaleRevision = settingsThemeLanguageScaleRevision;
        }

        internal int UiScaleMilli { get; }
        internal long SettingsThemeLanguageScaleRevision { get; }

        public bool Equals(PreparedPawnLabelMetricKey other)
        {
            return UiScaleMilli == other.UiScaleMilli &&
                   SettingsThemeLanguageScaleRevision ==
                       other.SettingsThemeLanguageScaleRevision;
        }

        public override bool Equals(object obj)
        {
            return obj is PreparedPawnLabelMetricKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (UiScaleMilli * 397) ^
                    SettingsThemeLanguageScaleRevision.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Prepared label text plus the exact source inputs used to produce it.
    /// SourceRichText is retained only to validate reuse after a full snapshot rebuild.
    /// </summary>
    internal readonly struct PreparedPawnLabelPresentation
    {
        internal PreparedPawnLabelPresentation(
            string sourceRichText,
            string richText,
            Color baseTextColor,
            bool showIcon,
            float maximumContentHeight,
            bool contrastMode,
            float preparedTextWidth,
            PreparedPawnLabelMetricKey metricKey)
        {
            SourceRichText = sourceRichText ??
                throw new ArgumentNullException(nameof(sourceRichText));
            RichText = richText ?? throw new ArgumentNullException(nameof(richText));
            BaseTextColor = baseTextColor;
            ShowIcon = showIcon;
            MaximumContentHeight = maximumContentHeight;
            ContrastMode = contrastMode;
            PreparedTextWidth = preparedTextWidth;
            MetricKey = metricKey;
            IsPrepared = true;
        }

        internal string SourceRichText { get; }
        internal string RichText { get; }
        internal Color BaseTextColor { get; }
        internal bool ShowIcon { get; }
        internal float MaximumContentHeight { get; }
        internal bool ContrastMode { get; }
        internal float PreparedTextWidth { get; }
        internal PreparedPawnLabelMetricKey MetricKey { get; }
        internal bool IsPrepared { get; }

        internal bool MatchesSource(
            string sourceRichText,
            Color baseTextColor,
            bool showIcon,
            float maximumContentHeight,
            bool contrastMode,
            float preparedTextWidth,
            PreparedPawnLabelMetricKey metricKey)
        {
            return IsPrepared &&
                   string.Equals(SourceRichText, sourceRichText, StringComparison.Ordinal) &&
                   BaseTextColor.Equals(baseTextColor) &&
                   ShowIcon == showIcon &&
                   MaximumContentHeight.Equals(maximumContentHeight) &&
                   ContrastMode == contrastMode &&
                   PreparedTextWidth.Equals(preparedTextWidth) &&
                   MetricKey.Equals(metricKey);
        }
    }

    internal static class PreparedPawnLabelCapture
    {
        private static readonly MethodInfo GetLabelMethod =
            AccessTools.Method(typeof(PawnColumnWorker_Label), "GetLabel", new[] { typeof(Pawn) });
        private static readonly Func<PawnColumnWorker_Label, Pawn, TaggedString> GetLabel =
            AccessTools.MethodDelegate<Func<PawnColumnWorker_Label, Pawn, TaggedString>>(GetLabelMethod);

        internal static PreparedPawnLabelPresentation Capture(
            PawnColumnWorker_Label worker,
            Pawn pawn,
            float columnWidth,
            float rowHeight,
            PreparedPawnLabelMetricKey metricKey,
            PreparedPawnLabelPresentation previous)
        {
            bool contrast = PawnColorDatabase.TryGetColor(pawn, out Color background) &&
                background.a > 0f;
            TaggedString nativeLabel = GetLabel(worker, pawn);
            bool colorizePawnName = pawn.IsSlave || pawn.IsColonyMech;
            Color pawnNameColor = !contrast && colorizePawnName
                ? PawnNameColorUtility.PawnNameColorOf(pawn)
                : Color.white;

            float maximumHeight = worker.def.groupable
                ? float.MaxValue
                : worker.GetMinCellHeight(pawn);
            float contentHeight = Mathf.Min(rowHeight, maximumHeight);
            float textWidth = columnWidth - 3f -
                (worker.def.showIcon ? contentHeight : 0f);

            // PawnColumnWorker_Label performs this exact conditional before
            // Widgets.Label. Keep it at snapshot time so the live draw still
            // owns glyph rasterization, while avoiding the old unconditional
            // custom ellipsis path. Contrast mode intentionally follows BWT's
            // native override, which strips markup and lets Widgets.Label clip.
            TaggedString preparedLabel = nativeLabel;
            if (!contrast)
            {
                // The TaggedString -> string conversion is intentional: native
                // DoCell uses it, and RimWorld's operator strips markup before
                // measuring. RawText would measure tags that native ignores.
                string nativeLabelForMeasurement = nativeLabel;
                if (Text.CalcSize(nativeLabelForMeasurement).x > textWidth)
                {
                    preparedLabel = GenText.Truncate(nativeLabel, textWidth, null);
                }
            }

            string resolvedLabel = preparedLabel.Resolve();
            Color baseTextColor = Color.white;
            string richText;
            if (contrast)
            {
                richText = PreparedPawnLabelText.StripMarkup(resolvedLabel);
                baseTextColor = TextColorHelper.GetContrastingTextColor(
                    background,
                    Color.black,
                    Color.white);
            }
            else
            {
                if (colorizePawnName)
                {
                    richText = resolvedLabel.Colorize(pawnNameColor);
                }
                else
                {
                    richText = resolvedLabel;
                }
            }

            if (previous.MatchesSource(
                    richText,
                    baseTextColor,
                    worker.def.showIcon,
                    maximumHeight,
                    contrast,
                    textWidth,
                    metricKey))
            {
                return previous;
            }

            // Keep the full resolved label. Widgets.Label owns clipping in the
            // same destination rectangle as the direct/native path; replacing
            // text with an ellipsis during capture changes the visible output.
            // Contrast markup stripping and pawn-name colorization above remain
            // capture-time presentation transforms.
            string preparedText = richText;
            return new PreparedPawnLabelPresentation(
                richText,
                preparedText,
                baseTextColor,
                worker.def.showIcon,
                maximumHeight,
                contrast,
                textWidth,
                metricKey);
        }

        internal static int ComputeSourceSignature(
            PawnColumnWorker_Label worker,
            PawnTable table)
        {
            unchecked
            {
                // Enumerate native label-visible inputs not covered by another
                // revision. Add new inputs here when that state grows.
                int hash = PawnColorDatabase.Version;
                hash = (hash * 397) ^ (worker?.def?.useLabelShort == true ? 1 : 0);
                if (table.cachedPawns == null)
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
