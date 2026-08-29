using System;
using System.Reflection;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
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

            // Native PawnColumnWorker_Label caches the original TaggedString
            // after its first width check. Stable repaints therefore draw the
            // full label and let Widgets.Label clip it to rect3. Preparing an
            // uncached truncated value here would make the transient ellipsis
            // permanent and diverge from the visible native result.
            string resolvedLabel = nativeLabel.Resolve();
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

        /// <summary>
        /// Captures only O(1) mode inputs. Per-pawn name, title, role, color,
        /// and contrast inputs are owned by PawnLabelPresentationInvalidation.
        /// The bounded audit is the compatibility backstop for external writers.
        /// </summary>
        internal static int ComputePresentationModeSignature(PawnColumnWorker_Label worker)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ (worker?.def?.useLabelShort == true ? 1 : 0);
                // PawnColorDatabase exposes a producer-owned O(1) version. It
                // covers contrast changes without making the render pass walk
                // every pawn just to rediscover the same state.
                hash = (hash * 397) ^ PawnColorDatabase.Version;
                hash = (hash * 397) ^ (BwtRaisedPriorityFeatureInstaller.IsFeatureActive ? 1 : 0);
                hash = (hash * 397) ^
                    (PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures ? 1 : 0);
                hash = (hash * 397) ^ (SleekWorkTabGateway.SleekOwnsWorkTab ? 1 : 0);
                return hash;
            }
        }
    }
}
