using System;
using System.Collections.Generic;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Spine.Api;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    public enum WorkGridForcedRendererMode
    {
        None,
        ForceVanilla,
        ForceOptimized,
        ForceOptimizedUnavailable
    }

    public enum WorkGridFallbackReasonCode
    {
        None,
        UserSelectedVanilla,
        ForcedVanilla,
        OptimizedUnavailable,
        ForcedOptimizedUnavailable,
        CapabilityUnavailable,
        RendererQuarantined
    }

    public readonly struct WorkGridFallbackReason
    {
        public WorkGridFallbackReason(WorkGridFallbackReasonCode code, string detail = null)
        {
            Code = code;
            Detail = detail;
        }

        public WorkGridFallbackReasonCode Code { get; }
        public string Detail { get; }
    }

    public interface IWorkGridRendererCapabilityCheck
    {
        bool IsSupported(IWorkGridRenderer renderer, in WorkGridRenderContext context, out string detail);
    }

    internal readonly struct WorkGridRendererSelection
    {
        internal WorkGridRendererSelection(
            IWorkGridRenderer renderer,
            string rendererId,
            WorkGridFallbackReason fallback)
        {
            Renderer = renderer;
            RendererId = rendererId;
            Fallback = fallback;
        }

        internal IWorkGridRenderer Renderer { get; }
        internal string RendererId { get; }
        internal WorkGridFallbackReason Fallback { get; }
    }

    /// <summary>
    /// Owns optimized-renderer registration, capability selection, and per-scope session quarantine.
    /// The permanent vanilla renderer is supplied by the facade and is never registered here.
    /// </summary>
    public sealed class WorkGridRendererSelector
    {
        private sealed class RendererEntry
        {
            internal IWorkGridRenderer Renderer;
            internal string Id;
            internal int Priority;
            internal long Sequence;
            internal RegistrationToken Token;
        }

        private sealed class RegistrationToken : IRegistrationToken
        {
            private WorkGridRendererSelector _owner;

            internal RegistrationToken(WorkGridRendererSelector owner, string id)
            {
                _owner = owner;
                Id = id;
            }

            public string Id { get; }
            public bool IsActive => _owner != null;

            public void Dispose()
            {
                WorkGridRendererSelector owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.Unregister(Id, this);
            }
        }

        private sealed class AllowAllCapabilityCheck : IWorkGridRendererCapabilityCheck
        {
            internal static readonly AllowAllCapabilityCheck Instance = new AllowAllCapabilityCheck();

            public bool IsSupported(
                IWorkGridRenderer renderer,
                in WorkGridRenderContext context,
                out string detail)
            {
                detail = null;
                return true;
            }
        }

        private readonly object _sync = new object();
        private readonly List<RendererEntry> _entries = new List<RendererEntry>();
        private readonly Dictionary<WorkGridSelectionScope, HashSet<string>> _quarantinedByScope =
            new Dictionary<WorkGridSelectionScope, HashSet<string>>();
        private readonly IWorkGridRendererCapabilityCheck _capabilityCheck;
        private readonly IRenderDiagnosticsSink _diagnostics;
        private volatile RendererEntry[] _snapshot = Array.Empty<RendererEntry>();
        private long _nextSequence;

        public WorkGridRendererSelector(
            IWorkGridRendererCapabilityCheck capabilityCheck = null,
            IRenderDiagnosticsSink diagnostics = null)
        {
            _capabilityCheck = capabilityCheck ?? AllowAllCapabilityCheck.Instance;
            _diagnostics = diagnostics;
        }

        public RegistrationResult Register(IWorkGridRenderer renderer)
        {
            if (renderer == null)
            {
                return RegistrationResult.Reject("The Work-grid renderer cannot be null.");
            }

            string id;
            int priority;
            try
            {
                id = renderer.Id;
                priority = renderer.Priority;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[BWT] Work-grid renderer registration failed while metadata was read.\n" + exception,
                    StringComparer.Ordinal.GetHashCode("work-grid:registration-metadata"));
                RecordRegistrationFailure(exception);
                return RegistrationResult.Reject("The Work-grid renderer threw while its metadata was read.");
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return RegistrationResult.Reject("The Work-grid renderer must declare a non-empty stable ID.");
            }

            if (string.Equals(id, VanillaWorkGridRenderer.RendererId, StringComparison.Ordinal))
            {
                return RegistrationResult.Reject("The permanent vanilla renderer ID is reserved.");
            }

            lock (_sync)
            {
                for (int index = 0; index < _entries.Count; index++)
                {
                    if (string.Equals(_entries[index].Id, id, StringComparison.Ordinal))
                    {
                        return RegistrationResult.Reject("A Work-grid renderer with ID '" + id + "' is already registered.");
                    }
                }

                var token = new RegistrationToken(this, id);
                _entries.Add(new RendererEntry
                {
                    Renderer = renderer,
                    Id = id,
                    Priority = priority,
                    Sequence = _nextSequence++,
                    Token = token
                });
                PublishSnapshot();
                return RegistrationResult.Accept(token);
            }
        }

        internal WorkGridRendererSelection Select(
            IWorkGridRenderer vanilla,
            WorkGridRendererMode userMode,
            WorkGridForcedRendererMode forcedMode,
            in WorkGridRenderContext context)
        {
            if (forcedMode == WorkGridForcedRendererMode.ForceVanilla)
            {
                return Vanilla(vanilla, WorkGridFallbackReasonCode.ForcedVanilla);
            }

            if (userMode == WorkGridRendererMode.Vanilla &&
                forcedMode == WorkGridForcedRendererMode.None)
            {
                return Vanilla(vanilla, WorkGridFallbackReasonCode.UserSelectedVanilla);
            }

            if (forcedMode == WorkGridForcedRendererMode.ForceOptimizedUnavailable)
            {
                return Vanilla(vanilla, WorkGridFallbackReasonCode.ForcedOptimizedUnavailable,
                    "Diagnostic mode intentionally made optimized rendering unavailable.");
            }

            RendererEntry[] candidates = _snapshot;
            WorkGridFallbackReason lastFallback = new WorkGridFallbackReason(
                WorkGridFallbackReasonCode.OptimizedUnavailable,
                "No optimized Work-grid renderer is registered.");

            for (int index = 0; index < candidates.Length; index++)
            {
                RendererEntry candidate = candidates[index];
                if (IsQuarantined(context.Scope, candidate.Id))
                {
                    lastFallback = new WorkGridFallbackReason(
                        WorkGridFallbackReasonCode.RendererQuarantined,
                        "Renderer '" + candidate.Id + "' is quarantined for " + context.Scope + ".");
                    continue;
                }

                string capabilityDetail;
                bool capabilitySupported;
                try
                {
                    capabilitySupported = _capabilityCheck.IsSupported(
                        candidate.Renderer,
                        in context,
                        out capabilityDetail);
                }
                catch (Exception exception)
                {
                    LogRendererFailureOnce(candidate.Id, context.Scope, "capability check", exception);
                    lastFallback = new WorkGridFallbackReason(
                        WorkGridFallbackReasonCode.CapabilityUnavailable,
                        "The capability check for renderer '" + candidate.Id + "' failed.");
                    continue;
                }

                if (!capabilitySupported)
                {
                    lastFallback = new WorkGridFallbackReason(
                        WorkGridFallbackReasonCode.CapabilityUnavailable,
                        capabilityDetail ?? "A required optimized-rendering capability is unavailable.");
                    continue;
                }

                try
                {
                    if (candidate.Renderer.IsAvailable(in context))
                    {
                        return new WorkGridRendererSelection(candidate.Renderer, candidate.Id, default);
                    }

                    lastFallback = new WorkGridFallbackReason(
                        WorkGridFallbackReasonCode.OptimizedUnavailable,
                        "Renderer '" + candidate.Id + "' reported unavailable.");
                }
                catch (Exception exception)
                {
                    Quarantine(context.Scope, candidate.Id);
                    LogRendererFailureOnce(candidate.Id, context.Scope, "availability check", exception);
                    RecordRendererFailure(candidate.Id, "availability", exception);
                    lastFallback = new WorkGridFallbackReason(
                        WorkGridFallbackReasonCode.RendererQuarantined,
                        "Renderer '" + candidate.Id + "' threw during its availability check.");
                }
            }

            return new WorkGridRendererSelection(vanilla, VanillaWorkGridRenderer.RendererId, lastFallback);
        }

        internal void Quarantine(WorkGridSelectionScope scope, string rendererId)
        {
            lock (_sync)
            {
                if (!_quarantinedByScope.TryGetValue(scope, out HashSet<string> rendererIds))
                {
                    rendererIds = new HashSet<string>(StringComparer.Ordinal);
                    _quarantinedByScope.Add(scope, rendererIds);
                }

                rendererIds.Add(rendererId);
            }
        }

        internal bool IsQuarantined(WorkGridSelectionScope scope, string rendererId)
        {
            lock (_sync)
            {
                return _quarantinedByScope.TryGetValue(scope, out HashSet<string> rendererIds) &&
                       rendererIds.Contains(rendererId);
            }
        }

        private static WorkGridRendererSelection Vanilla(
            IWorkGridRenderer vanilla,
            WorkGridFallbackReasonCode reason,
            string detail = null)
        {
            return new WorkGridRendererSelection(
                vanilla,
                VanillaWorkGridRenderer.RendererId,
                new WorkGridFallbackReason(reason, detail));
        }

        private void Unregister(string id, RegistrationToken token)
        {
            lock (_sync)
            {
                for (int index = 0; index < _entries.Count; index++)
                {
                    RendererEntry entry = _entries[index];
                    if (!string.Equals(entry.Id, id, StringComparison.Ordinal) || !ReferenceEquals(entry.Token, token))
                    {
                        continue;
                    }

                    _entries.RemoveAt(index);
                    PublishSnapshot();
                    return;
                }
            }
        }

        private void PublishSnapshot()
        {
            _entries.Sort((left, right) =>
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });
            _snapshot = _entries.ToArray();
        }

        private void RecordRegistrationFailure(Exception exception)
        {
            if (_diagnostics?.Enabled == true)
            {
                _diagnostics.Record(new RenderDiagnostic(
                    RenderDiagnosticSeverity.Error,
                    "work-grid-selector",
                    "Renderer registration failed while reading metadata.",
                    exception));
            }
        }

        private void RecordRendererFailure(string rendererId, string operation, Exception exception)
        {
            if (_diagnostics?.Enabled == true)
            {
                _diagnostics.Record(new RenderDiagnostic(
                    RenderDiagnosticSeverity.Error,
                    rendererId,
                    "Renderer failed during " + operation + " and was quarantined.",
                    exception));
            }
        }

        private static void LogRendererFailureOnce(
            string rendererId,
            WorkGridSelectionScope scope,
            string operation,
            Exception exception)
        {
            Log.ErrorOnce(
                "[BWT] Work-grid renderer '" + rendererId + "' failed during its " + operation +
                " for scope " + scope + ". Optimized selection fell back safely.\n" + exception,
                StringComparer.Ordinal.GetHashCode("work-grid:" + scope + ":" + rendererId + ":" + operation));
        }
    }
}
