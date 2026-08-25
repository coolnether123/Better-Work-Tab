using System;
using System.Collections.Generic;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Retains stable work-box pixels per visible pawn row. Surfaces are composed
    /// offscreen, then presented through IMGUI while the owning scroll view's clip
    /// is active.
    /// </summary>
    internal sealed class RetainedWorkBoxRowCache : IDisposable
    {
        private const int MaximumEntries = 128;
        private readonly Dictionary<RowKey, Entry> _entries =
            new Dictionary<RowKey, Entry>(64);
        private bool _disabled;

        internal readonly struct Cell
        {
            internal Cell(
                int pawnId,
                int columnIndex,
                Rect boxRect,
                WorkBoxVisualState visual,
                int displayPriority,
                bool compactText)
            {
                PawnId = pawnId;
                ColumnIndex = columnIndex;
                BoxRect = boxRect;
                Visual = visual;
                DisplayPriority = displayPriority;
                CompactText = compactText;
            }

            internal int PawnId { get; }
            internal int ColumnIndex { get; }
            internal Rect BoxRect { get; }
            internal WorkBoxVisualState Visual { get; }
            internal int DisplayPriority { get; }
            internal bool CompactText { get; }
        }

        internal bool TryDraw(
            List<Cell> cells,
            Color baseColor,
            WorkGridSnapshot snapshot)
        {
            if (_disabled || snapshot == null || cells == null || cells.Count == 0)
            {
                return false;
            }

            try
            {
                Cell first = cells[0];
                Cell last = cells[cells.Count - 1];
                var key = new RowKey(
                    first.PawnId,
                    first.ColumnIndex,
                    last.ColumnIndex,
                    cells.Count);
                Rect bounds = GetBounds(cells);
                float pixelScale = Verse.UI.screenWidth > 0
                    ? Mathf.Max(1f, (float)Screen.width / Verse.UI.screenWidth)
                    : 1f;
                int pixelWidth = Mathf.Max(1, Mathf.CeilToInt(bounds.width * pixelScale));
                int pixelHeight = Mathf.Max(1, Mathf.CeilToInt(bounds.height * pixelScale));
                ulong fingerprint = GetFingerprint(cells, baseColor, snapshot, pixelWidth, pixelHeight);

                if (!_entries.TryGetValue(key, out Entry entry))
                {
                    entry = new Entry();
                    _entries.Add(key, entry);
                }

                entry.LastUsedFrame = Time.frameCount;
                if (entry.Surface == null ||
                    !entry.Surface.IsCreated() ||
                    entry.PixelWidth != pixelWidth ||
                    entry.PixelHeight != pixelHeight)
                {
                    ReleaseSurface(entry);
                    entry.Surface = CreateSurface(pixelWidth, pixelHeight);
                    entry.PixelWidth = pixelWidth;
                    entry.PixelHeight = pixelHeight;
                    entry.Fingerprint = 0UL;
                }

                if (entry.Surface == null)
                {
                    return false;
                }

                if (entry.Fingerprint != fingerprint || entry.Bounds != bounds)
                {
                    if (!BuildSurface(entry.Surface, bounds, cells, baseColor))
                    {
                        DisableAfterFailure("retained row composition failed");
                        return false;
                    }
                    entry.Fingerprint = fingerprint;
                    entry.Bounds = bounds;
                }

                // The surface was composed through a top-left pixel matrix,
                // and IMGUI already presents RenderTextures in that logical
                // orientation. Applying graphicsUVStartsAtTop here flips the
                // completed row a second time on Direct3D.
                GUI.DrawTextureWithTexCoords(
                    bounds,
                    entry.Surface,
                    new Rect(0f, 0f, 1f, 1f),
                    true);
                TrimEntries();
                return true;
            }
            catch (Exception exception)
            {
                DisableAfterFailure("retained row exception: " + exception);
                return false;
            }
        }

        public void Dispose()
        {
            foreach (Entry entry in _entries.Values)
            {
                ReleaseSurface(entry);
            }
            _entries.Clear();
        }

        private static Rect GetBounds(List<Cell> cells)
        {
            Rect bounds = cells[0].BoxRect;
            for (int index = 1; index < cells.Count; index++)
            {
                Rect rect = cells[index].BoxRect;
                bounds.xMin = Mathf.Min(bounds.xMin, rect.xMin);
                bounds.yMin = Mathf.Min(bounds.yMin, rect.yMin);
                bounds.xMax = Mathf.Max(bounds.xMax, rect.xMax);
                bounds.yMax = Mathf.Max(bounds.yMax, rect.yMax);
            }
            return bounds;
        }

        private static RenderTexture CreateSurface(int width, int height)
        {
            if (width > SystemInfo.maxTextureSize ||
                height > SystemInfo.maxTextureSize)
            {
                return null;
            }

            var surface = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "BWT retained work row",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!surface.Create())
            {
                UnityEngine.Object.Destroy(surface);
                return null;
            }
            return surface;
        }

        private static bool BuildSurface(
            RenderTexture surface,
            Rect bounds,
            List<Cell> cells,
            Color baseColor)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = surface;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0f, bounds.width, bounds.height, 0f);
                    GL.Clear(true, true, Color.clear);
                    for (int index = 0; index < cells.Count; index++)
                    {
                        Cell cell = cells[index];
                        Rect localRect = cell.BoxRect;
                        localRect.x -= bounds.x;
                        localRect.y -= bounds.y;
                        Text.Font = cell.CompactText ? GameFont.Tiny : GameFont.Medium;
                        if (!PreparedWorkBoxRenderer.DrawRetained(
                                localRect,
                                cell.Visual,
                                cell.DisplayPriority,
                                baseColor))
                        {
                            return false;
                        }
                    }
                }
                finally
                {
                    GL.PopMatrix();
                }
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static ulong GetFingerprint(
            List<Cell> cells,
            Color baseColor,
            WorkGridSnapshot snapshot,
            int pixelWidth,
            int pixelHeight)
        {
            ulong hash = 1469598103934665603UL;
            Mix(ref hash, snapshot.LayoutRevision);
            Mix(ref hash, snapshot.UiScaleRevision);
            Mix(ref hash, snapshot.FontThemeRevision);
            Mix(ref hash, snapshot.PriorityRangeRevision);
            GUIStyle fontStyle = Text.CurFontStyle;
            Mix(ref hash, fontStyle?.font != null ? fontStyle.font.GetInstanceID() : 0);
            Mix(ref hash, fontStyle?.fontSize ?? 0);
            Mix(ref hash, (int)(fontStyle?.fontStyle ?? FontStyle.Normal));
            Mix(ref hash, pixelWidth);
            Mix(ref hash, pixelHeight);
            Mix(ref hash, baseColor.GetHashCode());
            for (int index = 0; index < cells.Count; index++)
            {
                Cell cell = cells[index];
                WorkBoxVisualState visual = cell.Visual;
                Mix(ref hash, cell.PawnId);
                Mix(ref hash, cell.ColumnIndex);
                Mix(ref hash, cell.BoxRect.GetHashCode());
                Mix(ref hash, visual.Priority);
                Mix(ref hash, visual.SkillBand);
                Mix(ref hash, visual.SkillBlend.GetHashCode());
                Mix(ref hash, visual.Passion);
                Mix(ref hash, unchecked((int)visual.PriorityColor));
                Mix(ref hash, (int)(visual.Flags &
                    ~(WorkCellVisualFlags.BestPawn | WorkCellVisualFlags.OverrideRing)));
                Mix(ref hash, cell.DisplayPriority);
                Mix(ref hash, cell.CompactText ? 1 : 0);
            }
            return hash;
        }

        private static void Mix(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }

        private void TrimEntries()
        {
            if (_entries.Count <= MaximumEntries)
            {
                return;
            }

            RowKey oldestKey = default(RowKey);
            Entry oldest = null;
            foreach (KeyValuePair<RowKey, Entry> pair in _entries)
            {
                if (oldest == null || pair.Value.LastUsedFrame < oldest.LastUsedFrame)
                {
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                }
            }
            if (oldest != null)
            {
                ReleaseSurface(oldest);
                _entries.Remove(oldestKey);
            }
        }

        private void DisableAfterFailure(string message)
        {
            if (_disabled)
            {
                return;
            }

            _disabled = true;
            Dispose();
            Log.ErrorOnce("[Better Work Tab] " + message + "; using direct clipped rendering.", 1884630217);
        }

        private static void ReleaseSurface(Entry entry)
        {
            if (entry?.Surface == null)
            {
                return;
            }

            entry.Surface.Release();
            UnityEngine.Object.Destroy(entry.Surface);
            entry.Surface = null;
        }

        private readonly struct RowKey : IEquatable<RowKey>
        {
            private readonly int _pawnId;
            private readonly int _firstColumn;
            private readonly int _lastColumn;
            private readonly int _cellCount;

            internal RowKey(int pawnId, int firstColumn, int lastColumn, int cellCount)
            {
                _pawnId = pawnId;
                _firstColumn = firstColumn;
                _lastColumn = lastColumn;
                _cellCount = cellCount;
            }

            public bool Equals(RowKey other)
            {
                return _pawnId == other._pawnId &&
                       _firstColumn == other._firstColumn &&
                       _lastColumn == other._lastColumn &&
                       _cellCount == other._cellCount;
            }

            public override bool Equals(object obj)
            {
                return obj is RowKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _pawnId;
                    hash = (hash * 397) ^ _firstColumn;
                    hash = (hash * 397) ^ _lastColumn;
                    hash = (hash * 397) ^ _cellCount;
                    return hash;
                }
            }
        }

        private sealed class Entry
        {
            internal RenderTexture Surface;
            internal Rect Bounds;
            internal int PixelWidth;
            internal int PixelHeight;
            internal ulong Fingerprint;
            internal int LastUsedFrame;
        }
    }
}
