using System;
using System.Collections.Generic;
using System.Threading;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    /// <summary>
    /// Neutral runtime owner for the provider scoped around a Work-grid pass.
    /// Feature-specific projections adapt their own keys and payloads outside
    /// this namespace.
    /// </summary>
    public static class WorkTabEffectiveStateRuntime
    {
        private static readonly HashSet<string> ReportedBlockedOperations =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly AsyncLocal<EffectiveStateRenderPass> RenderPass =
            new AsyncLocal<EffectiveStateRenderPass>();
        private static readonly AsyncLocal<long> PreparingRenderPass =
            new AsyncLocal<long>();
        private static Action _previewCacheClearer;
        private static Func<IDisposable> _previewScopePusher;

        public static IWorkTabEffectiveStateProvider CurrentProvider =>
            WorkTabEffectiveStateScope.CurrentOrDefault;

        public static bool IsPreviewActive =>
            WorkTabEffectiveStateScope.Current?.IsPreview == true;

        public static bool IsPreviewSpecificJobOrderingBlocked =>
            IsPreviewActive &&
            IsPreviewDimensionOwned(WorkTabEffectiveStateDimension.SpecificJobOrder);

        public static WorkTabEffectiveStateRevision CurrentRevision => EnsureRenderPass();

        public static long CurrentRenderPassId => RenderPass.Value?.Id ?? 0L;

        internal static void RegisterPreviewCacheClearer(Action clearer)
        {
            Interlocked.Exchange(ref _previewCacheClearer, clearer);
        }

        internal static void RegisterPreviewScopePusher(Func<IDisposable> pusher)
        {
            Interlocked.Exchange(ref _previewScopePusher, pusher);
        }

        internal static IDisposable PushCurrentPreviewScope()
        {
            return Volatile.Read(ref _previewScopePusher)?.Invoke();
        }

        internal static void ClearPreviewCacheResidue()
        {
            Action clearer = Volatile.Read(ref _previewCacheClearer);
            clearer?.Invoke();
        }

        internal static long PreparingRenderPassId => PreparingRenderPass.Value;

        public static WorkTabEffectiveStateRevision BeginRenderPass()
        {
            return EnsureRenderPass();
        }

        public static IWorkTabEffectiveStateProvider CaptureCurrentView(
            WorkTabEffectiveStateRevision revision)
        {
            IWorkTabEffectiveStateProvider provider = CurrentProvider;
            EffectiveStateRenderPass pass = RenderPass.Value;
            if (pass != null &&
                pass.FrameNumber == Time.frameCount &&
                ReferenceEquals(pass.Provider, provider) &&
                pass.Revision == revision &&
                pass.CapturedView != null)
            {
                return pass.CapturedView;
            }

            IWorkTabEffectiveStateProvider view = provider;
            if (provider is IWorkTabEffectiveStateViewSource source)
            {
                IWorkTabEffectiveStateProvider captured =
                    source.CaptureEffectiveStateView(revision);
                if (captured != null)
                {
                    view = captured;
                }
            }

            if (pass != null &&
                ReferenceEquals(pass.Provider, provider) &&
                pass.Revision == revision)
            {
                pass.CapturedView = view;
            }
            return view;
        }

        public static void InvalidateRenderPass()
        {
            EffectiveStateRenderPass previous = RenderPass.Value;
            RenderPass.Value = new EffectiveStateRenderPass(
                NextRenderPassId(previous),
                Time.frameCount,
                null,
                default(WorkTabEffectiveStateRevision));
        }

        public static bool IsCurrent(WorkTabEffectiveStateRevision revision)
        {
            return revision.IsCurrent(CurrentProvider);
        }

        public static bool IsPreviewDimensionOwned(
            WorkTabEffectiveStateDimension dimension)
        {
            if (!IsPreviewActive)
            {
                return false;
            }

            if (!(CurrentProvider is IWorkTabPreviewOwnership ownership))
            {
                return true;
            }

            return ownership.OwnsDimension(dimension);
        }

        internal static WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            ResolvePreviewSchedule(TimePriorityTarget target)
        {
            IWorkTabPreviewStateReader reader = CurrentPreviewState;
            return reader == null
                ? WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion
                : reader.ResolveSchedule(target);
        }

        internal static WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            ResolvePreviewScheduleIntent(TimePriorityTarget target)
        {
            IWorkTabPreviewStateReader reader = CurrentPreviewState;
            return reader == null
                ? WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion
                : reader.ResolvePreviewScheduleIntent(target);
        }

        internal static WorkTabEffectiveStateResolution<int>
            ResolvePreviewSpecificJobPriority(WorkTabSpecificJobTarget target)
        {
            IWorkTabPreviewStateReader reader = CurrentPreviewState;
            return reader == null
                ? WorkTabEffectiveStateResolution<int>.NoOpinion
                : reader.ResolveSpecificJobPriority(target);
        }

        internal static WorkTabEffectiveStateResolution<int>
            ResolvePreviewSpecificJobPriorityIntent(WorkTabSpecificJobTarget target)
        {
            IWorkTabPreviewStateReader reader = CurrentPreviewState;
            return reader == null
                ? WorkTabEffectiveStateResolution<int>.NoOpinion
                : reader.ResolvePreviewSpecificJobPriorityIntent(target);
        }

        internal static WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            ResolvePreviewWorkTypeOrder(WorkTabWorkTypeOrderTarget target)
        {
            IWorkTabPreviewStateReader reader = CurrentPreviewState;
            return reader == null
                ? WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion
                : reader.ResolveWorkTypeOrder(target);
        }

        internal static WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            ResolvePreviewWorkTypeOrderIntent(WorkTabWorkTypeOrderTarget target)
        {
            IWorkTabPreviewStateReader reader = CurrentPreviewState;
            return reader == null
                ? WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion
                : reader.ResolvePreviewWorkTypeOrderIntent(target);
        }

        internal static bool TryGetPreviewStateEditor(
            out IWorkTabPreviewStateEditor editor)
        {
            editor = null;
            if (!IsPreviewActive)
            {
                return false;
            }

            editor = CurrentProvider as IWorkTabPreviewStateEditor;
            if (editor != null)
            {
                return true;
            }

            ReportBlocked(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                "The active preview provider does not support typed preview state.");
            return false;
        }

        internal static bool TrySetPreviewScheduleIntent(
            TimePriorityTarget target,
            WorkTabEffectiveStateResolution<TimePriorityScheduleValue> intent,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.SetScheduleIntent(target, intent);
            return AcceptPreviewMutation(result);
        }

        internal static bool TrySetPreviewSpecificJobPriority(
            WorkTabSpecificJobTarget target,
            int priority,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.SetSpecificJobPriority(target, priority);
            return AcceptPreviewMutation(result);
        }

        internal static bool TrySetPreviewSpecificJobPriorityIntent(
            WorkTabSpecificJobTarget target,
            WorkTabEffectiveStateResolution<int> intent,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.SetSpecificJobPriorityIntent(target, intent);
            return AcceptPreviewMutation(result);
        }

        internal static bool TryClearPreviewSpecificJobPriority(
            WorkTabSpecificJobTarget target,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.ClearSpecificJobPriority(target);
            return AcceptPreviewMutation(result);
        }

        internal static bool TrySetPreviewWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target,
            IReadOnlyList<string> orderedWorkGiverNames,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.SetWorkTypeOrder(target, orderedWorkGiverNames);
            return AcceptPreviewMutation(result);
        }

        internal static bool TrySetPreviewWorkTypeOrderIntent(
            WorkTabWorkTypeOrderTarget target,
            WorkTabEffectiveStateResolution<IReadOnlyList<string>> intent,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.SetWorkTypeOrderIntent(target, intent);
            return AcceptPreviewMutation(result);
        }

        internal static bool TryClearPreviewWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewStateEditor(out IWorkTabPreviewStateEditor editor))
            {
                return false;
            }

            result = editor.ClearWorkTypeOrder(target);
            return AcceptPreviewMutation(result);
        }

        private static IWorkTabPreviewStateReader CurrentPreviewState =>
            CurrentProvider as IWorkTabPreviewStateReader;

        public static bool TrySetManualMode(bool manualMode)
        {
            return TrySetManualMode(manualMode, null);
        }

        internal static bool TrySetManualMode(
            bool manualMode,
            WorkTabApplication application)
        {
            if (!IsPreviewActive)
            {
                // HeaderButtons and the vanilla cell patch are static edges.
                // Window-owned chrome supplies its per-game command owner.
                return (application ?? WorkTabApplication.Current)?
                    .SetManualPriorityMode(manualMode) == true;
            }

            IWorkGridPreviewPort preview = WorkTabEffectiveStateScope.CurrentPreview;
            return preview != null && preview.TrySetManualMode(manualMode);
        }

        public static void ReportBlocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            string safeReason = reason ?? "BWT_Preview_OperationFailed".Translate();
            IWorkTabEffectiveStateProvider provider = CurrentProvider;
            string diagnosticKey = provider.ProviderId + "@" + provider.Revision + "|" +
                                   dimension + "|" + safeReason;
            if (!ReportedBlockedOperations.Add(diagnosticKey))
            {
                return;
            }

            Log.Warning("[BWT] Work-tab preview blocked " + dimension + ": " + safeReason);
        }

        public static bool AcceptPreviewMutation(
            WorkTabEffectiveStateMutationResult result)
        {
            if (!result.IsBlocked)
            {
                return result.Accepted || result.IsNoOp;
            }

            ReportBlocked(result.Dimension, result.Reason);
            return false;
        }

        public static WorkTabEffectiveStateMutationResult Blocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            return WorkTabEffectiveStateMutationResult.Blocked(
                dimension,
                CurrentProvider.Revision,
                reason);
        }

        private static WorkTabEffectiveStateRevision EnsureRenderPass()
        {
            IWorkTabEffectiveStateProvider provider = CurrentProvider;
            EffectiveStateRenderPass previous = RenderPass.Value;
            int frameNumber = Time.frameCount;
            if (previous != null &&
                ReferenceEquals(previous.Provider, provider) &&
                previous.FrameNumber == frameNumber)
            {
                return previous.Revision;
            }

            long passId = NextRenderPassId(previous);
            WorkTabEffectiveStateRevision revision;
            PreparingRenderPass.Value = passId;
            try
            {
                if (provider is IWorkTabEffectiveStatePassParticipant participant)
                {
                    participant.PrepareRenderPass(passId);
                }

                revision = provider.RevisionToken;
            }
            finally
            {
                PreparingRenderPass.Value = 0L;
            }

            RenderPass.Value = new EffectiveStateRenderPass(
                passId,
                frameNumber,
                provider,
                revision);
            return revision;
        }

        private static long NextRenderPassId(EffectiveStateRenderPass previous)
        {
            return unchecked((previous?.Id ?? 0L) + 1L);
        }

        private sealed class EffectiveStateRenderPass
        {
            internal EffectiveStateRenderPass(
                long id,
                int frameNumber,
                IWorkTabEffectiveStateProvider provider,
                WorkTabEffectiveStateRevision revision)
            {
                Id = id;
                FrameNumber = frameNumber;
                Provider = provider;
                Revision = revision;
            }

            internal long Id { get; }
            internal int FrameNumber { get; }
            internal IWorkTabEffectiveStateProvider Provider { get; }
            internal WorkTabEffectiveStateRevision Revision { get; }
            internal IWorkTabEffectiveStateProvider CapturedView { get; set; }
        }
    }
}
