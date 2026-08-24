using System.Threading;
using Better_Work_Tab.UI.WorkGrid.Contracts;

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

        internal static IWorkGridPreviewPort CurrentPreview =>
            CurrentFrame.Value?.ResolvePreview();

        public static System.IDisposable Push(IWorkTabEffectiveStateProvider provider)
        {
            return Push(provider, null);
        }

        internal static System.IDisposable Push(
            IWorkTabEffectiveStateProvider provider,
            IWorkGridPreviewPort preview)
        {
            IWorkTabEffectiveStateProvider actualProvider =
                provider ?? EmptyWorkTabEffectiveStateProvider.Instance;
            var frame = new ScopeFrame(
                actualProvider,
                CurrentFrame.Value,
                preview,
                preview != null &&
                ReferenceEquals(preview.ScopedProvider, actualProvider));
            CurrentFrame.Value = frame;
            return frame;
        }

        /// <summary>
        /// Installs the immutable provider captured by a WorkTabView for a
        /// render-only segment. The preview port remains available for visual
        /// consumers, but this frame never follows its mutable provider or
        /// clears cache residue on disposal.
        /// </summary>
        internal static System.IDisposable PushCapturedView(
            IWorkTabEffectiveStateProvider provider,
            IWorkGridPreviewPort preview)
        {
            var frame = new ScopeFrame(
                provider ?? EmptyWorkTabEffectiveStateProvider.Instance,
                CurrentFrame.Value,
                preview,
                followsPreview: false);
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
                IWorkGridPreviewPort preview,
                bool followsPreview)
            {
                Provider = provider;
                Parent = parent;
                Preview = preview;
                FollowsPreview = followsPreview;
            }

            internal IWorkTabEffectiveStateProvider Provider { get; }
            internal ScopeFrame Parent { get; }
            private IWorkGridPreviewPort Preview { get; }
            private bool FollowsPreview { get; }

            internal IWorkTabEffectiveStateProvider ResolveProvider()
            {
                if (!FollowsPreview)
                {
                    return Provider;
                }

                return Preview?.ScopedProvider ??
                       EmptyWorkTabEffectiveStateProvider.Instance;
            }

            internal IWorkGridPreviewPort ResolvePreview()
            {
                // Command-port lifetime is independent of active preview
                // reads. Consumers that require an open preview check IsActive.
                return Preview;
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
                if (FollowsPreview && Provider != null && Provider.IsPreview &&
                    (Preview == null ||
                     !ReferenceEquals(Preview.ScopedProvider, Provider)))
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
        public WorkTabEffectiveStateRevisionVector RevisionVector =>
            WorkTabEffectiveStateRevisionVector.FromRevision(Revision);
        public WorkTabEffectiveStateSource Source => WorkTabEffectiveStateSource.Live;
        public bool IsLive => true;
        public bool IsPreview => false;
        public WorkTabEffectiveStateRevision RevisionToken =>
            new WorkTabEffectiveStateRevision(ProviderId, Revision, Source);

    }
}
