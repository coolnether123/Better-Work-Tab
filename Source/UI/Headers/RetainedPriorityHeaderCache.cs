using System;
using System.Collections.Generic;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Headers.Vanilla;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Retains only the stable pixels of BWT-owned priority headers. Header
    /// interaction remains in the controller; any dynamic or unsupported state
    /// returns false so the existing direct renderer remains authoritative.
    /// </summary>
    internal sealed class RetainedPriorityHeaderCache : IDisposable
    {
        private const int MaximumEntries = 64;
        private const long MaximumEstimatedSurfaceBytes = 32L * 1024L * 1024L;

        private readonly Dictionary<PawnColumnDef, Entry> _entries =
            new Dictionary<PawnColumnDef, Entry>(32);
        private long _estimatedSurfaceBytes;
        private int _failedRenderResourcesRevision = int.MinValue;
        private Game _game;
        private WorkTabInvalidationVersion _versions;

        internal void PrepareFrame(WorkTabInvalidationVersion versions)
        {
            Game currentGame = Current.Game;
            if (!ReferenceEquals(_game, currentGame))
            {
                Dispose();
                _game = currentGame;
            }

            bool stablePixelsChanged =
                versions.HeaderText != _versions.HeaderText ||
                versions.HeaderGeometry != _versions.HeaderGeometry ||
                versions.Columns != _versions.Columns ||
                versions.RenderResources != _versions.RenderResources ||
                versions.CategoryRevisions.SettingsThemeLanguageScale !=
                    _versions.CategoryRevisions.SettingsThemeLanguageScale;
            if (stablePixelsChanged)
            {
                ReleaseEntries();
            }

            if (_failedRenderResourcesRevision != int.MinValue &&
                _failedRenderResourcesRevision != versions.RenderResources)
            {
                _failedRenderResourcesRevision = int.MinValue;
            }
            _versions = versions;
        }

        internal bool TryDraw(
            IHeaderPresentationRenderer renderer,
            AngledLabelDrawer.AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation)
        {
            if (!CanRetain(
                    renderer,
                    in layout,
                    isMouseOver,
                    isSorted,
                    headerRect,
                    column))
            {
                return false;
            }

            int renderResourcesRevision = _versions.RenderResources;
            if (_failedRenderResourcesRevision == renderResourcesRevision)
            {
                return false;
            }

            try
            {
                if (!TryResolveSurfaceGeometry(
                        in layout,
                        headerRect,
                        in presentation,
                        out RetainedHeaderSurfaceGeometry surfaceGeometry))
                {
                    return false;
                }

                long requestedBytes = surfaceGeometry.EstimatedBytes;
                if (requestedBytes > MaximumEstimatedSurfaceBytes)
                {
                    return false;
                }

                RetainedHeaderVisualKey visualKey = RetainedHeaderVisualKey.Capture(
                    in layout,
                    headerRect,
                    column,
                    showMarker,
                    in presentation,
                    in _versions,
                    in surfaceGeometry);

                if (!TryAcquire(
                        column,
                        surfaceGeometry.PixelWidth,
                        surfaceGeometry.PixelHeight,
                        requestedBytes,
                        renderResourcesRevision,
                        out Entry entry))
                {
                    return false;
                }

                if (!entry.VisualKey.Equals(visualKey))
                {
                    if (!BuildSurface(
                            entry.Surface,
                            in surfaceGeometry,
                            renderer,
                            in layout,
                            headerRect,
                            column,
                            showMarker,
                            in presentation))
                    {
                        LatchFailure(renderResourcesRevision, "retained priority-header composition failed");
                        return false;
                    }
                    entry.VisualKey = visualKey;
                }

                PresentSurface(
                    entry.Surface,
                    surfaceGeometry.Destination);
                DrawRetainedText(
                    renderer,
                    in layout,
                    headerRect,
                    column,
                    showMarker,
                    in presentation);
                return true;
            }
            catch (Exception exception)
            {
                LatchFailure(renderResourcesRevision, "retained priority-header exception: " + exception.Message);
                return false;
            }
        }

        private static bool TryResolveSurfaceGeometry(
            in AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            in HeaderPresentationPacket presentation,
            out RetainedHeaderSurfaceGeometry geometry)
        {
            Rect retainedBounds = ResolveStableBounds(in layout, headerRect, in presentation);
            retainedBounds.xMin = Mathf.Floor(retainedBounds.xMin - 3f);
            retainedBounds.yMin = Mathf.Floor(retainedBounds.yMin - 3f);
            retainedBounds.xMax = Mathf.Ceil(retainedBounds.xMax + 3f);
            retainedBounds.yMax = Mathf.Ceil(retainedBounds.yMax + 3f);
            if (retainedBounds.width <= 0f || retainedBounds.height <= 0f)
            {
                geometry = default(RetainedHeaderSurfaceGeometry);
                return false;
            }

            float logicalScreenWidth = Verse.UI.screenWidth;
            if (logicalScreenWidth <= 0f)
            {
                geometry = default(RetainedHeaderSurfaceGeometry);
                return false;
            }

            float pixelScale = Mathf.Max(1f, (float)Screen.width / logicalScreenWidth);
            geometry = new RetainedHeaderSurfaceGeometry(
                retainedBounds,
                logicalScreenWidth,
                Verse.UI.screenHeight,
                Mathf.Max(1, Mathf.CeilToInt(retainedBounds.width * pixelScale)),
                Mathf.Max(1, Mathf.CeilToInt(retainedBounds.height * pixelScale)),
                GUI.matrix);
            return true;
        }

        public void Dispose()
        {
            try
            {
                ReleaseEntries();
            }
            finally
            {
                _game = null;
                _failedRenderResourcesRevision = int.MinValue;
            }
        }

        internal void ResetFailureLatchesForReopen()
        {
            _failedRenderResourcesRevision = int.MinValue;
        }

        private static bool CanRetain(
            IHeaderPresentationRenderer renderer,
            in AngledLabelDrawer.AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            Rect headerRect,
            PawnColumnDef column)
        {
            // Dynamic visuals never enter retained pixels. Falling back for the
            // affected header keeps hover, sorting, selection, and every input
            // path on the existing direct renderer.
            if (Event.current.type != EventType.Repaint ||
                !GUI.enabled ||
                (renderer.GetType() != typeof(AngledHeaderRenderer) &&
                 renderer.GetType() != typeof(Vanilla.VanillaHeaderRenderer)) ||
                column == null ||
                column.workType == null ||
                column.Worker?.GetType() != typeof(PawnColumnWorker_WorkPriority) ||
                FluffyWorkTabGateway.IsFluffyColumn(column) ||
                SleekWorkTabGateway.BetterWorkTabHostsSleek ||
                headerRect.width <= 0f ||
                headerRect.height <= 0f ||
                layout.Alpha < 0.999f ||
                isMouseOver ||
                isSorted ||
                ColumnSelectionManager.IsSelected(column))
            {
                return false;
            }

            // Animated geometry and sub-work presentation carry live offsets,
            // alpha, and parent ghosts. They retain the direct renderer until
            // their normal settled root-header state returns.
            return !ColumnReorderAnimationState.IsActive &&
                   !PawnOrganizerSystem.Instance.IsDraggingColumn &&
                   !SubWorkDrilldownState.HasAnyDrilldown &&
                   !SubWorkDrilldownState.IsTransitioning &&
                   !SubWorkDrilldownState.IsExpandBesideTransitioning;
        }

        private static Rect ResolveStableBounds(
            in AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            in HeaderPresentationPacket presentation)
        {
            if (presentation.AngledHeadersEnabled)
            {
                float width = layout.HasCustomDrawRect
                    ? layout.CustomDrawRect.width
                    : layout.IsCJKVertical
                        ? layout.Size.x
                        : headerRect.height;
                float height = layout.HasCustomDrawRect
                    ? layout.CustomDrawRect.height
                    : layout.Size.y;
                if (layout.IsCJKVertical)
                {
                    return new Rect(
                        layout.Pivot.x - width * 0.5f,
                        layout.Pivot.y - height * 0.5f,
                        width,
                        height);
                }

                float cos = Mathf.Abs(presentation.RotationCos);
                float sin = Mathf.Abs(presentation.RotationSin);
                return new Rect(
                    layout.Pivot.x - (width * cos + height * sin) * 0.5f,
                    layout.Pivot.y - (width * sin + height * cos) * 0.5f,
                    width * cos + height * sin,
                    width * sin + height * cos);
            }

            Rect bounds = new Rect(
                layout.Pivot.x - layout.Size.x * 0.5f,
                layout.Pivot.y - layout.Size.y * 0.5f,
                layout.Size.x,
                layout.Size.y);
            bounds.xMin = Mathf.Min(bounds.xMin, headerRect.center.x - 2f);
            bounds.xMax = Mathf.Max(bounds.xMax, headerRect.center.x + 4f);
            bounds.yMax = headerRect.yMax + 2f;
            return bounds;
        }

        private static bool BuildSurface(
            RenderTexture surface,
            in RetainedHeaderSurfaceGeometry surfaceGeometry,
            IHeaderPresentationRenderer renderer,
            in AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation)
        {
            RenderTexture previousTarget = RenderTexture.active;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                RenderTexture.active = surface;
                GUI.matrix = Matrix4x4.identity;
                GL.PushMatrix();
                try
                {
                    Rect retainedBounds = surfaceGeometry.Destination;
                    GL.LoadPixelMatrix(0f, retainedBounds.width, retainedBounds.height, 0f);
                    GL.Clear(true, true, Color.clear);
                    Vector2 translation = -retainedBounds.position;
                    AngledLabelDrawer.AngledLabelLayout localLayout =
                        TranslateLayout(in layout, translation);
                    Rect localHeaderRect = headerRect;
                    localHeaderRect.position += translation;
                    if (renderer is AngledHeaderRenderer angled)
                    {
                        angled.DrawRetainedStable(
                            localLayout,
                            localHeaderRect,
                            column,
                            in presentation);
                    }
                    else if (renderer is VanillaHeaderRenderer vanilla)
                    {
                        vanilla.DrawRetainedStable(
                            localLayout,
                            localHeaderRect,
                            column,
                            showMarker,
                            in presentation);
                    }
                    else
                    {
                        return false;
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
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                RenderTexture.active = previousTarget;
            }
        }

        private static void DrawRetainedText(
            IHeaderPresentationRenderer renderer,
            in AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation)
        {
            if (renderer is AngledHeaderRenderer angled)
            {
                angled.DrawRetainedText(
                    layout,
                    headerRect,
                    column,
                    in presentation);
            }
            else if (renderer is VanillaHeaderRenderer vanilla)
            {
                vanilla.DrawRetainedText(
                    layout,
                    headerRect,
                    column,
                    showMarker,
                    in presentation);
            }
            else
            {
                throw new InvalidOperationException(
                    "retained priority-header renderer has no live text boundary");
            }
        }

        private static AngledLabelDrawer.AngledLabelLayout TranslateLayout(
            in AngledLabelDrawer.AngledLabelLayout layout,
            Vector2 translation)
        {
            Vector2 pivot = layout.Pivot + translation;
            if (!layout.HasCustomDrawRect)
            {
                return new AngledLabelDrawer.AngledLabelLayout(
                    layout.Text,
                    layout.Size,
                    pivot,
                    layout.ShowMarker,
                    layout.IsCJKVertical);
            }

            Rect customDrawRect = layout.CustomDrawRect;
            customDrawRect.position += translation;
            return new AngledLabelDrawer.AngledLabelLayout(
                layout.Text,
                layout.Size,
                pivot,
                layout.ShowMarker,
                layout.IsCJKVertical,
                customDrawRect,
                layout.UnderlineWidth);
        }

        private bool TryAcquire(
            PawnColumnDef column,
            int pixelWidth,
            int pixelHeight,
            long requestedBytes,
            int renderResourcesRevision,
            out Entry entry)
        {
            bool existing = _entries.TryGetValue(column, out entry);
            if (!existing)
            {
                entry = new Entry();
            }
            if (entry.Surface != null &&
                entry.Surface.IsCreated() &&
                entry.PixelWidth == pixelWidth &&
                entry.PixelHeight == pixelHeight)
            {
                return true;
            }

            ReleaseSurface(entry);
            if (!HasCapacity(requestedBytes, existing ? 0 : 1))
            {
                return false;
            }

            entry.Surface = CreateSurface(pixelWidth, pixelHeight);
            if (entry.Surface == null)
            {
                _failedRenderResourcesRevision = renderResourcesRevision;
                return false;
            }
            entry.PixelWidth = pixelWidth;
            entry.PixelHeight = pixelHeight;
            entry.EstimatedBytes = requestedBytes;
            entry.VisualKey = default(RetainedHeaderVisualKey);
            _estimatedSurfaceBytes += requestedBytes;
            if (!existing)
            {
                _entries.Add(column, entry);
            }
            return true;
        }

        private static RenderTexture CreateSurface(int width, int height)
        {
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
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
                name = "BWT retained priority header",
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

        private static void PresentSurface(
            RenderTexture surface,
            Rect destination)
        {
            // The retained IMGUI path already composes with a top-left pixel
            // matrix. Use the same logical orientation as the proven retained
            // row/chrome paths; a platform UV flip here would invert the text.
            using (RetainedSurfacePresentation.EnterNeutralTextureTint())
            {
                GUI.DrawTextureWithTexCoords(
                    destination,
                    surface,
                    new Rect(0f, 0f, 1f, 1f),
                    true);
            }
        }

        private bool HasCapacity(long requestedBytes, int entryDelta)
        {
            // Relevant layout/resource revisions release the complete set, so
            // entries in one generation are all current. Refuse admission once
            // bounded capacity is full instead of evicting them in draw order
            // and rebuilding the same headers on every high-resolution frame.
            return _entries.Count + entryDelta <= MaximumEntries &&
                   _estimatedSurfaceBytes + requestedBytes <=
                       MaximumEstimatedSurfaceBytes;
        }

        private void LatchFailure(int renderResourcesRevision, string message)
        {
            _failedRenderResourcesRevision = renderResourcesRevision;
            ReleaseEntries();
            Log.WarningOnce(
                "[Better Work Tab] " + message + "; using direct priority-header rendering.",
                1467358021);
        }

        private void ReleaseEntries()
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
            if (releaseFailure != null)
            {
                Log.Warning(
                    "[BWT] Failed to release one or more retained priority headers: " +
                    releaseFailure.GetType().Name + ": " + releaseFailure.Message);
            }
        }

        private void ReleaseSurface(Entry entry)
        {
            if (entry.Surface == null)
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
            try
            {
                surface.Release();
            }
            finally
            {
                UnityEngine.Object.Destroy(surface);
            }
        }

        private sealed class Entry
        {
            internal RenderTexture Surface;
            internal int PixelWidth;
            internal int PixelHeight;
            internal long EstimatedBytes;
            internal RetainedHeaderVisualKey VisualKey;
        }

    }
}
