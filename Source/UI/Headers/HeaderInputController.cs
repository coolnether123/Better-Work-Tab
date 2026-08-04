using UnityEngine;
using Verse;
using RimWorld;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.Tutorial;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Tracks mouse input and hover states for headers.
    /// Uses frame-based caching to prevent redundant calculations and flickering.
    /// </summary>
    public static class HeaderInputController
    {
        private static int _lastCacheFrame = -1;
        private static Vector2 _cachedMousePos;
        private static WorkTypeDef _cachedHoveredWorkType;
        private static WorkTypeDef _lastFrameHoveredWorkType;
        private static Rect? _cachedHoveredRect;

        /// <summary>
        /// Current mouse position in UI coordinates, cached once per frame.
        /// </summary>
        public static Vector2 MousePosition => BWTWorkTabTutorial.OwnsCurrentPointer
            ? new Vector2(-10000f, -10000f)
            : _cachedMousePos;

        /// <summary>
        /// The work type currently being hovered by the mouse, if any.
        /// </summary>
        public static WorkTypeDef HoveredWorkType =>
            _lastCacheFrame == Time.frameCount ? _cachedHoveredWorkType : null;

        /// <summary>
        /// The bounds of the currently hovered work type header (if known) for the current frame.
        /// </summary>
        public static Rect? HoveredWorkTypeRect =>
            _lastCacheFrame == Time.frameCount ? _cachedHoveredRect : null;

        /// <summary>
        /// Updates the mouse position cache and rotates the hover state tracking.
        /// Should be called at the start of header rendering each frame.
        /// </summary>
        /// <param name="evt">The current Unity event.</param>
        public static void UpdateCache(Event evt)
        {
            int currentFrame = Time.frameCount;
            if (_lastCacheFrame != currentFrame)
            {
                _lastFrameHoveredWorkType = _cachedHoveredWorkType;
                _cachedMousePos = evt?.mousePosition ?? Vector2.zero;
                _lastCacheFrame = currentFrame;
                _cachedHoveredWorkType = null;
                _cachedHoveredRect = null;

                // The selection deliberately outlives the Shift key.
                //
                // Clearing it the moment Shift came up meant it never survived
                // long enough to be dragged: releasing Shift is exactly what a
                // player does before grabbing the group, and IsHeaderDragging is
                // not set until the drag has already started, so it could not
                // protect the selection either. The group was wiped one frame
                // before the drag began and only the grabbed column moved.
                //
                // Selection now ends on intent rather than on modifier state: a
                // plain click on a header clears it, and so does a completed
                // drop.
            }
        }

        /// <summary>
        /// Sets the work type as being hovered for the current frame.
        /// </summary>
        /// <param name="workType">The work type that the mouse is over.</param>
        /// <param name="bounds">Optional header bounds for drag/drop visuals.</param>
        public static void SetHoveredWorkType(WorkTypeDef workType, Rect? bounds = null)
        {
            _cachedHoveredWorkType = workType;
            _cachedHoveredRect = bounds;
        }

        /// <summary>
        /// Checks if a specific work type was hovered in the previous frame.
        /// </summary>
        /// <param name="workType">The work type to check.</param>
        /// <returns>True if it was hovered last frame.</returns>
        public static bool WasHoveredLastFrame(WorkTypeDef workType)
        {
            return _lastFrameHoveredWorkType != null && workType == _lastFrameHoveredWorkType;
        }
    }
}
