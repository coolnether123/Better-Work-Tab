namespace Better_Work_Tab.Features.Migration
{
    /// <summary>
    /// Product-level feature gates for a fresh 2.0 install, a conservative
    /// public-1.0.5 migration, and an upgrading player who opts into a tutorial.
    /// Kept free of RimWorld APIs so both cohorts are covered by lightweight tests.
    /// </summary>
    internal static class BWT20CohortPolicy
    {
        internal static readonly BWT20FeatureGates FreshInstall =
            new BWT20FeatureGates(
                enableSubWorkDrilldown: true,
                enableFluffyStyleFeatures: false,
                useRuleBuilder2: true,
                showGeneralTutorial: true,
                enableTimePrioritySchedules: true);

        internal static readonly BWT20FeatureGates Public105Migration =
            new BWT20FeatureGates(
                enableSubWorkDrilldown: false,
                enableFluffyStyleFeatures: false,
                useRuleBuilder2: false,
                showGeneralTutorial: false,
                enableTimePrioritySchedules: false);

        internal static readonly BWT20FeatureGates TutorialOptIn = FreshInstall;
    }

    internal readonly struct BWT20FeatureGates
    {
        internal BWT20FeatureGates(
            bool enableSubWorkDrilldown,
            bool enableFluffyStyleFeatures,
            bool useRuleBuilder2,
            bool showGeneralTutorial,
            bool enableTimePrioritySchedules)
        {
            EnableSubWorkDrilldown = enableSubWorkDrilldown;
            EnableFluffyStyleFeatures = enableFluffyStyleFeatures;
            UseRuleBuilder2 = useRuleBuilder2;
            ShowGeneralTutorial = showGeneralTutorial;
            EnableTimePrioritySchedules = enableTimePrioritySchedules;
        }

        internal bool EnableSubWorkDrilldown { get; }
        internal bool EnableFluffyStyleFeatures { get; }
        internal bool UseRuleBuilder2 { get; }
        internal bool ShowGeneralTutorial { get; }
        internal bool EnableTimePrioritySchedules { get; }
    }
}
