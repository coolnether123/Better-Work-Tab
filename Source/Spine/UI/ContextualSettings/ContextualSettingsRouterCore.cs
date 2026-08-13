using System;
using System.Collections.Generic;

namespace Spine.UI.ContextualSettings
{
    internal static class ContextualTooltipComposition
    {
        internal static string FeatureOnly(string featureTooltip)
        {
            return string.IsNullOrWhiteSpace(featureTooltip)
                ? null
                : featureTooltip.TrimEnd();
        }
    }

    internal enum ContextualPointerEventType
    {
        None,
        MouseDown,
        MouseMove,
        Repaint
    }

    internal readonly struct ContextualHitRect : IEquatable<ContextualHitRect>
    {
        internal ContextualHitRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        internal float X { get; }
        internal float Y { get; }
        internal float Width { get; }
        internal float Height { get; }

        internal bool Contains(float x, float y) =>
            x >= X && y >= Y && x < X + Width && y < Y + Height;

        public bool Equals(ContextualHitRect other) =>
            X.Equals(other.X) && Y.Equals(other.Y) &&
            Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object obj) =>
            obj is ContextualHitRect other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                return (hash * 397) ^ Height.GetHashCode();
            }
        }
    }

    internal readonly struct ContextualPointerEvent
    {
        internal ContextualPointerEvent(
            ContextualPointerEventType type,
            int button,
            bool alt,
            float x,
            float y)
        {
            Type = type;
            Button = button;
            Alt = alt;
            X = x;
            Y = y;
        }

        internal ContextualPointerEventType Type { get; }
        internal int Button { get; }
        internal bool Alt { get; }
        internal float X { get; }
        internal float Y { get; }

        internal bool IsContextualClick =>
            Type == ContextualPointerEventType.MouseDown && Button == 0 && Alt;
    }

    internal sealed class ContextualBindingRecord
    {
        internal string ConsumerId;
        internal ContextualHitRect Rect;
        internal ContextualSettingsTarget Target;
        internal int Priority;
        internal long RegistrationOrder;
        internal long Frame;
    }

    internal sealed class ContextualSettingsRouterCore
    {
        private readonly List<ContextualBindingRecord> registrations =
            new List<ContextualBindingRecord>();
        private readonly HashSet<string> consumers =
            new HashSet<string>(StringComparer.Ordinal);
        private long nextRegistrationOrder;

        internal int ConsumerCount => consumers.Count;
        internal int RegistrationCount => registrations.Count;

        internal void Acquire(string consumerId)
        {
            if (!string.IsNullOrWhiteSpace(consumerId))
            {
                consumers.Add(consumerId);
            }
        }

        internal void Release(string consumerId)
        {
            consumers.Remove(consumerId);
            registrations.RemoveAll(record =>
                string.Equals(record.ConsumerId, consumerId, StringComparison.Ordinal));
        }

        internal bool Register(
            string consumerId,
            ContextualHitRect rect,
            ContextualSettingsTarget target,
            int priority,
            long frame)
        {
            if (!consumers.Contains(consumerId) || rect.Width <= 0f || rect.Height <= 0f)
            {
                return false;
            }

            registrations.RemoveAll(record => record.Frame < frame - 1);
            for (int i = 0; i < registrations.Count; i++)
            {
                ContextualBindingRecord existing = registrations[i];
                if (existing.Frame == frame &&
                    existing.ConsumerId == consumerId &&
                    existing.Rect.Equals(rect) &&
                    TargetsEqual(existing.Target, target) &&
                    existing.Priority == priority)
                {
                    return false;
                }
            }

            registrations.Add(new ContextualBindingRecord
            {
                ConsumerId = consumerId,
                Rect = rect,
                Target = target,
                Priority = priority,
                RegistrationOrder = ++nextRegistrationOrder,
                Frame = frame
            });
            return true;
        }

        internal bool TryRoute(
            ContextualPointerEvent pointerEvent,
            long frame,
            out ContextualBindingRecord winner)
        {
            winner = null;
            if (!pointerEvent.IsContextualClick)
            {
                return false;
            }

            registrations.RemoveAll(record => record.Frame < frame - 1);
            for (int i = 0; i < registrations.Count; i++)
            {
                ContextualBindingRecord candidate = registrations[i];
                if (!consumers.Contains(candidate.ConsumerId) ||
                    !candidate.Rect.Contains(pointerEvent.X, pointerEvent.Y) ||
                    candidate.Frame > frame)
                {
                    continue;
                }

                if (winner == null || Compare(candidate, winner) > 0)
                {
                    winner = candidate;
                }
            }

            return winner != null;
        }

        private static int Compare(
            ContextualBindingRecord left,
            ContextualBindingRecord right)
        {
            int result = left.Target.Level.CompareTo(right.Target.Level);
            if (result != 0)
            {
                return result;
            }

            result = left.Priority.CompareTo(right.Priority);
            return result != 0
                ? result
                : left.RegistrationOrder.CompareTo(right.RegistrationOrder);
        }

        private static bool TargetsEqual(
            ContextualSettingsTarget left,
            ContextualSettingsTarget right) =>
            left.Level == right.Level &&
            string.Equals(left.SettingId, right.SettingId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.FallbackGroupId, right.FallbackGroupId, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class DeferredContextualActionQueue
    {
        private Action pending;

        internal bool HasPending => pending != null;

        internal bool Enqueue(Action action)
        {
            if (action == null || pending != null)
            {
                return false;
            }

            pending = action;
            return true;
        }

        internal bool Drain(Action<Exception> onFailure = null)
        {
            Action action = pending;
            pending = null;
            if (action == null)
            {
                return false;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                onFailure?.Invoke(exception);
            }

            return true;
        }

        internal void Clear() => pending = null;
    }

    internal readonly struct ContextualNavigationCandidate
    {
        internal ContextualNavigationCandidate(
            string id,
            bool available,
            bool visibleInSimple)
        {
            Id = id;
            Available = available;
            VisibleInSimple = visibleInSimple;
        }

        internal string Id { get; }
        internal bool Available { get; }
        internal bool VisibleInSimple { get; }
    }

    internal readonly struct ContextualNavigationPlan
    {
        internal ContextualNavigationPlan(
            string targetId,
            bool useSimpleView,
            bool includeChildren)
        {
            TargetId = targetId;
            UseSimpleView = useSimpleView;
            IncludeChildren = includeChildren;
        }

        internal string TargetId { get; }
        internal bool UseSimpleView { get; }
        internal bool IncludeChildren { get; }
        internal bool IsRoot => string.IsNullOrEmpty(TargetId);
    }

    internal static class ContextualNavigationResolver
    {
        internal static ContextualNavigationPlan Resolve(
            ContextualSettingsTarget requested,
            Func<string, ContextualNavigationCandidate> lookup)
        {
            if (requested.Level == ContextualSettingsTargetLevel.Root || lookup == null)
            {
                return default(ContextualNavigationPlan);
            }

            ContextualNavigationCandidate candidate = lookup(requested.SettingId);
            bool fellBack = false;
            if (!candidate.Available &&
                requested.Level == ContextualSettingsTargetLevel.Exact &&
                !string.IsNullOrEmpty(requested.FallbackGroupId))
            {
                candidate = lookup(requested.FallbackGroupId);
                fellBack = true;
            }

            return candidate.Available
                ? new ContextualNavigationPlan(
                    candidate.Id,
                    candidate.VisibleInSimple,
                    requested.Level == ContextualSettingsTargetLevel.Group || fellBack)
                : default(ContextualNavigationPlan);
        }
    }

    internal static class ContextualPresentationMath
    {
        internal static float CenteredScroll(
            float targetY,
            float viewportHeight,
            float focusedRowHeight,
            float contentHeight)
        {
            float maximum = Math.Max(0f, contentHeight - viewportHeight);
            float centered = targetY - ((viewportHeight - focusedRowHeight) * 0.5f);
            return Math.Max(0f, Math.Min(centered, maximum));
        }

        internal static bool IsHighlightActive(
            float now,
            float startedAt,
            float lifetime) =>
            lifetime > 0f && now - startedAt <= lifetime;
    }
}
