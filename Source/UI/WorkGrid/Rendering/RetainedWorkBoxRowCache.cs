using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Retains stable work-box presentation per visible pawn row. Surfaces are
    /// composed offscreen, then presented through IMGUI while
    /// the owning scroll view's clip is active. Callers own the non-empty
    /// prepared-cell invariant; false means a runtime resource/composition failure
    /// and activates the direct draw fallback. A destination may be above, within,
    /// or below the viewport; no local vertical culling is applied, so the owning
    /// IMGUI clip remains authoritative for top, middle, and bottom rows.
    /// </summary>
    internal sealed class RetainedWorkBoxRowCache : IDisposable
    {
        private const int MaximumEntries = 128;
        private const long MaximumEstimatedSurfaceBytes = 64L * 1024L * 1024L;
        private readonly Dictionary<RowKey, Entry> _entries =
            new Dictionary<RowKey, Entry>(64);
        private bool _disabled;
        private long _estimatedSurfaceBytes;
        private long _accessSequence;
        private int _failedRenderResourcesRevision = int.MinValue;
        private int _compositionCapabilityRevision = int.MinValue;
        private bool _compositionCapabilityAvailable;

        internal readonly struct Cell
        {
            internal Cell(
                int pawnId,
                int columnIndex,
                Rect boxRect,
                WorkBoxVisualState visual,
                int displayPriority,
                bool bakePriorityLabel = false,
                GameFont priorityFont = GameFont.Medium,
                int priorityStyleRevision = 0)
            {
                PawnId = pawnId;
                ColumnIndex = columnIndex;
                BoxRect = boxRect;
                Visual = visual;
                DisplayPriority = displayPriority;
                BakePriorityLabel = bakePriorityLabel;
                PriorityFont = priorityFont;
                PriorityStyleRevision = priorityStyleRevision;
            }

            internal int PawnId { get; }
            internal int ColumnIndex { get; }
            internal Rect BoxRect { get; }
            internal WorkBoxVisualState Visual { get; }
            internal int DisplayPriority { get; }
            internal bool BakePriorityLabel { get; }
            internal GameFont PriorityFont { get; }
            internal int PriorityStyleRevision { get; }
        }

        internal sealed class PreparedRun
        {
            internal PreparedRun(Cell[] cells)
            {
                Cells = cells;
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
            int layoutRevision,
            WorkGridRetainedVisualKey retainedVisualKey,
            int renderResourcesRevision)
        {
            if (_disabled ||
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
                    layoutRevision,
                    retainedVisualKey,
                    renderResourcesRevision);
            }
            catch (Exception exception)
            {
                HandleCompositionException(renderResourcesRevision, exception);
                return false;
            }
        }

        internal bool TryDraw(
            List<Cell> cells,
            Color baseColor,
            int layoutRevision,
            WorkGridRetainedVisualKey retainedVisualKey,
            int renderResourcesRevision)
        {
            if (_disabled ||
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
                    layoutRevision,
                    retainedVisualKey,
                    renderResourcesRevision);
            }
            catch (Exception exception)
            {
                HandleCompositionException(renderResourcesRevision, exception);
                return false;
            }
        }

        public void Dispose()
        {
            Exception releaseFailure = null;
            foreach (Entry entry in _entries.Values)
            {
                try
                {
                    ReleaseSurface(entry);
                }
                catch (Exception exception)
                {
                    if (releaseFailure == null)
                    {
                        releaseFailure = exception;
                    }
                }
            }
            _entries.Clear();
            _estimatedSurfaceBytes = 0L;
            // Allocation and resource-readiness failures are scoped to the
            // generation whose surfaces just left this cache. The next open
            // must be allowed to retry. Keep _disabled untouched: only an
            // explicit unsupported composition is a permanent fallback.
            _failedRenderResourcesRevision = int.MinValue;
            _compositionCapabilityRevision = int.MinValue;
            _compositionCapabilityAvailable = false;
            PreparedWorkBoxRenderer.ReleaseRetainedResources();
            if (releaseFailure != null)
            {
                Log.Warning(
                    "[BWT] Failed to release one or more retained work-grid rows: " +
                    releaseFailure.GetType().Name + ": " + releaseFailure.Message);
            }
        }

        internal void ResetResourceFailureLatchForReopen()
        {
            // A resource allocation or readiness failure can recover after the
            // tab has been closed. An explicit unsupported composition remains
            // a permanent direct-render fallback.
            _failedRenderResourcesRevision = int.MinValue;
            if (!_compositionCapabilityAvailable)
            {
                _compositionCapabilityRevision = int.MinValue;
            }
        }

        private static Rect GetBounds(IReadOnlyList<Cell> cells)
        {
            Rect bounds = cells[0].BoxRect;
            float stableOutset = cells[0].BakePriorityLabel
                ? PreparedWorkBoxRenderer.PriorityLabelOutset
                : 0f;
            for (int index = 1; index < cells.Count; index++)
            {
                Cell cell = cells[index];
                Rect rect = cell.BoxRect;
                bounds.xMin = Mathf.Min(bounds.xMin, rect.xMin);
                bounds.yMin = Mathf.Min(bounds.yMin, rect.yMin);
                bounds.xMax = Mathf.Max(bounds.xMax, rect.xMax);
                bounds.yMax = Mathf.Max(bounds.yMax, rect.yMax);
                if (cell.BakePriorityLabel)
                {
                    stableOutset = Mathf.Max(
                        stableOutset,
                        PreparedWorkBoxRenderer.PriorityLabelOutset);
                }
            }

            // Native GUIStyle.Draw uses the same three-pixel outset as the
            // direct label path. Include it in the surface only when a stable
            // numeral is actually baked; live warning/passion pixels remain
            // outside the retained surface and therefore need no padding here.
            return stableOutset > 0f
                ? bounds.ExpandedBy(stableOutset)
                : bounds;
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
            Color baseColor,
            out RetainedWorkBoxDrawFailure failure)
        {
            failure = RetainedWorkBoxDrawFailure.None;
            RenderTexture previous = RenderTexture.active;
            int previousViewportWidth = previous == null ? Screen.width : previous.width;
            int previousViewportHeight = previous == null ? Screen.height : previous.height;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            bool previousSrgbWrite = GL.sRGBWrite;
            bool matrixPushed = false;
            try
            {
                RenderTexture.active = surface;
                GL.InvalidateState();
                PreparedWorkBoxRenderer.ConfigureSrgbWriteForSrgbTarget();
                GL.Viewport(new Rect(0f, 0f, surface.width, surface.height));
                // Keep prepared logical rects in surface coordinates. Stable
                // textures and proven native priority numerals are composed
                // here; translucent warnings, passion icons, and interaction
                // feedback remain live after presentation.
                GUI.matrix = Matrix4x4.identity;
                GL.PushMatrix();
                matrixPushed = true;
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
                        bool drawn = PreparedWorkBoxRenderer.DrawRetained(
                            localRect,
                            cell.Visual,
                            cell.DisplayPriority,
                            baseColor,
                            out RetainedWorkBoxDrawFailure cellFailure);
                        if (!drawn)
                        {
                            failure = cellFailure;
                            return false;
                        }

                        if (cell.BakePriorityLabel &&
                            !PreparedWorkBoxRenderer.DrawRetainedPriorityLabel(
                                localRect,
                                cell.Visual,
                                cell.DisplayPriority,
                                cell.PriorityFont,
                                baseColor,
                                out cellFailure))
                        {
                            failure = cellFailure;
                            return false;
                        }
                    }

                }
                finally
                {
                    if (matrixPushed)
                    {
                        GL.PopMatrix();
                        matrixPushed = false;
                    }
                }
                return true;
            }
            catch (NotSupportedException)
            {
                failure = RetainedWorkBoxDrawFailure.Unsupported;
                return false;
            }
            finally
            {
                if (matrixPushed)
                {
                    GL.PopMatrix();
                }

                RenderTexture.active = previous;
                if (previousViewportWidth > 0 && previousViewportHeight > 0)
                {
                    GL.Viewport(new Rect(
                        0f,
                        0f,
                        previousViewportWidth,
                        previousViewportHeight));
                }
                GL.sRGBWrite = previousSrgbWrite;
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GL.InvalidateState();
            }
        }

        private static ulong GetFingerprint(
            ulong staticFingerprint,
            Color baseColor,
            int layoutRevision,
            WorkGridRetainedVisualKey retainedVisualKey,
            int pixelWidth,
            int pixelHeight,
            int renderResourcesRevision)
        {
            ulong hash = 1469598103934665603UL;
            Mix(ref hash, layoutRevision);
            Mix(ref hash, retainedVisualKey.UiScaleMilli);
            Mix(ref hash, unchecked((int)retainedVisualKey.SettingsThemeLanguageScaleRevision));
            Mix(ref hash, unchecked((int)(retainedVisualKey.SettingsThemeLanguageScaleRevision >> 32)));
            Mix(ref hash, retainedVisualKey.MaximumPriority);
            Mix(ref hash, renderResourcesRevision);
            Mix(ref hash, pixelWidth);
            Mix(ref hash, pixelHeight);
            Mix(ref hash, baseColor.GetHashCode());
            // The material is shared by every retained row, but its identity
            // still belongs in the key. A device/resource reset can replace
            // it without changing the row topology.
            Mix(ref hash, PreparedWorkBoxRenderer.RetainedMaterialRevision);
            Mix(ref hash, unchecked((int)staticFingerprint));
            Mix(ref hash, unchecked((int)(staticFingerprint >> 32)));
            return hash;
        }

        private static ulong GetStaticFingerprint(IReadOnlyList<Cell> cells)
        {
            // Best-pawn and override-ring markers stay out of the stable surface because
            // they are live overlays and must remain responsive to current state.
            ulong hash = 1469598103934665603UL;
            for (int index = 0; index < cells.Count; index++)
            {
                Cell cell = cells[index];
                WorkBoxVisualState visual = cell.Visual;
                Mix(ref hash, cell.PawnId);
                Mix(ref hash, cell.ColumnIndex);
                Mix(ref hash, cell.BoxRect.GetHashCode());
                Mix(ref hash, visual.SkillBand);
                Mix(ref hash, visual.SkillBlend.GetHashCode());
                Mix(ref hash, (int)(visual.Flags &
                    ~(WorkCellVisualFlags.BestPawn |
                      WorkCellVisualFlags.OverrideRing |
                      WorkCellVisualFlags.LowSkillWarning)));
                // Non-manual cells retain only the checkbox state. Eligible
                // manual numerals are also retained, so their effective value,
                // color, font, and style revision must participate in the key.
                bool retainedCheck =
                    (visual.Flags & (WorkCellVisualFlags.Disabled |
                                     WorkCellVisualFlags.ManualPriorityMode)) == 0 &&
                    cell.DisplayPriority > WorkPrioritySystem.DisabledPriority;
                Mix(ref hash, retainedCheck ? 1 : 0);
                Mix(ref hash, cell.BakePriorityLabel ? 1 : 0);
                if (cell.BakePriorityLabel)
                {
                    Mix(ref hash, cell.DisplayPriority);
                    Mix(ref hash, unchecked((int)PreparedWorkBoxRenderer.GetPriorityLabelColor(
                        visual,
                        cell.DisplayPriority).GetHashCode()));
                    Mix(ref hash, (int)cell.PriorityFont);
                    Mix(ref hash, cell.PriorityStyleRevision);
                }
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
            int layoutRevision,
            WorkGridRetainedVisualKey retainedVisualKey,
            int renderResourcesRevision)
        {
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

            if (!TryEnsureCompositionCapability(renderResourcesRevision))
            {
                return false;
            }

            ulong fingerprint = GetFingerprint(
                staticFingerprint,
                baseColor,
                layoutRevision,
                retainedVisualKey,
                pixelWidth,
                pixelHeight,
                renderResourcesRevision);

            if (!TryAcquireSurface(
                    key,
                    pixelWidth,
                    pixelHeight,
                    requestedBytes,
                    renderResourcesRevision,
                    out Entry entry))
            {
                return false;
            }

            if (!TryRebuildSurfaceIfChanged(
                    entry,
                    fingerprint,
                    retainedVisualKey,
                    bounds,
                    cells,
                    baseColor,
                    renderResourcesRevision))
            {
                return false;
            }

            // Native GUIStyle.Draw is issued against the new target above, but
            // Unity may not expose the freshly written font atlas pixels until
            // the following repaint. Do not present a cold surface in that
            // frame: the caller's direct path is complete and clipped, so it
            // supplies the authoritative row while this surface warms.
            if (entry.SurfaceBuiltFrame == Time.frameCount)
            {
                return false;
            }

            PresentSurface(entry.Surface, destination);
            return true;
        }

        private bool TryEnsureCompositionCapability(int renderResourcesRevision)
        {
            if (_compositionCapabilityRevision == renderResourcesRevision)
            {
                return _compositionCapabilityAvailable;
            }

            bool available = PreparedWorkBoxRenderer.TryValidateRetainedComposition(
                out RetainedWorkBoxDrawFailure failure);
            _compositionCapabilityRevision = renderResourcesRevision;
            _compositionCapabilityAvailable = available;
            if (available)
            {
                return true;
            }

            if (failure == RetainedWorkBoxDrawFailure.Unsupported)
            {
                DisableAfterFailure("retained row composition unsupported");
            }
            else
            {
                LatchResourceFailure(
                    renderResourcesRevision,
                    "retained row composition capability unavailable");
            }
            return false;
        }

        private bool TryAcquireSurface(
            RowKey key,
            int pixelWidth,
            int pixelHeight,
            long requestedBytes,
            int renderResourcesRevision,
            out Entry entry)
        {
            bool existing = _entries.TryGetValue(key, out entry);
            if (!existing)
            {
                entry = new Entry();
            }

            entry.LastUsedSequence = ++_accessSequence;
            if (entry.Surface != null &&
                entry.Surface.IsCreated() &&
                entry.PixelWidth == pixelWidth &&
                entry.PixelHeight == pixelHeight)
            {
                return true;
            }

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
            entry.SurfaceBuiltFrame = -1;
            entry.Fingerprint = 0UL;
            if (!existing)
            {
                _entries.Add(key, entry);
            }
            return true;
        }

        private bool TryRebuildSurfaceIfChanged(
            Entry entry,
            ulong fingerprint,
            WorkGridRetainedVisualKey retainedVisualKey,
            Rect bounds,
            IReadOnlyList<Cell> cells,
            Color baseColor,
            int renderResourcesRevision)
        {
            if (entry.Fingerprint == fingerprint &&
                entry.RetainedVisualKey.Equals(retainedVisualKey) &&
                entry.Bounds == bounds)
            {
                return true;
            }

            if (!BuildSurface(
                    entry.Surface,
                    bounds,
                    cells,
                    baseColor,
                    out RetainedWorkBoxDrawFailure failure))
            {
                if (failure == RetainedWorkBoxDrawFailure.Unsupported)
                {
                    DisableAfterFailure("retained row composition unsupported");
                }
                else
                {
                    LatchResourceFailure(
                        renderResourcesRevision,
                        "retained row composition resources unavailable");
                }
                return false;
            }

            entry.Fingerprint = fingerprint;
            entry.RetainedVisualKey = retainedVisualKey;
            entry.Bounds = bounds;
            entry.SurfaceBuiltFrame = Time.frameCount;
            return true;
        }

        private static void PresentSurface(RenderTexture surface, Rect destination)
        {
            // The surface was composed through a top-left pixel matrix, and
            // IMGUI presents it inside the owning scroll-view/group clip. Its
            // pixels already contain their warning/glyph colors, so do not
            // multiply them by the caller's stale cell tint a second time.
            using (RetainedSurfacePresentation.EnterNeutralTextureTint())
            {
                GUI.DrawTextureWithTexCoords(
                    destination,
                    surface,
                    new Rect(0f, 0f, 1f, 1f),
                    true);
            }
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

        private void HandleCompositionException(
            int renderResourcesRevision,
            Exception exception)
        {
            // NotSupportedException is the only exception that proves this
            // composition cannot be supported. Other Unity/resource failures
            // may recover with the next render-resource generation.
            if (exception is NotSupportedException)
            {
                DisableAfterFailure("retained row composition unsupported: " + exception);
            }
            else
            {
                LatchResourceFailure(
                    renderResourcesRevision,
                    "retained row exception: " + exception);
            }
        }

        private void LatchResourceFailure(
            int renderResourcesRevision,
            string message)
        {
            _failedRenderResourcesRevision = renderResourcesRevision;
            Log.WarningOnce(
                "[Better Work Tab] " + message + "; using direct clipped rendering.",
                1884630218);
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
            // This path is reserved for an explicitly unsupported composition;
            // resource readiness failures use LatchResourceFailure instead.
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
            entry.SurfaceBuiltFrame = -1;
            try
            {
                surface.Release();
            }
            finally
            {
                UnityEngine.Object.Destroy(surface);
            }
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
            internal WorkGridRetainedVisualKey RetainedVisualKey;
            internal long LastUsedSequence;
            internal long EstimatedBytes;
            // A rebuilt surface is direct-rendered for the rest of its build
            // frame so native numerals cannot be presented before they settle.
            internal int SurfaceBuiltFrame = -1;
        }
    }
}
