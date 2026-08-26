using System;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Describes the retained surface produced for one header. This derived
    /// geometry is shared by allocation, composition, presentation, and the
    /// visual key so those paths cannot silently disagree about the crop.
    /// </summary>
    internal readonly struct RetainedHeaderSurfaceGeometry
    {
        internal RetainedHeaderSurfaceGeometry(
            Rect destination,
            float logicalScreenWidth,
            float logicalScreenHeight,
            int pixelWidth,
            int pixelHeight,
            Matrix4x4 guiMatrix)
        {
            Destination = destination;
            LogicalScreenWidth = logicalScreenWidth;
            LogicalScreenHeight = logicalScreenHeight;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            GuiMatrix = guiMatrix;
        }

        internal Rect Destination { get; }
        internal float LogicalScreenWidth { get; }
        internal float LogicalScreenHeight { get; }
        internal int PixelWidth { get; }
        internal int PixelHeight { get; }
        internal Matrix4x4 GuiMatrix { get; }
        internal long EstimatedBytes => (long)PixelWidth * PixelHeight * 4L;
    }

    /// <summary>
    /// Exact stable-pixel identity for a retained priority header. Adding a
    /// new stable visual input to either header renderer requires adding it
    /// here or invalidating the complete cache when that input changes.
    /// </summary>
    internal readonly struct RetainedHeaderVisualKey : IEquatable<RetainedHeaderVisualKey>
    {
        private readonly string _columnDefName;
        private readonly string _workTypeDefName;
        private readonly string _text;
        private readonly string _language;
        private readonly Rect _headerRect;
        private readonly Rect _retainedBounds;
        private readonly Vector2 _size;
        private readonly Vector2 _pivot;
        private readonly Rect _customDrawRect;
        private readonly float _underlineWidth;
        private readonly float _alpha;
        private readonly float _rotation;
        private readonly float _horizontalOffset;
        private readonly Color _angledColor;
        private readonly Color _underlineColor;
        private readonly Color _movedMarkerColor;
        private readonly int _flags;
        private readonly int _screenWidth;
        private readonly int _screenHeight;
        private readonly int _pixelWidth;
        private readonly int _pixelHeight;
        private readonly int _physicalScreenHeight;
        private readonly float _uiScale;
        private readonly Matrix4x4 _guiMatrix;
        private readonly int _headerTextRevision;
        private readonly int _headerGeometryRevision;
        private readonly int _columnRevision;
        private readonly int _renderResourcesRevision;
        private readonly long _settingsThemeLanguageScaleRevision;
        private readonly long _presentationRevision;
        private readonly GraphicsDeviceType _graphicsDeviceType;

        private RetainedHeaderVisualKey(
            in AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation,
            in WorkTabInvalidationVersion versions,
            in RetainedHeaderSurfaceGeometry surfaceGeometry)
        {
            _columnDefName = column.defName;
            _workTypeDefName = column.workType.defName;
            _text = layout.Text;
            _language = LanguageDatabase.activeLanguage.folderName;
            _headerRect = headerRect;
            _retainedBounds = surfaceGeometry.Destination;
            _size = layout.Size;
            _pivot = layout.Pivot;
            _customDrawRect = layout.CustomDrawRect;
            _underlineWidth = layout.UnderlineWidth;
            _alpha = layout.Alpha;
            _rotation = presentation.Rotation;
            _horizontalOffset = presentation.EffectiveHorizontalOffset;
            _angledColor = presentation.AngledColor;
            _underlineColor = presentation.UnderlineColor;
            _movedMarkerColor = presentation.MovedMarkerColor;
            _flags = (layout.ShowMarker ? 1 : 0) |
                     (layout.IsCJKVertical ? 2 : 0) |
                     (layout.HasCustomDrawRect ? 4 : 0) |
                     (showMarker ? 8 : 0) |
                     (presentation.ShowMovedColorTint ? 16 : 0) |
                     (presentation.RemoveUnderline ? 32 : 0) |
                     (presentation.UseVerticalStackingForCjk ? 64 : 0) |
                     (presentation.AngledHeadersEnabled ? 128 : 0);
            _screenWidth = Mathf.RoundToInt(surfaceGeometry.LogicalScreenWidth);
            _screenHeight = Mathf.RoundToInt(surfaceGeometry.LogicalScreenHeight);
            _pixelWidth = surfaceGeometry.PixelWidth;
            _pixelHeight = surfaceGeometry.PixelHeight;
            _physicalScreenHeight = Screen.height;
            _uiScale = Prefs.UIScale;
            _guiMatrix = surfaceGeometry.GuiMatrix;
            _headerTextRevision = versions.HeaderText;
            _headerGeometryRevision = versions.HeaderGeometry;
            _columnRevision = versions.Columns;
            _renderResourcesRevision = versions.RenderResources;
            _settingsThemeLanguageScaleRevision =
                versions.CategoryRevisions.SettingsThemeLanguageScale;
            _presentationRevision = WorkTabPresentationRevision.Current;
            _graphicsDeviceType = SystemInfo.graphicsDeviceType;
        }

        internal static RetainedHeaderVisualKey Capture(
            in AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation,
            in WorkTabInvalidationVersion versions,
            in RetainedHeaderSurfaceGeometry surfaceGeometry)
        {
            return new RetainedHeaderVisualKey(
                in layout,
                headerRect,
                column,
                showMarker,
                in presentation,
                in versions,
                in surfaceGeometry);
        }

        public bool Equals(RetainedHeaderVisualKey other)
        {
            return string.Equals(_columnDefName, other._columnDefName, StringComparison.Ordinal) &&
                   string.Equals(_workTypeDefName, other._workTypeDefName, StringComparison.Ordinal) &&
                   string.Equals(_text, other._text, StringComparison.Ordinal) &&
                   string.Equals(_language, other._language, StringComparison.Ordinal) &&
                   _headerRect == other._headerRect &&
                   _retainedBounds == other._retainedBounds &&
                   _size == other._size &&
                   _pivot == other._pivot &&
                   _customDrawRect == other._customDrawRect &&
                   _underlineWidth.Equals(other._underlineWidth) &&
                   _alpha.Equals(other._alpha) &&
                   _rotation.Equals(other._rotation) &&
                   _horizontalOffset.Equals(other._horizontalOffset) &&
                   _angledColor == other._angledColor &&
                   _underlineColor == other._underlineColor &&
                   _movedMarkerColor == other._movedMarkerColor &&
                   _flags == other._flags &&
                   _screenWidth == other._screenWidth &&
                   _screenHeight == other._screenHeight &&
                   _pixelWidth == other._pixelWidth &&
                   _pixelHeight == other._pixelHeight &&
                   _physicalScreenHeight == other._physicalScreenHeight &&
                   _uiScale.Equals(other._uiScale) &&
                   _guiMatrix == other._guiMatrix &&
                   _headerTextRevision == other._headerTextRevision &&
                   _headerGeometryRevision == other._headerGeometryRevision &&
                   _columnRevision == other._columnRevision &&
                   _renderResourcesRevision == other._renderResourcesRevision &&
                   _settingsThemeLanguageScaleRevision == other._settingsThemeLanguageScaleRevision &&
                   _presentationRevision == other._presentationRevision &&
                   _graphicsDeviceType == other._graphicsDeviceType;
        }

        public override bool Equals(object obj)
        {
            return obj is RetainedHeaderVisualKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _columnDefName == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(_columnDefName);
        }
    }
}
