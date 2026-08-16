using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// The Work tab's way into the 2.0 beta feedback portal.
    ///
    /// The portal used to be reachable only from the tutorial band, so anyone who
    /// answered "Not now" on the welcome screen — which is most experienced
    /// players, the people whose feedback is worth the most — had no way to reach
    /// it at all. This button sits at the left end of the bottom-right row and
    /// is always there.
    ///
    /// It stays quiet until the player has actually used the tab for a while.
    /// Asking for feedback in the first minute gets nothing useful, so the button
    /// is dim until a threshold of real Work-tab time has accumulated, then draws
    /// attention to itself once and explains why it is asking.
    /// </summary>
    internal static class BWTBetaFeedbackButton
    {
        internal const float NudgeAfterSeconds = 20f * 60f;

        private const float RestingAlpha = 0.35f;
        private const float HoverAlpha = 1f;
        private const float PulseSpeed = 2.6f;

        private static float lastTickRealtime = -1f;

        /// <summary>
        /// Accumulates Work-tab time. Called once per Work-tab pass; the elapsed
        /// value is derived from real time between calls so the counter follows
        /// how long the tab was actually on screen rather than how many frames it
        /// rendered.
        /// </summary>
        internal static void Tick()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            float now = Time.realtimeSinceStartup;
            if (settings == null || lastTickRealtime < 0f || now < lastTickRealtime)
            {
                lastTickRealtime = now;
                return;
            }

            float elapsed = now - lastTickRealtime;
            lastTickRealtime = now;

            // A pass can be many seconds apart if the tab was closed in between.
            // Only count time that plausibly belongs to a continuous view.
            if (elapsed <= 0f || elapsed > 1f || settings.betaFeedbackPromptAnswered)
            {
                return;
            }

            settings.betaFeedbackWorkTabSeconds += elapsed;
        }

        /// <summary>Resets the gap so a reopened tab does not bank the time it spent closed.</summary>
        internal static void NotifyTabClosed()
        {
            lastTickRealtime = -1f;
        }

        /// <summary>
        /// Draws the button into the rect the bottom bar reserved for it. The
        /// row owns the geometry; this only decides how the icon looks.
        /// </summary>
        internal static void Draw(Rect rect)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            bool hovered = Mouse.IsOver(rect);
            bool nudging = ShouldNudge(settings);

            Color previous = GUI.color;
            float alpha = hovered ? HoverAlpha : RestingAlpha;
            if (nudging && !hovered)
            {
                // A slow breath rather than a blink: enough to catch the eye at the
                // edge of vision without becoming something to be annoyed by.
                alpha = Mathf.Lerp(
                    RestingAlpha,
                    HoverAlpha,
                    0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * PulseSpeed));
            }

            GUI.color = new Color(1f, 1f, 1f, alpha);
            bool clicked = Widgets.ButtonImage(rect, TexButton.Suspend);
            GUI.color = previous;

            TooltipHandler.TipRegion(rect, nudging ? NudgeTooltip(settings) : RestingTooltip());

            if (!clicked)
            {
                return;
            }

            // Opening it is the answer to the prompt, whether or not anything is
            // ultimately submitted; the button should not keep pulsing afterwards.
            settings.betaFeedbackPromptAnswered = true;
            settings.Write();
            BWTGeneralTutorial.OpenBetaFeedback();
        }

        private static bool ShouldNudge(BetterWorkTabSettings settings)
        {
            return !settings.betaFeedbackPromptAnswered &&
                   settings.betaFeedbackWorkTabSeconds >= NudgeAfterSeconds;
        }

        private static string RestingTooltip()
        {
            return "BWT_BetaFeedback_ButtonTooltip".Translate();
        }

        private static string NudgeTooltip(BetterWorkTabSettings settings)
        {
            int minutes = Mathf.Max(1, Mathf.FloorToInt(settings.betaFeedbackWorkTabSeconds / 60f));
            return "BWT_BetaFeedback_NudgeTooltip".Translate(minutes);
        }
    }
}
