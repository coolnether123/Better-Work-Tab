using UnityEngine;
using Verse;
using RimWorld;

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

        /// <summary>
        /// Current mouse position in UI coordinates, cached once per frame.
        /// </summary>
        public static Vector2 MousePosition => _cachedMousePos;

        /// <summary>
        /// The work type currently being hovered by the mouse, if any.
        /// </summary>
        public static WorkTypeDef HoveredWorkType =>
            _lastCacheFrame == Time.frameCount ? _cachedHoveredWorkType : null;

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
            }
        }

        /// <summary>
        /// Sets the work type as being hovered for the current frame.
        /// </summary>
        /// <param name="workType">The work type that the mouse is over.</param>
        public static void SetHoveredWorkType(WorkTypeDef workType)
        {
            _cachedHoveredWorkType = workType;
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
