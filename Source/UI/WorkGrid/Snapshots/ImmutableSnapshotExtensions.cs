using System;
using System.Collections.Generic;
using Spine.Collections;

namespace Better_Work_Tab.UI.WorkGrid.Snapshots
{
    /// <summary>
    /// BWT-owned sparse publication helper built only on Spine's stable snapshot API.
    /// </summary>
    internal static class ImmutableSnapshotExtensions
    {
        internal static ImmutableSnapshotArray<T> WithReplacements<T>(
            this ImmutableSnapshotArray<T> source,
            IReadOnlyDictionary<int, T> replacements)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (replacements == null || replacements.Count == 0)
            {
                return source;
            }

            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            foreach (KeyValuePair<int, T> replacement in replacements)
            {
                if ((uint)replacement.Key >= (uint)copy.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(replacements));
                }

                copy[replacement.Key] = replacement.Value;
            }

            return ImmutableSnapshotArray<T>.CopyOf(copy);
        }
    }
}
