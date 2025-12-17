using System.Collections.Generic;
using UnityEngine;

namespace Spine.DragDropApi.Util
{
    /// <summary>
    /// Tracks a single click that may turn into a drag. Use Begin on mouse down,
    /// RegisterDrag on movement, and TryComplete on mouse up to decide whether
    /// to treat the interaction as a click. Call MarkDragStarted/ClearIfTracking
    /// when an actual drag begins or is cancelled.
    /// </summary>
    public sealed class ClickOrDragGate<T>
    {
        private readonly IEqualityComparer<T> _comparer;
        private T _key;
        private int _mouseButton = -1;
        private Vector2 _startPos;
        private bool _dragged;

        public ClickOrDragGate()
        {
            _comparer = EqualityComparer<T>.Default;
        }

        public void Begin(T key, int button, Vector2 startPos)
        {
            _key = key;
            _mouseButton = button;
            _startPos = startPos;
            _dragged = false;
        }

        public bool RegisterDrag(T key, Vector2 currentPos, float threshold)
        {
            if (!IsTracking(key) || _dragged)
            {
                return false;
            }

            float sqThreshold = threshold * threshold;
            if ((currentPos - _startPos).sqrMagnitude >= sqThreshold)
            {
                _dragged = true;
                return true;
            }

            return false;
        }

        public bool TryComplete(T key, int button, bool isMouseOver)
        {
            if (!IsTracking(key))
            {
                return false;
            }

            bool shouldClick = !_dragged && button == _mouseButton && isMouseOver;
            Clear();
            return shouldClick;
        }

        public void MarkDragStarted(T key)
        {
            if (IsTracking(key))
            {
                _dragged = true;
            }
        }

        public void ClearIfTracking(T key)
        {
            if (IsTracking(key))
            {
                Clear();
            }
        }

        private bool IsTracking(T key)
        {
            return _mouseButton != -1 && _comparer.Equals(_key, key);
        }

        private void Clear()
        {
            _key = default;
            _mouseButton = -1;
            _startPos = default;
            _dragged = false;
        }
    }
}
