using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Tracks whether a one-time modal choice is still awaiting the player.
    ///
    /// Presence is derived from the window still being in the stack rather than
    /// latched by the dialog's own callbacks. A latch cleared only on the
    /// accept/cancel path strands whenever the window leaves by any other route:
    /// a stack removal, a scene change, another mod closing it, or an older
    /// RimWorld build where no cancel action is wired and Escape simply closes
    /// the dialog. Anything gating on a stranded latch stays gated for the rest
    /// of the session with no way for the player to recover it.
    /// </summary>
    internal sealed class OneTimePromptPresence
    {
        private Window window;

        /// <summary>
        /// Set between deciding to prompt and the dialog actually appearing.
        /// This one is a latch by necessity — there is no window to observe yet —
        /// but it is cleared unconditionally on the next long-event tick, so it
        /// cannot outlive the frame that raised it.
        /// </summary>
        internal bool Queued { get; set; }

        internal bool IsPending => Queued || IsOnScreen;

        private bool IsOnScreen
        {
            get
            {
                if (window == null)
                {
                    return false;
                }

                if (Find.WindowStack?.Windows?.Contains(window) == true)
                {
                    return true;
                }

                // Gone by some path other than Resolve. Forget it so a dead
                // dialog cannot keep reporting itself as on screen.
                window = null;
                return false;
            }
        }

        internal void Track(Window opened)
        {
            Queued = false;
            window = opened;
        }

        internal void Clear()
        {
            Queued = false;
            window = null;
        }
    }
}
