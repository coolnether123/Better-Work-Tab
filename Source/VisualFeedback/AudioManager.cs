using Verse.Sound;
using RimWorld;

namespace Better_Work_Tab.VisualFeedback
{
    /// <summary>
    /// Centralized audio feedback for user interactions.
    /// Single responsibility: Play appropriate sounds.
    /// </summary>
    public static class AudioManager
    {
        public static void PlayClick()
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        public static void PlayTick()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        public static void PlayColumnSort()
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        public static void PlayColumnResize()
        {
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
        }

        public static void PlayError()
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        public static void PlaySelect()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        public static void PlayDeselect()
        {
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }
    }
}
