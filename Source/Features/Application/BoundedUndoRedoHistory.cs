using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Application
{
    /// <summary>
    /// Owns one bounded sequence of reversible transitions. Callers keep the
    /// transition payload domain-specific while sharing cursor semantics.
    /// </summary>
    internal sealed class BoundedUndoRedoHistory<T> where T : class
    {
        private readonly int _capacity;
        private readonly List<T> _undo = new List<T>();
        private readonly List<T> _redo = new List<T>();

        internal BoundedUndoRedoHistory(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        internal T PeekUndo => _undo.Count == 0 ? null : _undo[_undo.Count - 1];
        internal T PeekRedo => _redo.Count == 0 ? null : _redo[_redo.Count - 1];

        internal void Record(T transition)
        {
            if (transition == null) return;
            _undo.Add(transition);
            if (_undo.Count > _capacity) _undo.RemoveAt(0);
            _redo.Clear();
        }

        internal bool CompleteUndo(Predicate<T> matches) =>
            Move(_undo, _redo, matches);

        internal bool CompleteRedo(Predicate<T> matches) =>
            Move(_redo, _undo, matches);

        internal void Capture(out List<T> undo, out List<T> redo)
        {
            undo = new List<T>(_undo);
            redo = new List<T>(_redo);
        }

        internal void Restore(IEnumerable<T> undo, IEnumerable<T> redo)
        {
            _undo.Clear();
            _redo.Clear();
            AddBounded(_undo, undo);
            AddBounded(_redo, redo);
        }

        internal void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private bool Move(List<T> source, List<T> target, Predicate<T> matches)
        {
            if (source.Count == 0 || matches == null) return false;
            T transition = source[source.Count - 1];
            if (!matches(transition)) return false;
            source.RemoveAt(source.Count - 1);
            target.Add(transition);
            if (target.Count > _capacity) target.RemoveAt(0);
            return true;
        }

        private void AddBounded(List<T> target, IEnumerable<T> values)
        {
            if (values == null) return;
            foreach (T value in values)
            {
                if (value == null) continue;
                target.Add(value);
                if (target.Count > _capacity) target.RemoveAt(0);
            }
        }
    }
}
