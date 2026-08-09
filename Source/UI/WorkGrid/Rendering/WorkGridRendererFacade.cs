using System;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Spine.Api;
using Better_Work_Tab.Foundation;
using Spine.RimWorld.Rendering;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Sole Work-grid renderer entry point. Selection and exception containment end here;
    /// the permanent vanilla renderer remains an independent correctness floor.
    /// </summary>
    public sealed class WorkGridRendererFacade
    {
        private readonly VanillaWorkGridRenderer _vanilla;
        private readonly WorkGridRendererSelector _selector;
        private readonly Func<WorkGridRendererMode> _selectionMode;
        private readonly IRenderDiagnosticsSink _diagnostics;
        private IWorkGridRenderer _active;
        private string _activeId;
        private bool _fallbackPending;

        public WorkGridRendererFacade(
            IWorkGridDrawingSurface drawingSurface,
            Func<WorkGridRendererMode> selectionMode,
            IWorkGridRendererCapabilityCheck capabilityCheck = null,
            IRenderDiagnosticsSink diagnostics = null)
        {
            if (drawingSurface == null) throw new ArgumentNullException(nameof(drawingSurface));

            _selectionMode = selectionMode ?? throw new ArgumentNullException(nameof(selectionMode));
            _diagnostics = diagnostics ?? BwtWorkGridDiagnosticsSink.Instance;
            _vanilla = new VanillaWorkGridRenderer(drawingSurface);
            _selector = new WorkGridRendererSelector(capabilityCheck, _diagnostics);
            _active = _vanilla;
            _activeId = VanillaWorkGridRenderer.RendererId;
        }

        public RegistrationResult Register(IWorkGridRenderer renderer) => _selector.Register(renderer);

        public void PrepareFrame(WorkTabInvalidationVersion invalidationVersions)
        {
            _vanilla.PrepareInvalidation(invalidationVersions);
        }

        public void Render(in WorkGridRenderContext context)
        {
            WorkGridRendererMode userMode = _selectionMode();
            WorkGridForcedRendererMode forcedMode = WorkGridRendererDiagnostics.ForcedMode;

            if (context.EventPhase == ImGuiEventPhase.Layout)
            {
                _fallbackPending = false;
                WorkGridRendererSelection selection =
                    _selector.Select(_vanilla, userMode, forcedMode, in context);
                _active = selection.Renderer;
                _activeId = selection.RendererId;
                PublishSelection(userMode, forcedMode, selection.Fallback, context.Scope);
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
                if (ReferenceEquals(renderer, _vanilla))
                {
                    throw;
                }

                string failedRendererId = _activeId;
                _selector.Quarantine(context.Scope, failedRendererId);
                _active = _vanilla;
                _activeId = VanillaWorkGridRenderer.RendererId;
                _fallbackPending = true;
                var fallback = new WorkGridFallbackReason(
                    WorkGridFallbackReasonCode.RendererQuarantined,
                    "Renderer '" + failedRendererId + "' failed during " + context.EventPhase +
                    " at frame " + context.FrameNumber + " for " + context.Scope + ".");
                PublishSelection(userMode, forcedMode, fallback, context.Scope);
                Log.Message(
                    "[BWT] Optimized Work-grid renderer '" + failedRendererId + "' stopped during " + context.EventPhase +
                    " at frame " + context.FrameNumber + " for scope " + context.Scope +
                    ". It was quarantined for the session; vanilla rendering resumes at the next Layout event. " +
                    "Cause: " + exception.Message);

                if (_diagnostics.Enabled)
                {
                    _diagnostics.Record(new RenderDiagnostic(
                        RenderDiagnosticSeverity.Information,
                        failedRendererId,
                        fallback.Detail,
                        exception));
                }
            }
        }

        private void PublishSelection(
            WorkGridRendererMode userMode,
            WorkGridForcedRendererMode forcedMode,
            WorkGridFallbackReason fallback,
            WorkGridSelectionScope scope)
        {
            bool quarantined = !ReferenceEquals(_active, _vanilla) &&
                               _selector.IsQuarantined(scope, _activeId);
            if (fallback.Code == WorkGridFallbackReasonCode.RendererQuarantined)
            {
                quarantined = true;
            }

            bool changed = WorkGridRendererDiagnostics.Publish(
                _activeId,
                userMode,
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
                    "Active=" + _activeId + ", setting=" + userMode +
                    ", forced=" + forcedMode + ", fallback=" + fallbackText +
                    ", quarantined=" + quarantined + "."));
            }
        }
    }
}
