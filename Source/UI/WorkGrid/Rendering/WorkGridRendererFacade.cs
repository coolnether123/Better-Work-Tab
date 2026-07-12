using System;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Spine.Api;
using Spine.RimWorld.Rendering;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Sole Work-grid renderer entry point. Selection and exception containment end here;
    /// the permanent legacy renderer remains an independent correctness floor.
    /// </summary>
    public sealed class WorkGridRendererFacade
    {
        private readonly LegacyWorkGridRenderer _legacy;
        private readonly WorkGridRendererSelector _selector;
        private readonly IRenderDiagnosticsSink _diagnostics;
        private IWorkGridRenderer _active;
        private string _activeId;
        private bool _fallbackPending;

        public WorkGridRendererFacade(
            MainTabWindow_BetterWork host,
            IWorkGridRendererCapabilityCheck capabilityCheck = null,
            IRenderDiagnosticsSink diagnostics = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            _diagnostics = diagnostics ?? BwtWorkGridDiagnosticsSink.Instance;
            _legacy = new LegacyWorkGridRenderer(host);
            _selector = new WorkGridRendererSelector(capabilityCheck, _diagnostics);
            _active = _legacy;
            _activeId = LegacyWorkGridRenderer.RendererId;
        }

        public RegistrationResult Register(IWorkGridRenderer renderer) => _selector.Register(renderer);

        public void PrepareFrame(WorkTabInvalidationVersion invalidationVersions)
        {
            _legacy.PrepareInvalidation(invalidationVersions);
        }

        public void Render(in WorkGridRenderContext context)
        {
            WorkGridForcedRendererMode forcedMode = WorkGridRendererDiagnostics.ForcedMode;

            if (context.EventPhase == ImGuiEventPhase.Layout)
            {
                _fallbackPending = false;
                WorkGridRendererSelection selection =
                    _selector.Select(_legacy, forcedMode, in context);
                _active = selection.Renderer;
                _activeId = selection.RendererId;
                PublishSelection(forcedMode, selection.Fallback, context.Scope);
            }
            else if (_fallbackPending)
            {
                return;
            }

            IWorkGridRenderer renderer = _active;
            try
            {
                renderer.Prepare(in context);
                renderer.Draw(in context);

                if (context.EventPhase == ImGuiEventPhase.Input)
                {
                    renderer.HandleEvent(in context);
                }

                if (context.EventPhase == ImGuiEventPhase.Repaint)
                {
                    renderer.ReleaseTransient(in context);
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(renderer, _legacy))
                {
                    throw;
                }

                string failedRendererId = _activeId;
                _selector.Quarantine(context.Scope, failedRendererId);
                _active = _legacy;
                _activeId = LegacyWorkGridRenderer.RendererId;
                _fallbackPending = true;
                var fallback = new WorkGridFallbackReason(
                    WorkGridFallbackReasonCode.RendererQuarantined,
                    "Renderer '" + failedRendererId + "' failed during " + context.EventPhase +
                    " at frame " + context.FrameNumber + " for " + context.Scope + ".");
                PublishSelection(forcedMode, fallback, context.Scope);
                Log.ErrorOnce(
                    "[BWT] Work-grid renderer '" + failedRendererId + "' failed during " + context.EventPhase +
                    " at frame " + context.FrameNumber + " for scope " + context.Scope +
                    ". It was quarantined for the session; legacy rendering resumes at the next Layout event.\n" +
                    exception,
                    StringComparer.Ordinal.GetHashCode("work-grid:" + context.Scope + ":" + failedRendererId));

                if (_diagnostics.Enabled)
                {
                    _diagnostics.Record(new RenderDiagnostic(
                        RenderDiagnosticSeverity.Error,
                        failedRendererId,
                        fallback.Detail,
                        exception));
                }
            }
        }

        private void PublishSelection(
            WorkGridForcedRendererMode forcedMode,
            WorkGridFallbackReason fallback,
            WorkGridSelectionScope scope)
        {
            bool quarantined = !ReferenceEquals(_active, _legacy) &&
                               _selector.IsQuarantined(scope, _activeId);
            if (fallback.Code == WorkGridFallbackReasonCode.RendererQuarantined)
            {
                quarantined = true;
            }

            bool changed = WorkGridRendererDiagnostics.Publish(
                _activeId,
                forcedMode,
                fallback,
                quarantined);

            if (changed && _diagnostics.Enabled)
            {
                string fallbackText = fallback.Code == WorkGridFallbackReasonCode.None
                    ? "none"
                    : fallback.Code + (string.IsNullOrEmpty(fallback.Detail) ? string.Empty : " (" + fallback.Detail + ")");
                _diagnostics.Record(new RenderDiagnostic(
                    RenderDiagnosticSeverity.Information,
                    "work-grid-facade",
                    "Active=" + _activeId + ", forced=" + forcedMode + ", fallback=" + fallbackText +
                    ", quarantined=" + quarantined + "."));
            }
        }
    }
}
