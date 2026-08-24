using System;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGrid.Projection;
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
        private readonly IWorkGridDrawingSurface _drawingSurface;
        private readonly WorkGridRendererSelector _selector;
        private readonly Func<WorkGridRendererMode> _selectionMode;
        private readonly IRenderDiagnosticsSink _diagnostics;
        private IWorkGridRenderer _active;
        private string _activeId;

        public WorkGridRendererFacade(
            IWorkGridDrawingSurface drawingSurface,
            Func<WorkGridRendererMode> selectionMode,
            IWorkGridRendererCapabilityCheck capabilityCheck = null,
            IRenderDiagnosticsSink diagnostics = null)
        {
            if (drawingSurface == null) throw new ArgumentNullException(nameof(drawingSurface));

            _drawingSurface = drawingSurface;
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
            HeaderDrawingCoordinator.PrepareFrame(invalidationVersions);
        }

        public void Render(in WorkTabView context)
        {
            WorkGridRendererMode userMode = _selectionMode();
            WorkGridForcedRendererMode forcedMode = WorkGridRendererDiagnostics.ForcedMode;
            bool nativeOnly = PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority;

            if (nativeOnly)
            {
                // External priority authority is a correctness boundary for
                // the optimized snapshot renderer because it has no safe
                // content revision. Enforce it immediately, including input
                // that arrives before the next Layout selection pass.
                SwitchToRenderer(_vanilla, context);
                _activeId = VanillaWorkGridRenderer.RendererId;
                PublishSelection(
                    userMode,
                    forcedMode,
                    new WorkGridFallbackReason(
                        WorkGridFallbackReasonCode.CapabilityUnavailable,
                        "External priority authority requires the native renderer."),
                    context.Scope);
            }
            else if (context.EventPhase == ImGuiEventPhase.Layout)
            {
                WorkGridRendererSelection selection =
                    _selector.Select(_vanilla, userMode, forcedMode, in context);
                SwitchToRenderer(selection.Renderer, context);
                _activeId = selection.RendererId;
                PublishSelection(userMode, forcedMode, selection.Fallback, context.Scope);
            }

            IWorkGridRenderer renderer = _active;
            bool headersDrawn = false;
            try
            {
                renderer.Prepare(in context);
                if ((context.Configuration.Layers & WorkGridLayerFlags.Headers) != 0)
                {
                    _drawingSurface.DrawHeaders(in context);
                    headersDrawn = true;
                }
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
                ReleaseRenderer(renderer, in context);
                _selector.Quarantine(context.Scope, failedRendererId);
                _active = _vanilla;
                _activeId = VanillaWorkGridRenderer.RendererId;
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

                // Complete the current event with the native renderer so a
                // failure does not leave a half-rendered frame until Layout.
                _vanilla.Prepare(in context);
                if ((context.Configuration.Layers & WorkGridLayerFlags.Headers) != 0 &&
                    !headersDrawn)
                {
                    _drawingSurface.DrawHeaders(in context);
                }
                _vanilla.Draw(in context);
                if (context.EventPhase == ImGuiEventPhase.Input)
                {
                    _vanilla.HandleEvent(in context);
                }
                if (context.EventPhase == ImGuiEventPhase.Repaint)
                {
                    _vanilla.ReleaseTransient(in context);
                }
            }
        }

        private void SwitchToRenderer(
            IWorkGridRenderer renderer,
            in WorkTabView context)
        {
            IWorkGridRenderer next = renderer ?? _vanilla;
            if (ReferenceEquals(_active, next))
            {
                return;
            }

            ReleaseRenderer(_active, in context);
            _active = next;
        }

        private static void ReleaseRenderer(
            IWorkGridRenderer renderer,
            in WorkTabView context)
        {
            if (renderer == null || renderer is VanillaWorkGridRenderer)
            {
                return;
            }

            try
            {
                renderer.ReleaseTransient(in context);
            }
            catch (Exception exception)
            {
                Log.Warning("[BWT] Work-grid renderer cleanup failed: " + exception.Message);
            }

            if (renderer is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    Log.Warning("[BWT] Work-grid renderer resource release failed: " + exception.Message);
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
