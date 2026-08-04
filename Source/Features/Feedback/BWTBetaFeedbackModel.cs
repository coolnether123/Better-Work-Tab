using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Verse;

namespace Better_Work_Tab.Features.Feedback
{
    /// <summary>How a tester found a feature. "NotUsed" is a real answer, not a blank.</summary>
    internal enum BWTFeatureVerdict
    {
        Unanswered,
        Great,
        Works,
        Rough,
        Broken,
        NotUsed
    }

    internal enum BWTProblemSeverity
    {
        Unset,
        Cosmetic,
        Annoying,
        Blocking,
        Crash
    }

    internal sealed class BWTFeatureRating : IExposable
    {
        public string featureId = string.Empty;
        public BWTFeatureVerdict verdict;
        public string note = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref featureId, "featureId", string.Empty);
            Scribe_Values.Look(ref verdict, "verdict", BWTFeatureVerdict.Unanswered);
            Scribe_Values.Look(ref note, "note", string.Empty);
        }

        internal bool HasResponse =>
            verdict != BWTFeatureVerdict.Unanswered || !string.IsNullOrWhiteSpace(note);
    }

    internal sealed class BWTProblemReport : IExposable
    {
        public string areaId = BWTBetaFeatureCatalog.OtherAreaId;
        public BWTProblemSeverity severity;
        public string text = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref areaId, "areaId", BWTBetaFeatureCatalog.OtherAreaId);
            Scribe_Values.Look(ref severity, "severity", BWTProblemSeverity.Unset);
            Scribe_Values.Look(ref text, "text", string.Empty);
        }

        internal bool HasResponse => !string.IsNullOrWhiteSpace(text);
    }

    internal sealed class BWTBetaFeature
    {
        internal string Id;
        internal string LabelKey;
        internal string HintKey;
        private Func<bool> isRelevant;

        internal BWTBetaFeature(string id, string labelKey, string hintKey, Func<bool> isRelevant = null)
        {
            Id = id;
            LabelKey = labelKey;
            HintKey = hintKey;
            this.isRelevant = isRelevant;
        }

        /// <summary>
        /// Whether it is worth asking about at all. A tester with the drilldown
        /// switched off has nothing to say about it, and asking anyway teaches
        /// them that the form is not paying attention.
        /// </summary>
        internal bool IsRelevant => isRelevant == null || isRelevant();
    }

    /// <summary>
    /// The things 2.0 is asking about. This is the single list: the ratings
    /// section walks it, the problem-report area picker walks it, and the report
    /// text walks it. Adding a feature here adds it in all three places.
    /// </summary>
    internal static class BWTBetaFeatureCatalog
    {
        internal const string OtherAreaId = "other";

        private static readonly List<BWTBetaFeature> features = new List<BWTBetaFeature>
        {
            Feature("worktab", null),
            Feature("priorities", null),
            Feature("subwork", () => Enabled(s => s.enableSubWorkDrilldown, DefaultSettings.enableSubWorkDrilldown)),
            Feature("rulebuilder", () => Enabled(s => s.enableAutoAssignFeature, DefaultSettings.enableAutoAssignFeature)),
            Feature("workloads", () => Enabled(s => s.enableWorkloads, DefaultSettings.enableWorkloads)),
            Feature("schedules", () => Enabled(s => s.enableTimePrioritySchedules, DefaultSettings.enableTimePrioritySchedules)),
            Feature("columns", () =>
                Enabled(s => s.enableColumnGrouping, DefaultSettings.enableColumnGrouping) ||
                Enabled(s => s.enableDragDropReordering, DefaultSettings.enableDragDropReordering)),
            Feature("skills", () => Enabled(s => s.enableSkillOverlayFeature, DefaultSettings.enableSkillOverlayFeature)),
            Feature("settings", null),
            Feature("fluffy", () => FluffyWorkTabGateway.IsPresent),
            Feature("tutorial", null)
        };

        internal static IEnumerable<BWTBetaFeature> Relevant => features.Where(feature => feature.IsRelevant);

        internal static BWTBetaFeature Find(string id)
        {
            return features.FirstOrDefault(feature => string.Equals(feature.Id, id, StringComparison.Ordinal));
        }

        internal static string AreaLabel(string areaId)
        {
            if (string.Equals(areaId, OtherAreaId, StringComparison.Ordinal))
            {
                return "BWT_Beta_Area_Other".Translate();
            }

            BWTBetaFeature feature = Find(areaId);
            return feature != null ? feature.LabelKey.Translate().ToString() : areaId;
        }

        private static BWTBetaFeature Feature(string id, Func<bool> isRelevant)
        {
            return new BWTBetaFeature(id, "BWT_Beta_Feature_" + id, "BWT_Beta_FeatureHint_" + id, isRelevant);
        }

        private static bool Enabled(Func<BetterWorkTabSettings, bool> read, bool fallback)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings == null ? fallback : read(settings);
        }
    }

    /// <summary>Persistence and mutation only. It renders nothing and sends nothing.</summary>
    internal static class BWTBetaFeedbackStore
    {
        internal const int MaxProblemReports = 12;

        internal static BWTFeatureRating GetOrCreateRating(BetterWorkTabSettings settings, string featureId)
        {
            Ensure(settings);
            BWTFeatureRating rating = settings.betaFeatureRatings.FirstOrDefault(
                item => string.Equals(item.featureId, featureId, StringComparison.Ordinal));
            if (rating != null)
            {
                return rating;
            }

            rating = new BWTFeatureRating { featureId = featureId ?? string.Empty };
            settings.betaFeatureRatings.Add(rating);
            return rating;
        }

        internal static BWTProblemReport AddProblem(BetterWorkTabSettings settings)
        {
            Ensure(settings);
            if (settings.betaProblemReports.Count >= MaxProblemReports)
            {
                return null;
            }

            var report = new BWTProblemReport();
            settings.betaProblemReports.Add(report);
            return report;
        }

        internal static void RemoveProblem(BetterWorkTabSettings settings, BWTProblemReport report)
        {
            Ensure(settings);
            settings.betaProblemReports.Remove(report);
        }

        internal static void Clear(BetterWorkTabSettings settings)
        {
            Ensure(settings);
            settings.betaFeatureRatings.Clear();
            settings.betaProblemReports.Clear();
            settings.betaOverallFeedback = string.Empty;
            settings.betaTesterHandle = string.Empty;
        }

        internal static void Ensure(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.betaFeatureRatings ??= new List<BWTFeatureRating>();
            settings.betaProblemReports ??= new List<BWTProblemReport>();
            settings.betaOverallFeedback ??= string.Empty;
            settings.betaTesterHandle ??= string.Empty;
        }
    }
}
