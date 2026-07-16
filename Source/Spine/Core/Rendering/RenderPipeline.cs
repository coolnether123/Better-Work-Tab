using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Spine.Diagnostics;

namespace Spine.Api
{
    public enum RenderPhase
    {
        Background = 0,
        BaseContent = 100,
        Decoration = 200,
        Interaction = 300,
        Overlay = 400,
        Animation = 500
    }

    public interface IRegistrationToken : IDisposable
    {
        string Id { get; }
        bool IsActive { get; }
    }

    public sealed class RegistrationResult
    {
        private RegistrationResult(bool accepted, IRegistrationToken token, string rejectionReason)
        {
            Accepted = accepted;
            Token = token;
            RejectionReason = rejectionReason;
        }

        public bool Accepted { get; }
        public IRegistrationToken Token { get; }
        public string RejectionReason { get; }

        public static RegistrationResult Accept(IRegistrationToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            return new RegistrationResult(true, token, null);
        }

        public static RegistrationResult Reject(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejection reason is required.", nameof(reason));
            return new RegistrationResult(false, null, reason);
        }
    }

    public interface IRenderLayer<in TContext>
    {
        string Id { get; }
        RenderPhase Phase { get; }
        int Priority { get; }
        void Render(TContext context);
    }

    public interface IRenderPipeline<TContext> : IDisposable
    {
        IReadOnlyList<IRenderLayer<TContext>> ActiveLayers { get; }
        RegistrationResult Register(IRenderLayer<TContext> layer);
        void Render(TContext context);
        void Reset();
    }
}

namespace Spine.Rendering
{
    using Spine.Api;

    /// <summary>
    /// Stable pipeline ordered by phase, descending priority, then registration order.
    /// A throwing layer is disabled until reset or re-registration.
    /// </summary>
    public sealed class RenderPipeline<TContext> : IRenderPipeline<TContext>
    {
        private sealed class LayerEntry
        {
            public IRenderLayer<TContext> Layer;
            public long Sequence;
            public RegistrationToken Token;
        }

        private sealed class RegistrationToken : IRegistrationToken
        {
            private RenderPipeline<TContext> _owner;

            public RegistrationToken(RenderPipeline<TContext> owner, string id)
            {
                _owner = owner;
                Id = id;
            }

            public string Id { get; }
            public bool IsActive => _owner != null;

            public void Dispose()
            {
                RenderPipeline<TContext> owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.Remove(Id, this);
            }

            public void Deactivate()
            {
                _owner = null;
            }
        }

        private readonly Dictionary<string, LayerEntry> _byId =
            new Dictionary<string, LayerEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> _disabledIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<LayerEntry> _ordered = new List<LayerEntry>();
        private readonly List<IRenderLayer<TContext>> _activeLayers = new List<IRenderLayer<TContext>>();
        private readonly ReadOnlyCollection<IRenderLayer<TContext>> _readOnlyActiveLayers;
        private readonly IRenderDiagnosticsSink _diagnostics;
        private long _nextSequence;

        public RenderPipeline(IRenderDiagnosticsSink diagnostics = null)
        {
            _diagnostics = diagnostics ?? NullRenderDiagnosticsSink.Instance;
            _readOnlyActiveLayers = _activeLayers.AsReadOnly();
        }

        public IReadOnlyList<IRenderLayer<TContext>> ActiveLayers => _readOnlyActiveLayers;

        public RegistrationResult Register(IRenderLayer<TContext> layer)
        {
            if (layer == null)
            {
                return RegistrationResult.Reject("The render layer cannot be null.");
            }

            string id = layer.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                return RegistrationResult.Reject("The render layer must declare a non-empty stable ID.");
            }

            if (_byId.ContainsKey(id))
            {
                return RegistrationResult.Reject("A render layer with ID '" + id + "' is already registered. Dispose its registration token before registering a replacement.");
            }

            if (_disabledIds.Contains(id))
            {
                return RegistrationResult.Reject("Render layer ID '" + id + "' was disabled after an exception and remains quarantined until the pipeline is reset.");
            }

            var token = new RegistrationToken(this, id);
            var entry = new LayerEntry { Layer = layer, Sequence = _nextSequence++, Token = token };
            _byId.Add(id, entry);
            _ordered.Add(entry);
            SortAndPublish();
            return RegistrationResult.Accept(token);
        }

        public void Render(TContext context)
        {
            for (int index = 0; index < _ordered.Count; index++)
            {
                LayerEntry entry = _ordered[index];
                try
                {
                    entry.Layer.Render(context);
                }
                catch (Exception exception)
                {
                    Disable(entry, exception);
                    index--;
                }
            }
        }

        public void Reset()
        {
            foreach (LayerEntry entry in _ordered)
            {
                entry.Token.Deactivate();
            }

            _byId.Clear();
            _disabledIds.Clear();
            _ordered.Clear();
            _activeLayers.Clear();
            _nextSequence = 0;
        }

        public void Dispose()
        {
            Reset();
        }

        private void Disable(LayerEntry entry, Exception exception)
        {
            _byId.Remove(entry.Layer.Id);
            _disabledIds.Add(entry.Layer.Id);
            _ordered.Remove(entry);
            _activeLayers.Remove(entry.Layer);
            entry.Token.Deactivate();

            if (_diagnostics.Enabled)
            {
                _diagnostics.Record(new RenderDiagnostic(
                    RenderDiagnosticSeverity.Error,
                    entry.Layer.Id,
                    "Render layer threw an exception and was disabled for the session.",
                    exception));
            }
        }

        private void Remove(string id, RegistrationToken token)
        {
            if (!_byId.TryGetValue(id, out LayerEntry entry) || !ReferenceEquals(entry.Token, token))
            {
                return;
            }

            _byId.Remove(id);
            _ordered.Remove(entry);
            _activeLayers.Remove(entry.Layer);
        }

        private void SortAndPublish()
        {
            _ordered.Sort((left, right) =>
            {
                int phase = left.Layer.Phase.CompareTo(right.Layer.Phase);
                if (phase != 0) return phase;
                int priority = right.Layer.Priority.CompareTo(left.Layer.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });

            _activeLayers.Clear();
            foreach (LayerEntry entry in _ordered)
            {
                _activeLayers.Add(entry.Layer);
            }
        }
    }
}
