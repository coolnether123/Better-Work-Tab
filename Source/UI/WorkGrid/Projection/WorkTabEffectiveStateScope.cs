using System.Threading;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.Workloads;

namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    /// <summary>
    /// Holds the provider for the current Work-tab pass. The value is scoped to
    /// the current execution flow, and a linked frame makes nested and
    /// out-of-order disposal restore the prior value instead of leaking a
    /// preview into a later pass.
    /// </summary>
    public static class WorkTabEffectiveStateScope
    {
        private static readonly AsyncLocal<ScopeFrame> CurrentFrame =
            new AsyncLocal<ScopeFrame>();

        /// <summary>
        /// The provider at the current scope, or null when no pass has pushed
        /// one. Use <see cref="CurrentOrDefault"/> when a fallback-only reader
        /// is more convenient.
        /// </summary>
        public static IWorkTabEffectiveStateProvider Current =>
            CurrentFrame.Value?.ResolveProvider();

        public static IWorkTabEffectiveStateProvider CurrentOrDefault =>
            Current ?? EmptyWorkTabEffectiveStateProvider.Instance;

        public static System.IDisposable Push(IWorkTabEffectiveStateProvider provider)
        {
            IWorkTabEffectiveStateProvider actualProvider =
                provider ?? EmptyWorkTabEffectiveStateProvider.Instance;
            WorkloadPreviewController previewController =
                WorkloadPreviewController.Current;
            var frame = new ScopeFrame(
                actualProvider,
                CurrentFrame.Value,
                previewController != null &&
                ReferenceEquals(previewController.ProjectedProvider, actualProvider));
            CurrentFrame.Value = frame;
            return frame;
        }

        public static System.IDisposable Enter(IWorkTabEffectiveStateProvider provider)
        {
            return Push(provider);
        }

        private sealed class ScopeFrame : System.IDisposable
        {
            private bool _disposed;

            internal ScopeFrame(
                IWorkTabEffectiveStateProvider provider,
                ScopeFrame parent,
                bool followsPreviewController)
            {
                Provider = provider;
                Parent = parent;
                FollowsPreviewController = followsPreviewController;
            }

            internal IWorkTabEffectiveStateProvider Provider { get; }
            internal ScopeFrame Parent { get; }
            private bool FollowsPreviewController { get; }

            internal IWorkTabEffectiveStateProvider ResolveProvider()
            {
                if (!FollowsPreviewController)
                {
                    return Provider;
                }

                WorkloadPreviewController previewController =
                    WorkloadPreviewController.Current;
                return previewController?.ScopedProvider ??
                       EmptyWorkTabEffectiveStateProvider.Instance;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (ReferenceEquals(CurrentFrame.Value, this))
                {
                    RestoreNearestLiveParent();
                }

                // Retain the active preview snapshot between Layout, input,
                // and Repaint. Clear only when this pass's projected provider
                // was replaced or cancelled while the pass was running.
                WorkloadPreviewController previewController =
                    WorkloadPreviewController.Current;
                if (Provider != null && Provider.IsPreview &&
                    (previewController == null ||
                     !ReferenceEquals(previewController.ProjectedProvider, Provider)))
                {
                    WorkTabEffectiveStateRuntime.ClearPreviewCacheResidue();
                }
                WorkTabEffectiveStateRuntime.InvalidateRenderPass();
            }

            private void RestoreNearestLiveParent()
            {
                ScopeFrame frame = Parent;
                while (frame != null && frame._disposed)
                {
                    frame = frame.Parent;
                }

                CurrentFrame.Value = frame;
            }
        }
    }

    /// <summary>
    /// Fallback used by runtime convenience readers outside an effective-state
    /// scope. It never supplies a value of its own and therefore cannot make a
    /// preview or a missing live provider look authoritative.
    /// </summary>
    public sealed class EmptyWorkTabEffectiveStateProvider : IWorkTabEffectiveStateProvider
    {
        public static readonly EmptyWorkTabEffectiveStateProvider Instance =
            new EmptyWorkTabEffectiveStateProvider();

        private EmptyWorkTabEffectiveStateProvider()
        {
        }

        public string ProviderId => "bwt.none";
        public long Revision => 0;
        public WorkTabEffectiveStateSource Source => WorkTabEffectiveStateSource.Live;
        public bool IsLive => true;
        public bool IsPreview => false;
        public WorkTabEffectiveStateRevision RevisionToken =>
            new WorkTabEffectiveStateRevision(ProviderId, Revision, Source);

        public int GetParentPriority(WorkloadParentPriorityKey key, int fallbackPriority)
        {
            return fallbackPriority;
        }

        public bool TryGetParentPriority(WorkloadParentPriorityKey key, out int priority)
        {
            priority = 0;
            return false;
        }

        public bool IsManualMode(WorkloadParentPriorityKey key, bool fallbackManualMode)
        {
            return fallbackManualMode;
        }

        public bool TryGetManualMode(WorkloadParentPriorityKey key, out bool manualMode)
        {
            manualMode = false;
            return false;
        }

        public ScheduleKey GetSchedule(PawnKey key, ScheduleKey fallbackSchedule)
        {
            return fallbackSchedule;
        }

        public bool TryGetSchedule(PawnKey key, out ScheduleKey schedule)
        {
            schedule = null;
            return false;
        }

        public WorkloadScalarValue GetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue fallbackValue)
        {
            return fallbackValue;
        }

        public bool TryGetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            return false;
        }

        public int GetSpecificJobOrder(WorkloadSpecificJobKey key, int fallbackOrder)
        {
            return fallbackOrder;
        }

        public bool TryGetSpecificJobOrder(WorkloadSpecificJobKey key, out int order)
        {
            order = 0;
            return false;
        }

        public WorkloadScalarValue GetPresentationSetting(
            string key,
            WorkloadScalarValue fallbackValue)
        {
            return fallbackValue;
        }

        public bool TryGetPresentationSetting(string key, out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            return false;
        }
    }
}
