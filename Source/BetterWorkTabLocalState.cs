using System;

namespace Better_Work_Tab
{
    /// <summary>
    /// Tracks local UI state flags that should NOT be synced in multiplayer.
    /// </summary>
    public static class BetterWorkTabLocalState
    {
        /// <summary>
        /// True when the local player is currently dragging a column header.
        /// Used to prevent priority edits during drag operations.
        /// This is a LOCAL flag only - other players can still edit priorities.
        /// </summary>
        public static bool IsHeaderDragging { get; set; }
    }
}
