using UnityEngine;

namespace Better_Work_Tab.Diagnostics
{
    /// <summary>
    /// Tracks player-visible Work-tab time for the production profiler.
    /// </summary>
    internal static class WorkTabUsageState
    {
        private static bool _isOpen;
        private static double _openedAt;
        private static double _completedOpenSeconds;

        internal static double OpenSeconds => _completedOpenSeconds +
            (_isOpen ? Time.realtimeSinceStartup - _openedAt : 0.0);

        internal static void NotifyOpen(bool open)
        {
            if (_isOpen == open)
            {
                return;
            }

            double now = Time.realtimeSinceStartup;
            if (_isOpen)
            {
                _completedOpenSeconds += now - _openedAt;
            }

            _isOpen = open;
            _openedAt = open ? now : 0.0;
        }
    }
}
