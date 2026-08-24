using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads.V2;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Optional boundary between settings presentation and an active Work-tab
    /// preview. Settings owns its controls and global-store policy; the
    /// registered implementation owns preview mutation and synchronization.
    /// </summary>
    internal interface IWorkTabPresentationPreviewPort
    {
        bool IsPreviewActive { get; }

        bool TryReadPresentationPreview(
            out WorkTabPresentationPreviewState preview,
            out string reason);

        bool TryMutatePresentation(
            WorkTabPresentationPreviewMutation mutation,
            out string reason);

    }

    internal enum WorkTabPresentationPreviewMutationKind
    {
        Set = 0,
        Acquire = 1,
        Release = 2
    }

    /// <summary>
    /// A presentation-only edit request. The settings layer names the desired
    /// ownership action; the optional preview implementation owns the model
    /// mutation and synchronization needed to complete it.
    /// </summary>
    internal readonly struct WorkTabPresentationPreviewMutation
    {
        internal WorkTabPresentationPreviewMutation(
            WorkTabPresentationPreviewMutationKind kind,
            string settingId,
            WorkloadScalarValue value)
        {
            Kind = kind;
            SettingId = settingId ?? string.Empty;
            Value = value;
        }

        internal WorkTabPresentationPreviewMutationKind Kind { get; }
        internal string SettingId { get; }
        internal WorkloadScalarValue Value { get; }
    }

    /// <summary>
    /// Immutable presentation-only projection supplied by the optional preview
    /// port. It deliberately exposes neither preview-session identity objects
    /// nor a draft editor to settings code.
    /// </summary>
    internal readonly struct WorkTabPresentationPreviewState
    {
        internal WorkTabPresentationPreviewState(
            string identity,
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> beforeIntents,
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> afterIntents)
        {
            Identity = identity ?? string.Empty;
            BeforeIntents = beforeIntents ?? new WorkloadPresentationSettingIntentEntry[0];
            AfterIntents = afterIntents ?? new WorkloadPresentationSettingIntentEntry[0];
        }

        internal string Identity { get; }
        internal IReadOnlyList<WorkloadPresentationSettingIntentEntry> BeforeIntents { get; }
        internal IReadOnlyList<WorkloadPresentationSettingIntentEntry> AfterIntents { get; }
    }
}
