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
        internal enum CellKind : byte
        {
            WorkBox,
            PawnLabelText
        }
        private const int MaximumEntries = 128;
        private const long MaximumEstimatedSurfaceBytes = 64L * 1024L * 1024L;
        private readonly Dictionary<RowKey, Entry> _entries =
            new Dictionary<RowKey, Entry>(64);
        private bool _disabled;
        private long _estimatedSurfaceBytes;
        private long _accessSequence;
        private int _failedRenderResourcesRevision = int.MinValue;

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
                Kind = CellKind.WorkBox;
                PawnId = pawnId;
                ColumnIndex = columnIndex;
                BoxRect = boxRect;
                Visual = visual;
                DisplayPriority = displayPriority;
                CompactText = compactText;
                Text = string.Empty;
                TextColor = Color.white;
            }

            private Cell(
                int pawnId,
                int columnIndex,
                Rect textRect,
                string text,
                Color textColor)
            {
                Kind = CellKind.PawnLabelText;
                PawnId = pawnId;
                ColumnIndex = columnIndex;
                BoxRect = textRect;
                Visual = default;
                DisplayPriority = 0;
                CompactText = false;
                Text = text ?? string.Empty;
                TextColor = textColor;
            }

            internal static Cell PawnLabelText(
                int pawnId,
                int columnIndex,
                Rect textRect,
                string text,
                Color textColor)
            {
                return new Cell(pawnId, columnIndex, textRect, text, textColor);
            }

            internal CellKind Kind { get; }
            internal int PawnId { get; }
            internal int ColumnIndex { get; }
            internal Rect BoxRect { get; }
            internal WorkBoxVisualState Visual { get; }
            internal int DisplayPriority { get; }
            internal bool CompactText { get; }
            internal string Text { get; }
            internal Color TextColor { get; }
        }

        internal sealed class PreparedRun
        {
            internal PreparedRun(Cell[] cells)
            {
                Cells = cells ?? Array.Empty<Cell>();
                if (Cells.Length == 0)
                {
                    return;
                }

                Cell first = Cells[0];
                Cell last = Cells[Cells.Length - 1];
                Key = new RowKey(first.PawnId, first.ColumnIndex, last.ColumnIndex, Cells.Length);
                Bounds = GetBounds(Cells);
                StaticFingerprint = GetStaticFingerprint(Cells);
            }

            internal Cell[] Cells { get; }
            internal Rect Bounds { get; }
            internal ulong StaticFingerprint { get; }
            internal RowKey Key { get; }
        }

        internal long EstimatedSurfaceBytes => _estimatedSurfaceBytes;
        internal int EntryCount => _entries.Count;

        internal bool TryDraw(
            PreparedRun run,
            float rowOffsetY,
            Color baseColor,
            WorkGridSnapshot snapshot,
            int renderResourcesRevision)
        {
            if (_disabled || run == null || run.Cells.Length == 0 ||
                IsResourceFailureLatched(renderResourcesRevision))
            {
                return false;
            }

            try
            {
                Rect destination = run.Bounds;
                destination.y += rowOffsetY;
                return TryDrawCore(
                    run.Key,
                    run.Bounds,
                    destination,
                    run.Cells,
                    run.StaticFingerprint,
                    baseColor,
                    snapshot,
                    renderResourcesRevision);
            }
            catch (Exception exception)
            {
                DisableAfterFailure("retained row exception: " + exception);
                return false;
            }
        }

        internal bool TryDraw(
            List<Cell> cells,
            Color baseColor,
            WorkGridSnapshot snapshot,
            int renderResourcesRevision)
        {
            if (_disabled || snapshot == null || cells == null || cells.Count == 0 ||
                IsResourceFailureLatched(renderResourcesRevision))
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
                return TryDrawCore(
                    key,
                    bounds,
                    bounds,
                    cells,
                    GetStaticFingerprint(cells),
                    baseColor,
                    snapshot,
                    renderResourcesRevision);
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
            _estimatedSurfaceBytes = 0L;
        }

        private static Rect GetBounds(IReadOnlyList<Cell> cells)
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
            IReadOnlyList<Cell> cells,
            Color baseColor)
        {
            RenderTexture previous = RenderTexture.active;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                RenderTexture.active = surface;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0f, bounds.width, bounds.height, 0f);
                    GL.Clear(true, true, Color.clear);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.WordWrap = false;
                    for (int index = 0; index < cells.Count; index++)
                    {
                        Cell cell = cells[index];
                        Rect localRect = cell.BoxRect;
                        localRect.x -= bounds.x;
                        localRect.y -= bounds.y;
                        Text.Font = cell.CompactText ? GameFont.Tiny : GameFont.Medium;
                        bool drawn = cell.Kind == CellKind.PawnLabelText
                            ? PreparedWorkBoxRenderer.DrawRetainedText(
                                localRect,
                                cell.Text,
                                cell.TextColor,
                                GameFont.Small,
                                TextAnchor.MiddleLeft)
                            : PreparedWorkBoxRenderer.DrawRetained(
                                localRect,
                                cell.Visual,
                                cell.DisplayPriority,
                                baseColor);
                        if (!drawn)
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
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                RenderTexture.active = previous;
            }
        }

        private static ulong GetFingerprint(
            ulong staticFingerprint,
            Color baseColor,
            WorkGridSnapshot snapshot,
            int pixelWidth,
            int pixelHeight,
            int renderResourcesRevision)
        {
            ulong hash = 1469598103934665603UL;
            Mix(ref hash, snapshot.LayoutRevision);
            Mix(ref hash, snapshot.UiScaleRevision);
            Mix(ref hash, snapshot.FontThemeRevision);
            Mix(ref hash, snapshot.PriorityRangeRevision);
            Mix(ref hash, renderResourcesRevision);
            Mix(ref hash, pixelWidth);
            Mix(ref hash, pixelHeight);
            Mix(ref hash, baseColor.GetHashCode());
            Mix(ref hash, unchecked((int)staticFingerprint));
            Mix(ref hash, unchecked((int)(staticFingerprint >> 32)));
            return hash;
        }

        private static ulong GetStaticFingerprint(IReadOnlyList<Cell> cells)
        {
            ulong hash = 1469598103934665603UL;
            for (int index = 0; index < cells.Count; index++)
            {
                Cell cell = cells[index];
                WorkBoxVisualState visual = cell.Visual;
                Mix(ref hash, (int)cell.Kind);
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
                Mix(ref hash, StringComparer.Ordinal.GetHashCode(cell.Text ?? string.Empty));
                Mix(ref hash, cell.TextColor.GetHashCode());
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

        private bool TryDrawCore(
            RowKey key,
            Rect bounds,
            Rect destination,
            IReadOnlyList<Cell> cells,
            ulong staticFingerprint,
            Color baseColor,
            WorkGridSnapshot snapshot,
            int renderResourcesRevision)
        {
            if (_disabled || snapshot == null || cells == null || cells.Count == 0)
            {
                return false;
            }

            float pixelScale = Verse.UI.screenWidth > 0
                ? Mathf.Max(1f, (float)Screen.width / Verse.UI.screenWidth)
                : 1f;
            int pixelWidth = Mathf.Max(1, Mathf.CeilToInt(bounds.width * pixelScale));
            int pixelHeight = Mathf.Max(1, Mathf.CeilToInt(bounds.height * pixelScale));
            long requestedBytes = (long)pixelWidth * pixelHeight * 4L;
            if (requestedBytes > MaximumEstimatedSurfaceBytes)
            {
                return false;
            }

            ulong fingerprint = GetFingerprint(
                staticFingerprint,
                baseColor,
                snapshot,
                pixelWidth,
                pixelHeight,
                renderResourcesRevision);

            bool existing = _entries.TryGetValue(key, out Entry entry);
            if (!existing)
            {
                entry = new Entry();
            }

            entry.LastUsedSequence = ++_accessSequence;
            if (entry.Surface == null ||
                !entry.Surface.IsCreated() ||
                entry.PixelWidth != pixelWidth ||
                entry.PixelHeight != pixelHeight)
            {
                ReleaseSurface(entry);
                if (!EnsureCapacity(requestedBytes, existing ? 0 : 1, entry))
                {
                    return false;
                }

                entry.Surface = CreateSurface(pixelWidth, pixelHeight);
                if (entry.Surface == null)
                {
                    _failedRenderResourcesRevision = renderResourcesRevision;
                    return false;
                }

                entry.EstimatedBytes = requestedBytes;
                _estimatedSurfaceBytes += requestedBytes;
                entry.PixelWidth = pixelWidth;
                entry.PixelHeight = pixelHeight;
                entry.Fingerprint = 0UL;
                if (!existing)
                {
                    _entries.Add(key, entry);
                    existing = true;
                }
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

            // The surface was composed through a top-left pixel matrix, and
            // IMGUI presents it inside the owning scroll-view/group clip.
            GUI.DrawTextureWithTexCoords(
                destination,
                entry.Surface,
                new Rect(0f, 0f, 1f, 1f),
                true);
            return true;
        }

        private bool IsResourceFailureLatched(int renderResourcesRevision)
        {
            if (_failedRenderResourcesRevision == int.MinValue)
            {
                return false;
            }
            if (_failedRenderResourcesRevision != renderResourcesRevision)
            {
                _failedRenderResourcesRevision = int.MinValue;
                return false;
            }
            return true;
        }

        private bool EnsureCapacity(long requestedBytes, int entryDelta, Entry protectedEntry)
        {
            while (_entries.Count + entryDelta > MaximumEntries ||
                   _estimatedSurfaceBytes + requestedBytes > MaximumEstimatedSurfaceBytes)
            {
                if (!EvictOldest(protectedEntry))
                {
                    return false;
                }
            }
            return true;
        }

        private bool EvictOldest(Entry protectedEntry)
        {
            RowKey oldestKey = default(RowKey);
            Entry oldest = null;
            foreach (KeyValuePair<RowKey, Entry> pair in _entries)
            {
                if (ReferenceEquals(pair.Value, protectedEntry))
                {
                    continue;
                }
                if (oldest == null || pair.Value.LastUsedSequence < oldest.LastUsedSequence)
                {
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                }
            }
            if (oldest == null)
            {
                return false;
            }

            ReleaseSurface(oldest);
            _entries.Remove(oldestKey);
            return true;
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

        private void ReleaseSurface(Entry entry)
        {
            if (entry?.Surface == null)
            {
                return;
            }

            RenderTexture surface = entry.Surface;
            entry.Surface = null;
            _estimatedSurfaceBytes -= entry.EstimatedBytes;
            if (_estimatedSurfaceBytes < 0L)
            {
                _estimatedSurfaceBytes = 0L;
            }
            entry.EstimatedBytes = 0L;
            surface.Release();
            UnityEngine.Object.Destroy(surface);
        }

        internal readonly struct RowKey : IEquatable<RowKey>
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
            internal long LastUsedSequence;
            internal long EstimatedBytes;
        }
    }
}
